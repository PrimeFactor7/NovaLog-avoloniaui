using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NovaLog.Avalonia.ViewModels;

public partial class TimeSyncBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _timestampText = "";
    [ObservableProperty] private DateTime? _pinnedTimestamp;

    public event Action? ClearRequested;

    public void Pin(DateTime ts)
    {
        PinnedTimestamp = ts;
        TimestampText = $"Pinned: {ts:yyyy-MM-dd HH:mm:ss.fff}";
        IsVisible = true;
    }

    [RelayCommand]
    public void Clear()
    {
        PinnedTimestamp = null;
        TimestampText = "";
        IsVisible = false;
        ClearRequested?.Invoke();
    }
}
