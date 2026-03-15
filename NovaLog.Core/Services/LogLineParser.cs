using System;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Parses raw log text into structured LogLine records.
/// Format: "yyyy-MM-dd HH:mm:ss.fff level: \tmessage"
/// Uses Span-based parsing for maximum performance and zero allocations.
/// </summary>
public static partial class LogLineParser
{
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

        var span = rawText.AsSpan();

        // 1. Try parse timestamp (exactly 23 chars: "yyyy-MM-dd HH:mm:ss.fff")
        if (span.Length >= 23 && char.IsDigit(span[0]) && char.IsDigit(span[1]) && char.IsDigit(span[2]) && char.IsDigit(span[3]) && span[4] == '-')
        {
            if (DateTime.TryParse(span[..23], out var ts))
            {
                var afterTs = span[23..];
                
                // Skip spaces
                int i = 0;
                while (i < afterTs.Length && char.IsWhiteSpace(afterTs[i])) i++;
                
                if (i < afterTs.Length)
                {
                    var afterSpace = afterTs[i..];
                    int colonIdx = afterSpace.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var levelSpan = afterSpace[..colonIdx];
                        var level = ParseLevel(levelSpan);
                        if (level != LogLevel.Unknown)
                        {
                            var msgSpan = afterSpace[(colonIdx + 1)..];
                            // Skip leading spaces in message
                            int j = 0;
                            while (j < msgSpan.Length && char.IsWhiteSpace(msgSpan[j])) j++;
                            var message = msgSpan[j..].ToString();

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
                    }
                }
            }
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

    private static LogLevel ParseLevel(ReadOnlySpan<char> span)
    {
        if (span.Equals("info", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (span.Equals("error", StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;
        if (span.Equals("warn", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warn;
        if (span.Equals("debug", StringComparison.OrdinalIgnoreCase)) return LogLevel.Debug;
        if (span.Equals("fatal", StringComparison.OrdinalIgnoreCase)) return LogLevel.Fatal;
        if (span.Equals("trace", StringComparison.OrdinalIgnoreCase)) return LogLevel.Trace;
        if (span.Equals("verbose", StringComparison.OrdinalIgnoreCase)) return LogLevel.Verbose;
        return LogLevel.Unknown;
    }
}
