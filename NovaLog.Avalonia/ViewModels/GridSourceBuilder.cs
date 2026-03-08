using System.Collections.ObjectModel;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Transforms flat LogLineViewModel lists into GridRowViewModel structures
/// for use with TreeDataGrid sources.
/// </summary>
public static class GridSourceBuilder
{
    /// <summary>Build a flat list (no hierarchy). Skips file separator lines.</summary>
    public static List<GridRowViewModel> BuildFlat(
        IReadOnlyList<LogLineViewModel> lines, bool multiline = false)
    {
        var result = new List<GridRowViewModel>(lines.Count);
        foreach (var line in lines)
        {
            if (line.IsFileSeparator) continue;
            result.Add(new GridRowViewModel { Line = line });
        }
        return multiline ? MergeContinuations(result) : result;
    }

    /// <summary>
    /// Build hierarchical list grouped by file separators.
    /// File separator lines become parent nodes; subsequent lines become children.
    /// Lines before the first separator get a synthetic header.
    /// </summary>
    public static List<GridRowViewModel> BuildHierarchical(
        IReadOnlyList<LogLineViewModel> lines, string? defaultFileName = null,
        bool multiline = false)
    {
        var result = new List<GridRowViewModel>();
        GridRowViewModel? currentHeader = null;
        ObservableCollection<GridRowViewModel>? currentChildren = null;

        foreach (var line in lines)
        {
            if (line.IsFileSeparator)
            {
                // Flush previous group
                FlushGroup(result, ref currentHeader, multiline);

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
        FlushGroup(result, ref currentHeader, multiline);
        return result;
    }

    private static void FlushGroup(List<GridRowViewModel> result,
        ref GridRowViewModel? header, bool multiline = false)
    {
        if (header is null) return;

        if (multiline && header.Children is { Count: > 0 } children)
        {
            var merged = MergeContinuations(children);
            children.Clear();
            foreach (var row in merged)
                children.Add(row);
        }

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
}
