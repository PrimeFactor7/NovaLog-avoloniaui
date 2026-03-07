using System.Diagnostics;
using System.Text.RegularExpressions;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

// ── Shared types ─────────────────────────────────────────────────────

/// <summary>Fired when a rotation strategy detects the active file has changed.</summary>
public sealed class RotationEventArgs(
    string? previousFile,
    string newFile,
    string? prefix) : EventArgs
{
    public string? PreviousFile { get; } = previousFile;
    public string NewFile { get; } = newFile;
    public string? Prefix { get; } = prefix;
}

/// <summary>
/// Common interface for log rotation detection. Each strategy monitors a directory
/// and fires <see cref="RotationDetected"/> when the active log file changes.
/// </summary>
public interface IRotationStrategy : IDisposable
{
    string Name { get; }
    string Description { get; }
    event EventHandler<RotationEventArgs>? RotationDetected;
    void Start();
    void Stop();
}

// ═════════════════════════════════════════════════════════════════════
// Strategy 1: AuditJson — watch *-audit.json for changes
// ═════════════════════════════════════════════════════════════════════

/// <summary>
/// Watches for writes to *-audit.json files. When an audit file changes, re-parses
/// it and checks whether the newest entry differs from the previous snapshot.
/// Best for: Winston-managed logs with audit JSON enabled.
/// </summary>
public sealed class AuditJsonStrategy : IRotationStrategy
{
    private readonly AuditLogManager _manager;
    private readonly FileSystemWatcher _watcher;
    private readonly Dictionary<string, string> _snapshot = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string Name => "AuditJson";
    public string Description => "Watch *-audit.json for rotation entries (recommended for Winston)";
    public event EventHandler<RotationEventArgs>? RotationDetected;

