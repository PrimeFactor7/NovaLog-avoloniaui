using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;

namespace NovaLog.Tests.ViewModels;

/// <summary>
/// Tests for newly added features: minimap toggle, sync link, recent history,
/// copy formatted JSON, and pin timestamp.
/// </summary>
public class NewFeaturesTests
{
    // ── Minimap Visibility ──────────────────────────────────────────

    [Fact]
    public void MinimapVisible_DefaultsToTrue()
    {
        var vm = new SettingsViewModel();
        Assert.True(vm.MinimapVisible);
    }

    [Fact]
    public void MinimapVisible_Roundtrips()
    {
        var settings = new AppSettings { MinimapVisible = false };
        var vm = new SettingsViewModel();
        vm.LoadFrom(settings);
        Assert.False(vm.MinimapVisible);

        var copy = new AppSettings();
        vm.SaveTo(copy);
        Assert.False(copy.MinimapVisible);
    }

    [Fact]
    public void MinimapVisible_FiresSettingsChanged()
    {
        var vm = new SettingsViewModel();
        bool fired = false;
        vm.SettingsChanged += () => fired = true;

        vm.MinimapVisible = false;

        Assert.True(fired);
    }

    // ── Sync Link ───────────────────────────────────────────────────

    [Fact]
    public void IsLinked_DefaultsToTrue()
    {
        var vm = new LogViewViewModel();
        Assert.True(vm.IsLinked);
    }

    [Fact]
    public void IsLinked_CanBeToggled()
    {
        var vm = new LogViewViewModel();

        vm.IsLinked = false;
        Assert.False(vm.IsLinked);

        vm.IsLinked = true;
        Assert.True(vm.IsLinked);
    }

    // ── Recent History ──────────────────────────────────────────────

    [Fact]
    public void RecentSources_InitiallyEmpty()
    {
        var vm = new SourceManagerViewModel();
        Assert.Empty(vm.RecentSources);
    }

    [Fact]
    public void AddToRecentHistory_AddsEntry()
    {
        var vm = new SourceManagerViewModel();

        vm.AddToRecentHistory(@"C:\test\file.log", SourceKind.File);

        Assert.Single(vm.RecentSources);
        Assert.Equal(@"C:\test\file.log", vm.RecentSources[0].Path);
        Assert.Equal("File", vm.RecentSources[0].Kind);
    }

    [Fact]
    public void AddToRecentHistory_MovesExistingToFront()
    {
        var vm = new SourceManagerViewModel();

        vm.AddToRecentHistory(@"C:\test\file1.log", SourceKind.File);
        vm.AddToRecentHistory(@"C:\test\file2.log", SourceKind.File);
        vm.AddToRecentHistory(@"C:\test\file1.log", SourceKind.File);

        Assert.Equal(2, vm.RecentSources.Count);
        Assert.Equal(@"C:\test\file1.log", vm.RecentSources[0].Path);
        Assert.Equal(@"C:\test\file2.log", vm.RecentSources[1].Path);
    }

    [Fact]
    public void AddToRecentHistory_LimitsTo10()
    {
        var vm = new SourceManagerViewModel();

        for (int i = 0; i < 15; i++)
            vm.AddToRecentHistory($@"C:\test\file{i}.log", SourceKind.File);

        Assert.Equal(10, vm.RecentSources.Count);
        Assert.Equal(@"C:\test\file14.log", vm.RecentSources[0].Path);
        Assert.Equal(@"C:\test\file5.log", vm.RecentSources[9].Path);
    }

