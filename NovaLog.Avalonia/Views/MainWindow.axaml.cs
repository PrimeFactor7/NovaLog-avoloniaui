using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NovaLog.Avalonia.Services;
using NovaLog.Avalonia.ViewModels;
using AvaloniaApp = Avalonia.Application;

namespace NovaLog.Avalonia.Views;

public partial class MainWindow : Window
{
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

        // Tab bar: handle tab clicks, middle-click close, and context menu
        TabBar.AddHandler(Button.ClickEvent, OnTabButtonClick);
        TabBar.AddHandler(PointerPressedEvent, OnTabBarPointerPressed, handledEventsToo: true);
        TabBar.AddHandler(MenuItem.ClickEvent, OnTabContextMenuClick);
    }

    private void OnTabButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn && btn.DataContext is WorkspaceTabItem tab &&
            DataContext is MainWindowViewModel vm)
        {
            var idx = vm.Workspace.Tabs.IndexOf(tab);
            if (idx >= 0)
                vm.Workspace.SwitchTab(idx);
        }
    }

    private void OnTabBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Middle-click on tab button → close tab
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed &&
            e.Source is Button btn && btn.DataContext is WorkspaceTabItem tab &&
            DataContext is MainWindowViewModel vm)
        {
            var idx = vm.Workspace.Tabs.IndexOf(tab);
            if (idx >= 0)
                vm.Workspace.CloseTab(idx);
            e.Handled = true;
        }
    }

    private async void OnTabContextMenuClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not MenuItem mi || DataContext is not MainWindowViewModel vm) return;

        // Walk up to find which tab this context menu belongs to
        var contextMenu = mi.Parent as ContextMenu;
        if (contextMenu?.DataContext is not WorkspaceTabItem tab) return;

        var idx = vm.Workspace.Tabs.IndexOf(tab);
        if (idx < 0) return;

        var tag = mi.Tag as string;
        switch (tag)
        {
            case "rename":
                var dialog = new InputDialog("Rename Tab", "Enter tab name:", tab.Name);
                var newName = await dialog.ShowDialog<string?>(this);
                if (!string.IsNullOrWhiteSpace(newName))
                    vm.Workspace.RenameTab(idx, newName);
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
        if (DataContext is MainWindowViewModel vm &&
            AvaloniaApp.Current is { } app)
        {
            var mapper = new ThemeMapper(vm.ThemeService);
            mapper.ApplyTheme(app);
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
            vm.SaveSettings();
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Log File",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Log Files") { Patterns = ["*.log", "*.txt", "*.*"] }]
        });

        if (result.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = result[0].Path.LocalPath;
            vm.LoadFile(path);
            vm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.File);

            var memoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
            var privateMb = Process.GetCurrentProcess().PrivateMemorySize64 / (1024.0 * 1024.0);
            Console.WriteLine($"[BENCHMARK] Post-load Memory (WorkingSet): {memoryMb:F1} MB");
            Console.WriteLine($"[BENCHMARK] Post-load Memory (PrivateBytes): {privateMb:F1} MB");
        }
    }

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[WINDOW] OnOpenFolderClick called");

        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Log Folder",
            AllowMultiple = false
        });

        System.Diagnostics.Debug.WriteLine($"[WINDOW] Folder picker returned {result.Count} results");

        if (result.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = result[0].Path.LocalPath;
            System.Diagnostics.Debug.WriteLine($"[WINDOW] Selected folder: {path}");
            System.Diagnostics.Debug.WriteLine($"[WINDOW] Calling vm.LoadFolder");
            vm.LoadFolder(path);
            vm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.Folder);
            System.Diagnostics.Debug.WriteLine($"[WINDOW] OnOpenFolderClick completed");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[WINDOW] No folder selected or VM is null");
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
            // Ctrl+\ — split vertical (side-by-side)
            vm.Workspace.SplitFocused(horizontal: true);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemBackslash && shift)
        {
            // Ctrl+Shift+\ — split horizontal (stacked)
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
            // F — toggle follow mode (only when no text input is focused)
            if (FocusManager?.GetFocusedElement() is not TextBox && vm.Workspace.ActiveLogView != null)
            {
                vm.Workspace.ActiveLogView.IsFollowMode = !vm.Workspace.ActiveLogView.IsFollowMode;
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
