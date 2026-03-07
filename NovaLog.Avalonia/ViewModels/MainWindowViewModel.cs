using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Core.Theme;
using System.ComponentModel;

namespace NovaLog.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private WorkspaceViewModel _workspace;
    [ObservableProperty] private SourceManagerViewModel _sourceManager;
    [ObservableProperty] private SettingsViewModel _settings;
    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private bool _isTopmost;
    [ObservableProperty] private string _themeLabel = "Dark";

    public ThemeService ThemeService { get; } = new();
    private readonly WorkspaceManager _workspaceManager = new();
    private AppSettings _appSettings = new();
    private LogViewViewModel? _observedActiveLogView;

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
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        AttachActiveLogView(Workspace.ActiveLogView);

        Settings.SettingsChanged += OnSettingsChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;

        SourceManager.SourceSelected += (path, kind) =>
        {
            if (kind == SourceKind.File) Workspace.ActiveLogView?.LoadFile(path);
            else if (kind == SourceKind.Folder) Workspace.ActiveLogView?.LoadFolder(path);
            else if (kind == SourceKind.Merge && path.StartsWith("merge://"))
            {
                var ids = path.Substring(8).Split('|');
                var sourcesToMerge = SourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                Workspace.ActiveLogView?.LoadMerge(sourcesToMerge);
            }
        };

        SourceManager.SourceNewTabRequested += (path, kind) =>
        {
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

        SourceManager.AliasInputRequested += async (oldAlias) =>
        {
            var dialog = new Views.InputDialog("Set Alias", "Enter display name:", oldAlias);
            if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                return await dialog.ShowDialog<string?>(desktop.MainWindow);
            return null;
        };

        SourceManager.CloseRequested += () => IsSidebarVisible = false;

        SourceManager.SourceRemoved += (removedSource) =>
        {
            // Notify all panes to clear if they have this source loaded
            foreach (var pane in Workspace.GetAllPanes())
            {
                pane.LogView.ClearIfSourceRemoved(removedSource);
            }
        };

        LoadSession();
        AttachActiveLogView(Workspace.ActiveLogView);
        RaiseStatusProperties();
        ApplySettingsToTheme();
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

    public void SaveSettings()
    {
        Settings.SaveTo(_appSettings);

        // Save recent sources
        _appSettings.RecentSources.Clear();
        foreach (var recent in SourceManager.RecentSources)
            _appSettings.RecentSources.Add(recent);

        SettingsManager.Save(_appSettings);
        SaveSession();
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
    public string StatusFollow => Workspace.ActiveLogView?.IsFollowMode == true ? "Follow: On" : "Follow: Off";

    [RelayCommand]
    private void ToggleFollow() => Workspace.ActiveLogView?.ToggleFollowCommand.Execute(null);

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
                RaiseStatusProperties();
                break;
            case nameof(SettingsViewModel.FilterFollowEnabled):
                Workspace.SetFilterFollowDefault(Settings.FilterFollowEnabled, applyToExisting: true);
                break;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.ActiveLogView))
            return;

        AttachActiveLogView(Workspace.ActiveLogView);
        RaiseStatusProperties();
    }

    private void AttachActiveLogView(LogViewViewModel? logView)
    {
        if (_observedActiveLogView is not null)
            _observedActiveLogView.PropertyChanged -= OnActiveLogViewPropertyChanged;

        _observedActiveLogView = logView;

        if (_observedActiveLogView is not null)
            _observedActiveLogView.PropertyChanged += OnActiveLogViewPropertyChanged;
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
        }
    }

    private void RaiseStatusProperties()
    {
        OnPropertyChanged(nameof(StatusFile));
        OnPropertyChanged(nameof(StatusLines));
        OnPropertyChanged(nameof(StatusStreaming));
        OnPropertyChanged(nameof(StatusFollow));
    }
}
