using System.Globalization;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Push-based k-way merge engine. Receives lines from LogStreamers,
/// stores them in memory, and maintains a chronologically sorted merged index.
/// No file I/O — all data is pushed in via <see cref="PushHistory"/> and <see cref="AppendLines"/>.
/// </summary>
public sealed class ChronoMergeEngine : IMergedLogProvider
{
    private readonly record struct SourceInfo(string Tag, string TagColorHex, int Priority);
    private readonly record struct MergedLineRef(byte SourceIndex, int LineIndex);

    private readonly List<SourceInfo> _sources = [];
    private readonly List<List<LogLine>> _sourceLines = [];
    private readonly List<List<string>> _sourceRawLines = [];
    private readonly List<LogStreamer> _streamers = [];
    private readonly List<Action<IReadOnlyList<string>>> _lineHandlers = [];
    private readonly Lock _appendLock = new();

    private MergedLineRef[] _index = [];
    private volatile int _indexedCount;
    private volatile bool _isIndexing;
    private double _progress;
    private bool _disposed;

    public event Action<long>? IndexingProgressChanged;
    public event Action? IndexingCompleted;
    public event Action<long>? LinesAppended;

    // ── IVirtualLogProvider ───────────────────────────────────────

    public long LineCount => _indexedCount;
    public bool IsIndexing => _isIndexing;
    public double IndexingProgress => _progress;
    public string FilePath => "[Merge]";

    public LogLine? GetLine(long lineIndex)
    {
        var count = _indexedCount;
        if (lineIndex < 0 || lineIndex >= count) return null;

        // Snapshot the array ref so a concurrent Array.Resize can't pull it away
        var idx = Volatile.Read(ref _index);
        if (lineIndex >= idx.Length) return null;

        var entry = idx[lineIndex];
        if (entry.SourceIndex >= _sourceLines.Count) return null;

        var lines = _sourceLines[entry.SourceIndex];
        if (entry.LineIndex >= lines.Count) return null;

        return lines[entry.LineIndex] with { GlobalIndex = (int)Math.Min(lineIndex, int.MaxValue) };
    }

    public IReadOnlyList<LogLine> GetPage(long startLine, int count)
    {
        var result = new List<LogLine>(count);
        long max = _indexedCount;
        for (long i = startLine; i < startLine + count && i < max; i++)
        {
            var line = GetLine(i);
            if (line != null) result.Add(line.Value);
        }
        return result;
    }

    public string? GetRawLine(long lineIndex)
    {
        var count = _indexedCount;
        if (lineIndex < 0 || lineIndex >= count) return null;

        var idx = Volatile.Read(ref _index);
        if (lineIndex >= idx.Length) return null;

        var entry = idx[lineIndex];
        if (entry.SourceIndex >= _sourceRawLines.Count) return null;

        var raws = _sourceRawLines[entry.SourceIndex];
        return entry.LineIndex < raws.Count ? raws[entry.LineIndex] : null;
    }

    // ── IMergedLogProvider ────────────────────────────────────────

    public (string Tag, string TagColorHex) GetSourceInfo(long mergedLineIndex)
    {
        var count = _indexedCount;
        if (mergedLineIndex < 0 || mergedLineIndex >= count)
            return ("?", "#808080");

        var idx = Volatile.Read(ref _index);
        if (mergedLineIndex >= idx.Length)
            return ("?", "#808080");

        var entry = idx[mergedLineIndex];
        if (entry.SourceIndex >= _sources.Count)
            return ("?", "#808080");

        var src = _sources[entry.SourceIndex];
        return (src.Tag, src.TagColorHex);
    }

    public int MaxTagLength { get; private set; }

    public void ScrollToTimestamp(DateTime target, Action<long> onFound)
    {
        long targetTicks = target.Ticks;
        long lo = 0, hi = _indexedCount - 1, best = 0;

        while (lo <= hi)
        {
            long mid = lo + (hi - lo) / 2;
            long midTicks = GetTicksAt(mid);

            if (midTicks < targetTicks) { best = mid; lo = mid + 1; }
            else if (midTicks > targetTicks) { hi = mid - 1; }
            else { best = mid; break; }
        }

        onFound(best);
    }

    // ── Source management ─────────────────────────────────────────

    /// <summary>
    /// Registers a source. Returns the source index for use with
    /// <see cref="PushHistory"/> and <see cref="AppendLines"/>.
    /// The engine takes ownership of the streamer and will dispose it.
    /// </summary>
    public int AddSource(LogStreamer streamer, string tag, string tagColorHex, int priority)
    {
        int idx = _sources.Count;
        _sources.Add(new SourceInfo(tag, tagColorHex, priority));
        _sourceLines.Add([]);
        _sourceRawLines.Add([]);
        _streamers.Add(streamer);
        MaxTagLength = Math.Max(MaxTagLength, tag.Length);
        return idx;
    }

    public int SourceCount => _sources.Count;

    // ── Data push ─────────────────────────────────────────────────

    /// <summary>
    /// Pushes initial history lines for a source. Call before <see cref="Build"/>.
    /// </summary>
    public void PushHistory(int sourceIndex, IReadOnlyList<string> rawLines)
    {
        var lines = _sourceLines[sourceIndex];
        var raws = _sourceRawLines[sourceIndex];
        foreach (var raw in rawLines)
        {
            lines.Add(LogLineParser.Parse(raw, lines.Count));
            raws.Add(raw);
        }
    }

