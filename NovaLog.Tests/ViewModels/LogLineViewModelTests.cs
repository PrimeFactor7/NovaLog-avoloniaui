using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;

namespace NovaLog.Tests.ViewModels;

public class LogLineViewModelTests
{
    [Fact]
    public void Ctor_StandardLine_MapsAllProperties()
    {
        var line = new LogLine
        {
            GlobalIndex = 7,
            RawText = "2026-01-01 12:00:00.000 info: Hello",
            Timestamp = new DateTime(2026, 1, 1, 12, 0, 0),
            Level = LogLevel.Info,
            Message = "Hello",
            Flavor = SyntaxFlavor.None,
            IsContinuation = false,
            IsFileSeparator = false
        };

        var vm = new LogLineViewModel(line);

        Assert.Equal(7, vm.GlobalIndex);
        Assert.Equal("Hello", vm.Message);
        Assert.Equal(LogLevel.Info, vm.Level);
        Assert.Equal(SyntaxFlavor.None, vm.Flavor);
        Assert.False(vm.IsContinuation);
        Assert.False(vm.IsFileSeparator);
        Assert.Equal("2026-01-01 12:00:00.000", vm.TimestampText);
        Assert.Equal("info:", vm.LevelText);
    }

    [Fact]
    public void Ctor_ContinuationLine_EmptyLevelText()
    {
        var line = new LogLine
        {
            GlobalIndex = 2,
            RawText = "  continuation text",
            Message = "  continuation text",
            IsContinuation = true
        };

        var vm = new LogLineViewModel(line);

        Assert.True(vm.IsContinuation);
        Assert.Equal(string.Empty, vm.LevelText);
        Assert.Equal(string.Empty, vm.TimestampText);
    }

    [Fact]
    public void Ctor_NoTimestamp_EmptyTimestampText()
    {
        var line = new LogLine { Message = "no time" };
        var vm = new LogLineViewModel(line);

        Assert.Equal(string.Empty, vm.TimestampText);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trace:")]
    [InlineData(LogLevel.Verbose, "verbose:")]
    [InlineData(LogLevel.Debug, "debug:")]
    [InlineData(LogLevel.Info, "info:")]
    [InlineData(LogLevel.Warn, "warn:")]
    [InlineData(LogLevel.Error, "error:")]
    [InlineData(LogLevel.Fatal, "fatal:")]
    [InlineData(LogLevel.Unknown, "")]
    public void LevelText_AllLevels(LogLevel level, string expected)
    {
        var line = new LogLine { Level = level, Message = "test" };
        var vm = new LogLineViewModel(line);
        Assert.Equal(expected, vm.LevelText);
    }

    [Fact]
    public void Ctor_FileSeparator_MapsCorrectly()
    {
        var line = new LogLine
        {
            IsFileSeparator = true,
            Message = "app.log (2026-03-04)"
        };

        var vm = new LogLineViewModel(line);

        Assert.True(vm.IsFileSeparator);
        Assert.Equal("app.log (2026-03-04)", vm.Message);
    }

    [Fact]
    public void Ctor_PreservesRawText()
    {
        var raw = "2026-01-01 00:00:00.000 warn: Something bad";
        var line = new LogLine { RawText = raw, Message = "Something bad" };
        var vm = new LogLineViewModel(line);

        Assert.Equal(raw, vm.RawText);
    }

    [Fact]
    public void Ctor_PreservesFlavor()
    {
        var line = new LogLine { Message = "{\"key\": 1}", Flavor = SyntaxFlavor.Json };
        var vm = new LogLineViewModel(line);

        Assert.Equal(SyntaxFlavor.Json, vm.Flavor);
    }

    [Fact]
    public void Ctor_PreservesMergeSourceMetadata()
    {
        var line = new LogLine { GlobalIndex = 4, Message = "merged line" };

        var vm = new LogLineViewModel(line, "api-1", "#00D4FF");

        Assert.Equal("api-1", vm.MergeSourceTag);
        Assert.Equal("#00D4FF", vm.MergeSourceColorHex);
    }
}
