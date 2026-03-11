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
public partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private SplitNodeViewModel _rootNode;
    [ObservableProperty] private PaneNodeViewModel? _focusedPane;
    [ObservableProperty] private int _activeTabIndex;

    public ObservableCollection<WorkspaceTabItem> Tabs { get; } = [];
    public GlobalClockService Clock { get; } = new();
    public TimeSyncBarViewModel TimeSync { get; } = new();
    
    private SourceManagerViewModel? _sourceManager;
    private ThemeService? _theme;
    private bool _defaultMainFollow = true;
    private bool _defaultFilterFollow;
    private bool _defaultGridMode = true;
    private bool _defaultGridMultiline = true;
    private FormattingOptions? _defaultFormattingOptions;
    private int _defaultSearchResultCap = 500;
    private bool _defaultSearchNewestFirst = true;

    /// <summary>True when more than one tab exists (shows tab bar).</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    /// <summary>Convenience: the focused pane's LogViewViewModel.</summary>
    public LogViewViewModel? ActiveLogView => FocusedPane?.LogView;

    public WorkspaceViewModel()
    {
        var initialPane = new PaneNodeViewModel();
        ApplyDefaultFollowState(initialPane);
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

    public void SetMainFollowDefault(bool value, bool applyToExisting)
    {
        _defaultMainFollow = value;
        if (!applyToExisting)
            return;

        foreach (var pane in GetAllPanes())
            pane.LogView.IsFollowMode = value;
    }

    public void SetFilterFollowDefault(bool value, bool applyToExisting)
    {
        _defaultFilterFollow = value;
        if (!applyToExisting)
            return;

        foreach (var pane in GetAllPanes())
            pane.LogView.Filter.IsFollowMode = value;
    }

    public void SetFollowDefaults(bool mainFollow, bool filterFollow, bool applyToExisting)
    {
        _defaultMainFollow = mainFollow;
        _defaultFilterFollow = filterFollow;

        if (!applyToExisting)
            return;

        foreach (var pane in GetAllPanes())
        {
            pane.LogView.IsFollowMode = mainFollow;
            pane.LogView.Filter.IsFollowMode = filterFollow;
        }
    }

    public void SetGridModeDefault(bool value, bool applyToExisting)
    {
        _defaultGridMode = value;
        if (!applyToExisting) return;
        foreach (var pane in GetAllPanes())
            pane.LogView.IsGridMode = value;
    }

    public void SetGridMultilineDefault(bool value, bool applyToExisting)
    {
        _defaultGridMultiline = value;
        if (!applyToExisting) return;
        foreach (var pane in GetAllPanes())
            pane.LogView.GridMultiline = value;
    }

    public void SetFormattingOptions(FormattingOptions? options, bool applyToExisting)
    {
        _defaultFormattingOptions = options;
        if (!applyToExisting) return;
        foreach (var pane in GetAllPanes())
            pane.LogView.SetFormattingOptions(options);
    }

    public void SetSearchDefaults(int resultCap, bool newestFirst, bool applyToExisting)
    {
        _defaultSearchResultCap = resultCap;
        _defaultSearchNewestFirst = newestFirst;
        if (!applyToExisting) return;
        foreach (var pane in GetAllPanes())
        {
            pane.LogView.Filter.SearchResultCap = resultCap;
            pane.LogView.Filter.SearchNewestFirst = newestFirst;
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

    public List<WorkspaceTabLayout> SaveTabs()
    {
        SyncActiveTabLayout();

        var result = new List<WorkspaceTabLayout>(Tabs.Count);
        for (int i = 0; i < Tabs.Count; i++)
        {
            var tab = Tabs[i];
            var layoutNode = i == ActiveTabIndex
                ? RootNode
                : tab.SavedLayout ?? CreateFreshPaneNode();

            result.Add(new WorkspaceTabLayout
            {
                Id = tab.Id,
                DisplayName = tab.Name,
                Layout = SerializeNode(layoutNode),
                IsActive = i == ActiveTabIndex
            });
        }

        return result;
    }

    public void LoadTabs(IEnumerable<WorkspaceTabLayout> tabs)
    {
        Tabs.Clear();

        var layouts = tabs.ToList();
        if (layouts.Count == 0)
        {
            ResetToSingleTab();
            return;
        }

        foreach (var layout in layouts)
        {
            var savedLayout = layout.Layout is not null
                ? DeserializeNode(layout.Layout)
                : CreateFreshPaneNode();

            var tab = new WorkspaceTabItem(layout.DisplayName, false, layout.Id)
            {
                SavedLayout = savedLayout
            };

            Tabs.Add(tab);
        }

        OnPropertyChanged(nameof(HasMultipleTabs));

        var activeIndex = layouts.FindIndex(t => t.IsActive);
        if (activeIndex < 0)
            activeIndex = 0;

        ActivateTab(activeIndex, saveCurrent: false);
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
            ApplyDefaultFollowState(pane);
            if (_sourceManager != null && _theme != null)
            {
                pane.LogView.Initialize(Clock, _sourceManager, _theme);

                // Restore the follow mode state
                pane.LogView.IsFollowMode = node.IsFollowMode;
                pane.LogView.Filter.IsFollowMode = _defaultFilterFollow;

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
                        if (source.Kind == SourceKind.File && File.Exists(source.PhysicalPath))
                            pane.LogView.LoadFile(source.PhysicalPath);
                        else if (source.Kind == SourceKind.Folder && Directory.Exists(source.PhysicalPath))
                            pane.LogView.LoadFolder(source.PhysicalPath);
                        else if (source.Kind == SourceKind.Merge && source.PhysicalPath.StartsWith("merge://"))
                        {
                            var ids = source.PhysicalPath.Substring(8).Split('|');
                            var sourcesToMerge = _sourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                            if (sourcesToMerge.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[RESTORE] Loading merge with {sourcesToMerge.Count} sources");
                                pane.LogView.LoadMerge(sourcesToMerge);
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[RESTORE] Skipping missing source: {source.PhysicalPath}");
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
        ApplyDefaultFollowState(newPane);
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
            ApplyDefaultFollowState(fresh);
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
        SyncActiveTabLayout();

        foreach (var t in Tabs)
            t.IsActive = false;

        var fresh = CreateFreshPaneNode();
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

        ActivateTab(index, saveCurrent: true);
    }

    public void CloseTab(int index)
    {
        if (Tabs.Count <= 1) return;

        bool closingActive = index == ActiveTabIndex;
        if (closingActive)
            SyncActiveTabLayout();

        Tabs.RemoveAt(index);

        if (closingActive)
        {
            var nextIndex = Math.Clamp(index, 0, Tabs.Count - 1);
            ActivateTab(nextIndex, saveCurrent: false);
        }
        else if (index < ActiveTabIndex)
        {
            ActiveTabIndex--;
        }

        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void CloseOtherTabs(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= Tabs.Count) return;

        SyncActiveTabLayout();

        var keep = Tabs[keepIndex];

        Tabs.Clear();
        Tabs.Add(keep);
        ActivateTab(0, saveCurrent: false);

        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void CloseAllTabs()
    {
        ResetToSingleTab();
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

    private PaneNodeViewModel CreateFreshPaneNode()
    {
        var pane = new PaneNodeViewModel();
        ApplyDefaultFollowState(pane);
        if (_sourceManager != null && _theme != null)
            pane.LogView.Initialize(Clock, _sourceManager, _theme);
        return pane;
    }

    private void ApplyDefaultFollowState(PaneNodeViewModel pane)
    {
        pane.LogView.IsFollowMode = _defaultMainFollow;
        pane.LogView.Filter.IsFollowMode = _defaultFilterFollow;
        pane.LogView.Filter.SearchResultCap = _defaultSearchResultCap;
        pane.LogView.Filter.SearchNewestFirst = _defaultSearchNewestFirst;
        pane.LogView.IsGridMode = _defaultGridMode;
        pane.LogView.GridMultiline = _defaultGridMultiline;
        pane.LogView.SetFormattingOptions(_defaultFormattingOptions);
    }

    private void SyncActiveTabLayout()
    {
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            Tabs[ActiveTabIndex].SavedLayout = RootNode;
    }

    private void ActivateTab(int index, bool saveCurrent)
    {
        if (index < 0 || index >= Tabs.Count)
            return;

        if (saveCurrent)
            SyncActiveTabLayout();

        for (int i = 0; i < Tabs.Count; i++)
            Tabs[i].IsActive = i == index;

        ActiveTabIndex = index;

        var layout = Tabs[index].SavedLayout;
        if (layout is null)
        {
            layout = CreateFreshPaneNode();
            Tabs[index].SavedLayout = layout;
        }

        RootNode = layout;
        var firstPane = GetFirstPane(RootNode);
        if (firstPane != null)
            FocusPane(firstPane);
    }

    private void ResetToSingleTab()
    {
        Tabs.Clear();

        var fresh = CreateFreshPaneNode();
        var newTab = new WorkspaceTabItem("Workspace 1", true)
        {
            SavedLayout = fresh
        };

        Tabs.Add(newTab);
        ActiveTabIndex = 0;
        RootNode = fresh;
        FocusPane(fresh);
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    public void Dispose()
    {
        Clock.Dispose();
    }
}

/// <summary>Tab bar item.</summary>
public partial class WorkspaceTabItem : ObservableObject
{
    [ObservableProperty] private string _id;
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isActive;

    /// <summary>Stores this tab's workspace layout (split tree structure).</summary>
    public SplitNodeViewModel? SavedLayout { get; set; }

    public WorkspaceTabItem(string name, bool isActive, string? id = null)
    {
        _id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        _name = name;
        _isActive = isActive;
    }
}
