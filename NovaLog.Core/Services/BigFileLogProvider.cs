using System.IO.MemoryMappedFiles;
using System.Text;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// IVirtualLogProvider backed by a memory-mapped file and BigFileLineIndex.
/// Reads and parses lines on demand, caching only what the UI needs.
/// </summary>
public sealed class BigFileLogProvider : IVirtualLogProvider
{
    private readonly BigFileLineIndex _index;
    private readonly string _filePath;
    private readonly Encoding _encoding;
    private readonly object _tailReadLock = new();

    private System.Threading.Timer? _tailTimer;
    private bool _disposed;

    // LRU cache for recently decoded lines — accessed from UI thread + timer thread
    private readonly object _cacheLock = new();
    private readonly Dictionary<long, LogLine> _cache = new();
    private readonly LinkedList<long> _cacheOrder = new();
    private const int MaxCacheSize = 2048;
    private const int MaxDisplayLineLength = 10_000;

    public event Action<long>? IndexingProgressChanged;
    public event Action? IndexingCompleted;
    public event Action<long>? LinesAppended;

    public BigFileLogProvider(string filePath, Encoding? encoding = null)
    {
        _filePath = filePath;
        _encoding = encoding ?? Encoding.UTF8;
        _index = new BigFileLineIndex(filePath);
        _index.ProgressChanged += OnIndexProgressChanged;
        _index.Completed += OnIndexCompleted;
        _index.LinesAppended += OnIndexLinesAppended;
    }

    private void OnIndexProgressChanged(long count) => IndexingProgressChanged?.Invoke(count);
    private void OnIndexCompleted() => IndexingCompleted?.Invoke();
    private void OnIndexLinesAppended(long count) => LinesAppended?.Invoke(count);

    public long LineCount => _index.LineCount;
    public bool IsIndexing => !_index.IsComplete;
    public double IndexingProgress => _index.FileLength > 0
        ? (double)_index.IndexedBytes / _index.FileLength
        : 0.0;
    public string FilePath => _filePath;

    /// <summary>
    /// Opens the file and starts indexing. First page is available synchronously after return.
    /// </summary>
    public void Open()
    {
        _index.StartIndexing();

        // Start tail polling (every 250ms)
        _tailTimer = new System.Threading.Timer(_ => _index.TailCheck(), null, 250, 250);
    }

    public LogLine? GetLine(long lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _index.LineCount) return null;

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(lineIndex, out var cached))
                return cached;
        }

        string? raw = ReadRawLine(lineIndex);
        if (raw == null) return null;

        var line = LogLineParser.Parse(raw, (int)Math.Min(lineIndex, int.MaxValue));
        lock (_cacheLock) AddToCache(lineIndex, line);
        return line;
    }

    public IReadOnlyList<LogLine> GetPage(long startLine, int count)
    {
        var result = new List<LogLine>(count);
        long max = _index.LineCount;
        for (long i = startLine; i < startLine + count && i < max; i++)
        {
            var line = GetLine(i);
            if (line != null) result.Add(line.Value);
        }
        return result;
    }

    public string? GetRawLine(long lineIndex)
    {
        return ReadRawLine(lineIndex);
    }

    private string? ReadRawLine(long lineIndex)
    {
        long offset = _index.GetOffset(lineIndex);
        if (offset < 0) return null;

        int byteLen = _index.GetLineByteLength(lineIndex);
        if (byteLen <= 0) return string.Empty;

        // Cap read length for safety
        byteLen = Math.Min(byteLen, 40_000); // UTF-8 worst case (10K chars × 4 bytes)

        byte[] buffer = new byte[byteLen];

        try
        {
            if (offset + byteLen <= _index.OriginalMmfLength && _index.Mmf != null)
            {
                // Fast path: read from MMF
                using var accessor = _index.Mmf.CreateViewAccessor(offset, byteLen, MemoryMappedFileAccess.Read);
                accessor.ReadArray(0, buffer, 0, byteLen);
            }
            else if (_index.BaseStream != null)
            {
                // Tail path: read from FileStream
                lock (_tailReadLock)
                {
                    _index.BaseStream.Seek(offset, SeekOrigin.Begin);
                    int totalRead = 0;
                    while (totalRead < byteLen)
                    {
                        int read = _index.BaseStream.Read(buffer, totalRead, byteLen - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    byteLen = totalRead;
                }
            }
            else
            {
                return null;
            }
        }
        catch (Exception) { return null; }

        // Trim trailing \r\n or \n
        int end = byteLen;
        if (end > 0 && buffer[end - 1] == '\n') end--;
        if (end > 0 && buffer[end - 1] == '\r') end--;

        var text = _encoding.GetString(buffer, 0, end);

        // Truncate very long lines for display
        if (text.Length > MaxDisplayLineLength)
            text = string.Concat(text.AsSpan(0, MaxDisplayLineLength), "...(truncated)");

        return text;
    }

    private void AddToCache(long lineIndex, LogLine line)
    {
        if (_cache.Count >= MaxCacheSize)
        {
            var evict = _cacheOrder.First!.Value;
            _cacheOrder.RemoveFirst();
            _cache.Remove(evict);
        }
        _cache[lineIndex] = line;
        _cacheOrder.AddLast(lineIndex);
    }

    /// <summary>
    /// Binary-searches the indexed lines for the nearest line at or before the target timestamp.
    /// </summary>
    public void ScrollToTimestamp(DateTime target, Action<long> onFound)
    {
        long count = _index.LineCount;
        if (count == 0) { onFound(0); return; }

        long targetTicks = target.Ticks;
        long lo = 0, hi = count - 1, best = 0;

        while (lo <= hi)
        {
            long mid = lo + (hi - lo) / 2;
            var line = GetLine(mid);
            long midTicks = line?.Timestamp?.Ticks ?? long.MaxValue;

            if (line?.Timestamp == null)
            {
                // Probe backward to find a timestamped line
                long probe = mid - 1;
                while (probe >= lo && GetLine(probe)?.Timestamp == null) probe--;
                if (probe >= lo)
                {
                    midTicks = GetLine(probe)!.Value.Timestamp!.Value.Ticks;
                    mid = probe;
                }
                else
                {
                    lo = mid + 1;
                    continue;
                }
            }

            if (midTicks < targetTicks) { best = mid; lo = mid + 1; }
            else if (midTicks > targetTicks) { hi = mid - 1; }
            else { best = mid; break; }
        }

        onFound(best);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tailTimer?.Dispose();
        _index.ProgressChanged -= OnIndexProgressChanged;
        _index.Completed -= OnIndexCompleted;
        _index.LinesAppended -= OnIndexLinesAppended;
        _index.Dispose();
    }
}
