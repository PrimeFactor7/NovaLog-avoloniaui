using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

public enum NavigationCategory
{
    Error,
    Warn,
    SearchHit,
    Bookmark
}

/// <summary>
/// Manages sorted line-index collections for errors, warns, search hits, and bookmarks.
/// Supports O(log N) binary-search jumping and density bucketing for the minimap heatmap.
/// All public methods are thread-safe.
/// </summary>
public sealed class NavigationIndex
{
    private readonly object _lock = new();

    private List<long> _errors = [];
    private List<long> _warns = [];
    private List<long> _searchHits = [];
    private List<long> _bookmarks = [];

    /// <summary>Tracks how many lines the background level scan has processed so far.</summary>
    public long LevelScanUpTo { get; set; }

    /// <summary>Fires when any index collection changes. May fire from background threads.</summary>
    public event Action? IndicesChanged;

    // ── Getters ──────────────────────────────────────────────────────

    public int ErrorCount { get { lock (_lock) return _errors.Count; } }
    public int WarnCount { get { lock (_lock) return _warns.Count; } }
    public int SearchHitCount { get { lock (_lock) return _searchHits.Count; } }
    public int BookmarkCount { get { lock (_lock) return _bookmarks.Count; } }

    public IReadOnlyList<long> Bookmarks
    {
        get { lock (_lock) return _bookmarks.ToList(); }
    }

    /// <summary>Returns a snapshot of all indices for the given category.</summary>
    public IReadOnlyList<long> GetAll(NavigationCategory category)
    {
        lock (_lock) return GetList(category).ToList();
    }

    // ── Mutation ─────────────────────────────────────────────────────

    public void SetErrors(List<long> sorted)
    {
        lock (_lock) _errors = sorted;
        IndicesChanged?.Invoke();
    }

    public void SetWarns(List<long> sorted)
    {
        lock (_lock) _warns = sorted;
        IndicesChanged?.Invoke();
    }

    public void SetSearchHits(List<long> sorted)
    {
        lock (_lock) _searchHits = sorted;
        IndicesChanged?.Invoke();
    }

    public void SetBookmarks(List<long> sorted)
    {
        lock (_lock) _bookmarks = sorted;
        IndicesChanged?.Invoke();
    }

    /// <summary>Appends new error/warn indices from incremental tail scans.</summary>
    public void AppendLevelResults(List<long> newErrors, List<long> newWarns)
    {
        lock (_lock)
        {
            if (newErrors.Count > 0) _errors.AddRange(newErrors);
            if (newWarns.Count > 0) _warns.AddRange(newWarns);
        }
        if (newErrors.Count > 0 || newWarns.Count > 0)
            IndicesChanged?.Invoke();
    }

    /// <summary>Toggles a bookmark. Returns true if added, false if removed.</summary>
    public bool ToggleBookmark(long lineIndex)
    {
        bool added;
        lock (_lock)
        {
            int idx = _bookmarks.BinarySearch(lineIndex);
            if (idx >= 0)
            {
                _bookmarks.RemoveAt(idx);
                added = false;
            }
            else
            {
                _bookmarks.Insert(~idx, lineIndex);
                added = true;
            }
        }
        IndicesChanged?.Invoke();
        return added;
    }

    public bool IsBookmarked(long lineIndex)
    {
        lock (_lock)
            return _bookmarks.BinarySearch(lineIndex) >= 0;
    }

    // ── Navigation ──────────────────────────────────────────────────

    private List<long> GetList(NavigationCategory cat) => cat switch
    {
        NavigationCategory.Error => _errors,
        NavigationCategory.Warn => _warns,
        NavigationCategory.SearchHit => _searchHits,
        NavigationCategory.Bookmark => _bookmarks,
        _ => _errors
    };

