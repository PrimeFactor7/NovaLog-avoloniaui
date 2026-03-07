using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;

namespace NovaLog.Tests.ViewModels;

/// <summary>
/// Tests for SettingsViewModel: load/save roundtrip, toggle visibility,
/// SettingsChanged event, and property defaults.
/// </summary>
public class SettingsViewModelTests
{
    // ── Defaults ────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchAppSettingsDefaults()
    {
        var vm = new SettingsViewModel();
        Assert.Equal(AppConstants.ThemeDark, vm.Theme);
        Assert.Equal(10f, vm.FontSize);
        Assert.True(vm.MainFollowEnabled);
        Assert.True(vm.JsonHighlightEnabled);
        Assert.True(vm.SqlHighlightEnabled);
        Assert.True(vm.StackTraceHighlightEnabled);
        Assert.True(vm.NumberHighlightEnabled);
    }

    [Fact]
    public void IsVisible_InitiallyFalse()
    {
        var vm = new SettingsViewModel();
        Assert.False(vm.IsVisible);
    }

    // ── Load/Save Roundtrip ─────────────────────────────────────────

    [Fact]
    public void LoadFrom_CopiesAllFields()
    {
        var settings = new AppSettings
        {
            Theme = AppConstants.ThemeLight,
            FontSize = 14f,
            MainFollowEnabled = false,
            JsonHighlightEnabled = false,
            SqlHighlightEnabled = false,
            StackTraceHighlightEnabled = false,
            NumberHighlightEnabled = false,
            RotationStrategy = AppConstants.RotationStrategyDirectoryScan
        };

        var vm = new SettingsViewModel();
        vm.LoadFrom(settings);

        Assert.Equal(AppConstants.ThemeLight, vm.Theme);
        Assert.Equal(14f, vm.FontSize);
        Assert.False(vm.MainFollowEnabled);
        Assert.False(vm.JsonHighlightEnabled);
        Assert.False(vm.SqlHighlightEnabled);
        Assert.False(vm.StackTraceHighlightEnabled);
        Assert.False(vm.NumberHighlightEnabled);
        Assert.Equal(AppConstants.RotationStrategyDirectoryScan, vm.RotationStrategy);
    }

    [Fact]
    public void SaveTo_CopiesAllFieldsBack()
    {
        var vm = new SettingsViewModel
        {
            Theme = AppConstants.ThemeLight,
            FontSize = 16f,
            MainFollowEnabled = false,
            JsonHighlightEnabled = false,
            SqlHighlightEnabled = false,
            StackTraceHighlightEnabled = false,
            NumberHighlightEnabled = false,
            RotationStrategy = AppConstants.RotationStrategyFileCreation
        };

        var settings = new AppSettings();
        vm.SaveTo(settings);

        Assert.Equal(AppConstants.ThemeLight, settings.Theme);
        Assert.Equal(16f, settings.FontSize);
        Assert.False(settings.MainFollowEnabled);
        Assert.False(settings.JsonHighlightEnabled);
        Assert.False(settings.SqlHighlightEnabled);
        Assert.False(settings.StackTraceHighlightEnabled);
        Assert.False(settings.NumberHighlightEnabled);
        Assert.Equal(AppConstants.RotationStrategyFileCreation, settings.RotationStrategy);
    }

    [Fact]
    public void LoadThenSave_Roundtrips()
    {
        var original = new AppSettings
        {
            Theme = AppConstants.ThemeLight,
            FontSize = 12f,
            MainFollowEnabled = false,
            JsonHighlightEnabled = false,
            RotationStrategy = AppConstants.RotationStrategyDirectoryScan
        };

        var vm = new SettingsViewModel();
        vm.LoadFrom(original);

        var copy = new AppSettings();
        vm.SaveTo(copy);

        Assert.Equal(original.Theme, copy.Theme);
        Assert.Equal(original.FontSize, copy.FontSize);
        Assert.Equal(original.MainFollowEnabled, copy.MainFollowEnabled);
        Assert.Equal(original.JsonHighlightEnabled, copy.JsonHighlightEnabled);
        Assert.Equal(original.RotationStrategy, copy.RotationStrategy);
    }

    // ── Toggle ──────────────────────────────────────────────────────

    [Fact]
    public void Toggle_ShowsThenHides()
    {
        var vm = new SettingsViewModel();

        vm.Toggle();
        Assert.True(vm.IsVisible);

        vm.Toggle();
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Show_SetsVisible()
    {
        var vm = new SettingsViewModel();
        vm.Show();
        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void CloseCommand_SetsInvisible()
    {
        var vm = new SettingsViewModel();
        vm.Show();
        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    // ── SettingsChanged Event ───────────────────────────────────────

    [Fact]
    public void SettingsChanged_FiresOnThemeChange()
    {
        var vm = new SettingsViewModel();
        bool fired = false;
        vm.SettingsChanged += () => fired = true;

        vm.Theme = AppConstants.ThemeLight;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_FiresOnFontSizeChange()
    {
        var vm = new SettingsViewModel();
        bool fired = false;
        vm.SettingsChanged += () => fired = true;

        vm.FontSize = 14f;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_FiresOnSyntaxToggle()
    {
        var vm = new SettingsViewModel();
        int fireCount = 0;
        vm.SettingsChanged += () => fireCount++;

        vm.JsonHighlightEnabled = false;
        vm.SqlHighlightEnabled = false;
        vm.StackTraceHighlightEnabled = false;
        vm.NumberHighlightEnabled = false;

        Assert.Equal(4, fireCount);
    }

    // ── Available options ───────────────────────────────────────────

    [Fact]
    public void AvailableThemes_ContainsDarkAndLight()
    {
        var vm = new SettingsViewModel();
        Assert.Contains(AppConstants.ThemeDark, vm.AvailableThemes);
        Assert.Contains(AppConstants.ThemeLight, vm.AvailableThemes);
    }

    [Fact]
    public void AvailableStrategies_ContainsAllThree()
    {
        var vm = new SettingsViewModel();
        Assert.Equal(3, vm.AvailableStrategies.Length);
    }
}
