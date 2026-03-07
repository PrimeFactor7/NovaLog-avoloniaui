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

    public bool CanSelectForMerge => !IsChild && Kind != SourceKind.Merge;
    public bool IsMergeSource => Kind == SourceKind.Merge;
    public bool ShowSourcePip => !IsChild && Kind != SourceKind.Merge;
    public bool ShowChildColorBar => IsChild;

    partial void OnKindChanged(SourceKind value)
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(CanSelectForMerge));
        OnPropertyChanged(nameof(IsMergeSource));
        OnPropertyChanged(nameof(ShowSourcePip));
        OnPropertyChanged(nameof(ShowChildColorBar));
    }

    partial void OnIsChildChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectForMerge));
        OnPropertyChanged(nameof(ShowSourcePip));
        OnPropertyChanged(nameof(ShowChildColorBar));
    }
}

public sealed class RecentHistoryItemViewModel
{
    public required RecentSourceEntry Entry { get; init; }
    public required string DisplayName { get; init; }
    public required string Location { get; init; }
    public required string KindLabel { get; init; }
    public required string LastUsedText { get; init; }
    public required string LastModifiedText { get; init; }
    public required string StatusText { get; init; }
    public bool IsMissing { get; init; }
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
    public bool CanClear => Sources.Any(s => s.IsSelectedForMerge && !s.IsChild) || Sources.Any(s => s.Kind == SourceKind.Merge);

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

