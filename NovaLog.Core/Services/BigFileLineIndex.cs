using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace NovaLog.Core.Services;

/// <summary>
/// Builds and maintains a byte-offset index of every line start position in a file.
/// Uses Span&lt;byte&gt; scanning over memory-mapped views for maximum throughput.
/// Thread-safe: the offset list is append-only and reads use lock-protected access.
/// </summary>
public sealed class BigFileLineIndex : IDisposable
{
    private readonly List<long> _offsets = new(1024 * 1024); // pre-alloc 1M entries (~8MB)
    private readonly object _lock = new();
    private readonly string _filePath;

    private MemoryMappedFile? _mmf;
    private FileStream? _mmfStream;
    private long _fileLength;
    private long _indexedUpTo;
    private long _originalMmfLength;
    private volatile bool _indexingComplete;
    private volatile bool _disposed;
    private CancellationTokenSource? _cts;
    private Task? _indexTask;

    private const int ViewChunkSize = 64 * 1024 * 1024; // 64MB chunks for background indexing
    private const int FirstChunkSize = 2 * 1024 * 1024; // 2MB for instant display (~10K lines)
    private const int ProgressIntervalMs = 100;

    /// <summary>Fires with current line count, roughly every 100ms during indexing.</summary>
    public event Action<long>? ProgressChanged;

    /// <summary>Fires when initial indexing finishes.</summary>
    public event Action? Completed;

    /// <summary>Fires when tail scanning finds new lines. Argument = new total line count.</summary>
    public event Action<long>? LinesAppended;

    public BigFileLineIndex(string filePath)
    {
        _filePath = filePath;
    }

    public long LineCount
    {
        get { lock (_lock) return _offsets.Count; }
    }

    public bool IsComplete => _indexingComplete;
    public long FileLength => Volatile.Read(ref _fileLength);
    public long IndexedBytes => Volatile.Read(ref _indexedUpTo);
    public long OriginalMmfLength => _originalMmfLength;

    /// <summary>The underlying MMF for reading line data. Null before StartIndexing().</summary>
    public MemoryMappedFile? Mmf => _mmf;

    /// <summary>The underlying FileStream for reading tail data beyond MMF. Null before StartIndexing().</summary>
    public FileStream? BaseStream => _mmfStream;

    /// <summary>Gets the byte offset for line at the given index. Returns -1 if out of range.</summary>
    public long GetOffset(long lineIndex)
    {
        lock (_lock)
        {
            if (lineIndex < 0 || lineIndex >= _offsets.Count) return -1;
            return _offsets[(int)lineIndex];
        }
    }

    /// <summary>
    /// Gets the byte length of the line at the given index (including any line endings).
    /// Caller should trim \r\n during decode.
    /// </summary>
    public int GetLineByteLength(long lineIndex)
    {
        lock (_lock)
        {
            if (lineIndex < 0 || lineIndex >= _offsets.Count) return 0;
            long start = _offsets[(int)lineIndex];
            long end = (lineIndex + 1 < _offsets.Count)
                ? _offsets[(int)(lineIndex + 1)]
                : Volatile.Read(ref _fileLength);
            return (int)Math.Min(end - start, int.MaxValue);
        }
    }

