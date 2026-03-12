using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Watches a log directory for rotation events using a pluggable <see cref="IRotationStrategy"/>.
/// Marshals events to the captured SynchronizationContext for safe UI access.
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private readonly AuditLogManager _manager;
    private readonly SynchronizationContext? _syncContext;
    private IRotationStrategy? _strategy;
    private bool _disposed;

    /// <summary>Fired when the tail target changes because the active file rotated.</summary>
    public event EventHandler<ActiveFileChangedEventArgs>? ActiveFileChanged;

    public LogWatcher(AuditLogManager manager, SynchronizationContext? syncContext = null)
    {
        _manager = manager;
        _syncContext = syncContext;
    }

    /// <summary>The currently active strategy name, or null.</summary>
    public string? ActiveStrategyName => _strategy?.Name;

    /// <summary>
    /// Sets and starts the rotation strategy. Disposes any previous strategy.
    /// </summary>
    public void UseStrategy(IRotationStrategy strategy)
    {
        if (_strategy != null)
        {
            _strategy.RotationDetected -= OnStrategyRotation;
            _strategy.Stop();
        }

        _strategy = strategy;
        _strategy.RotationDetected += OnStrategyRotation;
    }

    /// <summary>
    /// Creates the named strategy and wires it up. Known names:
    /// "AuditJson", "DirectoryScan", "FileCreation".
    /// </summary>
    public IRotationStrategy CreateStrategy(string name)
    {
        return name switch
        {
            AppConstants.RotationStrategyAuditJson => CreateAuditJsonStrategy(),
            AppConstants.RotationStrategyDirectoryScan => CreateDirectoryScanStrategy(),
            AppConstants.RotationStrategyFileCreation => CreateFileCreationStrategy(),
            _ => throw new ArgumentException($"Unknown strategy: {name}")
        };
    }

    /// <summary>All available strategy names with descriptions.</summary>
    public static IReadOnlyList<(string Name, string Description)> AvailableStrategies { get; } =
    [
        (AppConstants.RotationStrategyAuditJson, "Watch *-audit.json for rotation entries (recommended for Winston)"),
        (AppConstants.RotationStrategyDirectoryScan, "Poll directory for newest log file by timestamp pattern (no audit JSON needed)"),
        (AppConstants.RotationStrategyFileCreation, "Watch for new .log files appearing in the directory (FSW-based, instant detection)")
    ];

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _strategy?.Start();
    }

    public void Stop()
    {
        _strategy?.Stop();
    }

    // ── Strategy factory helpers ──────────────────────────────────────

    private AuditJsonStrategy CreateAuditJsonStrategy()
    {
        var s = new AuditJsonStrategy(_manager);
        return s;
    }

    private DirectoryScanStrategy CreateDirectoryScanStrategy()
    {
        var s = new DirectoryScanStrategy(_manager.LogsDirectory);
        s.SeedFiles(GetCurrentPrefixFiles());
        return s;
    }

    private FileCreationStrategy CreateFileCreationStrategy()
    {
        var s = new FileCreationStrategy(_manager.LogsDirectory);
        s.SeedFiles(GetCurrentPrefixFiles());
        return s;
    }

    private IEnumerable<(string Prefix, string FilePath)> GetCurrentPrefixFiles()
    {
        foreach (var kvp in _manager.AuditLogs)
        {
            var prefix = _manager.GetLogPrefix(kvp.Key);
            var active = _manager.GetActiveFile(kvp.Key);
            if (prefix != null && active != null)
                yield return (prefix, active.ResolvedPath);
        }
    }

    // ── Event routing ────────────────────────────────────────────────

    private void OnStrategyRotation(object? sender, RotationEventArgs e)
    {
        var args = new ActiveFileChangedEventArgs(
            "", e.PreviousFile, e.NewFile, e.Prefix);

        if (_syncContext != null)
            _syncContext.Post(_ => ActiveFileChanged?.Invoke(this, args), null);
        else
            ActiveFileChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _strategy?.Dispose();
    }
}

// ── Event arg types ─────────────────────────────────────────────────

public sealed class AuditUpdatedEventArgs(string auditFilePath, AuditLog? audit) : EventArgs
{
    public string AuditFilePath { get; } = auditFilePath;
    public AuditLog? Audit { get; } = audit;
}

public sealed class ActiveFileChangedEventArgs(
    string auditFilePath,
    string? previousFile,
    string newFile,
    string? logPrefix) : EventArgs
{
    public string AuditFilePath { get; } = auditFilePath;
    public string? PreviousFile { get; } = previousFile;
    public string NewFile { get; } = newFile;
    public string? LogPrefix { get; } = logPrefix;
}
