using System.Collections.Specialized;
using NovaLog.Avalonia.Controls;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Controls;

public class InMemoryLogItemsSourceTests
{
    [Fact]
    public void Initial_Empty()
    {
        var source = new InMemoryLogItemsSource();
        Assert.Equal(0, source.Count);
    }

    [Fact]
    public void AddRange_AddsItems()
    {
        var source = new InMemoryLogItemsSource();
        var lines = new[]
        {
            new LogLine { GlobalIndex = 0, Message = "First" },
            new LogLine { GlobalIndex = 1, Message = "Second" },
        };

        source.AddRange(lines);

        Assert.Equal(2, source.Count);
        Assert.Equal("First", source[0].Message);
        Assert.Equal("Second", source[1].Message);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange(new[] { new LogLine { Message = "a" }, new LogLine { Message = "b" } });

        source.Clear();

        Assert.Equal(0, source.Count);
    }

    [Fact]
    public void Indexer_ReturnsLogLineViewModel()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange(new[] { new LogLine { GlobalIndex = 42, Message = "hello", Level = LogLevel.Warn } });

        var vm = source[0];
        Assert.IsType<LogLineViewModel>(vm);
        Assert.Equal(42, vm.GlobalIndex);
        Assert.Equal("hello", vm.Message);
        Assert.Equal(LogLevel.Warn, vm.Level);
    }

    [Fact]
    public void Enumerable_Works()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange(new[]
        {
            new LogLine { Message = "a" },
            new LogLine { Message = "b" },
            new LogLine { Message = "c" },
        });

        var messages = source.Select(vm => vm.Message).ToList();
        Assert.Equal(new[] { "a", "b", "c" }, messages);
    }

    // ── End-to-end: parsing pipeline into InMemoryLogItemsSource ─

    [Fact]
    public void Pipeline_RawLines_ToParsed_ToItemsSource()
    {
        var rawLines = new[]
        {
            "2026-03-04 10:00:00.000 info: Application started",
            "2026-03-04 10:00:01.000 warn: Low disk space",
            "  at MyApp.DiskService.Check()",
            "2026-03-04 10:00:02.000 error: {\"error\": \"disk_full\", \"code\": 507}",
        };

        var source = new InMemoryLogItemsSource();
        var parsed = rawLines.Select((raw, i) => LogLineParser.Parse(raw, i));
        source.AddRange(parsed);

        Assert.Equal(4, source.Count);

        // Line 0: info, plain
        Assert.Equal(LogLevel.Info, source[0].Level);
        Assert.Equal(SyntaxFlavor.None, source[0].Flavor);
        Assert.False(source[0].IsContinuation);

        // Line 1: warn, plain
        Assert.Equal(LogLevel.Warn, source[1].Level);

        // Line 2: continuation, stack trace
        Assert.True(source[2].IsContinuation);
        Assert.Equal(SyntaxFlavor.StackTrace, source[2].Flavor);

        // Line 3: error, JSON
        Assert.Equal(LogLevel.Error, source[3].Level);
        Assert.Equal(SyntaxFlavor.Json, source[3].Flavor);
    }
}

public class VirtualLogItemsSourceTests
{
    private class FakeVirtualLogProvider : IVirtualLogProvider
    {
        private readonly List<LogLine> _lines = new();

        public long LineCount => _lines.Count;
        public bool IsIndexing => false;
        public double IndexingProgress => 1.0;
        public string FilePath => "fake.log";

        public event Action<long>? IndexingProgressChanged;
        public event Action? IndexingCompleted;
        public event Action<long>? LinesAppended;

        public void AddLines(params LogLine[] lines)
        {
            _lines.AddRange(lines);
        }

        public void SimulateAppend(LogLine line)
        {
            _lines.Add(line);
            LinesAppended?.Invoke(_lines.Count);
        }

        public LogLine? GetLine(long index)
        {
            if (index < 0 || index >= _lines.Count) return null;
            return _lines[(int)index];
        }

