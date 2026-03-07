using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Models;

namespace NovaLog.Avalonia.ViewModels;

public partial class PanePickerItem : ObservableObject
{
    public string SourceId { get; }
    public string DisplayName { get; }
    public string Path { get; }
    public SourceKind Kind { get; }
    public bool IsRecent { get; }

    public PanePickerItem(string id, string name, string path, SourceKind kind, bool recent)
    {
        SourceId = id;
        DisplayName = name;
        Path = path;
        Kind = kind;
        IsRecent = recent;
    }
}

public partial class PanePickerViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private PanePickerItem? _selectedItem;

    public ObservableCollection<PanePickerItem> AllItems { get; } = [];
    public ObservableCollection<PanePickerItem> FilteredItems { get; } = [];

    public event Action<PanePickerItem>? ItemSelected;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void Show(IEnumerable<PanePickerItem> items)
    {
        AllItems.Clear();
        foreach (var item in items) AllItems.Add(item);
        SearchText = "";
        ApplyFilter();
        IsVisible = true;
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var query = SearchText.Trim();
        foreach (var item in AllItems)
        {
            if (string.IsNullOrEmpty(query) ||
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
        if (FilteredItems.Count > 0) SelectedItem = FilteredItems[0];
    }

    [RelayCommand]
    public void Confirm()
    {
        if (SelectedItem != null)
        {
            ItemSelected?.Invoke(SelectedItem);
            IsVisible = false;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        IsVisible = false;
    }
}
