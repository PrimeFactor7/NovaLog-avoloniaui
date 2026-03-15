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
        var overrides = VSCodeThemeMapping.ToThemeOverrides(SelectedVariant.Colors);
        var baseTheme = _themeService.CurrentTheme;
        var theme = LogThemeData.WithOverrides(baseTheme, overrides);
        _themeService.SetTheme(theme);
    }
}
