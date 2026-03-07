using System.Collections.ObjectModel;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Transforms flat LogLineViewModel lists into GridRowViewModel structures
/// for use with TreeDataGrid sources.
/// </summary>
public static class GridSourceBuilder
{
    /// <summary>Build a flat list (no hierarchy). Skips file separator lines.</summary>
    public static List<GridRowViewModel> BuildFlat(IReadOnlyList<LogLineViewModel> lines)
    {
        var result = new List<GridRowViewModel>(lines.Count);
        foreach (var line in lines)
        {
            if (line.IsFileSeparator) continue;
            result.Add(new GridRowViewModel { Line = line });
        }
        return result;
    }

    /// <summary>
    /// Build hierarchical list grouped by file separators.
    /// File separator lines become parent nodes; subsequent lines become children.
    /// Lines before the first separator get a synthetic header.
    /// </summary>
    public static List<GridRowViewModel> BuildHierarchical(
        IReadOnlyList<LogLineViewModel> lines, string? defaultFileName = null)
    {
        var result = new List<GridRowViewModel>();
        GridRowViewModel? currentHeader = null;
        ObservableCollection<GridRowViewModel>? currentChildren = null;

        foreach (var line in lines)
        {
            if (line.IsFileSeparator)
            {
                // Flush previous group
                FlushGroup(result, ref currentHeader);

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
        FlushGroup(result, ref currentHeader);
        return result;
    }

    private static void FlushGroup(List<GridRowViewModel> result, ref GridRowViewModel? header)
    {
        if (header is null) return;
        header.ChildCount = header.Children?.Count ?? 0;
        result.Add(header);
        header = null;
    }
}
