using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Avalonia.ViewModels;

public partial class LevelColorViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private Color _foreground;
    [ObservableProperty] private Color _background;
    [ObservableProperty] private bool _backgroundEnabled;

    public LevelColorViewModel(string name)
    {
        _name = name;
    }
}

/// <summary>
/// ViewModel for the settings flyout panel. Wraps AppSettings with
/// observable properties and persists on close.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _theme = AppConstants.ThemeDark;
    [ObservableProperty] private float _fontSize = 10f;
    [ObservableProperty] private int _lineHeight = 18;
    
    // Column Colors
    [ObservableProperty] private bool _timestampColorEnabled;
    [ObservableProperty] private Color _timestampColor = Colors.Gray;
    [ObservableProperty] private bool _messageColorEnabled;
    [ObservableProperty] private Color _messageColor = Colors.White;

    // Log Levels
    public ObservableCollection<LevelColorViewModel> LevelColors { get; } = new();
    private void OnLevelColorPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => SettingsChanged?.Invoke();
    [ObservableProperty] private bool _levelEntireLineEnabled;

    // Follow Mode
    [ObservableProperty] private bool _mainFollowEnabled = true;
    [ObservableProperty] private bool _filterFollowEnabled;

    // Syntax Highlighting
    [ObservableProperty] private bool _jsonHighlightEnabled = true;
    [ObservableProperty] private bool _sqlHighlightEnabled = true;
    [ObservableProperty] private bool _stackTraceHighlightEnabled = true;
    [ObservableProperty] private bool _numberHighlightEnabled = true;
    [ObservableProperty] private string _rotationStrategy = AppConstants.RotationStrategyAuditJson;

    // Minimap & Filter
    [ObservableProperty] private bool _minimapShowSearch = true;
    [ObservableProperty] private bool _minimapShowErrors;
    [ObservableProperty] private bool _minimapVisible = true;
    [ObservableProperty] private bool _filterPanelVisible;
    [ObservableProperty] private int _searchResultCap = 500;
    [ObservableProperty] private bool _searchNewestFirst = true;

    // Grid View
    [ObservableProperty] private bool _defaultGridMode = true;
    [ObservableProperty] private bool _gridLinesVisible = true;
    [ObservableProperty] private bool _gridMultiline = true;

    // Formatting (auto-format in Span Lines mode)
    [ObservableProperty] private bool _jsonFormatEnabled;
    [ObservableProperty] private bool _sqlFormatEnabled;
    [ObservableProperty] private int _formatIndentSize = 2;
    [ObservableProperty] private int _maxRowLines = 50;

    // Sections (Expanders)
    [ObservableProperty] private bool _sectionFormattingExpanded;
    [ObservableProperty] private bool _sectionGridExpanded;
    [ObservableProperty] private bool _sectionColumnColorsExpanded;
    [ObservableProperty] private bool _sectionLogLevelsExpanded;
    [ObservableProperty] private bool _sectionHighlightRulesExpanded;
    [ObservableProperty] private bool _sectionMinimapExpanded;
    [ObservableProperty] private bool _sectionThemeExpanded;
    [ObservableProperty] private bool _sectionFollowExpanded;
    [ObservableProperty] private bool _sectionDisplayExpanded = true;
    [ObservableProperty] private bool _sectionSyntaxHighlightingExpanded = true;

    public string[] AvailableThemes { get; } = [AppConstants.ThemeDark, AppConstants.ThemeLight];
    public string[] AvailableStrategies { get; } =
    [
        AppConstants.RotationStrategyAuditJson,
        AppConstants.RotationStrategyDirectoryScan,
        AppConstants.RotationStrategyFileCreation
    ];

    public event Action? SettingsChanged;
    public event Action? EditHighlightRulesRequested;

    public void LoadFrom(AppSettings settings)
    {
        Theme = settings.Theme;
        FontSize = settings.FontSize;
        LineHeight = settings.LineHeight;

        TimestampColorEnabled = settings.TimestampColorEnabled;
        if (Color.TryParse(settings.TimestampColor, out var tc)) TimestampColor = tc;
        MessageColorEnabled = settings.MessageColorEnabled;
        if (Color.TryParse(settings.MessageColor, out var mc)) MessageColor = mc;

        foreach (var old in LevelColors)
            old.PropertyChanged -= OnLevelColorPropertyChanged;
        LevelColors.Clear();
        foreach (var (name, entry) in settings.LevelColors)
        {
            var vm = new LevelColorViewModel(name);
            if (Color.TryParse(entry.Foreground, out var cFg)) vm.Foreground = cFg;
            if (entry.Background != null && Color.TryParse(entry.Background, out var cBg)) vm.Background = cBg;
            vm.BackgroundEnabled = entry.BackgroundEnabled;

            vm.PropertyChanged += OnLevelColorPropertyChanged;
            LevelColors.Add(vm);
        }
        LevelEntireLineEnabled = settings.LevelEntireLineEnabled;

        MainFollowEnabled = settings.MainFollowEnabled;
        FilterFollowEnabled = settings.FilterFollowEnabled;
        
        JsonHighlightEnabled = settings.JsonHighlightEnabled;
        SqlHighlightEnabled = settings.SqlHighlightEnabled;
        StackTraceHighlightEnabled = settings.StackTraceHighlightEnabled;
        NumberHighlightEnabled = settings.NumberHighlightEnabled;
        RotationStrategy = settings.RotationStrategy;

        MinimapShowSearch = settings.MinimapShowSearch;
        MinimapShowErrors = settings.MinimapShowErrors;
        MinimapVisible = settings.MinimapVisible;
        FilterPanelVisible = settings.FilterPanelVisible;
        SearchResultCap = settings.SearchResultCap;
        SearchNewestFirst = settings.SearchNewestFirst;

        DefaultGridMode = settings.DefaultGridMode;
        GridLinesVisible = settings.GridLinesVisible;
        GridMultiline = settings.GridMultiline;
        JsonFormatEnabled = settings.JsonFormatEnabled;
        SqlFormatEnabled = settings.SqlFormatEnabled;
        FormatIndentSize = settings.FormatIndentSize;
        MaxRowLines = settings.MaxRowLines;
        SectionFormattingExpanded = settings.SectionFormattingExpanded;
        SectionGridExpanded = settings.SectionGridExpanded;
        SectionColumnColorsExpanded = settings.SectionColumnColorsExpanded;
        SectionLogLevelsExpanded = settings.SectionLogLevelsExpanded;
        SectionHighlightRulesExpanded = settings.SectionHighlightRulesExpanded;
        SectionMinimapExpanded = settings.SectionMinimapExpanded;
        SectionThemeExpanded = settings.SectionThemeExpanded;
        SectionFollowExpanded = settings.SectionFollowExpanded;
        SectionDisplayExpanded = settings.SectionDisplayExpanded;
        SectionSyntaxHighlightingExpanded = settings.SectionSyntaxHighlightingExpanded;
    }

    public void SaveTo(AppSettings settings)
    {
        settings.Theme = Theme;
        settings.FontSize = FontSize;
        settings.LineHeight = LineHeight;

        settings.TimestampColorEnabled = TimestampColorEnabled;
        settings.TimestampColor = TimestampColor.ToString();
        settings.MessageColorEnabled = MessageColorEnabled;
        settings.MessageColor = MessageColor.ToString();

        settings.LevelColors.Clear();
        foreach (var vm in LevelColors)
        {
            settings.LevelColors[vm.Name] = new LevelColorEntry
            {
                Foreground = vm.Foreground.ToString(),
                Background = vm.BackgroundEnabled ? vm.Background.ToString() : null,
                BackgroundEnabled = vm.BackgroundEnabled
            };
        }
        settings.LevelEntireLineEnabled = LevelEntireLineEnabled;

        settings.MainFollowEnabled = MainFollowEnabled;
        settings.FilterFollowEnabled = FilterFollowEnabled;
        
        settings.JsonHighlightEnabled = JsonHighlightEnabled;
        settings.SqlHighlightEnabled = SqlHighlightEnabled;
        settings.StackTraceHighlightEnabled = StackTraceHighlightEnabled;
        settings.NumberHighlightEnabled = NumberHighlightEnabled;
        settings.RotationStrategy = RotationStrategy;

        settings.MinimapShowSearch = MinimapShowSearch;
        settings.MinimapShowErrors = MinimapShowErrors;
        settings.MinimapVisible = MinimapVisible;
        settings.FilterPanelVisible = FilterPanelVisible;
        settings.SearchResultCap = SearchResultCap;
        settings.SearchNewestFirst = SearchNewestFirst;

        settings.DefaultGridMode = DefaultGridMode;
        settings.GridLinesVisible = GridLinesVisible;
        settings.GridMultiline = GridMultiline;
        settings.JsonFormatEnabled = JsonFormatEnabled;
        settings.SqlFormatEnabled = SqlFormatEnabled;
        settings.FormatIndentSize = FormatIndentSize;
        settings.MaxRowLines = MaxRowLines;
        settings.SectionFormattingExpanded = SectionFormattingExpanded;
        settings.SectionGridExpanded = SectionGridExpanded;
        settings.SectionColumnColorsExpanded = SectionColumnColorsExpanded;
        settings.SectionLogLevelsExpanded = SectionLogLevelsExpanded;
        settings.SectionHighlightRulesExpanded = SectionHighlightRulesExpanded;
        settings.SectionMinimapExpanded = SectionMinimapExpanded;
        settings.SectionThemeExpanded = SectionThemeExpanded;
        settings.SectionFollowExpanded = SectionFollowExpanded;
        settings.SectionDisplayExpanded = SectionDisplayExpanded;
        settings.SectionSyntaxHighlightingExpanded = SectionSyntaxHighlightingExpanded;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(IsVisible) && e.PropertyName != null && !e.PropertyName.StartsWith("Section"))
        {
            SettingsChanged?.Invoke();
        }
    }

    [RelayCommand]
    private void EditHighlightRules() => EditHighlightRulesRequested?.Invoke();

    [RelayCommand]
    private void Close() => IsVisible = false;

    public void Show() => IsVisible = true;

    public void Toggle()
    {
        if (IsVisible)
            Close();
        else
            Show();
    }
}
