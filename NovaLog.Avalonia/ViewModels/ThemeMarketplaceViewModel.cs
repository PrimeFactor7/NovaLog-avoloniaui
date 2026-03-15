using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Theme;

namespace NovaLog.Avalonia.ViewModels;

public partial class ThemeMarketplaceViewModel : ObservableObject
{
    private readonly VSCodeThemeService _marketplace = new();
    private readonly ThemeService _themeService;

    [ObservableProperty] private string _searchTerm = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string? _searchError;

    public ObservableCollection<VSCodeExtensionSummary> SearchResults { get; } = new();

    [ObservableProperty] private VSCodeExtensionSummary? _selectedExtension;
    [ObservableProperty] private string _publisher = "";
    [ObservableProperty] private string _extension = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private string? _fetchError;

    public ObservableCollection<VSCodeThemeVariant> Variants { get; } = new();
    [ObservableProperty] private VSCodeThemeVariant? _selectedVariant;

    /// <summary>When applying, use theme for app UI (sidebar, tabs, panels).</summary>
    [ObservableProperty] private bool _applyToApp = true;
    /// <summary>When applying, use theme for log content and syntax (JSON/SQL, levels).</summary>
    [ObservableProperty] private bool _applyToLogs = true;

    public ThemeMarketplaceViewModel(ThemeService themeService)
    {
        _themeService = themeService;
    }

    partial void OnSelectedExtensionChanged(VSCodeExtensionSummary? value)
    {
        if (value != null)
        {
            Publisher = value.Publisher;
            Extension = value.ExtensionName;
            Version = value.Version ?? "";
            Variants.Clear();
            SelectedVariant = null;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchError = null;
        SearchResults.Clear();
        SelectedExtension = null;
        if (string.IsNullOrWhiteSpace(SearchTerm)) return;
        IsSearching = true;
        try
        {
            var list = await _marketplace.SearchAsync(SearchTerm).ConfigureAwait(true);
            SearchResults.Clear();
            foreach (var ext in list)
                SearchResults.Add(ext);
        }
        catch (Exception ex)
        {
            SearchError = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task FetchThemesAsync()
    {
        FetchError = null;
        Variants.Clear();
        SelectedVariant = null;
        if (string.IsNullOrWhiteSpace(Publisher) || string.IsNullOrWhiteSpace(Extension))
        {
            FetchError = "Enter publisher and extension.";
            return;
        }
        IsFetching = true;
        try
        {
            var list = await _marketplace.FetchThemesAsync(
                Publisher,
                Extension,
                string.IsNullOrWhiteSpace(Version) ? null : Version).ConfigureAwait(true);
            Variants.Clear();
            foreach (var v in list)
                Variants.Add(v);
        }
        catch (Exception ex)
        {
            FetchError = ex.Message;
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        if (SelectedVariant == null) return;
        var variant = SelectedVariant;
        if (ApplyToApp)
        {
            var uiOverrides = VSCodeThemeMapping.ToThemeOverrides(variant.Colors);
            var appTheme = LogThemeData.WithOverrides(_themeService.AppTheme, uiOverrides);
            _themeService.SetAppTheme(appTheme);
        }
        if (ApplyToLogs)
        {
            var syntaxOverrides = VSCodeThemeMapping.ToSyntaxOverrides(variant.TokenColors);
            var logTheme = LogThemeData.WithOverrides(_themeService.LogTheme, syntaxOverrides);
            _themeService.SetLogTheme(logTheme);
        }
    }
}