    [Fact]
    public void PreviewRecent_LoadsWithoutAddingSource()
    {
        var vm = new SourceManagerViewModel();
        var tempFile = Path.GetTempFileName();
        try
        {
            string? selectedPath = null;
            SourceKind? selectedKind = null;
            vm.SourceSelected += (path, kind) =>
            {
                selectedPath = path;
                selectedKind = kind;
            };

            vm.AddToRecentHistory(tempFile, SourceKind.File);

            var result = vm.PreviewRecent(vm.RecentSources[0]);

            Assert.True(result);
            Assert.Empty(vm.Sources);
            Assert.Equal(tempFile, selectedPath);
            Assert.Equal(SourceKind.File, selectedKind);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void AddRecentToSources_AddsAndSelectsSource()
    {
        var vm = new SourceManagerViewModel();
        var tempFile = Path.GetTempFileName();
        try
        {
            string? selectedPath = null;
            vm.SourceSelected += (path, _) => selectedPath = path;
            vm.AddToRecentHistory(tempFile, SourceKind.File);

            var result = vm.AddRecentToSources(vm.RecentSources[0]);

            Assert.True(result);
            Assert.Single(vm.Sources);
            Assert.Equal(tempFile, vm.Sources[0].PhysicalPath);
            Assert.Same(vm.Sources[0], vm.SelectedSource);
            Assert.Equal(tempFile, selectedPath);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void BuildRecentHistoryItems_ReportsMissingStatus()
    {
        var vm = new SourceManagerViewModel();
        var missingPath = Path.Combine(Path.GetTempPath(), $"novalog_missing_{Guid.NewGuid():N}.log");
        vm.AddToRecentHistory(missingPath, SourceKind.File);

        var items = vm.BuildRecentHistoryItems();

        Assert.Single(items);
        Assert.True(items[0].IsMissing);
        Assert.Equal("Missing", items[0].StatusText);
        Assert.Equal("FILE", items[0].KindLabel);
    }

    [Fact]
    public void ShowRecents_DefaultsToFalse()
    {
        var vm = new SourceManagerViewModel();
        Assert.False(vm.ShowRecents);
    }

    [Fact]
    public void ToggleRecentsCommand_TogglesVisibility()
    {
        var vm = new SourceManagerViewModel();

        vm.ToggleRecentsCommand.Execute(null);
        Assert.True(vm.ShowRecents);

        vm.ToggleRecentsCommand.Execute(null);
        Assert.False(vm.ShowRecents);
    }

    // ── Copy Formatted JSON ─────────────────────────────────────────

    [Fact]
    public void GetFormattedJson_ReturnsNullForEmptyLine()
    {
        var vm = new LogViewViewModel();
        var json = vm.GetFormattedJson();
        Assert.Null(json);
    }

    [Fact]
    public void GetFormattedJson_ReturnsNullForNonJsonLine()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { "2025-01-01 00:00:00 info: This is plain text" });
        vm.SetCurrentLine(0);

        var json = vm.GetFormattedJson();
        Assert.Null(json);
    }

    [Fact]
    public void GetFormattedJson_FormatsValidJson()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { @"2025-01-01 00:00:00 info: {""key"":""value"",""nested"":{""id"":123}}" });
        vm.SetCurrentLine(0);

        var json = vm.GetFormattedJson();

        Assert.NotNull(json);
        Assert.Contains("\"key\"", json);
        Assert.Contains("\"value\"", json);
        Assert.Contains("\"nested\"", json);
        Assert.Contains("\n", json); // Should be indented/formatted
    }

    [Fact]
    public void GetFormattedJson_HandlesJsonArray()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { @"2025-01-01 00:00:00 info: [{""id"":1},{""id"":2}]" });
        vm.SetCurrentLine(0);

        var json = vm.GetFormattedJson();

        Assert.NotNull(json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("1", json);
        Assert.Contains("2", json);
    }

    // ── Pin Timestamp ───────────────────────────────────────────────

    [Fact]
    public void PinCurrentTimestamp_DoesNothingWithoutTimestamp()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { "No timestamp here" });
        vm.SetCurrentLine(0);

        // Should not throw
        vm.PinCurrentTimestamp();
    }


    // ── Recent Sources Persistence ──────────────────────────────────

    [Fact]
    public void RecentSources_PersistsInAppSettings()
    {
        var settings = new AppSettings();
        settings.RecentSources.Add(new RecentSourceEntry
        {
            Path = @"C:\test\file.log",
            Kind = "File",
            LastAccessed = DateTime.UtcNow
        });

        var vm = new SourceManagerViewModel();
        // Simulate loading from settings (done in MainWindowViewModel)
        foreach (var recent in settings.RecentSources)
            vm.RecentSources.Add(recent);

        Assert.Single(vm.RecentSources);
        Assert.Equal(@"C:\test\file.log", vm.RecentSources[0].Path);
        Assert.Equal("File", vm.RecentSources[0].Kind);
    }
}
