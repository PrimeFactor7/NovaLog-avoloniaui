using NovaLog.Core.Models;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Lightweight view model for a single log line displayed in the ItemsRepeater.
/// </summary>
public sealed class LogLineViewModel
{
    public string TimestampText { get; }
    public DateTime? Timestamp { get; }
    public string LevelText { get; }
    public string Message { get; }
    public LogLevel Level { get; }
    public SyntaxFlavor Flavor { get; set; }
    public IReadOnlyList<HighlightToken> MessageTokens { get; }
    public bool IsContinuation { get; }
    public bool IsFileSeparator { get; }
    public long FileSize { get; }
    public string FileSizeText { get; }
    public int GlobalIndex { get; }
    public string RawText { get; }
    public string? MergeSourceTag { get; }
    public string? MergeSourceColorHex { get; }

    public LogLineViewModel(LogLine line, string? mergeSourceTag = null, string? mergeSourceColorHex = null)
    {
        GlobalIndex = line.GlobalIndex;
        RawText = line.RawText;
        Timestamp = line.Timestamp;
        Level = line.Level;
        Flavor = line.Flavor;
        IsContinuation = line.IsContinuation;
        IsFileSeparator = line.IsFileSeparator;
        FileSize = line.FileSize;
        FileSizeText = FormatFileSize(line.FileSize);
        Message = line.Message;
        MergeSourceTag = mergeSourceTag;
        MergeSourceColorHex = mergeSourceColorHex;

        TimestampText = line.Timestamp.HasValue
            ? line.Timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss.fff")
            : string.Empty;

        LevelText = line.IsContinuation ? string.Empty : LevelToString(line.Level);

        MessageTokens = SyntaxHighlighter.Tokenize(Message, Flavor, IsContinuation);
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
    };

    private static string LevelToString(LogLevel level) => level switch
    {
        LogLevel.Trace   => "trace:",
        LogLevel.Verbose => "verbose:",
        LogLevel.Debug   => "debug:",
        LogLevel.Info    => "info:",
        LogLevel.Warn    => "warn:",
        LogLevel.Error   => "error:",
        LogLevel.Fatal   => "fatal:",
        _                => ""
    };
}
