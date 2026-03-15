using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NovaLog.Avalonia.Services;
using NovaLog.Avalonia.ViewModels;
using AvaloniaApp = Avalonia.Application;

namespace NovaLog.Avalonia.Views;

public partial class MainWindow : Window
{
    private const string TabDragFormat = "NovaLogTab";
    private ContextMenu? _tabOverflowMenu;
    private Point _tabDragStartPoint;
    private bool _tabDragPending;
    private WorkspaceTabItem? _tabDragItem;
    private PointerPressedEventArgs? _tabDragPressArgs;

    public MainWindow()
    {
        InitializeComponent();

        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.Settings.EditHighlightRulesRequested += async () =>
            {
                var dialog = new HighlightRulesDialog
                {
                    DataContext = new HighlightRulesViewModel(vm.Workspace.ActiveLogView?.HighlightRules ?? [])
                };
                await dialog.ShowDialog(this);
            };
        }

        SidebarPanel.AddFileRequested += OnOpenFileClick;
        SidebarPanel.AddFolderRequested += OnOpenFolderClick;

        // Tab bar: handle tab clicks and middle-click close
        TabBar.AddHandler(Button.ClickEvent, OnTabButtonClick);
        TabBar.AddHandler(PointerPressedEvent, OnTabBarPointerPressed, handledEventsToo: true);
        TabBar.AddHandler(PointerMovedEvent, OnTabBarPointerMoved, handledEventsToo: true);

