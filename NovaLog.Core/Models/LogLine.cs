namespace NovaLog.Core.Models;

public enum LogLevel
{
    Trace,
    Verbose,
    Debug,
    Info,
    Warn,
    Error,
    Fatal,
    Unknown
}

/// <summary>Detected syntax flavor for per-line highlighting.</summary>
public enum SyntaxFlavor { None, Json, Sql, StackTrace }

/// <summary>
/// Core log line data. Readonly record struct to minimize GC pressure
/// when scrolling millions of lines at high speed.
/// </summary>
public readonly record struct LogLine
{
    public int GlobalIndex { get; init; }
    public string RawText { get; init; }
    public DateTime? Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }
    public bool IsContinuation { get; init; }
    public SyntaxFlavor Flavor { get; init; }
    public bool IsFileSeparator { get; init; }

    public LogLine()
    {
        RawText = string.Empty;
        Message = string.Empty;
        Level = LogLevel.Unknown;
    }
}