        var item = CreateSourceItem(path, kind, sourceId);
        Sources.Add(item);
        AddToRecentHistory(path, kind);
    }

    public void RestoreSources(IEnumerable<LogSource> sources)
    {
        Sources.Clear();

        var sourceList = sources.ToList();
        foreach (var source in sourceList)
        {
            var item = CreateSourceItem(source);
            Sources.Add(item);
        }

        foreach (var source in sourceList)
        {
            var item = Sources.FirstOrDefault(s => s.SourceId == source.Id);
            if (item is null)
                continue;

            item.IsChild = source.IsChild;
            item.IsMissing = source.Kind switch
            {
                SourceKind.File => !File.Exists(source.PhysicalPath),
                SourceKind.Folder => !Directory.Exists(source.PhysicalPath),
                _ => false
            };

            item.ChildSourceIds.Clear();
            if (source.ChildSourceIds is { Count: > 0 })
            {
                foreach (var childId in source.ChildSourceIds)
                    item.ChildSourceIds.Add(childId);
            }
        }

        UpdateDisplayList();
        OnPropertyChanged(nameof(MergeStatusText));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(CanClear));
    }

    public List<LogSource> CreateSnapshot()
    {
        return Sources.Select(src => new LogSource
        {
            Id = src.SourceId,
            PhysicalPath = src.PhysicalPath,
            Alias = GetPersistedAlias(src),
            SourceColorHex = src.SourceColorHex,
            IsSelectedForMerge = src.IsSelectedForMerge,
            Status = src.IsMissing ? SourceStatus.Missing : SourceStatus.Active,
            Kind = src.Kind,
            ChildSourceIds = src.ChildSourceIds.ToList(),
            IsExpanded = src.IsExpanded,
            IsChild = src.IsChild
        }).ToList();
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

    public IReadOnlyList<RecentHistoryItemViewModel> BuildRecentHistoryItems()
    {
        return RecentSources
            .Select(CreateRecentHistoryItem)
            .ToList();
    }

    [RelayCommand]
    private void ToggleRecents()
    {
        ShowRecents = !ShowRecents;
    }

    [RelayCommand]
    private void LoadRecent(RecentSourceEntry recent)
    {
        PreviewRecent(recent, trackUsage: true);
    }

    public bool PreviewRecent(RecentSourceEntry recent, bool trackUsage = false)
    {
        if (!TryResolveRecentSource(recent, out var kind))
            return false;

        SourceSelected?.Invoke(recent.Path, kind);
        if (trackUsage)
            AddToRecentHistory(recent.Path, kind);

        return true;
    }

    public bool AddRecentToSources(RecentSourceEntry recent)
    {
        if (!TryResolveRecentSource(recent, out var kind))
            return false;

        AddSource(recent.Path, kind);
        AddToRecentHistory(recent.Path, kind);

        var existing = Sources.FirstOrDefault(s =>
            !s.IsChild && PathsEqual(s.PhysicalPath, recent.Path));
        if (existing != null)
            SelectedSource = existing;

        SourceSelected?.Invoke(recent.Path, kind);
        return true;
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
        // Remove merge sources first (and fully remove their children)
        var merges = Sources.Where(s => s.Kind == SourceKind.Merge).ToList();
        foreach (var merge in merges)
            RemoveMergeWithChildren(merge);

        // Then remove remaining selected sources
        var toRemove = Sources.Where(s => s.IsSelectedForMerge).ToList();
        foreach (var item in toRemove)
            RemoveSourceCore(item);
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

        AttachSourceHandlers(mergeItem);
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
            RemoveSourceCore(src);
    }

    [RelayCommand]
    private void RemoveSourceItem(SourceItemViewModel? source)
    {
        if (source is null)
            return;

        RemoveSourceCore(source);
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

    private SourceItemViewModel CreateSourceItem(string path, SourceKind kind, string? sourceId)
    {
        var item = new SourceItemViewModel
        {
            DisplayName = GetDefaultDisplayName(path, kind),
            PhysicalPath = path,
            Kind = kind,
            SourceColorHex = GetNextColor(Sources.Count)
        };

        if (!string.IsNullOrEmpty(sourceId))
            item.SourceId = sourceId;

        AttachSourceHandlers(item);
        return item;
    }

    private SourceItemViewModel CreateSourceItem(LogSource source)
    {
        var item = new SourceItemViewModel
        {
            DisplayName = !string.IsNullOrWhiteSpace(source.Alias)
                ? source.Alias
                : GetDefaultDisplayName(source.PhysicalPath, source.Kind),
            PhysicalPath = source.PhysicalPath,
            Kind = source.Kind,
            SourceColorHex = string.IsNullOrWhiteSpace(source.SourceColorHex)
                ? GetNextColor(Sources.Count)
                : source.SourceColorHex,
            IsSelectedForMerge = source.IsSelectedForMerge,
            IsExpanded = source.IsExpanded
        };

        if (!string.IsNullOrEmpty(source.Id))
            item.SourceId = source.Id;

        AttachSourceHandlers(item);
        return item;
    }

    private void AttachSourceHandlers(SourceItemViewModel item)
    {
        item.PropertyChanged += (_, e) =>
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
    }

    private void RemoveSourceCore(SourceItemViewModel source)
    {
        if (source.Kind == SourceKind.Merge)
        {
            DissolveMergeSource(source);
            return;
        }

        RemoveFromParentMerges(source.SourceId);
        Sources.Remove(source);
        if (ReferenceEquals(SelectedSource, source))
            SelectedSource = null;
        SourceRemoved?.Invoke(source);
    }

    private void RemoveFromParentMerges(string childSourceId)
    {
        var mergesToDissolve = Sources
            .Where(s => s.Kind == SourceKind.Merge && s.ChildSourceIds.Contains(childSourceId))
            .ToList();

        foreach (var merge in mergesToDissolve)
        {
            merge.ChildSourceIds.Remove(childSourceId);
            if (merge.ChildSourceIds.Count < 2)
                DissolveMergeSource(merge);
        }
    }

    /// <summary>Fully remove a merge and all its child sources.</summary>
    private void RemoveMergeWithChildren(SourceItemViewModel mergeSource)
    {
        foreach (var childId in mergeSource.ChildSourceIds.ToList())
        {
            var child = Sources.FirstOrDefault(s => s.SourceId == childId);
            if (child != null)
            {
                Sources.Remove(child);
                SourceRemoved?.Invoke(child);
            }
        }

        mergeSource.ChildSourceIds.Clear();
        Sources.Remove(mergeSource);

        if (ReferenceEquals(SelectedSource, mergeSource))
            SelectedSource = null;

        SourceRemoved?.Invoke(mergeSource);
    }

    private void DissolveMergeSource(SourceItemViewModel mergeSource)
    {
        foreach (var childId in mergeSource.ChildSourceIds.ToList())
        {
            var child = Sources.FirstOrDefault(s => s.SourceId == childId);
            if (child != null)
            {
                child.IsChild = false;
                child.IsSelectedForMerge = false;
            }
        }

        mergeSource.ChildSourceIds.Clear();
        Sources.Remove(mergeSource);

        if (ReferenceEquals(SelectedSource, mergeSource))
            SelectedSource = null;

        SourceRemoved?.Invoke(mergeSource);
    }

    private static string GetDefaultDisplayName(string path, SourceKind kind) => kind switch
    {
        SourceKind.Folder => new DirectoryInfo(path).Name,
        SourceKind.File => Path.GetFileName(path),
        SourceKind.Merge => "Merged View",
        _ => Path.GetFileName(path)
    };

    private static string? GetPersistedAlias(SourceItemViewModel src)
    {
        if (src.Kind == SourceKind.Merge)
            return src.DisplayName;

        var defaultName = GetDefaultDisplayName(src.PhysicalPath, src.Kind);
        return string.Equals(src.DisplayName, defaultName, StringComparison.Ordinal)
            ? null
            : src.DisplayName;
    }

    private RecentHistoryItemViewModel CreateRecentHistoryItem(RecentSourceEntry recent)
    {
        var kind = ParseRecentKind(recent);
        var exists = kind switch
        {
            SourceKind.Folder => Directory.Exists(recent.Path),
            _ => File.Exists(recent.Path)
        };

        DateTime? lastModified = null;
        if (exists)
        {
            lastModified = kind switch
            {
                SourceKind.Folder => Directory.GetLastWriteTime(recent.Path),
                _ => File.GetLastWriteTime(recent.Path)
            };
        }

        var inSources = Sources.Any(s => !s.IsChild && PathsEqual(s.PhysicalPath, recent.Path));
        var displayName = GetDefaultDisplayName(recent.Path, kind);
        var lastUsed = recent.LastAccessed.Kind == DateTimeKind.Utc
            ? recent.LastAccessed.ToLocalTime()
            : recent.LastAccessed;

        return new RecentHistoryItemViewModel
        {
            Entry = recent,
            DisplayName = displayName,
            Location = Path.GetDirectoryName(recent.Path) ?? recent.Path,
            KindLabel = kind switch
            {
                SourceKind.Folder => "DIR",
                SourceKind.Merge => "MERGE",
                _ => "FILE"
            },
            LastUsedText = lastUsed.ToString("yyyy-MM-dd HH:mm"),
            LastModifiedText = lastModified?.ToString("yyyy-MM-dd HH:mm") ?? "Missing",
            StatusText = !exists ? "Missing" : inSources ? "In Sources" : "Available",
            IsMissing = !exists
        };
    }

    private static SourceKind ParseRecentKind(RecentSourceEntry recent)
    {
        if (Enum.TryParse<SourceKind>(recent.Kind, ignoreCase: true, out var parsed))
            return parsed;

        return Directory.Exists(recent.Path) ? SourceKind.Folder : SourceKind.File;
    }

    private static bool TryResolveRecentSource(RecentSourceEntry recent, out SourceKind kind)
    {
        kind = ParseRecentKind(recent);
        return kind switch
        {
            SourceKind.Folder => Directory.Exists(recent.Path),
            _ => File.Exists(recent.Path)
        };
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
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