        // Tab bar drag-drop reorder
        DragDrop.SetAllowDrop(TabBar, true);
        TabBar.AddHandler(DragDrop.DragOverEvent, OnTabBarDragOver);
        TabBar.AddHandler(DragDrop.DropEvent, OnTabBarDrop);
    }

    private void OnTabButtonClick(object? sender, RoutedEventArgs e)
    {
        // Ignore clicks on the tab close button (handled by OnTabCloseClick)
        if (e.Source is Button { Tag: "tab-close" })
            return;

        if (e.Source is Button btn && btn.DataContext is WorkspaceTabItem tab &&
            DataContext is MainWindowViewModel vm)
        {
            var idx = vm.Workspace.Tabs.IndexOf(tab);
            if (idx >= 0)
                vm.Workspace.SwitchTab(idx);
        }
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WorkspaceTabItem tab &&
            DataContext is MainWindowViewModel vm)
        {
            var idx = vm.Workspace.Tabs.IndexOf(tab);
            if (idx >= 0)
                vm.Workspace.CloseTab(idx);
            e.Handled = true;
        }
    }

    private void OnTabOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (DataContext is not MainWindowViewModel vm) return;

        _tabOverflowMenu ??= new ContextMenu();
        _tabOverflowMenu.Items.Clear();

        for (int i = 0; i < vm.Workspace.Tabs.Count; i++)
        {
            var tab = vm.Workspace.Tabs[i];
            var tabId = tab.Id; // capture stable ID, not index

            if (i > 0)
                _tabOverflowMenu.Items.Add(new Separator());

            var tabItem = new MenuItem
            {
                Header = tab.IsActive ? $"\u25B6 {tab.Name}" : $"   {tab.Name}",
                FontWeight = global::Avalonia.Media.FontWeight.Bold,
            };
            tabItem.Click += (_, _) =>
            {
                var idx = vm.Workspace.Tabs.Select((t, j) => (t, j)).FirstOrDefault(x => x.t.Id == tabId).j;
                if (idx >= 0 && idx < vm.Workspace.Tabs.Count)
                    vm.Workspace.SwitchTab(idx);
            };
            _tabOverflowMenu.Items.Add(tabItem);

            var titles = vm.Workspace.GetTabSourceTitles(i);
            foreach (var title in titles)
            {
                _tabOverflowMenu.Items.Add(new MenuItem
                {
                    Header = $"      {title}",
                    IsEnabled = false,
                    FontSize = 11,
                });
            }
        }

        _tabOverflowMenu.Open(btn);
    }

    private void OnTabBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        // Middle-click on tab button → close tab
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var source = e.Source as Control;
            while (source is not null and not Button)
                source = source.GetVisualParent() as Control;

            if (source is Button btn && btn.DataContext is WorkspaceTabItem tab)
            {
                var idx = vm.Workspace.Tabs.IndexOf(tab);
                if (idx >= 0)
                    vm.Workspace.CloseTab(idx);
                e.Handled = true;
            }
            return;
        }

        // Left-click: start drag tracking for tab reorder
        if (point.Properties.IsLeftButtonPressed)
        {
            var source = e.Source as Control;
            while (source is not null and not Button)
                source = source.GetVisualParent() as Control;

            if (source is Button btn && btn.DataContext is WorkspaceTabItem tab && btn.Tag as string != "tab-close")
            {
                _tabDragStartPoint = e.GetPosition(TabBar);
                _tabDragPending = true;
                _tabDragItem = tab;
                _tabDragPressArgs = e; // Store the original press args for DoDragDropAsync
            }
        }
    }

    private async void OnTabBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_tabDragPending || _tabDragItem is null || _tabDragPressArgs is null) return;

        var pos = e.GetPosition(TabBar);
        var delta = pos - _tabDragStartPoint;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;

        _tabDragPending = false;
        var pressArgs = _tabDragPressArgs;
        _tabDragPressArgs = null;

        var transfer = new DataTransfer();
        var format = DataFormat.CreateStringApplicationFormat(TabDragFormat);
        transfer.Add(DataTransferItem.Create(format, _tabDragItem.Id));
        await DragDrop.DoDragDropAsync(pressArgs, transfer, DragDropEffects.Move);
        _tabDragItem = null;
    }

    private void OnTabBarDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(TabDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTabBarDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(TabDragFormat)) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var draggedId = e.Data.Get(TabDragFormat) as string;
        if (string.IsNullOrEmpty(draggedId)) return;

        var fromIdx = vm.Workspace.Tabs.Select((t, i) => (t, i)).FirstOrDefault(x => x.t.Id == draggedId).i;

        // Determine target index from pointer X position
        var pos = e.GetPosition(TabBar);
        var toIdx = GetTabIndexFromPosition(vm, pos.X);

        vm.Workspace.ReorderTab(fromIdx, toIdx);
        e.Handled = true;
    }

    private int GetTabIndexFromPosition(MainWindowViewModel vm, double x)
    {
        // Walk the visual children of the tab bar's panel to find insertion point
        var panel = TabBar.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault();
        if (panel is null) return vm.Workspace.Tabs.Count - 1;

        double accum = 0;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            var midpoint = accum + child.Bounds.Width / 2;
            if (x < midpoint) return i;
            accum += child.Bounds.Width + 2; // 2 = spacing
        }
        return vm.Workspace.Tabs.Count - 1;
    }

    private async void OnTabContextMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || DataContext is not MainWindowViewModel vm) return;

        // MenuItem inherits DataContext from the Button it's attached to
        if (mi.DataContext is not WorkspaceTabItem tab) return;

        var idx = vm.Workspace.Tabs.IndexOf(tab);
        if (idx < 0) return;

        switch (mi.Tag as string)
        {
            case "rename":
                var dialog = new InputDialog("Rename Tab", "Enter tab name:", tab.Name);
                var newName = await dialog.ShowDialog<string?>(this);
                if (!string.IsNullOrWhiteSpace(newName))
                    vm.Workspace.RenameTab(idx, newName);
                break;
            case "closepanes":
                vm.Workspace.SwitchTab(idx);
                vm.CloseAllPanesInActiveTabCommand.Execute(null);
                break;
            case "closeothers":
                vm.Workspace.CloseOtherTabs(idx);
                break;
            case "closeall":
                vm.Workspace.CloseAllTabs();
                break;
            case "close":
                vm.Workspace.CloseTab(idx);
                break;
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (AvaloniaApp.Current is { } app)
            {
                var mapper = new ThemeMapper(vm.ThemeService);
                mapper.ApplyTheme(app);
            }

            // Monitor Explosion: provide window reference for screen detection
            vm.SetMainWindow(this);

            // Tab Fairness: register split-size checker that inspects the active pane's visual bounds
            vm.Workspace.SplitSizeChecker = horizontal =>
            {
                // Find the focused LogViewPanel in the visual tree
                var dockControl = this.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockControl>().FirstOrDefault();
                if (dockControl is null) return true;

                // Find the active LogViewPanel
                var panels = dockControl.GetVisualDescendants().OfType<LogViewPanel>().ToList();
                var activePanel = panels.FirstOrDefault(p => p.DataContext == vm.Workspace.ActiveLogView);
                if (activePanel is null) return true;

                // Check if splitting would create a pane below 250px
                return horizontal
                    ? activePanel.Bounds.Width >= 500
                    : activePanel.Bounds.Height >= 500;
            };

            // Restore window state
            var (w, h, maximized) = vm.GetSavedWindowState();
            if (w > 0 && h > 0)
            {
                Width = w;
                Height = h;
            }
            if (maximized)
                WindowState = WindowState.Maximized;
        }

        Program.StartupStopwatch.Stop();
        var startupMs = Program.StartupStopwatch.ElapsedMilliseconds;
        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        var privateMb = Process.GetCurrentProcess().PrivateMemorySize64 / (1024.0 * 1024.0);

        Console.WriteLine($"[BENCHMARK] Startup: {startupMs}ms");
        Console.WriteLine($"[BENCHMARK] Memory (WorkingSet): {memoryMb:F1} MB");
        Console.WriteLine($"[BENCHMARK] Memory (PrivateBytes): {privateMb:F1} MB");
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SaveWindowState(
                (int)Width,
                (int)Height,
                WindowState == WindowState.Maximized);
            vm.SaveSettings();
            vm.SaveDockLayout();
            vm.Dispose();
        }
    }

    private async Task<IStorageFolder?> GetLastDirectoryFolder()
    {
        if (DataContext is MainWindowViewModel vm && !string.IsNullOrEmpty(vm.LastDirectory))
        {
            try { return await StorageProvider.TryGetFolderFromPathAsync(vm.LastDirectory); }
            catch { return null; }
        }
        return null;
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var startDir = await GetLastDirectoryFolder();
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Log File",
            AllowMultiple = false,
            SuggestedStartLocation = startDir,
            FileTypeFilter = [new FilePickerFileType("Log Files") { Patterns = ["*.log", "*.txt", "*.*"] }]
        });

        if (result.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = result[0].Path.LocalPath;
            vm.LastDirectory = Path.GetDirectoryName(path);
            vm.LoadFile(path);
            vm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.File);
        }
    }

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var startDir = await GetLastDirectoryFolder();
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Log Folder",
            AllowMultiple = false,
            SuggestedStartLocation = startDir
        });

        if (result.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = result[0].Path.LocalPath;
            vm.LastDirectory = Path.GetDirectoryName(path) ?? path;
            vm.LoadFolder(path);
            vm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.Folder);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            base.OnKeyDown(e);
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == Key.OemBackslash && !shift)
        {
            // Ctrl+\ — split horizontal (side-by-side)
            vm.Workspace.SplitFocused(horizontal: true);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemBackslash && shift)
        {
            // Ctrl+Shift+\ — split vertical (stacked)
            vm.Workspace.SplitFocused(horizontal: false);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W)
        {
            // Ctrl+W — close focused pane
            vm.Workspace.CloseFocused();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.T)
        {
            // Ctrl+T — new tab
            vm.Workspace.AddTab($"Workspace {vm.Workspace.Tabs.Count + 1}");
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Left)
        {
            vm.Workspace.MoveFocus(WorkspaceViewModel.Direction.Left);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Right)
        {
            vm.Workspace.MoveFocus(WorkspaceViewModel.Direction.Right);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Up)
        {
            vm.Workspace.MoveFocus(WorkspaceViewModel.Direction.Up);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Down)
        {
            vm.Workspace.MoveFocus(WorkspaceViewModel.Direction.Down);
            e.Handled = true;
        }
        else if (e.Key == Key.T && !ctrl && !shift && !alt)
        {
            // T — toggle stay-on-top (only when no text input is focused)
            if (FocusManager?.GetFocusedElement() is not TextBox)
            {
                vm.IsTopmost = !vm.IsTopmost;
                Topmost = vm.IsTopmost;
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }
        else if (e.Key == Key.F && !ctrl && !shift && !alt)
        {
            // F — toggle master follow (all panes, only when no text input is focused)
            if (FocusManager?.GetFocusedElement() is not TextBox)
            {
                vm.Workspace.IsMasterFollowOn = !vm.Workspace.IsMasterFollowOn;
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }
        else if (e.Key == Key.M && !ctrl && !shift && !alt)
        {
            // M — toggle minimap (only when no text input is focused)
            if (FocusManager?.GetFocusedElement() is not TextBox)
            {
                vm.Settings.MinimapVisible = !vm.Settings.MinimapVisible;
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }
        else if (ctrl && e.Key == Key.E)
        {
            // Ctrl+E — toggle source manager sidebar
            vm.IsSidebarVisible = !vm.IsSidebarVisible;
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemComma)
        {
            // Ctrl+, — toggle settings flyout
            vm.Settings.IsVisible = !vm.Settings.IsVisible;
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.O && !shift)
        {
            // Ctrl+O — open folder
            OnOpenFolderClick(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.O)
        {
            // Ctrl+Shift+O — open file
            OnOpenFileClick(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Escape — close settings flyout if open
            if (vm.Settings.IsVisible)
            {
                vm.Settings.IsVisible = false;
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }
        else
        {
            base.OnKeyDown(e);
        }
    }
}
