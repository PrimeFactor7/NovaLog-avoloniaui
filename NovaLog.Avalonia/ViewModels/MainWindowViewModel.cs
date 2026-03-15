using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using NovaLog.Avalonia.Controls;
using NovaLog.Avalonia.Docking;
using NovaLog.Avalonia.Services;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Core.Theme;
using System.ComponentModel;

namespace NovaLog.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private WorkspaceViewModel _workspace;
    [ObservableProperty] private SourceManagerViewModel _sourceManager;
    [ObservableProperty] private SettingsViewModel _settings;
    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private bool _isTopmost;
    [ObservableProperty] private string _themeLabel = "Dark";

    public ThemeService ThemeService { get; } = new();
    private readonly WorkspaceManager _workspaceManager = new();
    private MonitorManager? _monitorManager;
    private AppSettings _appSettings = new();
    private LogViewViewModel? _observedActiveLogView;
    private FilterPanelViewModel? _observedActiveFilter;
    private EventHandler<Dock.Model.Core.Events.DockableClosedEventArgs>? _dockableClosedHandler;
    private Action<string, SourceKind>? _sourceSelectedHandler;
    private Action<string, SourceKind>? _sourceNewTabHandler;
    private Func<string, Task<string?>>? _aliasInputHandler;
    private Action? _closeRequestedHandler;
    private Action<SourceItemViewModel>? _sourceRemovedHandler;

    public MainWindowViewModel()
    {
        Workspace = new WorkspaceViewModel();
        SourceManager = new SourceManagerViewModel();
        Settings = new SettingsViewModel();

        _appSettings = SettingsManager.Load();
        Settings.LoadFrom(_appSettings);
        ApplySettingsToTheme();

        // Load recent sources
        foreach (var recent in _appSettings.RecentSources)
            SourceManager.RecentSources.Add(recent);

        Workspace.Initialize(SourceManager, ThemeService);
        Workspace.SetFollowDefaults(Settings.MainFollowEnabled, Settings.FilterFollowEnabled, applyToExisting: true);
        Workspace.IsMasterFollowOn = Settings.MainFollowEnabled;
        Workspace.SetGridModeDefault(Settings.DefaultGridMode, applyToExisting: true);
        Workspace.SetGridMultilineDefault(Settings.GridMultiline, applyToExisting: true);
        Workspace.SetFormattingOptions(BuildFormattingOptions(), applyToExisting: true);
        Workspace.SetSearchDefaults(Settings.SearchResultCap, Settings.SearchNewestFirst, applyToExisting: true);
        LogLineRow.RowHeight = Settings.LineHeight;
        CreateDockLayout();
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        AttachActiveLogView(Workspace.ActiveLogView);

        Settings.SettingsChanged += OnSettingsChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        _sourceSelectedHandler = (path, kind) =>
        {
            if (kind == SourceKind.File) Workspace.ActiveLogView?.LoadFile(path);
            else if (kind == SourceKind.Folder) Workspace.ActiveLogView?.LoadFolder(path);
            else if (kind == SourceKind.Merge && path.StartsWith("merge://"))
            {
                var ids = path.Substring(8).Split('|');
                var sourcesToMerge = SourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                Workspace.ActiveLogView?.LoadMerge(sourcesToMerge);
            }
            if (Workspace.ActiveLogView is { } alv)
                RestoreBookmarksForPane(alv);
        };
        SourceManager.SourceSelected += _sourceSelectedHandler;

        _sourceNewTabHandler = (path, kind) =>
        {
            // If the active pane is empty, load into it directly instead of creating a new tab
            var active = Workspace.ActiveLogView;
            if (active is null || !active.IsEmpty)
                Workspace.AddTab($"Workspace {Workspace.Tabs.Count + 1}");

            if (kind == SourceKind.File) Workspace.ActiveLogView?.LoadFile(path);
            else if (kind == SourceKind.Folder) Workspace.ActiveLogView?.LoadFolder(path);
            else if (kind == SourceKind.Merge && path.StartsWith("merge://"))
            {
                var ids = path.Substring(8).Split('|');
                var sourcesToMerge = SourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                Workspace.ActiveLogView?.LoadMerge(sourcesToMerge);
            }
        };
        SourceManager.SourceNewTabRequested += _sourceNewTabHandler;

        _aliasInputHandler = async (oldAlias) =>
        {
            try
            {
                var dialog = new Views.InputDialog("Set Alias", "Enter display name:", oldAlias);
                if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                    return await dialog.ShowDialog<string?>(desktop.MainWindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AliasInputRequested failed: {ex.Message}");
            }
            return null;
        };
        SourceManager.AliasInputRequested += _aliasInputHandler;

        _closeRequestedHandler = () => IsSidebarVisible = false;
        SourceManager.CloseRequested += _closeRequestedHandler;

        _sourceRemovedHandler = (removedSource) =>
        {
            foreach (var pane in Workspace.GetAllPanes())
                pane.LogView.ClearIfSourceRemoved(removedSource);
        };
        SourceManager.SourceRemoved += _sourceRemovedHandler;

        LoadSession();
        AttachActiveLogView(Workspace.ActiveLogView);
        RaiseStatusProperties();
        ApplySettingsToTheme();
    }

    /// <summary>Builds or restores Dock layout. If layout.json is missing or invalid, creates a fresh layout.
    /// To force a fresh layout (e.g. empty document pane after restore), delete <see cref="LayoutPersistence.GetLayoutPath"/>.</summary>
    private void CreateDockLayout()
    {
        var factory = new NovaLogDockFactory();
        var layout = LayoutPersistence.Load();
        if (layout is null)
        {
            layout = (IRootDock)factory.CreateLayout();
            factory.InitLayout(layout);
        }
        else
        {
            try
            {
                factory.InitLayout(layout);
            }
            catch
            {
                layout = (IRootDock)factory.CreateLayout();
                factory.InitLayout(layout);
            }
        }
        // When a document is closed, detach its event handler and dispose it.
        // If the last document is closed, create a fresh empty one.
        _dockableClosedHandler = (_, args) =>
        {
            if (args.Dockable is not Docking.LogViewDocument closedDoc) return;
            closedDoc.Detach();
            closedDoc.LogView.Dispose();

            var remaining = Docking.DockLayoutHelper.GetAllDocuments(Workspace.Layout);
            if (remaining.Count > 0) return;

            // All documents gone — add a fresh one
            var newDoc = (Docking.LogViewDocument)factory.CreateDocument();
            Workspace.InitializeDockDocument(newDoc.LogView);
            var docDock = DockLayoutHelper.FindFirstDocumentDock(Workspace.Layout);
            if (docDock is not null)
            {
                factory.AddDockable(docDock, newDoc);
                docDock.ActiveDockable = newDoc;
            }
        };
        factory.DockableClosed += _dockableClosedHandler;

        Workspace.DockFactory = factory;
        Workspace.Layout = layout;
    }

    /// <summary>Called on window close to persist Dock layout to %AppData%/NovaLog/layout.json.</summary>
    public void SaveDockLayout()
    {
        if (Workspace.Layout is { } layout)
            LayoutPersistence.Save(layout);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeService.CycleTheme();
        ThemeLabel = ThemeService.CurrentTheme.Name;
        Settings.Theme = ThemeLabel;
        SaveSettings();
    }

    private void ApplySettingsToTheme()
    {
        ThemeService.SetTheme(Settings.Theme);
        ThemeLabel = ThemeService.CurrentTheme.Name;
        
        ThemeService.SetTimestampOverride(Settings.TimestampColorEnabled ? Settings.TimestampColor.ToString() : null);
        ThemeService.SetMessageOverride(Settings.MessageColorEnabled ? Settings.MessageColor.ToString() : null);
        
        foreach (var vm in Settings.LevelColors)
        {
            if (Enum.TryParse<LogLevel>(vm.Name, out var level))
            {
                ThemeService.SetLevelFgOverride(level, vm.Foreground.ToString());
                ThemeService.SetLevelBgOverride(level, vm.BackgroundEnabled ? vm.Background.ToString() : null);
            }
        }

        ThemeService.LevelEntireLineEnabled = Settings.LevelEntireLineEnabled;
        ThemeService.JsonHighlightEnabled = Settings.JsonHighlightEnabled;
        ThemeService.SqlHighlightEnabled = Settings.SqlHighlightEnabled;
        ThemeService.StackTraceHighlightEnabled = Settings.StackTraceHighlightEnabled;
        ThemeService.NumberHighlightEnabled = Settings.NumberHighlightEnabled;
        RaiseStatusProperties();
    }

    private void LoadSession()
    {
        System.Diagnostics.Debug.WriteLine("[SESSION] Loading workspace");
        _workspaceManager.Load();

        System.Diagnostics.Debug.WriteLine($"[SESSION] Loaded {_workspaceManager.Sources.Count} sources from workspace");
        SourceManager.RestoreSources(_workspaceManager.Sources);

        System.Diagnostics.Debug.WriteLine($"[SESSION] SourceManager now has {SourceManager.Sources.Count} sources");
        foreach (var src in SourceManager.Sources)
        {
            System.Diagnostics.Debug.WriteLine($"[SESSION]   - {src.SourceId}: {src.PhysicalPath} ({src.Kind})");
        }

        if (_workspaceManager.WorkspaceTabs.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[SESSION] Loading {_workspaceManager.WorkspaceTabs.Count} workspace tab(s)");
            Workspace.LoadTabs(_workspaceManager.WorkspaceTabs);
        }
    }

    public void SaveSession()
    {
        _workspaceManager.WorkspaceTabs.Clear();
        _workspaceManager.WorkspaceTabs.AddRange(Workspace.SaveTabs());

        _workspaceManager.SetSources(SourceManager.CreateSnapshot());

        _workspaceManager.Save();
    }

    [RelayCommand] private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;
    [RelayCommand] private void ToggleSettings() => Settings.IsVisible = !Settings.IsVisible;

    public (int Width, int Height, bool Maximized) GetSavedWindowState()
        => (_appSettings.WindowWidth, _appSettings.WindowHeight, _appSettings.WindowMaximized);

    public void SaveWindowState(int width, int height, bool maximized)
    {
        _appSettings.WindowWidth = width;
        _appSettings.WindowHeight = height;
        _appSettings.WindowMaximized = maximized;
    }

    public string? LastDirectory
    {
        get => _appSettings.LastDirectory;
        set => _appSettings.LastDirectory = value;
    }

    public void SaveSettings()
    {
        Settings.SaveTo(_appSettings);

        // Save recent sources
        _appSettings.RecentSources.Clear();
        foreach (var recent in SourceManager.RecentSources)
            _appSettings.RecentSources.Add(recent);

        // Save bookmarks from all panes
        _appSettings.Bookmarks.Clear();
        foreach (var pane in Workspace.GetAllPanes())
        {
            var key = pane.LogView.GetBookmarkKey();
            var bookmarks = pane.LogView.NavIndex.Bookmarks;
            if (key != null && bookmarks.Count > 0)
                _appSettings.Bookmarks[key] = bookmarks.ToList();
        }

        SettingsManager.Save(_appSettings);
        SaveSession();
    }

    /// <summary>Restores bookmarks for a pane after loading its source.</summary>
    public void RestoreBookmarksForPane(LogViewViewModel logView)
    {
        var key = logView.GetBookmarkKey();
        if (key != null && _appSettings.Bookmarks.TryGetValue(key, out var saved))
            logView.RestoreBookmarks(saved);
    }

    public void LoadFile(string path)
    {
        Workspace.ActiveLogView?.LoadFile(path);
        SourceManager.AddSource(path, SourceKind.File);
    }

    public bool LoadPath(string path)
    {
        if (File.Exists(path))
        {
            LoadFile(path);
            return true;
        }

        if (Directory.Exists(path))
        {
            LoadFolder(path);
            return true;
        }

        return false;
    }

    public void LoadFolder(string path)
    {
        System.Diagnostics.Debug.WriteLine($"[MAIN] LoadFolder called with path: {path}");
        System.Diagnostics.Debug.WriteLine($"[MAIN] Workspace.ActiveLogView is null: {Workspace.ActiveLogView == null}");

        if (Workspace.ActiveLogView == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MAIN] ERROR: ActiveLogView is null! Cannot load folder.");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MAIN] Calling Workspace.ActiveLogView.LoadFolder");
            Workspace.ActiveLogView.LoadFolder(path);
        }

        SourceManager.AddSource(path, SourceKind.Folder);
        System.Diagnostics.Debug.WriteLine($"[MAIN] LoadFolder completed");
    }

    // ── Toolbar Navigation Commands ──
    [RelayCommand] private void ToggleFilter() => Workspace.ActiveLogView?.ToggleFilter();
    [RelayCommand] private void SearchPrev() => Workspace.ActiveLogView?.NavigateSearchHit(false);
    [RelayCommand] private void SearchNext() => Workspace.ActiveLogView?.NavigateSearchHit(true);
    [RelayCommand] private void ErrorPrev() => Workspace.ActiveLogView?.NavigateError(false);
    [RelayCommand] private void ErrorNext() => Workspace.ActiveLogView?.NavigateError(true);
    [RelayCommand] private void BookmarkPrev() => Workspace.ActiveLogView?.NavigateBookmark(false);
    [RelayCommand] private void BookmarkNext() => Workspace.ActiveLogView?.NavigateBookmark(true);
    [RelayCommand] private void ToggleBookmark() => Workspace.ActiveLogView?.ToggleBookmark();
    
    [RelayCommand] private void SplitVertical() => Workspace.SplitFocused(false);
    [RelayCommand] private void SplitHorizontal() => Workspace.SplitFocused(true);

    /// <summary>Sets the main window reference for MonitorManager. Call from Window.Opened.</summary>
    public void SetMainWindow(global::Avalonia.Controls.Window window)
    {
        _monitorManager = new MonitorManager(window);
    }

    [RelayCommand]
    private void ExplodePanes()
    {
        if (_monitorManager is null || Workspace.DockFactory is null) return;
        _monitorManager.ExplodePanesToMonitors(Workspace, Workspace.DockFactory);
    }

    [RelayCommand]
    private void ExplodeTabs()
    {
        if (_monitorManager is null) return;
        _monitorManager.ExplodeTabsToMonitors(Workspace, SourceManager, ThemeService);
    }

    [RelayCommand] 
    private void CloseAllPanesInActiveTab()
    {
        var fresh = new PaneNodeViewModel();
        fresh.LogView.Initialize(Workspace.Clock, SourceManager, ThemeService);
        Workspace.RootNode = fresh;
        Workspace.FocusPane(fresh);
    }

    [RelayCommand] private void CloseTabs() => Workspace.CloseAllTabs();

    public string StatusFile => Workspace.ActiveLogView?.Title ?? "No file loaded";
    public string StatusLines => $"{Workspace.ActiveLogView?.TotalLineCount ?? 0} lines";
    public string StatusStreaming => Workspace.ActiveLogView?.IsStreaming == true ? "Streaming" : "";
    public string StatusFollow => Workspace.IsMasterFollowOn ? "Follow: All"
        : Workspace.ActiveLogView?.IsFollowMode == true ? "Follow: On" : "Follow: Off";

    /// <summary>Null-safe binding for Filter.IsVisible when no active pane.</summary>
    public bool ActiveFilterVisible
    {
        get => Workspace.ActiveLogView?.Filter?.IsVisible ?? false;
        set
        {
            if (Workspace.ActiveLogView?.Filter != null)
                Workspace.ActiveLogView.Filter.IsVisible = value;
            OnPropertyChanged(nameof(ActiveFilterVisible));
        }
    }

    /// <summary>Null-safe binding for IsGridMode when no active pane.</summary>
    public bool ActiveIsGridMode
    {
        get => Workspace.ActiveLogView?.IsGridMode ?? false;
        set
        {
            if (Workspace.ActiveLogView != null)
                Workspace.ActiveLogView.IsGridMode = value;
            OnPropertyChanged(nameof(ActiveIsGridMode));
        }
    }

    /// <summary>Null-safe binding for NavStatus when no active pane.</summary>
    public string ActiveNavStatus => Workspace.ActiveLogView?.NavStatus ?? "";

    /// <summary>Load error message from active pane for status bar (e.g. "Load failed: ...").</summary>
    public string ActiveLoadError => Workspace.ActiveLogView?.LoadErrorMessage ?? "";

    // Simulators
    private readonly List<LogSimulator> _simulators = new();
    public void StartSimulator(int intervalMs, bool showcase = false)
    {
        var simDir = Path.Combine(Path.GetTempPath(), "NovaLogSim_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var sim = new LogSimulator(simDir) { ShowcaseMode = showcase };
        _simulators.Add(sim);
        sim.Start(intervalMs);
        LoadFolder(simDir);
    }

    [RelayCommand] private void StopSimulator()
    {
        foreach (var sim in _simulators)
        {
            sim.Stop();
            sim.Dispose();
        }
        _simulators.Clear();
    }
    [RelayCommand] private void SimSlow() => StartSimulator(1000);
    [RelayCommand] private void SimMedium() => StartSimulator(200);
    [RelayCommand] private void SimFast() => StartSimulator(50);
    [RelayCommand] private void SimShowcaseSlow() => StartSimulator(1000, showcase: true);
    [RelayCommand] private void SimShowcaseMedium() => StartSimulator(200, showcase: true);
    [RelayCommand] private void SimShowcaseFast() => StartSimulator(50, showcase: true);

    private FormattingOptions? BuildFormattingOptions()
    {
        if (!Settings.JsonFormatEnabled && !Settings.SqlFormatEnabled)
            return null;
        return new FormattingOptions(
            Settings.JsonFormatEnabled, Settings.SqlFormatEnabled,
            Settings.FormatIndentSize, Settings.MaxRowLines);
    }

    private void OnSettingsChanged()
    {
        ApplySettingsToTheme();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.MainFollowEnabled):
                Workspace.SetMainFollowDefault(Settings.MainFollowEnabled, applyToExisting: true);
                Workspace.IsMasterFollowOn = Settings.MainFollowEnabled;
                RaiseStatusProperties();
                break;
            case nameof(SettingsViewModel.FilterFollowEnabled):
                Workspace.SetFilterFollowDefault(Settings.FilterFollowEnabled, applyToExisting: true);
                break;
            case nameof(SettingsViewModel.DefaultGridMode):
                Workspace.SetGridModeDefault(Settings.DefaultGridMode, applyToExisting: true);
                break;
            case nameof(SettingsViewModel.GridMultiline):
                Workspace.SetGridMultilineDefault(Settings.GridMultiline, applyToExisting: true);
                break;
            case nameof(SettingsViewModel.JsonFormatEnabled):
            case nameof(SettingsViewModel.SqlFormatEnabled):
            case nameof(SettingsViewModel.FormatIndentSize):
            case nameof(SettingsViewModel.MaxRowLines):
                Workspace.SetFormattingOptions(BuildFormattingOptions(), applyToExisting: true);
                break;
            case nameof(SettingsViewModel.LineHeight):
                LogLineRow.RowHeight = Settings.LineHeight;
                foreach (var pane in Workspace.GetAllPanes())
                    pane.LogView.NotifyRowVisualsChanged();
                break;
            case nameof(SettingsViewModel.SearchResultCap):
            case nameof(SettingsViewModel.SearchNewestFirst):
                Workspace.SetSearchDefaults(Settings.SearchResultCap, Settings.SearchNewestFirst, applyToExisting: true);
                break;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.ActiveLogView))
            return;

        AttachActiveLogView(Workspace.ActiveLogView);
        RaiseStatusProperties();
        RaiseActiveLogViewDependentProperties();
    }

    private void AttachActiveLogView(LogViewViewModel? logView)
    {
        if (_observedActiveLogView is not null)
            _observedActiveLogView.PropertyChanged -= OnActiveLogViewPropertyChanged;
        if (_observedActiveFilter is not null)
            _observedActiveFilter.PropertyChanged -= OnActiveFilterPropertyChanged;

        _observedActiveLogView = logView;
        _observedActiveFilter = logView?.Filter;

        if (_observedActiveLogView is not null)
            _observedActiveLogView.PropertyChanged += OnActiveLogViewPropertyChanged;
        if (_observedActiveFilter is not null)
            _observedActiveFilter.PropertyChanged += OnActiveFilterPropertyChanged;
    }

    private void OnActiveLogViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LogViewViewModel.Title):
            case nameof(LogViewViewModel.TotalLineCount):
            case nameof(LogViewViewModel.IsStreaming):
            case nameof(LogViewViewModel.IsFollowMode):
                RaiseStatusProperties();
                break;
            case nameof(LogViewViewModel.IsGridMode):
            case nameof(LogViewViewModel.NavStatus):
            case nameof(LogViewViewModel.LoadErrorMessage):
                RaiseActiveLogViewDependentProperties();
                break;
        }
    }

    private void OnActiveFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterPanelViewModel.IsVisible))
            RaiseActiveLogViewDependentProperties();
    }

    private void RaiseActiveLogViewDependentProperties()
    {
        OnPropertyChanged(nameof(ActiveFilterVisible));
        OnPropertyChanged(nameof(ActiveIsGridMode));
        OnPropertyChanged(nameof(ActiveNavStatus));
        OnPropertyChanged(nameof(ActiveLoadError));
    }

    private void RaiseStatusProperties()
    {
        OnPropertyChanged(nameof(StatusFile));
        OnPropertyChanged(nameof(StatusLines));
        OnPropertyChanged(nameof(StatusStreaming));
        OnPropertyChanged(nameof(StatusFollow));
    }

    public void Dispose()
    {
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        Settings.SettingsChanged -= OnSettingsChanged;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;

        if (_observedActiveLogView is not null)
            _observedActiveLogView.PropertyChanged -= OnActiveLogViewPropertyChanged;
        if (_observedActiveFilter is not null)
            _observedActiveFilter.PropertyChanged -= OnActiveFilterPropertyChanged;

        // Unsubscribe SourceManager events
        if (_sourceSelectedHandler is not null) SourceManager.SourceSelected -= _sourceSelectedHandler;
        if (_sourceNewTabHandler is not null) SourceManager.SourceNewTabRequested -= _sourceNewTabHandler;
        if (_aliasInputHandler is not null) SourceManager.AliasInputRequested -= _aliasInputHandler;
        if (_closeRequestedHandler is not null) SourceManager.CloseRequested -= _closeRequestedHandler;
        if (_sourceRemovedHandler is not null) SourceManager.SourceRemoved -= _sourceRemovedHandler;

        // Unsubscribe DockableClosed
        if (_dockableClosedHandler is not null && Workspace.DockFactory is not null)
            Workspace.DockFactory.DockableClosed -= _dockableClosedHandler;

        foreach (var sim in _simulators)
        {
            sim.Stop();
            sim.Dispose();
        }
        _simulators.Clear();

        Workspace.Dispose();
    }
}
