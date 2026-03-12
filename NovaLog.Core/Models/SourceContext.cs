using NovaLog.Core.Services;

namespace NovaLog.Core.Models;

/// <summary>
/// Shared provider wrapper for a loaded source. Decoupled from UI.
/// Multiple LogPanes can share one SourceContext via ref-counting.
/// </summary>
public sealed class SourceContext : IDisposable
{
    public required string TabKey;
    public required string SourceId;
    public required string DisplayName;
    public string AuditFilePath = "";
    public LogStreamer? Streamer;
    public BigFileLogProvider? BigFileProvider;
    public ChronoMergeEngine? MergeEngine;
    public CancellationTokenSource? LevelScanCts;
    public NavigationIndex NavIndex = new();
    private int _refCount;
    public int RefCount => Volatile.Read(ref _refCount);
    public int IncrementRef() => Interlocked.Increment(ref _refCount);
    public int DecrementRef() => Interlocked.Decrement(ref _refCount);

    public bool IsBigFile => BigFileProvider != null;
    public bool IsMerge => MergeEngine != null;

    /// <summary>Gets the IVirtualLogProvider (BigFile or Merge), or null for in-memory.</summary>
    public IVirtualLogProvider? VirtualProvider =>
        (IVirtualLogProvider?)BigFileProvider ?? MergeEngine;

    private List<Action>? _disposeActions;

    /// <summary>Register an action to run when this context is disposed (e.g. unsubscribe event handlers).</summary>
    public void RegisterDisposeAction(Action action)
    {
        _disposeActions ??= [];
        _disposeActions.Add(action);
    }

    public void Dispose()
    {
        if (_disposeActions != null)
        {
            foreach (var a in _disposeActions)
                a();
            _disposeActions.Clear();
        }
        try { LevelScanCts?.Cancel(); } catch (ObjectDisposedException) { }
        LevelScanCts?.Dispose();
        Streamer?.Dispose();
        BigFileProvider?.Dispose();
        MergeEngine?.Dispose();
    }
}