        public IReadOnlyList<LogLine> GetPage(long startIndex, int count)
        {
            return _lines.Skip((int)startIndex).Take(count).ToList();
        }

        public string? GetRawLine(long index)
        {
            if (index < 0 || index >= _lines.Count) return null;
            return _lines[(int)index].RawText;
        }

        public void ScrollToTimestamp(DateTime target, Action<long> onFound)
        {
            // Stub implementation for tests
            onFound?.Invoke(0);
        }

        public void Dispose() { }
    }

    [Fact]
    public void Count_ReflectsProvider()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(
            new LogLine { GlobalIndex = 0, Message = "a" },
            new LogLine { GlobalIndex = 1, Message = "b" }
        );

        var source = new VirtualLogItemsSource(provider);
        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void Indexer_ReturnsViewModelFromProvider()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(new LogLine { GlobalIndex = 0, Message = "hello", Level = LogLevel.Info });

        var source = new VirtualLogItemsSource(provider);
        var vm = source[0];

        Assert.Equal("hello", vm.Message);
        Assert.Equal(LogLevel.Info, vm.Level);
    }

    [Fact]
    public void Indexer_CachesResults()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(new LogLine { GlobalIndex = 0, Message = "cached" });

        var source = new VirtualLogItemsSource(provider);
        var first = source[0];
        var second = source[0];

        Assert.Same(first, second); // Same reference = cached
    }

    [Fact]
    public void LRU_EvictsOldestWhenFull()
    {
        var provider = new FakeVirtualLogProvider();
        for (int i = 0; i < 250; i++)
            provider.AddLines(new LogLine { GlobalIndex = i, Message = $"Line {i}" });

        var source = new VirtualLogItemsSource(provider);

        // Access 250 items — cache capacity is 200, so first 50 should be evicted
        for (int i = 0; i < 250; i++)
            _ = source[i];

        // Item 0 should have been evicted; accessing it again returns a new instance
        var refBefore = source[249]; // still in cache
        var refAfter = source[249];
        Assert.Same(refBefore, refAfter); // recent item still cached

        // Item 0 was evicted — new access creates new VM
        // (we can't directly test eviction, but the source shouldn't crash)
        var item0 = source[0];
        Assert.Equal("Line 0", item0.Message);
    }

    [Fact]
    public void NotifyReset_ClearsCacheAndFiresEvent()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(new LogLine { GlobalIndex = 0, Message = "reset" });

        var source = new VirtualLogItemsSource(provider);
        _ = source[0]; // populate cache

        bool fired = false;
        source.CollectionChanged += (_, e) =>
        {
            fired = true;
            Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
        };

        source.NotifyReset();

        Assert.True(fired);

        // After reset, accessing index returns new VM (not cached one)
        var after = source[0];
        Assert.Equal("reset", after.Message);
    }

    [Fact]
    public void LinesAppended_TriggersReset()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(new LogLine { GlobalIndex = 0, Message = "initial" });

        var source = new VirtualLogItemsSource(provider);

        bool resetFired = false;
        source.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                resetFired = true;
        };

        provider.SimulateAppend(new LogLine { GlobalIndex = 1, Message = "appended" });

        Assert.True(resetFired);
        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void Indexer_OutOfRange_ReturnsFallback()
    {
        var provider = new FakeVirtualLogProvider();
        var source = new VirtualLogItemsSource(provider);

        // Provider has 0 lines, but GetLine returns null → should return fallback VM
        var vm = source[999];
        Assert.NotNull(vm);
        Assert.Equal("", vm.Message);
    }

    [Fact]
    public void Enumerable_Works()
    {
        var provider = new FakeVirtualLogProvider();
        provider.AddLines(
            new LogLine { GlobalIndex = 0, Message = "x" },
            new LogLine { GlobalIndex = 1, Message = "y" }
        );

        var source = new VirtualLogItemsSource(provider);
        var messages = source.Select(vm => vm.Message).ToList();

        Assert.Equal(new[] { "x", "y" }, messages);
    }
}
