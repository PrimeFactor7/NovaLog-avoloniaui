using System.Collections.ObjectModel;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>Options for auto-formatting JSON/SQL in span-lines mode.</summary>
public sealed record FormattingOptions(
    bool JsonFormatEnabled, bool SqlFormatEnabled,
    int IndentSize = 2, int MaxRowLines = 50);

/// <summary>
/// Transforms flat LogLineViewModel lists into GridRowViewModel structures
/// for use with TreeDataGrid sources.
/// </summary>
public static class GridSourceBuilder
{
    /// <summary>Build a flat list (no hierarchy). Skips file separator lines.</summary>
    public static List<GridRowViewModel> BuildFlat(
        IReadOnlyList<LogLineViewModel> lines, bool multiline = false,
        FormattingOptions? formatting = null)
    {
        var result = new List<GridRowViewModel>(lines.Count);
        foreach (var line in lines)
        {
            if (line.IsFileSeparator) continue;
            result.Add(new GridRowViewModel { Line = line });
        }
        var merged = multiline ? MergeContinuations(result) : result;
        if (formatting is not null) ApplyFormatting(merged, formatting);
        return merged;
    }

    /// <summary>
    /// Build hierarchical list grouped by file separators.
    /// File separator lines become parent nodes; subsequent lines become children.
    /// Lines before the first separator get a synthetic header.
    /// </summary>
    public static List<GridRowViewModel> BuildHierarchical(
        IReadOnlyList<LogLineViewModel> lines, string? defaultFileName = null,
        bool multiline = false, FormattingOptions? formatting = null)
    {
        var result = new List<GridRowViewModel>();
        GridRowViewModel? currentHeader = null;
        ObservableCollection<GridRowViewModel>? currentChildren = null;

        foreach (var line in lines)
        {
            if (line.IsFileSeparator)
            {
                // Flush previous group
                FlushGroup(result, ref currentHeader, multiline, formatting);

                // Start new group
                currentChildren = new ObservableCollection<GridRowViewModel>();
                currentHeader = new GridRowViewModel
                {
                    IsFileHeader = true,
                    FileName = line.Message,
                    FileSizeText = line.FileSizeText,
                    Line = line,
                    Children = currentChildren,
                };
            }
            else
            {
                if (currentChildren is null)
                {
                    // Lines before first separator — synthesize a header
                    currentChildren = new ObservableCollection<GridRowViewModel>();
                    currentHeader = new GridRowViewModel
                    {
                        IsFileHeader = true,
                        FileName = defaultFileName ?? "(current file)",
                        Children = currentChildren,
                    };
                }
                currentChildren.Add(new GridRowViewModel { Line = line });
            }
        }

        // Flush last group
        FlushGroup(result, ref currentHeader, multiline, formatting);
        return result;
    }

    private static void FlushGroup(List<GridRowViewModel> result,
        ref GridRowViewModel? header, bool multiline = false,
        FormattingOptions? formatting = null)
    {
        if (header is null) return;

        if (multiline && header.Children is { Count: > 0 } children)
        {
            var merged = MergeContinuations(children);
            children.Clear();
            foreach (var row in merged)
                children.Add(row);
        }

        if (formatting is not null && header.Children is { Count: > 0 } kids)
            ApplyFormatting(kids, formatting);

        header.ChildCount = header.Children?.Count ?? 0;
        result.Add(header);
        header = null;
    }

    /// <summary>
    /// Merges continuation lines into the preceding primary line,
    /// creating multiline rows with SubLines.
    /// </summary>
    private static List<GridRowViewModel> MergeContinuations(IReadOnlyList<GridRowViewModel> rows)
    {
        var result = new List<GridRowViewModel>(rows.Count);
        List<LogLineViewModel>? pendingSub = null;
        GridRowViewModel? pendingPrimary = null;

        foreach (var row in rows)
        {
            if (row.Line is null) { result.Add(row); continue; }

            if (row.IsContinuation && pendingPrimary is not null)
            {
                // Merge into pending multiline group
                pendingSub ??= [pendingPrimary.Line!];
                pendingSub.Add(row.Line);
            }
            else
            {
                // Flush previous group
                FlushMerged(result, pendingPrimary, pendingSub);
                pendingPrimary = row;
                pendingSub = null;
            }
        }

        FlushMerged(result, pendingPrimary, pendingSub);
        return result;
    }

    private static void FlushMerged(List<GridRowViewModel> result,
        GridRowViewModel? primary, List<LogLineViewModel>? subLines)
    {
        if (primary is null) return;
        if (subLines is { Count: > 1 })
        {
            result.Add(new GridRowViewModel
            {
                Line = primary.Line,
                SubLines = subLines,
            });
        }
        else
        {
            result.Add(primary);
        }
    }

    /// <summary>
    /// Apply auto-formatting (JSON pretty-print, SQL clause splitting) to rows.
    /// Sets FormattedLines on applicable rows.
    /// </summary>
    private static void ApplyFormatting(IReadOnlyList<GridRowViewModel> rows, FormattingOptions fmt)
    {
        foreach (var row in rows)
        {
            if (row.IsFileHeader || row.Line is null) continue;

            // Determine source text and flavor
            string message;
            SyntaxFlavor flavor;
            if (row.SubLines is { Count: > 0 } subs)
            {
                message = string.Join("\n", subs.Select(s => s.Message));
                flavor = subs[0].Flavor;
            }
            else
            {
                message = row.Line.Message;
                flavor = row.Line.Flavor;
            }

            var formatted = MessageFormatter.Format(
                message, flavor,
                fmt.JsonFormatEnabled, fmt.SqlFormatEnabled,
                fmt.IndentSize, fmt.MaxRowLines);

            if (formatted is not null)
                row.FormattedLines = formatted;
        }
    }
}
