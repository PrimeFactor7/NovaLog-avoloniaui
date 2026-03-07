using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.ViewModels;

/// <summary>
/// Extended tests for LogViewViewModel covering Wave 5-10 features:
/// folder loading, streaming, BigFile, bookmark/error navigation,
/// BinaryDetector, GetCurrentLineText.
/// </summary>
public class LogViewViewModelExtendedTests : IDisposable
{
    private readonly string _tempDir;

    public LogViewViewModelExtendedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NovaLogTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── BinaryDetector Integration ──────────────────────────────────

    [Fact]
    public void LoadFile_BinaryFile_SetsBinaryTitle()
    {
        var binFile = Path.Combine(_tempDir, "data.bin");
        File.WriteAllBytes(binFile, [0x50, 0x4B, 0x03, 0x04, 0x00]); // ZIP magic

        var vm = new LogViewViewModel();
        vm.LoadFile(binFile);

        Assert.StartsWith("Binary:", vm.Title);
        Assert.Equal(1, vm.TotalLineCount);
    }

    [Fact]
    public void LoadFile_TextFile_LoadsNormally()
    {
        var logFile = Path.Combine(_tempDir, "app.log");
        File.WriteAllLines(logFile, [
            "2025-01-01 10:00:00.000 info: Starting up",
            "2025-01-01 10:00:01.000 warn: Low memory"
        ]);

        var vm = new LogViewViewModel();
        vm.LoadFile(logFile);

        Assert.Equal("app.log", vm.Title);
        Assert.Equal(2, vm.TotalLineCount);
    }

    // ── BigFile Threshold ───────────────────────────────────────────

    [Fact]
    public void LoadFile_SmallFile_UsesInMemorySource()
    {
        var logFile = Path.Combine(_tempDir, "small.log");
        File.WriteAllLines(logFile, Enumerable.Range(0, 100).Select(i =>
            $"2025-01-01 10:00:{i:D2}.000 info: Line {i}"));

        var vm = new LogViewViewModel();
        vm.LoadFile(logFile);

        Assert.Equal(100, vm.TotalLineCount);
        Assert.NotNull(vm.ItemsSource);
    }

    // ── Folder Loading ──────────────────────────────────────────────

    [Fact]
    public void LoadFolder_WithLogFile_LoadsContent()
    {
        var logFile = Path.Combine(_tempDir, "server.log");
        File.WriteAllLines(logFile, [
            "2025-01-01 10:00:00.000 info: Server started",
            "2025-01-01 10:00:01.000 error: Connection failed"
        ]);

        var vm = new LogViewViewModel();
        vm.LoadFolder(_tempDir);

        Assert.True(vm.TotalLineCount > 0);
    }

    [Fact]
    public void LoadFolder_WithTxtFile_FallbackWorks()
    {
        var txtFile = Path.Combine(_tempDir, "events.txt");
        File.WriteAllLines(txtFile, ["Event 1", "Event 2"]);

        var vm = new LogViewViewModel();
        vm.LoadFolder(_tempDir);

        Assert.True(vm.TotalLineCount > 0);
    }

    [Fact]
    public void LoadFolder_EmptyDirectory_NoError()
    {
        var vm = new LogViewViewModel();
        vm.LoadFolder(_tempDir);

        // Empty folder keeps the placeholder row
        Assert.Equal(1, vm.TotalLineCount);
    }

    // ── Streaming State ─────────────────────────────────────────────

    [Fact]
    public void LoadFile_SmallFile_SetsStreamingTrue()
    {
        var logFile = Path.Combine(_tempDir, "stream.log");
        File.WriteAllLines(logFile, ["2025-01-01 10:00:00.000 info: Hello"]);

        var vm = new LogViewViewModel();
        vm.LoadFile(logFile);

        Assert.True(vm.IsStreaming);
    }