    /// <summary>
    /// Finds the next (or previous) index in the given category relative to currentLine.
    /// Returns -1 if the collection is empty. Wraps around at boundaries.
    /// </summary>
    public long GetNext(NavigationCategory category, long currentLine, bool forward)
    {
        lock (_lock)
        {
            var list = GetList(category);
            if (list.Count == 0) return -1;

            int idx = list.BinarySearch(currentLine);

            if (forward)
            {
                int next = idx >= 0 ? idx + 1 : ~idx;
                return next < list.Count ? list[next] : list[0]; // wrap
            }
            else
            {
                int prev = idx >= 0 ? idx - 1 : ~idx - 1;
                return prev >= 0 ? list[prev] : list[^1]; // wrap
            }
        }
    }

    /// <summary>
    /// Returns (1-based current index, total count) for status display like "Error 3 of 42".
    /// CurrentIndex is 0 if currentLine is not exactly on a match.
    /// </summary>
    public (int CurrentIndex, int TotalCount) GetPositionInfo(NavigationCategory category, long currentLine)
    {
        lock (_lock)
        {
            var list = GetList(category);
            if (list.Count == 0) return (0, 0);

            int idx = list.BinarySearch(currentLine);
            if (idx >= 0)
                return (idx + 1, list.Count); // 1-based

            // Not exactly on a match — find nearest previous
            int insertPoint = ~idx;
            return insertPoint > 0 ? (insertPoint, list.Count) : (0, list.Count);
        }
    }

    // ── Density bucketing for minimap ───────────────────────────────

    /// <summary>
    /// Distributes indices into buckets for minimap heatmap rendering.
    /// Returns an int[] of length bucketCount where each element is the count of items in that bucket.
    /// </summary>
    public int[] ComputeDensityBuckets(NavigationCategory category, long totalLines, int bucketCount)
    {
        var result = new int[bucketCount];
        if (totalLines <= 0 || bucketCount <= 0) return result;

        lock (_lock)
        {
            var list = GetList(category);
            if (list.Count == 0) return result;

            double linesPerBucket = (double)totalLines / bucketCount;
            foreach (var lineIdx in list)
            {
                int bucket = (int)(lineIdx / linesPerBucket);
                if (bucket >= bucketCount) bucket = bucketCount - 1;
                if (bucket < 0) bucket = 0;
                result[bucket]++;
            }
        }
        return result;
    }

    // ── Level scanning ──────────────────────────────────────────────

    /// <summary>
    /// Scans an in-memory line list for Error and Warn levels.
    /// Returns (errors, warns) as sorted lists.
    /// </summary>
    public static (List<long> Errors, List<long> Warns) ScanLevels(IReadOnlyList<LogLine> lines)
    {
        var errors = new List<long>();
        var warns = new List<long>();

        for (int i = 0; i < lines.Count; i++)
        {
            var level = lines[i].Level;
            if (level == LogLevel.Error || level == LogLevel.Fatal)
                errors.Add(lines[i].GlobalIndex);
            else if (level == LogLevel.Warn)
                warns.Add(lines[i].GlobalIndex);
        }

        return (errors, warns);
    }

    /// <summary>
    /// Scans a BigFile provider for Error and Warn levels using raw line text.
    /// Uses Span.Contains on the level column (chars 24-34) for speed.
    /// </summary>
    public static (List<long> Errors, List<long> Warns) ScanLevels(
        IVirtualLogProvider provider, long startLine, long endLine, CancellationToken token)
    {
        var errors = new List<long>();
        var warns = new List<long>();

        for (long i = startLine; i < endLine; i++)
        {
            token.ThrowIfCancellationRequested();

            var raw = provider.GetRawLine(i);
            if (raw == null || raw.Length < 25) continue;

            // Level column is roughly chars 24-34 in standard log format
            var levelSpan = raw.AsSpan(24, Math.Min(10, raw.Length - 24));

            if (levelSpan.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                levelSpan.Contains("fatal", StringComparison.OrdinalIgnoreCase))
                errors.Add(i);
            else if (levelSpan.Contains("warn", StringComparison.OrdinalIgnoreCase))
                warns.Add(i);
        }

        return (errors, warns);
    }
}