    public AuditJsonStrategy(AuditLogManager manager)
    {
        _manager = manager;
        _watcher = new FileSystemWatcher(manager.LogsDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = false
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        TakeSnapshot();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (!_disposed) _watcher.EnableRaisingEvents = false;
    }

    public void TakeSnapshot()
    {
        _snapshot.Clear();
        foreach (var kvp in _manager.AuditLogs)
        {
            var active = _manager.GetActiveFile(kvp.Key);
            if (active != null)
                _snapshot[kvp.Key] = active.ResolvedPath;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name == null) return;
        if (!e.Name.EndsWith("-audit.json", StringComparison.OrdinalIgnoreCase)) return;

        Thread.Sleep(50); // let the writer finish flushing

        AuditLog? audit;
        try { audit = _manager.RefreshSingle(e.FullPath); }
        catch { return; }

        if (audit == null || audit.Files.Count == 0) return;

        var newActive = audit.Files[^1].ResolvedPath;
        _snapshot.TryGetValue(e.FullPath, out var prev);

        if (!string.Equals(newActive, prev, StringComparison.OrdinalIgnoreCase))
        {
            _snapshot[e.FullPath] = newActive;
            RotationDetected?.Invoke(this, new RotationEventArgs(
                prev, newActive, _manager.GetLogPrefix(e.FullPath)));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}

// ═════════════════════════════════════════════════════════════════════
// Strategy 2: DirectoryScan — poll for newest file matching pattern
// ═════════════════════════════════════════════════════════════════════

/// <summary>
/// Periodically scans the directory for the newest file matching each known prefix.
/// Detects rotation when a newer file appears (by filename timestamp).
/// Best for: Directories without audit JSON, or any rolling log scheme.
/// </summary>
public sealed class DirectoryScanStrategy : IRotationStrategy
{
    private readonly string _directory;
    private readonly int _intervalMs;
    private readonly Dictionary<string, string> _currentFiles = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _timer;
    private bool _disposed;

    // Matches {prefix}-{yyyy}-{MM}-{dd}-{HH}.{log|gz}
    private static readonly Regex FilePattern = new(
        @"^(?<prefix>.+?)-(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})-(?<hour>\d{2})\.(?<ext>log|gz)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "DirectoryScan";
    public string Description => "Poll directory for newest log file by timestamp pattern (no audit JSON needed)";
    public event EventHandler<RotationEventArgs>? RotationDetected;

    public DirectoryScanStrategy(string directory, int intervalMs = 2000)
    {
        _directory = directory;
        _intervalMs = intervalMs;
    }

    /// <summary>Seed the scanner with known prefix→file mappings (e.g. from initial load).</summary>
    public void SeedFiles(IEnumerable<(string Prefix, string FilePath)> files)
    {
        foreach (var (prefix, path) in files)
            _currentFiles[prefix] = path;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer = new System.Threading.Timer(Scan, null, _intervalMs, _intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Scan(object? state)
    {
        if (_disposed) return;
        try
        {
            var logFiles = Directory.GetFiles(_directory, "*.log");
            var grouped = new Dictionary<string, (string Path, DateTime Ts)>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in logFiles)
            {
                var m = FilePattern.Match(Path.GetFileName(file));
                if (!m.Success) continue;

                var prefix = m.Groups["prefix"].Value;
                var ts = new DateTime(
                    int.Parse(m.Groups["year"].Value),
                    int.Parse(m.Groups["month"].Value),
                    int.Parse(m.Groups["day"].Value),
                    int.Parse(m.Groups["hour"].Value),
                    0, 0, DateTimeKind.Local);

                if (!grouped.TryGetValue(prefix, out var existing) || ts > existing.Ts)
                    grouped[prefix] = (file, ts);
            }

            foreach (var (prefix, (newestPath, _)) in grouped)
            {
                _currentFiles.TryGetValue(prefix, out var prev);
                if (!string.Equals(newestPath, prev, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFiles[prefix] = newestPath;
                    if (prev != null) // don't fire on first discovery
                        RotationDetected?.Invoke(this, new RotationEventArgs(prev, newestPath, prefix));
                }
            }
        }
        catch (IOException ex) { Debug.WriteLine($"Rotation strategy IO (directory): {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

// ═════════════════════════════════════════════════════════════════════
// Strategy 3: FileCreation — FSW watching for new *.log files
// ═════════════════════════════════════════════════════════════════════

/// <summary>
/// Uses FileSystemWatcher to react to new .log files being created in the directory.
/// When a new file appears that matches the prefix pattern and is newer than the current,
/// fires rotation. Also handles the case where the current file disappears (compressed to .gz).
/// Best for: Real-time detection without polling overhead.
/// </summary>
public sealed class FileCreationStrategy : IRotationStrategy
{
    private readonly string _directory;
    private readonly FileSystemWatcher _watcher;
    private readonly Dictionary<string, string> _currentFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private static readonly Regex FilePattern = new(
        @"^(?<prefix>.+?)-(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})-(?<hour>\d{2})\.(?<ext>log|gz)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "FileCreation";
    public string Description => "Watch for new .log files appearing in the directory (FSW-based, instant detection)";
    public event EventHandler<RotationEventArgs>? RotationDetected;

    public FileCreationStrategy(string directory)
    {
        _directory = directory;
        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            Filter = "*.log",
            IncludeSubdirectories = false,
            EnableRaisingEvents = false
        };
        _watcher.Created += OnCreated;
        _watcher.Renamed += OnRenamed;
    }

    /// <summary>Seed with known prefix→file mappings.</summary>
    public void SeedFiles(IEnumerable<(string Prefix, string FilePath)> files)
    {
        foreach (var (prefix, path) in files)
            _currentFiles[prefix] = path;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (!_disposed) _watcher.EnableRaisingEvents = false;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (e.Name == null) return;
        var m = FilePattern.Match(e.Name);
        if (!m.Success || m.Groups["ext"].Value.Equals("gz", StringComparison.OrdinalIgnoreCase))
            return;

        var prefix = m.Groups["prefix"].Value;
        _currentFiles.TryGetValue(prefix, out var prev);

        if (!string.Equals(e.FullPath, prev, StringComparison.OrdinalIgnoreCase))
        {
            // Small delay so the creating process can finish
            Thread.Sleep(100);
            _currentFiles[prefix] = e.FullPath;
            if (prev != null)
                RotationDetected?.Invoke(this, new RotationEventArgs(prev, e.FullPath, prefix));
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (e.OldName == null || e.Name == null) return;

        var oldMatch = FilePattern.Match(e.OldName);
        if (!oldMatch.Success) return;

        var prefix = oldMatch.Groups["prefix"].Value;
        if (!_currentFiles.TryGetValue(prefix, out var current)) return;
        if (!string.Equals(current, e.OldFullPath, StringComparison.OrdinalIgnoreCase)) return;

        Thread.Sleep(100);
        try
        {
            var newest = Directory.GetFiles(_directory, $"{prefix}-????-??-??-??.log")
                .OrderDescending()
                .FirstOrDefault();

            if (newest != null && !string.Equals(newest, e.OldFullPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFiles[prefix] = newest;
                RotationDetected?.Invoke(this, new RotationEventArgs(e.OldFullPath, newest, prefix));
            }
        }
        catch (IOException ex) { Debug.WriteLine($"Rotation strategy IO: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