    /// <summary>
    /// Pushes new tail lines from a source. Called from LogStreamer.LinesReceived.
    /// Thread-safe — may be called from any thread.
    /// </summary>
    public void AppendLines(int sourceIndex, IReadOnlyList<string> rawLines)
    {
        lock (_appendLock)
        {
            if (_disposed || _isIndexing || rawLines.Count == 0) return;
            var lines = _sourceLines[sourceIndex];
            var raws = _sourceRawLines[sourceIndex];
            var src = _sources[sourceIndex];

            var newRefs = new List<(MergedLineRef Ref, long Ticks)>(rawLines.Count);
            long lastTicks = lines.Count > 0 ? (lines[^1].Timestamp?.Ticks ?? 0) : 0;

            foreach (var raw in rawLines)
            {
                int lineIdx = lines.Count;
                var parsed = LogLineParser.Parse(raw, lineIdx);
                lines.Add(parsed);
                raws.Add(raw);

                long ticks = parsed.Timestamp?.Ticks ?? lastTicks;
                lastTicks = ticks;
                newRefs.Add((new MergedLineRef((byte)sourceIndex, lineIdx), ticks));
            }

            // Sort new lines by timestamp, priority, line index
            newRefs.Sort((a, b) =>
            {
                int c = a.Ticks.CompareTo(b.Ticks);
                if (c != 0) return c;
                c = _sources[a.Ref.SourceIndex].Priority.CompareTo(
                    _sources[b.Ref.SourceIndex].Priority);
                if (c != 0) return c;
                return a.Ref.LineIndex.CompareTo(b.Ref.LineIndex);
            });

            // Grow index array if needed
            int needed = _indexedCount + newRefs.Count;
            if (needed > _index.Length)
            {
                var grown = new MergedLineRef[Math.Max(needed, _index.Length * 2)];
                Array.Copy(_index, grown, _indexedCount);
                Volatile.Write(ref _index, grown);
            }

            int pos = _indexedCount;
            foreach (var (r, _) in newRefs)
                _index[pos++] = r;

            _indexedCount = pos;
        }

        LinesAppended?.Invoke(rawLines.Count);
    }

    // ── Build (initial k-way merge of history) ────────────────────

    /// <summary>
    /// K-way merges all history lines pushed via <see cref="PushHistory"/>.
    /// Call once after all sources are loaded, then call <see cref="StartTailing"/>
    /// to begin receiving live updates.
    /// </summary>
    public void Build()
    {
        _isIndexing = true;
        _progress = 0;
        _indexedCount = 0;

        try
        {
            long totalLines = 0;
            foreach (var lines in _sourceLines)
                totalLines += lines.Count;

            if (totalLines == 0)
            {
                _index = [];
                return;
            }

            _index = new MergedLineRef[totalLines];

            var heap = new PriorityQueue<(byte SrcIdx, int LineIdx), (long Ticks, int Priority, int LineIdx)>(_sources.Count);
            var lastTicks = new long[_sources.Count];

            for (int s = 0; s < _sources.Count; s++)
            {
                if (_sourceLines[s].Count == 0) continue;
                long ticks = ExtractTicks(s, 0, ref lastTicks[s]);
                heap.Enqueue(((byte)s, 0), (ticks, _sources[s].Priority, 0));
            }

            int built = 0;
            while (heap.Count > 0)
            {
                var (srcIdx, lineIdx) = heap.Dequeue();
                _index[built] = new MergedLineRef(srcIdx, lineIdx);
                built++;
                _indexedCount = built;

                int nextLine = lineIdx + 1;
                if (nextLine < _sourceLines[srcIdx].Count)
                {
                    long ticks = ExtractTicks(srcIdx, nextLine, ref lastTicks[srcIdx]);
                    heap.Enqueue((srcIdx, nextLine), (ticks, _sources[srcIdx].Priority, nextLine));
                }

                if (built % 10_000 == 0)
                {
                    _progress = (double)built / totalLines;
                    IndexingProgressChanged?.Invoke(built);
                }
            }

            _progress = 1.0;
            IndexingProgressChanged?.Invoke(built);
        }
        finally
        {
            _isIndexing = false;
            IndexingCompleted?.Invoke();
        }
    }

    /// <summary>
    /// Wires up all LogStreamers to push new lines into the engine and starts tailing.
    /// Call after <see cref="Build"/>.
    /// </summary>
    public void StartTailing()
    {
        _lineHandlers.Clear();
        for (int s = 0; s < _streamers.Count; s++)
        {
            int srcIdx = s; // capture for closure
            Action<IReadOnlyList<string>> handler = lines => AppendLines(srcIdx, lines);
            _lineHandlers.Add(handler);
            _streamers[s].LinesReceived += handler;
            _streamers[s].StartTailing();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private long ExtractTicks(int sourceIndex, int lineIndex, ref long lastTicks)
    {
        var raw = _sourceRawLines[sourceIndex][lineIndex];
        if (raw.Length >= 23 &&
            DateTime.TryParseExact(raw.AsSpan(0, 23), "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
        {
            lastTicks = ts.Ticks;
            return ts.Ticks;
        }
        return lastTicks;
    }

    private long GetTicksAt(long mergedIdx)
    {
        var entry = _index[mergedIdx];
        if (entry.SourceIndex >= _sourceLines.Count) return long.MaxValue;

        var lines = _sourceLines[entry.SourceIndex];
        if (entry.LineIndex >= lines.Count) return long.MaxValue;

        return lines[entry.LineIndex].Timestamp?.Ticks ?? long.MaxValue;
    }

    // ── Dispose ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _streamers.Count && i < _lineHandlers.Count; i++)
            _streamers[i].LinesReceived -= _lineHandlers[i];
        _lineHandlers.Clear();

        foreach (var streamer in _streamers)
            streamer.Dispose();

        _streamers.Clear();
        _sourceLines.Clear();
        _sourceRawLines.Clear();
        _sources.Clear();
        _index = [];
        _indexedCount = 0;
    }
}
