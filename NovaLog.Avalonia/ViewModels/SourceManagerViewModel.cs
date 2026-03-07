using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Models;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace NovaLog.Avalonia.ViewModels;

public partial class SourceItemViewModel : ObservableObject
{
    [ObservableProperty] private string _sourceId = Guid.NewGuid().ToString();
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _sourceColorHex = "#00FF41";
    [ObservableProperty] private SourceKind _kind;
    [ObservableProperty] private bool _isSelectedForMerge;
    [ObservableProperty] private string _physicalPath = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isChild;
    [ObservableProperty] private bool _isMissing;

    public ObservableCollection<string> ChildSourceIds { get; } = new();

    public string KindLabel => Kind switch
    {
        SourceKind.Folder => "DIR",
        SourceKind.File => "FILE",
        SourceKind.Merge => "MERGE",
        _ => ""
    };
}

public partial class SourceManagerViewModel : ObservableObject
{
    [ObservableProperty] private SourceItemViewModel? _selectedSource;
    [ObservableProperty] private bool _showRecents;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = new();
    public ObservableCollection<SourceItemViewModel> DisplaySources { get; } = new();
    public ObservableCollection<RecentSourceEntry> RecentSources { get; } = new();

    public string MergeStatusText => $"{Sources.Count(s => s.IsSelectedForMerge && !s.IsChild)} selected for merge";
    public bool CanMerge => Sources.Count(s => s.IsSelectedForMerge && !s.IsChild) >= 2;
    public bool CanClear => Sources.Any(s => s.IsSelectedForMerge && !s.IsChild);

    public event Action<string, SourceKind>? SourceSelected;
    public event Action<string, SourceKind>? SourceNewTabRequested;
    public event Func<string, Task<string?>>? AliasInputRequested;
    public event Action<SourceItemViewModel>? SourceRemoved;
    public event Action? CloseRequested;

    public SourceManagerViewModel()
    {
        Sources.CollectionChanged += (_, _) => UpdateDisplayList();
    }

    public void AddSource(string path, SourceKind kind)
    {
        AddSource(path, kind, null);
    }

