using System.Collections.ObjectModel;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Row model for the TreeDataGrid. Represents either a file header (parent)
/// or a log line (leaf). File headers have Children; log lines do not.
/// </summary>
public sealed class GridRowViewModel
{
    public bool IsFileHeader { get; init; }

    // File header properties
    public string FileName { get; init; } = "";
    public string FileDate { get; init; } = "";
    public string? FileSizeText { get; init; }
    public int ChildCount { get; set; }

    // Log line data (null for headers)
    public LogLineViewModel? Line { get; init; }

    // Merged sub-lines for multiline span mode (null = single line)
    public List<LogLineViewModel>? SubLines { get; init; }

    // Auto-formatted lines (JSON pretty-print, SQL formatting) — takes priority over SubLines
    public List<FormattedSubLine>? FormattedLines { get; set; }

    /// <summary>Number of visual lines this row spans (1 for normal, N for multiline).</summary>
    public int LineCount => FormattedLines?.Count ?? SubLines?.Count ?? 1;

    // Convenience accessors for column bindings
    public string TimestampText => Line?.TimestampText ?? "";
    public string LevelText => IsFileHeader ? "" : (Line?.LevelText ?? "");
    public string Message => IsFileHeader ? FileName : (Line?.Message ?? "");
    public LogLevel Level => Line?.Level ?? LogLevel.Unknown;
    public SyntaxFlavor Flavor => Line?.Flavor ?? SyntaxFlavor.None;
    public bool IsContinuation => Line?.IsContinuation ?? false;

    // Children for hierarchical mode (null = leaf)
    public ObservableCollection<GridRowViewModel>? Children { get; init; }
}
