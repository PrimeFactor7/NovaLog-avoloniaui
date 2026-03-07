using NovaLog.Core.Models;
using NovaLog.Core.Theme;

namespace NovaLog.Tests.Theme;

public class ThemeServiceTests
{
    [Fact]
    public void DefaultTheme_IsDark()
    {
        var svc = new ThemeService();
        Assert.True(svc.IsDark);
        Assert.Equal("Deep Space", svc.CurrentTheme.Name);
    }

    [Fact]
    public void SetTheme_ToLight_SwitchesCorrectly()
    {
        var svc = new ThemeService();
        svc.SetTheme(AppConstants.ThemeLight);

        Assert.False(svc.IsDark);
        Assert.Equal("Daylight", svc.CurrentTheme.Name);
    }

    [Fact]
    public void SetTheme_BackToDark_SwitchesCorrectly()
    {
        var svc = new ThemeService();
        svc.SetTheme(AppConstants.ThemeLight);
        svc.SetTheme(AppConstants.ThemeDark);

        Assert.True(svc.IsDark);
    }

    [Fact]
    public void SetTheme_FiresThemeChangedEvent()
    {
        var svc = new ThemeService();
        LogThemeData? received = null;
        svc.ThemeChanged += t => received = t;

        svc.SetTheme(AppConstants.ThemeLight);

        Assert.NotNull(received);
        Assert.Equal("Daylight", received!.Name);
    }

    [Fact]
    public void SetTheme_DirectThemeData_Works()
    {
        var svc = new ThemeService();
        svc.SetTheme(LogThemeData.Light);

        Assert.Equal("Daylight", svc.CurrentTheme.Name);
        Assert.False(svc.IsDark);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Verbose)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Fatal)]
    [InlineData(LogLevel.Unknown)]
    public void GetLevelColorHex_AllLevels_ReturnValidHex(LogLevel level)
    {
        var svc = new ThemeService();
        var hex = svc.GetLevelColorHex(level);

        Assert.NotNull(hex);
        Assert.StartsWith("#", hex);
        Assert.True(hex.Length is 7 or 9, $"Expected 7 or 9 char hex, got: {hex}");
    }
}

public class LogThemeDataTests
{
    [Fact]
    public void DarkPreset_HasAllRequiredColors()
    {
        var dark = LogThemeData.Dark;

        Assert.Equal("Deep Space", dark.Name);
        Assert.NotEmpty(dark.Background);
        Assert.NotEmpty(dark.TextInfo);
        Assert.NotEmpty(dark.TextWarn);
        Assert.NotEmpty(dark.TextError);
        Assert.NotEmpty(dark.JsonKey);
        Assert.NotEmpty(dark.SqlKeyword);
        Assert.NotEmpty(dark.StackMethod);
    }

    [Fact]
    public void LightPreset_HasAllRequiredColors()
    {
        var light = LogThemeData.Light;

        Assert.Equal("Daylight", light.Name);
        Assert.NotEmpty(light.Background);
        Assert.NotEmpty(light.TextInfo);
    }

    [Fact]
    public void DarkAndLight_HaveDifferentColors()
    {
        Assert.NotEqual(LogThemeData.Dark.Background, LogThemeData.Light.Background);
        Assert.NotEqual(LogThemeData.Dark.TextInfo, LogThemeData.Light.TextInfo);
    }

    [Fact]
    public void GetLevelColorHex_Info_ReturnsCyan()
    {
        Assert.Equal("#00D4FF", LogThemeData.Dark.GetLevelColorHex(LogLevel.Info));
    }

    [Fact]
    public void GetLevelColorHex_Error_ReturnsRed()
    {
        Assert.Equal("#FF3E3E", LogThemeData.Dark.GetLevelColorHex(LogLevel.Error));
    }

    [Fact]
    public void AllHexColors_AreValidFormat()
    {
        var dark = LogThemeData.Dark;
        var props = typeof(LogThemeData).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name != "Name");

        foreach (var prop in props)
        {
            var val = (string)prop.GetValue(dark)!;
            Assert.StartsWith("#", val);
            Assert.True(val.Length is 7 or 9,
                $"{prop.Name}: expected 7 or 9 chars, got '{val}' ({val.Length} chars)");
        }
    }
}