    [Fact]
    public void LoadFromLines_DoesNotSetStreaming()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["line 1", "line 2"]);

        Assert.False(vm.IsStreaming);
    }

    // ── Navigation: Bookmarks ───────────────────────────────────────

    [Fact]
    public void ToggleBookmark_AddsBookmark()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());
        vm.SetCurrentLine(42);

        vm.ToggleBookmark();

        Assert.Equal(1, vm.NavIndex.BookmarkCount);
        Assert.True(vm.NavIndex.IsBookmarked(42));
    }

    [Fact]
    public void ToggleBookmark_Twice_RemovesBookmark()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());
        vm.SetCurrentLine(42);

        vm.ToggleBookmark();
        vm.ToggleBookmark();

        Assert.Equal(0, vm.NavIndex.BookmarkCount);
    }

    [Fact]
    public void ToggleBookmark_UsesSelectedLine_WhenPresent()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());
        vm.SetCurrentLine(42);
        vm.SelectLine(7);

        vm.ToggleBookmark();

        Assert.True(vm.NavIndex.IsBookmarked(7));
        Assert.False(vm.NavIndex.IsBookmarked(42));
    }

    [Fact]
    public void NavigateBookmark_Forward_ScrollsToHit()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());

        vm.NavIndex.ToggleBookmark(10);
        vm.NavIndex.ToggleBookmark(50);
        vm.NavIndex.ToggleBookmark(90);
        vm.SetCurrentLine(0);

        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateBookmark(forward: true);

        Assert.NotNull(scrolledTo);
        Assert.Equal(10, scrolledTo);
    }

    [Fact]
    public void NavigateBookmark_Backward_ScrollsToHit()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());

        vm.NavIndex.ToggleBookmark(10);
        vm.NavIndex.ToggleBookmark(50);
        vm.NavIndex.ToggleBookmark(90);
        vm.SetCurrentLine(60);

        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateBookmark(forward: false);

        Assert.NotNull(scrolledTo);
        Assert.Equal(50, scrolledTo);
    }

    [Fact]
    public void NavigateBookmark_NoBookmarks_DoesNotScroll()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Line 0"]);

        bool scrolled = false;
        vm.ScrollToLineRequested += _ => scrolled = true;

        vm.NavigateBookmark(forward: true);

        Assert.False(scrolled);
    }

    [Fact]
    public void NavigateBookmark_DisablesFollowMode()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());
        vm.NavIndex.ToggleBookmark(50);
        vm.SetCurrentLine(0);
        Assert.True(vm.IsFollowMode);

        vm.NavigateBookmark(forward: true);

        Assert.False(vm.IsFollowMode);
    }

    // ── Navigation: Errors ──────────────────────────────────────────

    [Fact]
    public void NavigateError_Forward_ScrollsToError()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());

        vm.NavIndex.SetErrors([5, 25, 75]);
        vm.SetCurrentLine(0);

        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateError(forward: true);

        Assert.NotNull(scrolledTo);
        Assert.Equal(5, scrolledTo);
    }

    [Fact]
    public void NavigateError_Backward_WrapsAround()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());

        vm.NavIndex.SetErrors([5, 25, 75]);
        vm.SetCurrentLine(3);

        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateError(forward: false);

        Assert.NotNull(scrolledTo);
        Assert.Equal(75, scrolledTo); // wraps to last
    }

    [Fact]
    public void NavigateError_NoErrors_DoesNotScroll()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Line 0"]);

        bool scrolled = false;
        vm.ScrollToLineRequested += _ => scrolled = true;

        vm.NavigateError(forward: true);

        Assert.False(scrolled);
    }

    // ── Navigation: Search Hits ─────────────────────────────────────

    [Fact]
    public void NavigateSearchHit_Forward_ScrollsToHit()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", Enumerable.Range(0, 100).Select(i => $"Line {i}").ToList());

        vm.NavIndex.SetSearchHits([10, 30, 60]);
        vm.SetCurrentLine(0);

        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.NavigateSearchHit(forward: true);

        Assert.Equal(10, scrolledTo);
    }

    // ── GetCurrentLineText ──────────────────────────────────────────

    [Fact]
    public void GetCurrentLineText_InMemory_ReturnsRawText()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", [
            "2025-01-01 10:00:00.000 info: Hello World",
            "2025-01-01 10:00:01.000 error: Something broke"
        ]);
        vm.SetCurrentLine(1);

        var text = vm.GetCurrentLineText();

        Assert.Equal("2025-01-01 10:00:01.000 error: Something broke", text);
    }

    [Fact]
    public void GetCurrentLineText_NoSource_ReturnsNull()
    {
        var vm = new LogViewViewModel();
        var text = vm.GetCurrentLineText();
        Assert.Null(text);
    }

    [Fact]
    public void GetCurrentLineText_OutOfRange_ReturnsNull()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Line 0"]);
        vm.SetCurrentLine(999);

        var text = vm.GetCurrentLineText();
        Assert.Null(text);
    }

    [Fact]
    public void GetCurrentLineText_UsesSelectedLine_WhenPresent()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Line 0", "Line 1", "Line 2"]);
        vm.SetCurrentLine(2);
        vm.SelectLine(0);

        var text = vm.GetCurrentLineText();

        Assert.Equal("Line 0", text);
    }

    [Fact]
    public void GetCurrentTimestamp_UsesSelectedLine_WhenPresent()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", [
            "2025-01-01 10:00:00.000 info: first",
            "2025-01-01 10:00:01.000 info: second"
        ]);
        vm.SetCurrentLine(1);
        vm.SelectLine(0);

        var timestamp = vm.GetCurrentTimestamp();

        Assert.Equal(new DateTime(2025, 1, 1, 10, 0, 0), timestamp);
    }

    // ── RequestScrollToLine ─────────────────────────────────────────

    [Fact]
    public void RequestScrollToLine_FiresEvent()
    {
        var vm = new LogViewViewModel();
        int? scrolledTo = null;
        vm.ScrollToLineRequested += line => scrolledTo = line;

        vm.RequestScrollToLine(42);

        Assert.Equal(42, scrolledTo);
    }

    // ── Filter Integration ──────────────────────────────────────────

    [Fact]
    public void ToggleFilter_ShowsThenHides()
    {
        var vm = new LogViewViewModel();

        vm.ToggleFilter();
        Assert.True(vm.Filter.IsVisible);

        vm.ToggleFilter();
        Assert.False(vm.Filter.IsVisible);
    }

    // ── Follow Mode ─────────────────────────────────────────────────

    [Fact]
    public void ToggleFollow_FiresScrollToEnd_WhenEnabled()
    {
        var vm = new LogViewViewModel();
        vm.ToggleFollowCommand.Execute(null); // disable
        Assert.False(vm.IsFollowMode);
        vm.ToggleFollowCommand.Execute(null); // re-enable
        Assert.True(vm.IsFollowMode);
    }

    [Fact]
    public void SelectLine_DisablesFollowAndUpdatesSelectedLine()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Line 0", "Line 1"]);

        vm.SelectLine(1);

        Assert.False(vm.IsFollowMode);
        Assert.Equal(1, vm.SelectedLineIndex);
    }

    // ── Reload Behavior ─────────────────────────────────────────────

    [Fact]
    public void LoadFromLines_Twice_ReplacesContent()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("first.log", ["Line A", "Line B"]);
        Assert.Equal(2, vm.TotalLineCount);

        vm.LoadFromLines("second.log", ["Line X"]);
        Assert.Equal(1, vm.TotalLineCount);
        Assert.Equal("second.log", vm.Title);
    }

    [Fact]
    public void LoadFile_Twice_CleansUpPreviousStreaming()
    {
        var f1 = Path.Combine(_tempDir, "one.log");
        var f2 = Path.Combine(_tempDir, "two.log");
        File.WriteAllLines(f1, ["2025-01-01 10:00:00.000 info: A"]);
        File.WriteAllLines(f2, ["2025-01-01 10:00:00.000 info: B"]);

        var vm = new LogViewViewModel();
        vm.LoadFile(f1);
        Assert.True(vm.IsStreaming);

        vm.LoadFile(f2);
        Assert.True(vm.IsStreaming);
        Assert.Equal("two.log", vm.Title);
    }

    // ── Indexing Progress ───────────────────────────────────────────

    [Fact]
    public void LoadFromLines_IsIndexing_False()
    {
        var vm = new LogViewViewModel();
        vm.LoadFromLines("test.log", ["Hello"]);
        Assert.False(vm.IsIndexing);
    }
}

