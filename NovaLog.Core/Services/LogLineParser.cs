using System.Text.RegularExpressions;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Parses raw log text into structured LogLine records.
/// Format: "yyyy-MM-dd HH:mm:ss.fff level: \tmessage"
/// </summary>
public static partial class LogLineParser
{
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+(?<level>fatal|error|warn|info|debug|verbose|trace):\s*(?<msg>.*)",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex LinePattern();

    private const string FileSepPrefix = "$$FILE_SEP::";

    public static LogLine Parse(string rawText, int globalIndex)
    {
        if (rawText.StartsWith(FileSepPrefix, StringComparison.Ordinal))
        {
            var parts = rawText[FileSepPrefix.Length..].Split("::", 3);
            var label = parts.Length >= 2 ? $"{parts[0]}  ({parts[1]})" : parts[0];
            long fileSize = 0;
            if (parts.Length >= 3)
                long.TryParse(parts[2], out fileSize);
            return new LogLine
            {
                GlobalIndex = globalIndex,
                RawText = rawText,
                Message = label,
                IsFileSeparator = true,
                FileSize = fileSize
            };
        }

        var match = LinePattern().Match(rawText);
        if (match.Success)
        {
            DateTime? ts = DateTime.TryParse(match.Groups["ts"].ValueSpan, out var parsed) ? parsed : null;

            var level = match.Groups["level"].Value.ToLowerInvariant() switch
            {
                "fatal"   => LogLevel.Fatal,
                "error"   => LogLevel.Error,
                "warn"    => LogLevel.Warn,
                "info"    => LogLevel.Info,
                "debug"   => LogLevel.Debug,
                "verbose" => LogLevel.Verbose,
                "trace"   => LogLevel.Trace,
                _         => LogLevel.Unknown
            };

            var message = match.Groups["msg"].Value;
            return new LogLine
            {
                GlobalIndex = globalIndex,
                RawText = rawText,
                Timestamp = ts,
                Level = level,
                Message = message,
                Flavor = SyntaxResolver.Detect(message)
            };
        }

        return new LogLine
        {
            GlobalIndex = globalIndex,
            RawText = rawText,
            IsContinuation = true,
            Message = rawText,
            Flavor = SyntaxResolver.Detect(rawText)
        };
    }
}