    public void AddSource(string path, SourceKind kind, string? sourceId)
    {
        if (Sources.Any(s => s.PhysicalPath == path && !s.IsChild)) return;

        var item = new SourceItemViewModel
        {
            DisplayName = kind == SourceKind.Folder
                ? new DirectoryInfo(path).Name
                : Path.GetFileName(path),
            PhysicalPath = path,
            Kind = kind,
            SourceColorHex = GetNextColor(Sources.Count)
        };

        // Preserve the source ID if provided (for session restore)
        if (!string.IsNullOrEmpty(sourceId))
            item.SourceId = sourceId;

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SourceItemViewModel.IsSelectedForMerge))
            {
                OnPropertyChanged(nameof(MergeStatusText));
                OnPropertyChanged(nameof(CanMerge));
                OnPropertyChanged(nameof(CanClear));
            }
            if (e.PropertyName == nameof(SourceItemViewModel.IsExpanded))
            {
                UpdateDisplayList();
            }
        };

        Sources.Add(item);
        AddToRecentHistory(path, kind);
    }

    public void AddToRecentHistory(string path, SourceKind kind)
    {
        // Remove existing entry if present
        var existing = RecentSources.FirstOrDefault(r => r.Path == path);
        if (existing != null)
            RecentSources.Remove(existing);

        // Add to front
        RecentSources.Insert(0, new RecentSourceEntry
        {
            Path = path,
            Kind = kind.ToString(),
            LastAccessed = DateTime.UtcNow
        });

        // Keep only last 10
        while (RecentSources.Count > 10)
            RecentSources.RemoveAt(RecentSources.Count - 1);
    }

    [RelayCommand]
    private void ToggleRecents()
    {
        ShowRecents = !ShowRecents;
    }

    [RelayCommand]
    private void LoadRecent(RecentSourceEntry recent)
    {
        if (!File.Exists(recent.Path) && !Directory.Exists(recent.Path))
            return;

        var kind = Enum.Parse<SourceKind>(recent.Kind);
        SourceSelected?.Invoke(recent.Path, kind);
        AddToRecentHistory(recent.Path, kind);
    }

    private void UpdateDisplayList()
    {
        DisplaySources.Clear();
        foreach (var src in Sources.Where(s => !s.IsChild))
        {
            DisplaySources.Add(src);
            if (src.Kind == SourceKind.Merge && src.IsExpanded)
            {
                foreach (var childId in src.ChildSourceIds)
                {
                    var child = Sources.FirstOrDefault(s => s.SourceId == childId);
                    if (child != null)
                        DisplaySources.Add(child);
                }
            }
        }
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var selectable = Sources.Where(s => s.Kind != SourceKind.Merge && !s.IsChild).ToList();
        if (selectable.Count == 0) return;

        bool allSelected = selectable.All(s => s.IsSelectedForMerge);
        foreach (var src in selectable)
            src.IsSelectedForMerge = !allSelected;
    }

    [RelayCommand]
    private void ClearSelected()
    {
        var toRemove = Sources.Where(s => s.IsSelectedForMerge && !s.IsChild).ToList();
        foreach (var item in toRemove)
        {
            Sources.Remove(item);
            SourceRemoved?.Invoke(item);
        }
    }

    [RelayCommand]
    private void MergeSelected()
    {
        var selected = Sources.Where(s => s.IsSelectedForMerge && !s.IsChild).ToList();
        if (selected.Count < 2) return;

        var mergeItem = new SourceItemViewModel
        {
            DisplayName = $"Merged ({selected.Count} sources)",
            Kind = SourceKind.Merge,
            SourceColorHex = GetNextColor(Sources.Count),
            IsExpanded = true
        };

        mergeItem.PhysicalPath = "merge://" + string.Join("|", selected.Select(s => s.SourceId));

        foreach (var src in selected)
        {
            src.IsSelectedForMerge = false;
            src.IsChild = true;
            mergeItem.ChildSourceIds.Add(src.SourceId);
        }

        mergeItem.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(SourceItemViewModel.IsExpanded))
                UpdateDisplayList();
        };

        Sources.Add(mergeItem);
        SourceSelected?.Invoke(mergeItem.PhysicalPath, SourceKind.Merge);
    }

    [RelayCommand]
    private void LoadSelected()
    {
        if (SelectedSource is { } src)
            SourceSelected?.Invoke(src.PhysicalPath, src.Kind);
    }

    [RelayCommand]
    private void OpenInNewTab()
    {
        if (SelectedSource is { } src)
            SourceNewTabRequested?.Invoke(src.PhysicalPath, src.Kind);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedSource is { } src)
        {
            Sources.Remove(src);
            SourceRemoved?.Invoke(src);
        }
    }

    [RelayCommand]
    private async Task SetAlias()
    {
        if (SelectedSource is not { } src) return;
        if (AliasInputRequested is null) return;

        var newAlias = await AliasInputRequested.Invoke(src.DisplayName);
        if (!string.IsNullOrWhiteSpace(newAlias))
            src.DisplayName = newAlias;
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (SelectedSource is not { } src || src.Kind == SourceKind.Merge) return;

        var target = src.Kind == SourceKind.Folder
            ? src.PhysicalPath
            : Path.GetDirectoryName(src.PhysicalPath);

        if (target is not null && (Directory.Exists(target) || File.Exists(src.PhysicalPath)))
        {
            if (OperatingSystem.IsWindows())
            {
                if (src.Kind == SourceKind.File)
                    Process.Start("explorer.exe", $"/select,\"{src.PhysicalPath}\"");
                else
                    Process.Start("explorer.exe", $"\"{target}\"");
            }
        }
    }

    [RelayCommand]
    private void CloseSidebar() => CloseRequested?.Invoke();

    private static string GetNextColor(int index)
    {
        string[] palette = ["#00FF41", "#00D4FF", "#FF00FF", "#FFB000", "#FF3E3E", "#FFD700"];
        return palette[index % palette.Length];
    }
}

public class IndentConverter : IValueConverter
{
    public static readonly IndentConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isChild && isChild) ? new global::Avalonia.Thickness(20, 0, 0, 0) : new global::Avalonia.Thickness(0);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class NotMergeKindConverter : IValueConverter
{
    public static readonly NotMergeKindConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is SourceKind kind && kind != SourceKind.Merge);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class MergeKindConverter : IValueConverter
{
    public static readonly MergeKindConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is SourceKind kind && kind == SourceKind.Merge);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class ChevronConverter : IValueConverter
{
    public static readonly ChevronConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isExpanded && isExpanded) ? "\uE70D" : "\uE70E";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class MissingOpacityConverter : IValueConverter
{
    public static readonly MissingOpacityConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isMissing && isMissing) ? 0.6 : 1.0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class MissingFontStyleConverter : IValueConverter
{
    public static readonly MissingFontStyleConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool isMissing && isMissing) ? FontStyle.Italic : FontStyle.Normal;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}