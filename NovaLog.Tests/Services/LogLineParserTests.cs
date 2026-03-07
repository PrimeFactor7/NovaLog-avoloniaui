using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class LogLineParserTests
{
    // ── Standard log line parsing ────────────────────────────────

    [Fact]
    public void Parse_StandardInfoLine_ExtractsAllFields()
    {
        var raw = "2026-03-04 10:30:45.123 info: \tUser logged in successfully";
        var line = LogLineParser.Parse(raw, 42);

        Assert.Equal(42, line.GlobalIndex);
        Assert.Equal(raw, line.RawText);
        Assert.NotNull(line.Timestamp);
        Assert.Equal(2026, line.Timestamp!.Value.Year);
        Assert.Equal(3, line.Timestamp!.Value.Month);
        Assert.Equal(4, line.Timestamp!.Value.Day);
        Assert.Equal(10, line.Timestamp!.Value.Hour);
        Assert.Equal(30, line.Timestamp!.Value.Minute);
        Assert.Equal(45, line.Timestamp!.Value.Second);
        Assert.Equal(123, line.Timestamp!.Value.Millisecond);
        Assert.Equal(LogLevel.Info, line.Level);
        Assert.Contains("User logged in", line.Message);
        Assert.False(line.IsContinuation);
        Assert.False(line.IsFileSeparator);
    }

    [Theory]
    [InlineData("2026-01-01 00:00:00.000 trace: msg", LogLevel.Trace)]
    [InlineData("2026-01-01 00:00:00.000 verbose: msg", LogLevel.Verbose)]
    [InlineData("2026-01-01 00:00:00.000 debug: msg", LogLevel.Debug)]
    [InlineData("2026-01-01 00:00:00.000 info: msg", LogLevel.Info)]
    [InlineData("2026-01-01 00:00:00.000 warn: msg", LogLevel.Warn)]
    [InlineData("2026-01-01 00:00:00.000 error: msg", LogLevel.Error)]
    [InlineData("2026-01-01 00:00:00.000 fatal: msg", LogLevel.Fatal)]
    public void Parse_AllLogLevels_DetectsCorrectly(string raw, LogLevel expected)
    {
        var line = LogLineParser.Parse(raw, 0);
        Assert.Equal(expected, line.Level);
        Assert.Equal("msg", line.Message);
    }

    [Theory]
    [InlineData("2026-01-01 00:00:00.000 INFO: msg", LogLevel.Info)]
    [InlineData("2026-01-01 00:00:00.000 WARN: msg", LogLevel.Warn)]
    [InlineData("2026-01-01 00:00:00.000 Error: msg", LogLevel.Error)]
    public void Parse_CaseInsensitiveLevels(string raw, LogLevel expected)
    {
        var line = LogLineParser.Parse(raw, 0);
        Assert.Equal(expected, line.Level);
    }

    // ── Continuation lines ───────────────────────────────────────

    [Fact]
    public void Parse_NonMatchingLine_IsContinuation()
    {
        var raw = "    at MyApp.Program.Main(String[] args)";
        var line = LogLineParser.Parse(raw, 5);

        Assert.True(line.IsContinuation);
        Assert.Equal(raw, line.Message);
        Assert.Null(line.Timestamp);
        Assert.Equal(5, line.GlobalIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text with no timestamp")]
    [InlineData("--- some separator ---")]
    public void Parse_UnstructuredText_IsContinuation(string raw)
    {
        var line = LogLineParser.Parse(raw, 0);
        Assert.True(line.IsContinuation);
    }

    // ── File separators ──────────────────────────────────────────

    [Fact]
    public void Parse_FileSeparator_DetectsCorrectly()
    {
        var raw = "$$FILE_SEP::app.log::2026-03-04";
        var line = LogLineParser.Parse(raw, 10);

        Assert.True(line.IsFileSeparator);
        Assert.Contains("app.log", line.Message);
        Assert.Contains("2026-03-04", line.Message);
        Assert.Equal(10, line.GlobalIndex);
    }

    [Fact]
    public void Parse_FileSeparator_SinglePart()
    {
        var raw = "$$FILE_SEP::logfile.txt";
        var line = LogLineParser.Parse(raw, 0);

        Assert.True(line.IsFileSeparator);
        Assert.Equal("logfile.txt", line.Message);
    }

    // ── Syntax flavor detection ──────────────────────────────────

    [Fact]
    public void Parse_JsonMessage_DetectsJsonFlavor()
    {
        var raw = "2026-01-01 00:00:00.000 info: {\"key\": \"value\"}";
        var line = LogLineParser.Parse(raw, 0);

        Assert.Equal(SyntaxFlavor.Json, line.Flavor);
    }

    [Fact]
    public void Parse_SqlMessage_DetectsSqlFlavor()
    {
        var raw = "2026-01-01 00:00:00.000 debug: SELECT * FROM Users WHERE Id = 1";
        var line = LogLineParser.Parse(raw, 0);

        Assert.Equal(SyntaxFlavor.Sql, line.Flavor);
    }

    [Fact]
    public void Parse_StackTraceMessage_DetectsStackTrace()
    {
        var raw = "2026-01-01 00:00:00.000 error: at MyApp.Services.UserService.GetUser(Int32 id)";
        var line = LogLineParser.Parse(raw, 0);

        Assert.Equal(SyntaxFlavor.StackTrace, line.Flavor);
    }

    [Fact]
    public void Parse_PlainMessage_DetectsNone()
    {
        var raw = "2026-01-01 00:00:00.000 info: Application started";
        var line = LogLineParser.Parse(raw, 0);

        Assert.Equal(SyntaxFlavor.None, line.Flavor);
    }

    // ── GlobalIndex ──────────────────────────────────────────────

    [Fact]
    public void Parse_PreservesGlobalIndex()
    {
        for (int i = 0; i < 100; i++)
        {
            var line = LogLineParser.Parse("2026-01-01 00:00:00.000 info: line", i);
            Assert.Equal(i, line.GlobalIndex);
        }
    }

    // ── LogLine is value type ────────────────────────────────────

    [Fact]
    public void LogLine_IsReadonlyRecordStruct()
    {
        var line = new LogLine();
        Assert.True(typeof(LogLine).IsValueType);
        Assert.Equal(string.Empty, line.RawText);
        Assert.Equal(string.Empty, line.Message);
        Assert.Equal(LogLevel.Unknown, line.Level);
    }

    [Fact]
    public void LogLine_StructEquality()
    {
        var a = new LogLine { GlobalIndex = 1, RawText = "test", Message = "test" };
        var b = new LogLine { GlobalIndex = 1, RawText = "test", Message = "test" };
        Assert.Equal(a, b);
    }

    // ── Batch parsing (simulates file load) ──────────────────────

    [Fact]
    public void Parse_BatchOfLines_ProducesCorrectCount()
    {
        var lines = new[]
        {
            "2026-01-01 00:00:00.000 info: Line 1",
            "2026-01-01 00:00:00.001 warn: Line 2",
            "continuation line",
            "2026-01-01 00:00:00.002 error: Line 4",
            "{\"nested\": true}",
        };

        var parsed = lines.Select((raw, i) => LogLineParser.Parse(raw, i)).ToList();

        Assert.Equal(5, parsed.Count);
        Assert.Equal(LogLevel.Info, parsed[0].Level);
        Assert.Equal(LogLevel.Warn, parsed[1].Level);
        Assert.True(parsed[2].IsContinuation);
        Assert.Equal(LogLevel.Error, parsed[3].Level);
        Assert.True(parsed[4].IsContinuation);
        Assert.Equal(SyntaxFlavor.Json, parsed[4].Flavor);
    }
}
