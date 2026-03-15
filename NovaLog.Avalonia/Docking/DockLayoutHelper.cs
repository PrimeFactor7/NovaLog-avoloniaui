using System.Collections.Generic;
using Dock.Model.Core;
using Dock.Model.Controls;

namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Helpers to traverse a Dock layout and find LogView documents and the active pane.
/// </summary>
public static class DockLayoutHelper
{
    private const int MaxDepth = 64;

    /// <summary>
    /// Gets the currently active LogViewViewModel from the layout by following ActiveDockable.
    /// Returns null if the active dockable is not a LogViewDocument.
    /// </summary>
    public static ViewModels.LogViewViewModel? GetActiveLogView(IDock? root, int depth = 0)
    {
        if (root?.ActiveDockable is null || depth > MaxDepth)
            return null;
        var dockable = root.ActiveDockable;
        if (dockable is LogViewDocument doc)
            return doc.LogView;
        if (dockable is IDock child)
            return GetActiveLogView(child, depth + 1);
        return null;
    }

    /// <summary>
    /// Gets the currently active LogViewDocument from the layout by following ActiveDockable.
    /// </summary>
    public static LogViewDocument? GetActiveDocument(IDock? root, int depth = 0)
    {
        if (root?.ActiveDockable is null || depth > MaxDepth)
            return null;
        var dockable = root.ActiveDockable;
        if (dockable is LogViewDocument doc)
            return doc;
        if (dockable is IDock child)
            return GetActiveDocument(child, depth + 1);
        return null;
    }

    /// <summary>
    /// Collects all LogViewViewModel instances from all LogViewDocuments in the layout.
    /// </summary>
    public static List<ViewModels.LogViewViewModel> GetAllLogViews(IDock? root)
    {
        var list = new List<ViewModels.LogViewViewModel>();
        if (root is null) return list;
        CollectLogViews(root, list, 0);
        return list;
    }

    /// <summary>
    /// Collects all LogViewDocument instances in the layout.
    /// </summary>
    public static List<LogViewDocument> GetAllDocuments(IDock? root)
    {
        var list = new List<LogViewDocument>();
        if (root is null) return list;
        CollectDocuments(root, list, 0);
        return list;
    }

    private static void CollectLogViews(IDock dock, List<ViewModels.LogViewViewModel> list, int depth)
    {
        if (dock.VisibleDockables is null || depth > MaxDepth)
            return;
        foreach (var d in dock.VisibleDockables)
        {
            if (d is LogViewDocument doc)
                list.Add(doc.LogView);
            else if (d is IDock child)
                CollectLogViews(child, list, depth + 1);
        }
    }

    /// <summary>Finds the first IDocumentDock in the layout tree.</summary>
    public static IDocumentDock? FindFirstDocumentDock(IDock? root, int depth = 0)
    {
        if (root is null || depth > MaxDepth) return null;
        if (root is IDocumentDock dd) return dd;
        if (root.VisibleDockables is null) return null;
        foreach (var d in root.VisibleDockables)
        {
            if (d is IDocumentDock found) return found;
            if (d is IDock child)
            {
                var result = FindFirstDocumentDock(child, depth + 1);
                if (result is not null) return result;
            }
        }
        return null;
    }

    private static void CollectDocuments(IDock dock, List<LogViewDocument> list, int depth)
    {
        if (dock.VisibleDockables is null || depth > MaxDepth)
            return;
        foreach (var d in dock.VisibleDockables)
        {
            if (d is LogViewDocument doc)
                list.Add(doc);
            else if (d is IDock child)
                CollectDocuments(child, list, depth + 1);
        }
    }
}
