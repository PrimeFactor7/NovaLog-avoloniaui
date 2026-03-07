namespace NovaLog.Core.Services;

/// <summary>
/// Broadcasts a timestamp from the actively-scrolled pane to all sync-linked panes.
/// Uses a 100ms debounce timer so rapid scroll events coalesce into one broadcast.
/// UI-agnostic: uses System.Threading.Timer instead of WinForms Timer.
/// The sender is typed as object — the UI layer casts to its pane type.
/// </summary>
public sealed class GlobalClockService : IDisposable
{
    private Timer? _debounceTimer;
    private DateTime _pendingTime;
    private object? _pendingSender;

    /// <summary>
    /// Fires after debounce completes. Args: (timestamp, senderPane).
    /// </summary>
    public event Action<DateTime, object>? TimeChanged;

    public void BroadcastTime(DateTime time, object sender)
    {
        _pendingTime = time;
        _pendingSender = sender;

        if (_debounceTimer == null)
        {
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        // Reset the 100ms debounce
        _debounceTimer.Change(100, Timeout.Infinite);
    }

    public void NotifyTimeChanged(DateTime time, object sender)
    {
        BroadcastTime(time, sender);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_pendingSender != null)
            TimeChanged?.Invoke(_pendingTime, _pendingSender);
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}
