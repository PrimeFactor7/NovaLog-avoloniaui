using System.Collections.Specialized;
using NovaLog.Avalonia.Controls;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Controls;

/// <summary>
/// Tests for InMemoryLogItemsSource.AppendLines, which enables
/// live streaming without rebinding ItemsSource.
/// </summary>
public class InMemoryAppendTests
{
    [Fact]
    public void AppendLines_IncreasesCount()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([
            new LogLine { GlobalIndex = 0, Message = "Initial" }
        ]);
        Assert.Equal(1, source.Count);

        source.AppendLines([
            new LogLine { GlobalIndex = 1, Message = "Appended 1" },
            new LogLine { GlobalIndex = 2, Message = "Appended 2" }
        ]);

        Assert.Equal(3, source.Count);
    }

    [Fact]
    public void AppendLines_PreservesExistingItems()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([
            new LogLine { GlobalIndex = 0, Message = "Original" }
        ]);

        source.AppendLines([
            new LogLine { GlobalIndex = 1, Message = "New" }
        ]);

        Assert.Equal("Original", source[0].Message);
        Assert.Equal("New", source[1].Message);
    }

    [Fact]
    public void AppendLines_FiresCollectionChangedReset()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([new LogLine { GlobalIndex = 0, Message = "A" }]);

        NotifyCollectionChangedAction? action = null;
        ((INotifyCollectionChanged)source).CollectionChanged += (_, e) =>
        {
            action = e.Action;
        };

        source.AppendLines([
            new LogLine { GlobalIndex = 1, Message = "B" }
        ]);

        Assert.Equal(NotifyCollectionChangedAction.Reset, action);
    }

    [Fact]
    public void AppendLines_EmptyList_StillFiresReset()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([new LogLine { GlobalIndex = 0, Message = "A" }]);

        bool fired = false;
        ((INotifyCollectionChanged)source).CollectionChanged += (_, _) => fired = true;

        source.AppendLines(Enumerable.Empty<LogLine>());

        // Even empty append fires Reset (this is the documented behavior)
        Assert.True(fired);
    }

    [Fact]
    public void AppendLines_OnEmptySource_Works()
    {
        var source = new InMemoryLogItemsSource();

        source.AppendLines([
            new LogLine { GlobalIndex = 0, Message = "First ever" }
        ]);

        Assert.Equal(1, source.Count);
        Assert.Equal("First ever", source[0].Message);
    }

    [Fact]
    public void AppendLines_MultipleAppends_Accumulate()
    {
        var source = new InMemoryLogItemsSource();

        for (int batch = 0; batch < 5; batch++)
        {
            var lines = Enumerable.Range(batch * 10, 10)
                .Select(i => new LogLine { GlobalIndex = i, Message = $"Line {i}" });
            source.AppendLines(lines);
        }

        Assert.Equal(50, source.Count);
        Assert.Equal("Line 0", source[0].Message);
        Assert.Equal("Line 49", source[49].Message);
    }

    [Fact]
    public void AppendLines_GlobalIndex_PreservedCorrectly()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([
            new LogLine { GlobalIndex = 0, Message = "A" },
            new LogLine { GlobalIndex = 1, Message = "B" }
        ]);

        source.AppendLines([
            new LogLine { GlobalIndex = 2, Message = "C" },
            new LogLine { GlobalIndex = 3, Message = "D" }
        ]);

        Assert.Equal(0, source[0].GlobalIndex);
        Assert.Equal(3, source[3].GlobalIndex);
    }

    [Fact]
    public void AppendLines_ParsedFromRawText_LevelDetected()
    {
        var source = new InMemoryLogItemsSource();

        var parsed = new[]
        {
            "2025-01-01 10:00:00.000 error: Connection failed",
            "2025-01-01 10:00:01.000 warn: Retrying..."
        }.Select((raw, i) => LogLineParser.Parse(raw, i));

        source.AppendLines(parsed);

        Assert.Equal(2, source.Count);
        Assert.Equal(LogLevel.Error, source[0].Level);
        Assert.Equal(LogLevel.Warn, source[1].Level);
    }

    [Fact]
    public void AppendLines_StreamingSimulation_LargeAppend()
    {
        var source = new InMemoryLogItemsSource();

        // Simulate initial file load
        var initial = Enumerable.Range(0, 1000)
            .Select(i => new LogLine { GlobalIndex = i, Message = $"Initial {i}" });
        source.AddRange(initial);
        Assert.Equal(1000, source.Count);

        // Simulate streaming append
        var streamed = Enumerable.Range(1000, 100)
            .Select(i => new LogLine { GlobalIndex = i, Message = $"Streamed {i}" });
        source.AppendLines(streamed);

        Assert.Equal(1100, source.Count);
        Assert.Equal("Streamed 1099", source[1099].Message);
    }

    [Fact]
    public void Enumerable_IncludesAppendedItems()
    {
        var source = new InMemoryLogItemsSource();
        source.AddRange([new LogLine { GlobalIndex = 0, Message = "A" }]);
        source.AppendLines([new LogLine { GlobalIndex = 1, Message = "B" }]);

        var messages = source.Select(vm => vm.Message).ToList();
        Assert.Equal(["A", "B"], messages);
    }
}
