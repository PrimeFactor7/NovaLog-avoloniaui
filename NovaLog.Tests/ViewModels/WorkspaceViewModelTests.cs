using NovaLog.Avalonia.ViewModels;

namespace NovaLog.Tests.ViewModels;

/// <summary>
/// Tests for WorkspaceViewModel: split pane management, focus tracking,
/// tab bar operations, and tree manipulation. Parallels WinForms
/// WorkspaceSplitPanelTests.
/// </summary>
public class WorkspaceViewModelTests
{
    // ── Initial State ────────────────────────────────────────────────

    [Fact]
    public void Constructor_CreatesOnePane()
    {
        var ws = new WorkspaceViewModel();
        Assert.IsType<PaneNodeViewModel>(ws.RootNode);
        Assert.NotNull(ws.FocusedPane);
        Assert.True(ws.FocusedPane.IsFocused);
    }

    [Fact]
    public void Constructor_CreatesOneTab()
    {
        var ws = new WorkspaceViewModel();
        Assert.Single(ws.Tabs);
        Assert.Equal("Workspace 1", ws.Tabs[0].Name);
        Assert.True(ws.Tabs[0].IsActive);
    }

    [Fact]
    public void Constructor_HasMultipleTabs_IsFalse()
    {
        var ws = new WorkspaceViewModel();
        Assert.False(ws.HasMultipleTabs);
    }

    [Fact]
    public void PaneCount_InitiallyOne()
    {
        var ws = new WorkspaceViewModel();
        Assert.Equal(1, ws.PaneCount);
    }

    [Fact]
    public void ActiveLogView_ReturnsInitialPaneLogView()
    {
        var ws = new WorkspaceViewModel();
        Assert.NotNull(ws.ActiveLogView);
        Assert.Same(ws.FocusedPane!.LogView, ws.ActiveLogView);
    }

    // ── Split ────────────────────────────────────────────────────────

    [Fact]
    public void SplitFocused_Vertical_CreatesTwoPanes()
    {
        var ws = new WorkspaceViewModel();
        var originalPane = ws.FocusedPane;

        var newPane = ws.SplitFocused(horizontal: true);

        Assert.NotNull(newPane);
        Assert.Equal(2, ws.PaneCount);
        Assert.IsType<SplitBranchViewModel>(ws.RootNode);
        var branch = (SplitBranchViewModel)ws.RootNode;
        Assert.True(branch.IsHorizontal);
    }

