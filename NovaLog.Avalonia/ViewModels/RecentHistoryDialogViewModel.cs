using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaLog.Avalonia.ViewModels;

public partial class RecentHistoryDialogViewModel : ObservableObject
{
    [ObservableProperty] private RecentHistoryItemViewModel? _selectedItem;

    public ObservableCollection<RecentHistoryItemViewModel> Items { get; }

    public bool CanAddSelected => SelectedItem is { IsMissing: false };

    public RecentHistoryDialogViewModel(IReadOnlyList<RecentHistoryItemViewModel> items)
    {
        Items = new ObservableCollection<RecentHistoryItemViewModel>(items);
    }

    partial void OnSelectedItemChanged(RecentHistoryItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanAddSelected));
    }
}
