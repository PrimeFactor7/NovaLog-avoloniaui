using System.Text.Json;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class SettingsManagerTests
{
    // ── CreateDefaults ────────────────────────────────────────────

    [Fact]
    public void CreateDefaults_PopulatesAllLevelColors()
    {
        var settings = SettingsManager.CreateDefaults();

        Assert.Contains("Trace", settings.LevelColors.Keys);
        Assert.Contains("Verbose", settings.LevelColors.Keys);
        Assert.Contains("Debug", settings.LevelColors.Keys);
        Assert.Contains("Info", settings.LevelColors.Keys);
        Assert.Contains("Warn", settings.LevelColors.Keys);
        Assert.Contains("Error", settings.LevelColors.Keys);
        Assert.Contains("Fatal", settings.LevelColors.Keys);
        Assert.Contains("Unknown", settings.LevelColors.Keys);
        Assert.Equal(8, settings.LevelColors.Count);
    }

    [Fact]
    public void CreateDefaults_PopulatesHighlightRules()
    {
        var settings = SettingsManager.CreateDefaults();

        Assert.True(settings.HighlightRules.Count >= 3);
        Assert.Contains(settings.HighlightRules, r => r.Pattern.Contains("error"));
        Assert.Contains(settings.HighlightRules, r => r.Pattern.Contains("warn"));
    }

    [Fact]
    public void CreateDefaults_HasExpectedDefaultValues()
    {
        var settings = SettingsManager.CreateDefaults();

        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(10f, settings.FontSize);
        Assert.True(settings.MainFollowEnabled);
        Assert.Equal("AuditJson", settings.RotationStrategy);
    }

    // ── Serialization roundtrip ───────────────────────────────────

    [Fact]
    public void AppSettings_JsonRoundtrip_PreservesAllFields()
    {
        var original = SettingsManager.CreateDefaults();
        original.Theme = "Light";
        original.FontSize = 14f;
        original.TimestampColorEnabled = true;
        original.TimestampColor = "#AABBCC";
        original.LastDirectory = @"C:\logs";
        original.WindowMaximized = true;

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("Light", deserialized.Theme);
        Assert.Equal(14f, deserialized.FontSize);
        Assert.True(deserialized.TimestampColorEnabled);
        Assert.Equal("#AABBCC", deserialized.TimestampColor);
        Assert.Equal(@"C:\logs", deserialized.LastDirectory);
        Assert.True(deserialized.WindowMaximized);
        Assert.Equal(8, deserialized.LevelColors.Count);
    }
}
