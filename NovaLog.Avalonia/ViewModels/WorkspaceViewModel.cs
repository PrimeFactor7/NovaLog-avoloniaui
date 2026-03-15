using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using Dock.Model.Controls;
using NovaLog.Avalonia.Docking;
using NovaLog.Core.Services;
using NovaLog.Core.Models;
using NovaLog.Core.Theme;
using AvDispatcher = global::Avalonia.Threading.Dispatcher;

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
    [ObservableProperty] private bool _isMasterFollowOn = true;

    /// <summary>When set, the Dock is used for layout; ActiveLogView and GetAllPanes() use the Dock.</summary>
    [ObservableProperty] private IRootDock? _layout;
    /// <summary>Factory that created <see cref="Layout"/>; set together with Layout.</summary>
    public NovaLogDockFactory? DockFactory { get; set; }

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
    private List<IDock>? _layoutDockSubscriptions;

    /// <summary>True when more than one tab exists (shows tab bar).</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    /// <summary>Convenience: the focused pane's LogViewViewModel (from Dock when Layout is set, else from FocusedPane).</summary>
    public LogViewViewModel? ActiveLogView => Layout != null ? DockLayoutHelper.GetActiveLogView(Layout) : FocusedPane?.LogView;

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
            // Ensure we're on the UI thread (timer callback may arrive on threadpool)
            if (!AvDispatcher.UIThread.CheckAccess())
            {
                AvDispatcher.UIThread.Post(() => HandleTimeChanged(timestamp, sender));
                return;
            }
            HandleTimeChanged(timestamp, sender);
        };
    }

    partial void OnLayoutChanged(IRootDock? value)
    {
        UnsubscribeLayoutActiveDockable();
        _layoutDockSubscriptions = null;
        if (value is null)
            return;
        _layoutDockSubscriptions = new List<IDock>();
        SubscribeDock(value);
        InitializeDockDocuments();
    }

    private void SubscribeDock(IDock dock)
    {
        if (dock is INotifyPropertyChanged npc)
        {
            _layoutDockSubscriptions!.Add(dock);
            npc.PropertyChanged += OnLayoutDockPropertyChanged;
        }
        if (dock.VisibleDockables is null) return;
        foreach (var d in dock.VisibleDockables)
        {
            if (d is IDock child)
                SubscribeDock(child);
        }
    }

    private void UnsubscribeLayoutActiveDockable()
    {
        if (_layoutDockSubscriptions is null) return;
        foreach (var dock in _layoutDockSubscriptions)
        {
            if (dock is INotifyPropertyChanged npc)
                npc.PropertyChanged -= OnLayoutDockPropertyChanged;
        }
    }

    private void OnLayoutDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDock.ActiveDockable))
            OnPropertyChanged(nameof(ActiveLogView));
    }

    /// <summary>Initializes all LogViews in the Dock layout (Clock, SourceManager, Theme, follow defaults). Call after setting Layout.</summary>
    public void InitializeDockDocuments()
    {
        if (Layout is null || _sourceManager is null || _theme is null) return;
        var logViews = DockLayoutHelper.GetAllLogViews(Layout);
        foreach (var lv in logViews)
            InitializeDockDocument(lv);
    }

    /// <summary>Initializes a single LogViewViewModel with workspace defaults.</summary>
    public void InitializeDockDocument(LogViewViewModel lv)
    {
        if (_sourceManager is null || _theme is null) return;
        lv.Initialize(Clock, _sourceManager, _theme);
        lv.IsFollowMode = _defaultMainFollow;
        lv.Filter.IsFollowMode = _defaultFilterFollow;
        lv.Filter.SearchResultCap = _defaultSearchResultCap;
        lv.Filter.SearchNewestFirst = _defaultSearchNewestFirst;
        lv.IsGridMode = _defaultGridMode;
        lv.GridMultiline = _defaultGridMultiline;
        lv.SetFormattingOptions(_defaultFormattingOptions);
    }

    private void HandleTimeChanged(DateTime timestamp, object sender)
    {
        TimeSync.Pin(timestamp);
        foreach (var pane in GetAllPanes())
        {
            if (pane.LogView == sender) continue;
            if (!pane.LogView.IsLinked) continue;
            pane.LogView.SeekToTimestamp(timestamp);
        }
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

    partial void OnIsMasterFollowOnChanged(bool value)
    {
        // When turning ON "Follow all", set every pane to follow. When turning OFF, only clear
        // the master switch — leave each pane's follow state unchanged.
        if (!value)
            return;
        foreach (var pane in GetAllPanes())
            pane.LogView.IsFollowMode = true;
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
            SplitLayoutNode? serialized = null;

            // Dock mode: serialize source IDs from dock documents as a flat leaf list
            var dockLayout = i == ActiveTabIndex ? Layout : tab.SavedDockLayout;
            if (dockLayout is not null)
            {
                var logViews = DockLayoutHelper.GetAllLogViews(dockLayout);
                if (logViews.Count <= 1)
                {
                    var lv = logViews.Count == 1 ? logViews[0] : null;
                    serialized = SplitLayoutNode.Leaf(lv?.GetLoadedSourceId(), null, 0, lv?.IsFollowMode ?? true);
                }
                else
                {
                    // Serialize multiple docs as a horizontal branch chain
                    serialized = SplitLayoutNode.Leaf(logViews[^1].GetLoadedSourceId(), null, 0, logViews[^1].IsFollowMode);
                    for (int j = logViews.Count - 2; j >= 0; j--)
                    {
                        var leaf = SplitLayoutNode.Leaf(logViews[j].GetLoadedSourceId(), null, 0, logViews[j].IsFollowMode);
                        serialized = SplitLayoutNode.Branch(SplitOrientation.Vertical, 1.0 / (j + 2), leaf, serialized);
                    }
                }
            }
            else
            {
                var layoutNode = i == ActiveTabIndex
                    ? RootNode
                    : tab.SavedLayout ?? CreateFreshPaneNode();
                serialized = SerializeNode(layoutNode);
            }

            result.Add(new WorkspaceTabLayout
            {
                Id = tab.Id,
                DisplayName = tab.Name,
                Layout = serialized,
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
            var tab = new WorkspaceTabItem(layout.DisplayName, false, layout.Id);

            // In Dock mode, create a Dock layout and restore sources into it
            if (DockFactory is not null && layout.Layout is not null)
            {
                var dockLayout = (IRootDock)DockFactory.CreateLayout();
                DockFactory.InitLayout(dockLayout);
                RestoreSourcesIntoDock(dockLayout, layout.Layout);
                tab.SavedDockLayout = dockLayout;
            }
            else
            {
                tab.SavedLayout = layout.Layout is not null
                    ? DeserializeNode(layout.Layout)
                    : CreateFreshPaneNode();
            }

            Tabs.Add(tab);
        }

        OnPropertyChanged(nameof(HasMultipleTabs));

        var activeIndex = layouts.FindIndex(t => t.IsActive);
        if (activeIndex < 0)
            activeIndex = 0;

        ActivateTab(activeIndex, saveCurrent: false);
    }

    /// <summary>Restores sources from a serialized split layout into a Dock layout's first document.</summary>
    private void RestoreSourcesIntoDock(IRootDock dockLayout, SplitLayoutNode node)
    {
        var sourceIds = new List<(string? SourceId, bool IsFollowMode)>();
        CollectLeafSourceIds(node, sourceIds);

        var docs = DockLayoutHelper.GetAllDocuments(dockLayout);

        // First source goes into the existing document (created by factory)
        for (int i = 0; i < sourceIds.Count; i++)
        {
            LogViewViewModel lv;
            if (i == 0 && docs.Count > 0)
            {
                lv = docs[0].LogView;
            }
            else if (DockFactory is not null)
            {
                var newDoc = (LogViewDocument)DockFactory.CreateDocument();
                var docDock = DockLayoutHelper.FindFirstDocumentDock(dockLayout);
                if (docDock is not null)
                    DockFactory.AddDockable(docDock, newDoc);
                lv = newDoc.LogView;
            }
            else continue;

            InitializeDockDocument(lv);
            lv.IsFollowMode = sourceIds[i].IsFollowMode;
            LoadSourceById(lv, sourceIds[i].SourceId);
        }
    }

    private static void CollectLeafSourceIds(SplitLayoutNode node, List<(string? SourceId, bool IsFollowMode)> result)
    {
        if (node.IsLeaf)
        {
            result.Add((node.SourceId, node.IsFollowMode));
        }
        else
        {
            if (node.Child1 is not null) CollectLeafSourceIds(node.Child1, result);
            if (node.Child2 is not null) CollectLeafSourceIds(node.Child2, result);
        }
    }

    private void LoadSourceById(LogViewViewModel lv, string? sourceId)
    {
        if (string.IsNullOrEmpty(sourceId) || _sourceManager is null) return;

        var source = _sourceManager.Sources.FirstOrDefault(s => s.SourceId == sourceId);
        if (source is null) return;

        if (source.Kind == SourceKind.File && File.Exists(source.PhysicalPath))
            lv.LoadFile(source.PhysicalPath);
        else if (source.Kind == SourceKind.Folder && Directory.Exists(source.PhysicalPath))
            lv.LoadFolder(source.PhysicalPath);
        else if (source.Kind == SourceKind.Merge && source.PhysicalPath.StartsWith("merge://"))
        {
            var ids = source.PhysicalPath.Substring(8).Split('|');
            var sourcesToMerge = _sourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
            if (sourcesToMerge.Count > 0)
                lv.LoadMerge(sourcesToMerge);
        }
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
        // Use Dock API when dock layout is active
        if (Layout is not null && DockFactory is not null)
        {
            SplitDockFocused(horizontal);
            return null; // Dock mode doesn't use PaneNodeViewModel
        }

        if (FocusedPane is null) return null;
        return SplitPane(FocusedPane, horizontal);
    }

    /// <summary>Splits the active Dock document into a new pane using the Dock API.</summary>
    private void SplitDockFocused(bool horizontal)
    {
        if (Layout is null || DockFactory is null) return;

        var activeDoc = DockLayoutHelper.GetActiveDocument(Layout);
        var ownerDock = activeDoc?.Owner as IDock;
        if (ownerDock is null) return;

        // Create a new document with a fresh LogViewViewModel
        var newDoc = (LogViewDocument)DockFactory.CreateDocument();
        var lv = newDoc.LogView;
        if (_sourceManager is not null && _theme is not null)
        {
            lv.Initialize(Clock, _sourceManager, _theme);
            lv.IsFollowMode = _defaultMainFollow;
            lv.Filter.IsFollowMode = _defaultFilterFollow;
            lv.Filter.SearchResultCap = _defaultSearchResultCap;
            lv.Filter.SearchNewestFirst = _defaultSearchNewestFirst;
            lv.IsGridMode = _defaultGridMode;
            lv.GridMultiline = _defaultGridMultiline;
            lv.SetFormattingOptions(_defaultFormattingOptions);
        }

        // horizontal param: true = side-by-side (Right), false = top/bottom (Bottom)
        var operation = horizontal
            ? Dock.Model.Core.DockOperation.Right
            : Dock.Model.Core.DockOperation.Bottom;

        DockFactory.SplitToDock(ownerDock, newDoc, operation);
        OnPropertyChanged(nameof(ActiveLogView));
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
        {
            FocusedPane.IsFocused = false;
            FocusedPane.LogView.SetAsBroadcastSource(false);
        }

        FocusedPane = pane;
        pane.IsFocused = true;
        pane.LogView.SetAsBroadcastSource(true);
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

        // In Dock mode, create a fresh Dock layout for the new tab
        if (DockFactory is not null)
        {
            var newLayout = (IRootDock)DockFactory.CreateLayout();
            DockFactory.InitLayout(newLayout);
            var newTab = new WorkspaceTabItem(name, true);
            Tabs.Add(newTab);
            ActiveTabIndex = Tabs.Count - 1;
            OnPropertyChanged(nameof(HasMultipleTabs));
            Layout = newLayout;
            OnPropertyChanged(nameof(ActiveLogView));
            return;
        }

        var fresh = CreateFreshPaneNode();
        var newTab2 = new WorkspaceTabItem(name, true);
        newTab2.SavedLayout = fresh;
        Tabs.Add(newTab2);

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
        if (Tabs.Count <= 1)
        {
            ResetToSingleTab();
            return;
        }

        bool closingActive = index == ActiveTabIndex;
        if (closingActive)
            SyncActiveTabLayout();

        DisposeTab(Tabs[index]);
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
        for (int i = Tabs.Count - 1; i >= 0; i--)
        {
            if (i != keepIndex)
                DisposeTab(Tabs[i]);
        }

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

    public IReadOnlyList<IWorkspacePane> GetAllPanes()
    {
        if (Layout != null)
        {
            var logViews = DockLayoutHelper.GetAllLogViews(Layout);
            return logViews.ConvertAll(lv => (IWorkspacePane)new DockPaneWrapper(lv));
        }
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

        // When any pane's follow turns off, turn off master follow
        pane.LogView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LogViewViewModel.IsFollowMode) && !pane.LogView.IsFollowMode)
                IsMasterFollowOn = false;
        };
    }

    private void SyncActiveTabLayout()
    {
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
        {
            Tabs[ActiveTabIndex].SavedLayout = RootNode;
            if (Layout is not null)
                Tabs[ActiveTabIndex].SavedDockLayout = Layout;
        }
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

        // Dock mode: restore saved Dock layout (or create fresh)
        if (DockFactory is not null)
        {
            var dockLayout = Tabs[index].SavedDockLayout;
            if (dockLayout is null)
            {
                dockLayout = (IRootDock)DockFactory.CreateLayout();
                DockFactory.InitLayout(dockLayout);
                Tabs[index].SavedDockLayout = dockLayout;
            }
            Layout = dockLayout;
            OnPropertyChanged(nameof(ActiveLogView));
            return;
        }

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

    /// <summary>Disposes all LogViewViewModels held by a tab (both Dock and split-tree layouts).</summary>
    private void DisposeTab(WorkspaceTabItem tab)
    {
        if (tab.SavedDockLayout is not null)
        {
            var docs = DockLayoutHelper.GetAllDocuments(tab.SavedDockLayout);
            foreach (var doc in docs)
            {
                doc.Detach();
                doc.LogView.Dispose();
            }
        }
        else if (tab.SavedLayout is not null)
        {
            var panes = new List<PaneNodeViewModel>();
            CollectPanes(tab.SavedLayout, panes);
            foreach (var pane in panes)
                pane.LogView.Dispose();
        }
    }

    private void ResetToSingleTab()
    {
        foreach (var tab in Tabs)
            DisposeTab(tab);
        Tabs.Clear();

        var newTab = new WorkspaceTabItem("Workspace 1", true);
        Tabs.Add(newTab);
        ActiveTabIndex = 0;

        if (DockFactory is not null)
        {
            var newLayout = (IRootDock)DockFactory.CreateLayout();
            DockFactory.InitLayout(newLayout);
            Layout = newLayout;
        }
        else
        {
            var fresh = CreateFreshPaneNode();
            newTab.SavedLayout = fresh;
            RootNode = fresh;
            FocusPane(fresh);
        }

        OnPropertyChanged(nameof(HasMultipleTabs));
        OnPropertyChanged(nameof(ActiveLogView));
    }

    /// <summary>Returns a list of source titles for the given tab index (for overflow menu display).</summary>
    public List<string> GetTabSourceTitles(int tabIndex)
    {
        if (tabIndex == ActiveTabIndex)
        {
            var panes = GetAllPanes();
            return panes.Select(p => p.LogView.Title).ToList();
        }

        var tab = Tabs[tabIndex];

        // Dock mode: get titles from the saved Dock layout
        if (tab.SavedDockLayout is not null)
        {
            var logViews = DockLayoutHelper.GetAllLogViews(tab.SavedDockLayout);
            if (logViews.Count > 0)
                return logViews.Select(lv => lv.Title).ToList();
            return ["(empty)"];
        }

        if (tab.SavedLayout is null) return ["(empty)"];

        var titles = new List<string>();
        CollectSourceTitles(tab.SavedLayout, titles);
        return titles.Count > 0 ? titles : ["(empty)"];
    }

    private static void CollectSourceTitles(SplitNodeViewModel node, List<string> titles)
    {
        if (node is PaneNodeViewModel pane)
            titles.Add(pane.LogView.Title);
        else if (node is SplitBranchViewModel branch)
        {
            CollectSourceTitles(branch.Child1, titles);
            CollectSourceTitles(branch.Child2, titles);
        }
    }

    public void Dispose()
    {
        UnsubscribeLayoutActiveDockable();
        foreach (var pane in GetAllPanes())
            pane.LogView.Dispose();
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

    /// <summary>Stores this tab's Dock layout (used when Dock mode is active).</summary>
    public IRootDock? SavedDockLayout { get; set; }

    public WorkspaceTabItem(string name, bool isActive, string? id = null)
    {
        _id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        _name = name;
        _isActive = isActive;
    }
}
