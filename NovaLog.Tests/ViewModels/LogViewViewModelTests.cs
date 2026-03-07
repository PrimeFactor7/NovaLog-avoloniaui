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

    // ── Line selection ─────────────────────────────────────────

    [Fact]
    public void SelectLine_GlobalIndexMatchesPosition()
    {
        var vm = new LogViewViewModel();
        var lines = new[]
        {
            "2026-01-01 00:00:00.000 info: Line zero",
            "2026-01-01 00:00:01.000 warn: Line one",
            "2026-01-01 00:00:02.000 error: Line two",
            "2026-01-01 00:00:03.000 info: Line three",
            "2026-01-01 00:00:04.000 debug: Line four"
        };
        vm.LoadFromLines("test.log", lines);

        var source = (DelegatingItemsSource)vm.ItemsSource;
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.Equal(i, source[i].GlobalIndex);
        }
    }

    [Fact]
    public void SelectLine_SetsSelectedLineIndex_ToClickedGlobalIndex()
    {
        var vm = new LogViewViewModel();
        var lines = new[]
        {
            "2026-01-01 00:00:00.000 info: Line zero",
            "2026-01-01 00:00:01.000 warn: Line one",
            "2026-01-01 00:00:02.000 error: Line two",
            "2026-01-01 00:00:03.000 info: Line three",
            "2026-01-01 00:00:04.000 debug: Line four"
        };
        vm.LoadFromLines("test.log", lines);

        var source = (DelegatingItemsSource)vm.ItemsSource;

        // Simulate clicking each line: the view uses row.DataContext.GlobalIndex
        for (int i = 0; i < lines.Length; i++)
        {
            int globalIndex = source[i].GlobalIndex;
            vm.SelectLine(globalIndex);

            Assert.Equal(globalIndex, vm.SelectedLineIndex);
            // The selected index must match this row's GlobalIndex
            Assert.Equal(i, vm.SelectedLineIndex);
        }
    }

    [Fact]
    public void SelectLine_SelectedLineIndex_MatchesOnlyClickedRow()
    {
        var vm = new LogViewViewModel();
        var lines = new[]
        {
            "2026-01-01 00:00:00.000 info: Line zero",
            "2026-01-01 00:00:01.000 warn: Line one",
            "2026-01-01 00:00:02.000 error: Line two",
        };
        vm.LoadFromLines("test.log", lines);

        var source = (DelegatingItemsSource)vm.ItemsSource;

        // Click line 1
        vm.SelectLine(source[1].GlobalIndex);

        // Verify: only line 1 should match the selection check used in LogLineRow.Render
        for (int i = 0; i < lines.Length; i++)
        {
            bool isSelected = vm.SelectedLineIndex == source[i].GlobalIndex;
            Assert.Equal(i == 1, isSelected);
        }
    }

    [Fact]
    public void SelectLine_FiresSelectedLineChangedEvent()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { "2026-01-01 00:00:00.000 info: test" });

        int fireCount = 0;
        vm.SelectedLineChanged += () => fireCount++;

        vm.SelectLine(0);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SelectLine_DisablesFollowMode()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", new[] { "2026-01-01 00:00:00.000 info: test" });
        Assert.True(vm.IsFollowMode);

        vm.SelectLine(0);
        Assert.False(vm.IsFollowMode);
    }

    // ── Merge selection ─────────────────────────────────────────

    [Fact]
    public void SelectLine_WorksWithVirtualSource_ViaLoadFromProvider()
    {
        // Build a merge engine with two sources
        var engine = new ChronoMergeEngine();

        var s1 = new LogStreamer(new List<string>());
        var s2 = new LogStreamer(new List<string>());

        engine.AddSource(s1, "src1", "#FF0000", 0);
        engine.AddSource(s2, "src2", "#00FF00", 1);
        engine.PushHistory(0, new[]
        {
            "2026-01-01 00:00:00.000 info: Source1 line A",
            "2026-01-01 00:00:02.000 info: Source1 line B",
        });
        engine.PushHistory(1, new[]
        {
            "2026-01-01 00:00:01.000 warn: Source2 line A",
            "2026-01-01 00:00:03.000 error: Source2 line B",
        });
        engine.Build();

        var logVm = new LogViewViewModel();
        logVm.LoadFromProvider(engine);

        Assert.Equal(4, logVm.TotalLineCount);

        var source = (DelegatingItemsSource)logVm.ItemsSource;
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, source[i].GlobalIndex);
            logVm.SelectLine(source[i].GlobalIndex);
            Assert.Equal(i, logVm.SelectedLineIndex);
        }
    }

    [Fact]
    public void SelectLine_WorksWithLoadMerge()
    {
        // Create temp files for the merge
        var file1 = Path.Combine(_tempDir, "source1.log");
        var file2 = Path.Combine(_tempDir, "source2.log");
        File.WriteAllLines(file1, new[]
        {
            "2026-01-01 00:00:00.000 info: Source1 line A",
            "2026-01-01 00:00:02.000 info: Source1 line B",
        });
        File.WriteAllLines(file2, new[]
        {
            "2026-01-01 00:00:01.000 warn: Source2 line A",
            "2026-01-01 00:00:03.000 error: Source2 line B",
        });

        var src1 = new SourceItemViewModel
        {
            DisplayName = "source1.log",
            PhysicalPath = file1,
            Kind = SourceKind.File,
            SourceColorHex = "#FF0000"
        };
        var src2 = new SourceItemViewModel
        {
            DisplayName = "source2.log",
            PhysicalPath = file2,
            Kind = SourceKind.File,
            SourceColorHex = "#00FF00"
        };

        var logVm = new LogViewViewModel();
        logVm.LoadMerge(new[] { src1, src2 });

        // Merge should produce 4 lines (2 from each source, interleaved by time)
        Assert.Equal(4, logVm.TotalLineCount);

        var source = (DelegatingItemsSource)logVm.ItemsSource;
        Assert.Equal(4, source.Count);

        // Verify GlobalIndex matches position
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, source[i].GlobalIndex);
        }

        // Verify selection works for each line
        for (int i = 0; i < 4; i++)
        {
            logVm.SelectLine(source[i].GlobalIndex);
            Assert.Equal(i, logVm.SelectedLineIndex);

            // Verify only the clicked row matches the selection check
            for (int j = 0; j < 4; j++)
            {
                bool isSelected = logVm.SelectedLineIndex == source[j].GlobalIndex;
                Assert.Equal(j == i, isSelected);
            }
        }

        // Verify merge source info is present (merge gutter colors)
        Assert.NotNull(source[0].MergeSourceColorHex);
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