    /// <summary>
    /// Opens the file (MMF + FileShare.ReadWrite) and starts indexing.
    /// Indexes the first chunk synchronously for instant-open, then continues in background.
    /// </summary>
    public void StartIndexing()
    {
        _mmfStream = new FileStream(
            _filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _fileLength = _mmfStream.Length;
        _originalMmfLength = _fileLength;

        if (_fileLength > 0)
        {
            _mmf = MemoryMappedFile.CreateFromFile(
                _mmfStream, mapName: null, capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true);
        }

        // First line always starts at byte 0
        _offsets.Add(0);

        // Index only a small first chunk synchronously for truly instant display (<50ms)
        long firstChunkEnd = Math.Min(_fileLength, FirstChunkSize);
        if (firstChunkEnd > 0)
            ScanRange(0, firstChunkEnd);

        _indexedUpTo = firstChunkEnd;

        if (_indexedUpTo >= _fileLength)
        {
            _indexingComplete = true;
            Completed?.Invoke();
            return;
        }

        // Continue indexing in background
        _cts = new CancellationTokenSource();
        _indexTask = Task.Run(() => BackgroundIndex(_cts.Token));
    }

    /// <summary>
    /// Scans a byte range of the file for newlines using MMF view accessors.
    /// </summary>
    private void ScanRange(long startByte, long endByte)
    {
        if (_mmf == null) return;
        long pos = startByte;

        while (pos < endByte)
        {
            long chunkLen = Math.Min(endByte - pos, ViewChunkSize);

            using var accessor = _mmf.CreateViewAccessor(pos, chunkLen, MemoryMappedFileAccess.Read);
            unsafe
            {
                byte* ptr = null;
                accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                try
                {
                    ptr += accessor.PointerOffset;
                    var span = new ReadOnlySpan<byte>(ptr, (int)chunkLen);
                    ScanSpan(span, pos);
                }
                finally
                {
                    accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }

            pos += chunkLen;
        }
    }

    /// <summary>
    /// Scans a Span&lt;byte&gt; for \n bytes. Each \n marks the end of a line;
    /// the byte after \n is the start of the next line.
    /// Uses IndexOf for SIMD-accelerated scanning on .NET 10 (AVX2: 32 bytes/cycle).
    /// </summary>
    private void ScanSpan(ReadOnlySpan<byte> span, long baseOffset)
    {
        int searchStart = 0;
        while (searchStart < span.Length)
        {
            int idx = span[searchStart..].IndexOf((byte)'\n');
            if (idx < 0) break;

            int absoluteIdx = searchStart + idx;
            long lineStart = baseOffset + absoluteIdx + 1;

            if (lineStart < Volatile.Read(ref _fileLength))
            {
                lock (_lock)
                    _offsets.Add(lineStart);
            }

            searchStart = absoluteIdx + 1;
        }
    }

    private void BackgroundIndex(CancellationToken ct)
    {
        long lastProgress = Environment.TickCount64;

        while (_indexedUpTo < _fileLength && !ct.IsCancellationRequested)
        {
            long chunkEnd = Math.Min(_indexedUpTo + ViewChunkSize, _fileLength);
            ScanRange(_indexedUpTo, chunkEnd);
            Volatile.Write(ref _indexedUpTo, chunkEnd);

            long now = Environment.TickCount64;
            if (now - lastProgress >= ProgressIntervalMs)
            {
                lastProgress = now;
                ProgressChanged?.Invoke(LineCount);
            }
        }

        _indexingComplete = true;
        ProgressChanged?.Invoke(LineCount);
        Completed?.Invoke();
    }

    /// <summary>
    /// Checks if the file has grown and indexes new data (live tail support).
    /// Call periodically from a timer.
    /// </summary>
    public void TailCheck()
    {
        if (_disposed) return;

        long newLength;
        try
        {
            var fi = new FileInfo(_filePath);
            newLength = fi.Length;
        }
        catch (IOException) { return; }

        if (newLength <= Volatile.Read(ref _fileLength)) return;

        long oldLength = Volatile.Read(ref _fileLength);
        Volatile.Write(ref _fileLength, newLength);

        long prevLineCount = LineCount;
        ScanTailRange(oldLength, newLength);
        long newLineCount = LineCount;

        if (newLineCount > prevLineCount)
            LinesAppended?.Invoke(newLineCount);
    }

    /// <summary>
    /// Scans new tail bytes via direct FileStream read (since MMF is fixed-size).
    /// </summary>
    private void ScanTailRange(long from, long to)
    {
        const int bufSize = 64 * 1024;
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(from, SeekOrigin.Begin);

            byte[] buf = new byte[bufSize];
            long pos = from;
            while (pos < to)
            {
                int toRead = (int)Math.Min(bufSize, to - pos);
                int read = fs.Read(buf, 0, toRead);
                if (read == 0) break;

                var span = new ReadOnlySpan<byte>(buf, 0, read);
                ScanSpan(span, pos);
                pos += read;
            }
        }
        catch (IOException) { /* file may be temporarily locked */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        try { _indexTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { Debug.WriteLine($"BigFileLineIndex.Dispose: index task wait: {ex.Message}"); }
        _cts?.Dispose();
        _mmf?.Dispose();
        _mmfStream?.Dispose();
    }
}
