using System.IO.Compression;

namespace NovaLog.Core.Services;

/// <summary>
/// Tails the active log file (via polling) and emits new lines.
/// On rotation, pivots to the new file seamlessly.
/// All file I/O uses FileShare.ReadWrite so the writing process isn't blocked.
/// </summary>
public sealed class LogStreamer : IDisposable
{
    private readonly AuditLogManager? _manager;
    private readonly string? _auditPath;
    private readonly List<string>? _directFiles;
    private readonly object _lock = new();

    private System.Threading.Timer? _pollTimer;
    private FileStream? _stream;
    private StreamReader? _reader;
    private string? _currentFile;
    private bool _disposed;

    /// <summary>Fires on a thread-pool thread with each batch of new raw lines.</summary>
    public event Action<IReadOnlyList<string>>? LinesReceived;

    /// <summary>Create a streamer backed by an audit JSON file.</summary>
    public LogStreamer(AuditLogManager manager, string auditPath)
    {
        _manager = manager;
        _auditPath = auditPath;
    }

    /// <summary>Create a streamer backed by a direct list of log file paths (oldest → newest).</summary>
    public LogStreamer(List<string> filePaths)
    {
        _directFiles = filePaths;
    }

    public string? CurrentFile => _currentFile;

    // ── Initial load ────────────────────────────────────────────────

    /// <summary>
    /// Reads every line from every file in order (oldest → newest).
    /// Inserts a sentinel separator string between files: "$$FILE_SEP::{filename}::{created}"
    /// </summary>
    public List<string> LoadHistory()
    {
        var filePaths = GetOrderedFiles();
        var lines = new List<string>();
        bool first = true;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (!first)
                {
                    var created = File.GetCreationTime(path);
                    lines.Add($"$$FILE_SEP::{Path.GetFileName(path)}::{created:yyyy-MM-dd HH:mm:ss}");
                }
                first = false;

                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Stream source = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? new GZipStream(fs, CompressionMode.Decompress)
                    : fs;
                using var sr = new StreamReader(source);
                while (sr.ReadLine() is { } line)
                    if (line.Length > 0) lines.Add(line);
                if (source != fs) source.Dispose();
            }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"[LogStreamer] Skip file: {path}: {ex.Message}"); }
        }
        return lines;
    }

    // ── Tailing ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens the active file at EOF and starts polling for appended lines.
    /// </summary>
    public void StartTailing()
    {
        var activePath = GetActiveFilePath();
        if (activePath != null)
            OpenFile(activePath, seekToEnd: true);

        _pollTimer = new System.Threading.Timer(Poll, null, 100, 100);
    }

    /// <summary>
    /// Called by the rotation handler: drains the old file, opens the new one from the start.
    /// </summary>
    public void PivotToNewFile(string newFilePath)
    {
        lock (_lock)
        {
            Drain();            // flush remaining lines from old file
            CloseStream();

            if (File.Exists(newFilePath))
                OpenFile(newFilePath, seekToEnd: false);
        }
    }

    // ── Internals ───────────────────────────────────────────────────

    private void Poll(object? state)
    {
        if (_disposed) return;
        lock (_lock) { Drain(); }
    }

    private void Drain()
    {
        if (_reader == null) return;

        var batch = new List<string>();
        while (_reader.ReadLine() is { } line)
            if (line.Length > 0) batch.Add(line);

        if (batch.Count > 0)
            LinesReceived?.Invoke(batch);
    }

    private void OpenFile(string path, bool seekToEnd)
    {
        CloseStream();
        if (!File.Exists(path)) return; // file may have been rotated away
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _reader = new StreamReader(_stream);
        _currentFile = path;
        if (seekToEnd)
            _stream.Seek(0, SeekOrigin.End);
    }

    private void CloseStream()
    {
        _reader?.Dispose();
        _stream?.Dispose();
        _reader = null;
        _stream = null;
    }

    private IReadOnlyList<string> GetOrderedFiles()
    {
        if (_directFiles != null)
            return _directFiles;
        return _manager!.GetFilesInOrder(_auditPath!)
            .Select(e => e.ResolvedPath).ToList();
    }

    private string? GetActiveFilePath()
    {
        if (_directFiles is { Count: > 0 })
            return _directFiles[^1]; // newest file
        return _manager!.GetActiveFile(_auditPath!)?.ResolvedPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        lock (_lock) { CloseStream(); }
    }
}
