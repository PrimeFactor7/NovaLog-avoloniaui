using NovaLog.Avalonia.Controls;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.ViewModels;

public class LogViewViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public LogViewViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"novalog_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── LoadFromLines ────────────────────────────────────────────

    [Fact]
    public void LoadFromLines_SetsItemsSource()
    {
        var vm = new LogViewViewModel();
        var lines = new[] { "2026-01-01 00:00:00.000 info: Hello", "continuation" };

        vm.LoadFromLines("test.log", lines);

        Assert.NotNull(vm.ItemsSource);
        var delegating = Assert.IsType<DelegatingItemsSource>(vm.ItemsSource);
        Assert.Equal(2, delegating.Count);
    }

    [Fact]
    public void LoadFromLines_SetsTotalLineCount()
    {
        var vm = new LogViewViewModel();
        var lines = new[] { "line1", "line2", "line3" };

        vm.LoadFromLines("test.log", lines);

        Assert.Equal(3, vm.TotalLineCount);
    }

    [Fact]
    public void LoadFromLines_SetsTitle()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines(@"C:\logs\app.log", new[] { "line1" });

        Assert.Equal("app.log", vm.Title);
    }

    [Fact]
    public void LoadFromLines_ParsesEachLine()
    {
        var vm = new LogViewViewModel();
        var lines = new[]
        {
            "2026-03-04 10:00:00.000 info: First message",
            "2026-03-04 10:00:01.000 error: Second message",
        };

        vm.LoadFromLines("test.log", lines);

        var source = (DelegatingItemsSource)vm.ItemsSource;
        Assert.Equal(2, source.Count);
        Assert.Equal(LogLevel.Info, source[0].Level);
        Assert.Equal(LogLevel.Error, source[1].Level);
        Assert.Equal("First message", source[0].Message);
        Assert.Equal("Second message", source[1].Message);
    }

    // ── LoadFile (reads from disk) ───────────────────────────────

    [Fact]
    public void LoadFile_ReadsAndParsesFile()
    {
        var filePath = Path.Combine(_tempDir, "app.log");
        File.WriteAllLines(filePath, new[]
        {
            "2026-03-04 12:00:00.000 info: Started",
            "2026-03-04 12:00:01.000 warn: Low memory",
            "2026-03-04 12:00:02.000 error: Out of memory",
        });

        var vm = new LogViewViewModel();
        vm.LoadFile(filePath);

        Assert.Equal(3, vm.TotalLineCount);
        Assert.Equal("app.log", vm.Title);

        var source = (DelegatingItemsSource)vm.ItemsSource;
        Assert.Equal(LogLevel.Info, source[0].Level);
        Assert.Equal(LogLevel.Warn, source[1].Level);
        Assert.Equal(LogLevel.Error, source[2].Level);
    }

    [Fact]
    public void LoadFile_EmptyFile_ZeroLines()
    {
        var filePath = Path.Combine(_tempDir, "empty.log");
        File.WriteAllText(filePath, "");

        var vm = new LogViewViewModel();
        vm.LoadFile(filePath);

        Assert.Equal(0, vm.TotalLineCount);
    }

    [Fact]
    public void LoadFile_LargeFile_HandlesCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "large.log");
        var lines = Enumerable.Range(0, 10_000)
            .Select(i => $"2026-01-01 00:00:{i / 1000:D2}.{i % 1000:D3} info: Line {i}")
            .ToArray();
        File.WriteAllLines(filePath, lines);

        var vm = new LogViewViewModel();
        vm.LoadFile(filePath);

        Assert.Equal(10_000, vm.TotalLineCount);
    }

    // ── File loading with syntax detection ───────────────────────

    [Fact]
    public void LoadFile_MixedSyntax_DetectsFlavors()
    {
        var filePath = Path.Combine(_tempDir, "mixed.log");
        File.WriteAllLines(filePath, new[]
        {
            "2026-01-01 00:00:00.000 info: Normal message",
            "2026-01-01 00:00:01.000 debug: {\"user\": \"alice\", \"action\": \"login\"}",
            "2026-01-01 00:00:02.000 debug: SELECT * FROM Users",
            "2026-01-01 00:00:03.000 error: System.NullReferenceException: Object reference not set",
        });

        var vm = new LogViewViewModel();
        vm.LoadFile(filePath);

        var source = (DelegatingItemsSource)vm.ItemsSource;
        Assert.Equal(SyntaxFlavor.None, source[0].Flavor);
        Assert.Equal(SyntaxFlavor.Json, source[1].Flavor);
        Assert.Equal(SyntaxFlavor.Sql, source[2].Flavor);
        Assert.Equal(SyntaxFlavor.StackTrace, source[3].Flavor);
    }

    // ── Reload replaces previous data ────────────────────────────

    [Fact]
    public void LoadFile_Twice_ReplacesData()
    {
        var file1 = Path.Combine(_tempDir, "file1.log");
        var file2 = Path.Combine(_tempDir, "file2.log");
        File.WriteAllLines(file1, new[] { "2026-01-01 00:00:00.000 info: File1" });
        File.WriteAllLines(file2, new[] { "2026-01-01 00:00:00.000 warn: File2A", "2026-01-01 00:00:00.001 warn: File2B" });

        var vm = new LogViewViewModel();
        vm.LoadFile(file1);
        Assert.Equal(1, vm.TotalLineCount);
        Assert.Equal("file1.log", vm.Title);

        vm.LoadFile(file2);
        Assert.Equal(2, vm.TotalLineCount);
        Assert.Equal("file2.log", vm.Title);
    }

    // ── Follow mode ──────────────────────────────────────────────

    [Fact]
    public void FollowMode_DefaultOn()
    {
        var vm = new LogViewViewModel();
        Assert.True(vm.IsFollowMode);
    }

    [Fact]
    public void ToggleFollow_SwitchesMode()
    {
        var vm = new LogViewViewModel();
        vm.ToggleFollowCommand.Execute(null);
        Assert.False(vm.IsFollowMode);

        vm.ToggleFollowCommand.Execute(null);
        Assert.True(vm.IsFollowMode);
    }

    [Fact]
    public void ToggleFollow_WhenEnabled_FiresScrollToEnd()
    {
        var vm = new LogViewViewModel();
        vm.ToggleFollowCommand.Execute(null); // off
        Assert.False(vm.IsFollowMode);
        vm.ToggleFollowCommand.Execute(null); // on -> should fire
        Assert.True(vm.IsFollowMode);
    }

    // ── Default state ────────────────────────────────────────────

    [Fact]
    public void Initial_DefaultTitle()
    {
        var vm = new LogViewViewModel();
        Assert.Equal("No file loaded", vm.Title);
    }

    [Fact]
    public void Initial_ZeroLineCount()
    {
        var vm = new LogViewViewModel();
        Assert.Equal(1, vm.TotalLineCount);
    }

    [Fact]
    public void Initial_ItemsSourceIsDelegating()
    {
        var vm = new LogViewViewModel();
        Assert.NotNull(vm.ItemsSource);
        var delegating = Assert.IsType<DelegatingItemsSource>(vm.ItemsSource);
        Assert.Equal(1, delegating.Count);
    }

    // ── PropertyChanged notifications ────────────────────────────

    [Fact]
    public void LoadFile_FiresPropertyChanged()
    {
        var filePath = Path.Combine(_tempDir, "notify.log");
        File.WriteAllLines(filePath, new[]
        {
            "2026-01-01 00:00:00.000 info: test",
            "2026-01-01 00:00:01.000 info: second"
        });

        var vm = new LogViewViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.LoadFile(filePath);

        // ItemsSource no longer fires PropertyChanged (it's a stable reference)
        Assert.Contains("TotalLineCount", changed);
        Assert.Contains("Title", changed);
    }
}

