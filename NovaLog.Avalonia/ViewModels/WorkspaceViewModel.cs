using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaLog.Core.Services;
using NovaLog.Core.Models;
using NovaLog.Core.Theme;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Manages the workspace: a binary tree of split panes with focus tracking,
/// and a tab bar for multiple independent workspace layouts.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    [ObservableProperty] private SplitNodeViewModel _rootNode;
    [ObservableProperty] private PaneNodeViewModel? _focusedPane;
    [ObservableProperty] private int _activeTabIndex;

    public ObservableCollection<WorkspaceTabItem> Tabs { get; } = [];
    public GlobalClockService Clock { get; } = new();
    public TimeSyncBarViewModel TimeSync { get; } = new();
    
    private SourceManagerViewModel? _sourceManager;
    private ThemeService? _theme;

    /// <summary>True when more than one tab exists (shows tab bar).</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    /// <summary>Convenience: the focused pane's LogViewViewModel.</summary>
    public LogViewViewModel? ActiveLogView => FocusedPane?.LogView;

    public WorkspaceViewModel()
    {
        var initialPane = new PaneNodeViewModel();
        _rootNode = initialPane;
        FocusPane(initialPane);

        var firstTab = new WorkspaceTabItem("Workspace 1", true);
        firstTab.SavedLayout = initialPane;
        Tabs.Add(firstTab);

        Clock.TimeChanged += (timestamp, sender) =>
        {
            TimeSync.Pin(timestamp);
            foreach (var pane in GetAllPanes())
            {
                if (pane.LogView == sender) continue;
                if (!pane.LogView.IsLinked) continue; // Skip unlinked panes
                pane.LogView.SeekToTimestamp(timestamp);
            }
        };
    }

    public void Initialize(SourceManagerViewModel sourceManager, ThemeService theme)
    {
        _sourceManager = sourceManager;
        _theme = theme;
        foreach (var pane in GetAllPanes())
        {
            pane.LogView.Initialize(Clock, _sourceManager, _theme);
        }
    }

    // ── Persistence ──────────────────────────────────────────────────

    public SplitLayoutNode SaveLayout()
    {
        return SerializeNode(RootNode);
    }

    public void LoadLayout(SplitLayoutNode? node)
    {
        if (node == null) return;
        RootNode = DeserializeNode(node);
        
        var first = GetFirstPane(RootNode);
        if (first != null) FocusPane(first);
    }

    private SplitLayoutNode SerializeNode(SplitNodeViewModel node)
    {
        if (node is PaneNodeViewModel pane)
        {
            // Save the source ID if this pane has a source loaded
            var sourceId = pane.LogView.GetLoadedSourceId();
            var isFollowMode = pane.LogView.IsFollowMode;
            System.Diagnostics.Debug.WriteLine($"[SAVE] Serializing pane, sourceId: {sourceId ?? "(null)"}, followMode: {isFollowMode}");
            return SplitLayoutNode.Leaf(sourceId, null, 0, isFollowMode);
        }
        else if (node is SplitBranchViewModel branch)
        {
            return SplitLayoutNode.Branch(
                branch.IsHorizontal ? SplitOrientation.Vertical : SplitOrientation.Horizontal,
                branch.SplitterRatio,
                SerializeNode(branch.Child1),
                SerializeNode(branch.Child2));
        }
        return SplitLayoutNode.Leaf(null, null);
    }

    private SplitNodeViewModel DeserializeNode(SplitLayoutNode node)
    {
        if (node.IsLeaf)
        {
            System.Diagnostics.Debug.WriteLine($"[RESTORE] Deserializing leaf node, sourceId: {node.SourceId ?? "(null)"}, followMode: {node.IsFollowMode}");
            var pane = new PaneNodeViewModel();
            if (_sourceManager != null && _theme != null)
            {
                pane.LogView.Initialize(Clock, _sourceManager, _theme);

                // Restore the follow mode state
                pane.LogView.IsFollowMode = node.IsFollowMode;

                // Restore the source if one was saved
                if (!string.IsNullOrEmpty(node.SourceId))
                {
                    System.Diagnostics.Debug.WriteLine($"[RESTORE] Looking for source with ID: {node.SourceId}");
                    System.Diagnostics.Debug.WriteLine($"[RESTORE] Available sources: {_sourceManager.Sources.Count}");
                    foreach (var s in _sourceManager.Sources)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RESTORE]   - {s.SourceId}: {s.PhysicalPath} ({s.Kind})");
                    }

                    var source = _sourceManager.Sources.FirstOrDefault(s => s.SourceId == node.SourceId);
                    if (source != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RESTORE] Found source, loading {source.Kind}: {source.PhysicalPath}");
                        // Load the source based on its type
                        if (source.Kind == SourceKind.File)
                            pane.LogView.LoadFile(source.PhysicalPath);
                        else if (source.Kind == SourceKind.Folder)
                            pane.LogView.LoadFolder(source.PhysicalPath);
                        else if (source.Kind == SourceKind.Merge && source.PhysicalPath.StartsWith("merge://"))
                        {
                            var ids = source.PhysicalPath.Substring(8).Split('|');
                            var sourcesToMerge = _sourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                            System.Diagnostics.Debug.WriteLine($"[RESTORE] Loading merge with {sourcesToMerge.Count} sources");
                            pane.LogView.LoadMerge(sourcesToMerge);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[RESTORE] ERROR: Source not found with ID {node.SourceId}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[RESTORE] No source ID saved for this pane");
                }
            }
            return pane;
        }
        else
        {
            var child1 = DeserializeNode(node.Child1!);
            var child2 = DeserializeNode(node.Child2!);
            var branch = new SplitBranchViewModel(child1, child2,
                node.Orientation == SplitOrientation.Vertical);
            branch.SplitterRatio = node.SplitterPct;
            return branch;
        }
    }

    // ── Split ───────────────────────────────────────────────────────

    public PaneNodeViewModel? SplitFocused(bool horizontal)
    {
        if (FocusedPane is null) return null;
        return SplitPane(FocusedPane, horizontal);
    }

    public PaneNodeViewModel? SplitTarget(PaneNodeViewModel target, bool horizontal)
    {
        return SplitPane(target, horizontal);
    }

    private PaneNodeViewModel SplitPane(PaneNodeViewModel target, bool horizontal)
    {
        var newPane = new PaneNodeViewModel();
        if (_sourceManager != null && _theme != null)
            newPane.LogView.Initialize(Clock, _sourceManager, _theme);
        
        var originalParent = target.Parent;
        var branch = new SplitBranchViewModel(target, newPane, horizontal);

        branch.Parent = originalParent;
        if (originalParent is null)
        {
            RootNode = branch;
        }
        else
        {
            if (originalParent.Child1 == target)
                originalParent.Child1 = branch;
            else
                originalParent.Child2 = branch;
        }

        FocusPane(newPane);
        return newPane;
    }

    // ── Close ───────────────────────────────────────────────────────

    public void CloseFocused()
    {
        if (FocusedPane is null) return;
        ClosePane(FocusedPane);
    }

    public void ClosePane(PaneNodeViewModel target)
    {
        var parent = target.Parent;
        if (parent is null)
        {
            var fresh = new PaneNodeViewModel();
            if (_sourceManager != null && _theme != null)
                fresh.LogView.Initialize(Clock, _sourceManager, _theme);
            RootNode = fresh;
            FocusPane(fresh);
            return;
        }

        var sibling = parent.Child1 == target ? parent.Child2 : parent.Child1;
        sibling.Parent = parent.Parent;
        ReplaceInParent(parent, sibling);

        var focusTarget = GetFirstPane(sibling);
        if (focusTarget is not null)
            FocusPane(focusTarget);
    }

    // ── Focus ───────────────────────────────────────────────────────

    public void FocusPane(PaneNodeViewModel pane)
    {
        if (FocusedPane == pane) return;

        if (FocusedPane is not null)
            FocusedPane.IsFocused = false;

        FocusedPane = pane;
        pane.IsFocused = true;
        OnPropertyChanged(nameof(ActiveLogView));
    }

    public enum Direction { Left, Right, Up, Down }

    public void MoveFocus(Direction direction)
    {
        var panes = new List<PaneNodeViewModel>();
        CollectPanes(RootNode, panes);
        if (panes.Count <= 1 || FocusedPane is null) return;

        var currentIdx = panes.IndexOf(FocusedPane);
        if (currentIdx < 0) return;

        int targetIdx = direction is Direction.Left or Direction.Up
            ? (currentIdx - 1 + panes.Count) % panes.Count
            : (currentIdx + 1) % panes.Count;

        FocusPane(panes[targetIdx]);
    }

    // ── Tabs ────────────────────────────────────────────────────────

    public void AddTab(string name)
    {
        // Save current tab's layout before switching
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            Tabs[ActiveTabIndex].SavedLayout = RootNode;

        foreach (var t in Tabs) t.IsActive = false;

        var fresh = new PaneNodeViewModel();
        if (_sourceManager != null && _theme != null)
            fresh.LogView.Initialize(Clock, _sourceManager, _theme);

        var newTab = new WorkspaceTabItem(name, true);
        newTab.SavedLayout = fresh;
        Tabs.Add(newTab);

        ActiveTabIndex = Tabs.Count - 1;
        OnPropertyChanged(nameof(HasMultipleTabs));

        RootNode = fresh;
        FocusPane(fresh);
    }

    public void SwitchTab(int index)
    {
        if (index < 0 || index >= Tabs.Count) return;
        if (index == ActiveTabIndex) return; // Already on this tab

        // Save current tab's layout
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            Tabs[ActiveTabIndex].SavedLayout = RootNode;

        // Switch to new tab
        foreach (var t in Tabs) t.IsActive = false;
        Tabs[index].IsActive = true;
        ActiveTabIndex = index;

        // Restore new tab's layout
        var newLayout = Tabs[index].SavedLayout;
        if (newLayout != null)
        {
            RootNode = newLayout;
            var firstPane = GetFirstPane(RootNode);
            if (firstPane != null)
                FocusPane(firstPane);
        }
        else
        {
            // Tab has no saved layout - create a fresh one
            var fresh = new PaneNodeViewModel();
            if (_sourceManager != null && _theme != null)
                fresh.LogView.Initialize(Clock, _sourceManager, _theme);
            RootNode = fresh;
            Tabs[index].SavedLayout = fresh;
            FocusPane(fresh);
        }
    }

    public void CloseTab(int index)
    {
        if (Tabs.Count <= 1) return;
        Tabs.RemoveAt(index);
        if (ActiveTabIndex >= Tabs.Count)
            ActiveTabIndex = Tabs.Count - 1;
        SwitchTab(ActiveTabIndex);
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void CloseOtherTabs(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= Tabs.Count) return;

        // Save current tab's layout if it's not the one we're keeping
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count && ActiveTabIndex != keepIndex)
            Tabs[ActiveTabIndex].SavedLayout = RootNode;

        var keep = Tabs[keepIndex];

        // If we're keeping a tab that's not currently active, make sure its layout is up to date
        if (keepIndex == ActiveTabIndex)
            keep.SavedLayout = RootNode;

        Tabs.Clear();
        Tabs.Add(keep);
        keep.IsActive = true;
        ActiveTabIndex = 0;

        // Restore the kept tab's layout
        if (keep.SavedLayout != null)
        {
            RootNode = keep.SavedLayout;
            var firstPane = GetFirstPane(RootNode);
            if (firstPane != null)
                FocusPane(firstPane);
        }

        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void CloseAllTabs()
    {
        Tabs.Clear();

        var fresh = new PaneNodeViewModel();
        if (_sourceManager != null && _theme != null)
            fresh.LogView.Initialize(Clock, _sourceManager, _theme);

        var newTab = new WorkspaceTabItem("Workspace 1", true);
        newTab.SavedLayout = fresh;
        Tabs.Add(newTab);

        ActiveTabIndex = 0;
        RootNode = fresh;
        FocusPane(fresh);
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void RenameTab(int index, string newName)
    {
        if (index >= 0 && index < Tabs.Count)
            Tabs[index].Name = newName;
    }

    // ── Tree helpers ────────────────────────────────────────────────

    private void ReplaceInParent(SplitNodeViewModel oldNode, SplitNodeViewModel newNode)
    {
        var parent = oldNode.Parent;
        if (parent is null)
        {
            RootNode = newNode;
            newNode.Parent = null;
            return;
        }

        if (parent.Child1 == oldNode)
            parent.Child1 = newNode;
        else
            parent.Child2 = newNode;

        newNode.Parent = parent;
    }

    private static PaneNodeViewModel? GetFirstPane(SplitNodeViewModel node) => node switch
    {
        PaneNodeViewModel pane => pane,
        SplitBranchViewModel branch => GetFirstPane(branch.Child1),
        _ => null
    };

    private static void CollectPanes(SplitNodeViewModel node, List<PaneNodeViewModel> result)
    {
        switch (node)
        {
            case PaneNodeViewModel pane:
                result.Add(pane);
                break;
            case SplitBranchViewModel branch:
                CollectPanes(branch.Child1, result);
                CollectPanes(branch.Child2, result);
                break;
        }
    }

    public List<PaneNodeViewModel> GetAllPanes()
    {
        var list = new List<PaneNodeViewModel>();
        CollectPanes(RootNode, list);
        return list;
    }

    public int PaneCount => GetAllPanes().Count;
}

/// <summary>Tab bar item.</summary>
public partial class WorkspaceTabItem : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isActive;

    /// <summary>Stores this tab's workspace layout (split tree structure).</summary>
    public SplitNodeViewModel? SavedLayout { get; set; }

    public WorkspaceTabItem(string name, bool isActive)
    {
        _name = name;
        _isActive = isActive;
    }
}