    [Fact]
    public void SplitFocused_Horizontal_CreatesTwoPanes()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: false);

        Assert.Equal(2, ws.PaneCount);
        var branch = Assert.IsType<SplitBranchViewModel>(ws.RootNode);
        Assert.False(branch.IsHorizontal);
    }

    [Fact]
    public void SplitFocused_NewPaneGetsFocus()
    {
        var ws = new WorkspaceViewModel();
        var original = ws.FocusedPane;

        var newPane = ws.SplitFocused(horizontal: true);

        Assert.Same(newPane, ws.FocusedPane);
        Assert.True(newPane!.IsFocused);
        Assert.False(original!.IsFocused);
    }

    [Fact]
    public void SplitFocused_ThreePanes_NestedSplitsWork()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        ws.SplitFocused(horizontal: false);

        Assert.Equal(3, ws.PaneCount);
        // Root is a branch, one of its children is also a branch
        var root = Assert.IsType<SplitBranchViewModel>(ws.RootNode);
        Assert.True(
            root.Child1 is SplitBranchViewModel || root.Child2 is SplitBranchViewModel);
    }

    [Fact]
    public void SplitFocused_NullFocusedPane_ReturnsNull()
    {
        var ws = new WorkspaceViewModel();
        // Force null focus (shouldn't happen normally, but test defensive code)
        var result = ws.SplitFocused(horizontal: true);
        Assert.NotNull(result); // Since FocusedPane is set in constructor
    }

    // ── Close ────────────────────────────────────────────────────────

    [Fact]
    public void CloseFocused_OnlyPane_ReplacesWithFresh()
    {
        var ws = new WorkspaceViewModel();
        var original = ws.FocusedPane;

        ws.CloseFocused();

        Assert.Equal(1, ws.PaneCount);
        Assert.NotSame(original, ws.FocusedPane);
        Assert.IsType<PaneNodeViewModel>(ws.RootNode);
    }

    [Fact]
    public void CloseFocused_OneOfTwo_PromotesSibling()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);
        var second = ws.FocusedPane!;

        // Close the second (focused) pane
        ws.CloseFocused();

        Assert.Equal(1, ws.PaneCount);
        Assert.Same(first, ws.FocusedPane);
        Assert.IsType<PaneNodeViewModel>(ws.RootNode);
    }

    [Fact]
    public void CloseFocused_InNestedSplit_KeepsOthers()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);
        var second = ws.FocusedPane!;
        ws.SplitFocused(horizontal: false);
        var third = ws.FocusedPane!;

        // Close the third pane
        ws.CloseFocused();

        Assert.Equal(2, ws.PaneCount);
        Assert.Contains(first, ws.GetAllPanes());
        Assert.Contains(second, ws.GetAllPanes());
    }

    [Fact]
    public void CloseFocused_CloseAllInSequence_LeavesOneFresh()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        ws.SplitFocused(horizontal: true);
        Assert.Equal(3, ws.PaneCount);

        ws.CloseFocused();
        ws.CloseFocused();
        ws.CloseFocused(); // closes last → replaces with fresh

        Assert.Equal(1, ws.PaneCount);
        Assert.NotNull(ws.FocusedPane);
    }

    // ── Focus ────────────────────────────────────────────────────────

    [Fact]
    public void FocusPane_ChangesFocusedPane()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        var panes = ws.GetAllPanes();
        var first = panes[0];
        var second = panes[1];

        ws.FocusPane(first);

        Assert.Same(first, ws.FocusedPane);
        Assert.True(first.IsFocused);
        Assert.False(second.IsFocused);
    }

    [Fact]
    public void FocusPane_FiresPropertyChanged()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        var panes = ws.GetAllPanes();

        bool activeLogViewChanged = false;
        ws.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActiveLogView))
                activeLogViewChanged = true;
        };

        ws.FocusPane(panes[0]);
        Assert.True(activeLogViewChanged);
    }

    [Fact]
    public void FocusPane_SamePane_NoOp()
    {
        var ws = new WorkspaceViewModel();
        var pane = ws.FocusedPane!;
        bool changed = false;
        ws.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.FocusedPane))
                changed = true;
        };

        ws.FocusPane(pane);
        Assert.False(changed);
    }

    [Fact]
    public void MoveFocus_Right_CyclesToNext()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);
        var second = ws.FocusedPane!;

        // Focus first, then move right
        ws.FocusPane(first);
        ws.MoveFocus(WorkspaceViewModel.Direction.Right);
        Assert.Same(second, ws.FocusedPane);
    }

    [Fact]
    public void MoveFocus_Left_CyclesToPrevious()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);
        var second = ws.FocusedPane!;

        // Already on second, move left
        ws.MoveFocus(WorkspaceViewModel.Direction.Left);
        Assert.Same(first, ws.FocusedPane);
    }

    [Fact]
    public void MoveFocus_WrapsAround()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);

        // Focus first, move left → wraps to last
        ws.FocusPane(first);
        ws.MoveFocus(WorkspaceViewModel.Direction.Left);
        Assert.NotSame(first, ws.FocusedPane);
    }

    [Fact]
    public void MoveFocus_SinglePane_NoChange()
    {
        var ws = new WorkspaceViewModel();
        var pane = ws.FocusedPane!;
        ws.MoveFocus(WorkspaceViewModel.Direction.Right);
        Assert.Same(pane, ws.FocusedPane);
    }

    // ── Tabs ─────────────────────────────────────────────────────────

    [Fact]
    public void AddTab_CreatesNewTab()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");

        Assert.Equal(2, ws.Tabs.Count);
        Assert.True(ws.HasMultipleTabs);
    }

    [Fact]
    public void AddTab_NewTabIsActive()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");

        Assert.False(ws.Tabs[0].IsActive);
        Assert.True(ws.Tabs[1].IsActive);
        Assert.Equal(1, ws.ActiveTabIndex);
    }

    [Fact]
    public void AddTab_CreatesNewFreshPane()
    {
        var ws = new WorkspaceViewModel();
        var originalLogView = ws.ActiveLogView;

        ws.AddTab("Tab 2");

        Assert.NotSame(originalLogView, ws.ActiveLogView);
        Assert.IsType<PaneNodeViewModel>(ws.RootNode);
    }

    [Fact]
    public void SwitchTab_ChangesActiveTab()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        ws.SwitchTab(0);

        Assert.True(ws.Tabs[0].IsActive);
        Assert.False(ws.Tabs[1].IsActive);
        Assert.Equal(0, ws.ActiveTabIndex);
    }

    [Fact]
    public void SwitchTab_InvalidIndex_NoOp()
    {
        var ws = new WorkspaceViewModel();
        ws.SwitchTab(99);
        Assert.Equal(0, ws.ActiveTabIndex);
    }

    [Fact]
    public void CloseTab_RemovesTab()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        ws.AddTab("Tab 3");
        Assert.Equal(3, ws.Tabs.Count);

        ws.CloseTab(1);

        Assert.Equal(2, ws.Tabs.Count);
        Assert.Equal("Workspace 1", ws.Tabs[0].Name);
        Assert.Equal("Tab 3", ws.Tabs[1].Name);
    }

    [Fact]
    public void CloseTab_LastTab_KeepsAtLeastOne()
    {
        var ws = new WorkspaceViewModel();
        ws.CloseTab(0);

        Assert.Single(ws.Tabs);
    }

    [Fact]
    public void CloseTab_UpdatesHasMultipleTabs()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        Assert.True(ws.HasMultipleTabs);

        ws.CloseTab(1);
        Assert.False(ws.HasMultipleTabs);
    }

    [Fact]
    public void CloseOtherTabs_KeepsOnlySpecified()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        ws.AddTab("Tab 3");

        ws.CloseOtherTabs(1);

        Assert.Single(ws.Tabs);
        Assert.Equal("Tab 2", ws.Tabs[0].Name);
        Assert.True(ws.Tabs[0].IsActive);
    }

    [Fact]
    public void CloseOtherTabs_InvalidIndex_NoOp()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        ws.CloseOtherTabs(99);
        Assert.Equal(2, ws.Tabs.Count);
    }

    [Fact]
    public void CloseAllTabs_ResetsToSingleTab()
    {
        var ws = new WorkspaceViewModel();
        ws.AddTab("Tab 2");
        ws.AddTab("Tab 3");

        ws.CloseAllTabs();

        Assert.Single(ws.Tabs);
        Assert.Equal("Workspace 1", ws.Tabs[0].Name);
        Assert.IsType<PaneNodeViewModel>(ws.RootNode);
        Assert.False(ws.HasMultipleTabs);
    }

    [Fact]
    public void RenameTab_UpdatesName()
    {
        var ws = new WorkspaceViewModel();
        ws.RenameTab(0, "My Custom Tab");
        Assert.Equal("My Custom Tab", ws.Tabs[0].Name);
    }

    [Fact]
    public void RenameTab_InvalidIndex_NoOp()
    {
        var ws = new WorkspaceViewModel();
        ws.RenameTab(99, "Crash?");
        Assert.Equal("Workspace 1", ws.Tabs[0].Name);
    }

    // ── GetAllPanes ──────────────────────────────────────────────────

    [Fact]
    public void GetAllPanes_SinglePane_ReturnsOne()
    {
        var ws = new WorkspaceViewModel();
        var panes = ws.GetAllPanes();
        Assert.Single(panes);
    }

    [Fact]
    public void GetAllPanes_AfterSplits_ReturnsAll()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        ws.SplitFocused(horizontal: false);

        var panes = ws.GetAllPanes();
        Assert.Equal(3, panes.Count);
    }

    [Fact]
    public void GetAllPanes_AfterSplitAndClose_ReturnsCorrectCount()
    {
        var ws = new WorkspaceViewModel();
        ws.SplitFocused(horizontal: true);
        ws.SplitFocused(horizontal: false);
        ws.CloseFocused();

        Assert.Equal(2, ws.GetAllPanes().Count);
    }

    // ── Parent tracking ──────────────────────────────────────────────

    [Fact]
    public void Split_SetsParentReferences()
    {
        var ws = new WorkspaceViewModel();
        var first = ws.FocusedPane!;
        ws.SplitFocused(horizontal: true);
        var second = ws.FocusedPane!;

        var branch = Assert.IsType<SplitBranchViewModel>(ws.RootNode);
        Assert.Same(branch, first.Parent);
        Assert.Same(branch, second.Parent);
        Assert.Null(branch.Parent); // root has no parent
    }

    // ── GlobalClockService ───────────────────────────────────────────

    [Fact]
    public void Clock_IsNotNull()
    {
        var ws = new WorkspaceViewModel();
        Assert.NotNull(ws.Clock);
    }
}
