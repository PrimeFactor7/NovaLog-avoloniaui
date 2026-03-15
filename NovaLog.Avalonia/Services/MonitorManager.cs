using Avalonia.Controls;
using Dock.Model.Core;
using NovaLog.Avalonia.Docking;
using NovaLog.Avalonia.ViewModels;

namespace NovaLog.Avalonia.Services;

/// <summary>
/// Handles detection of monitor layouts and the "Explosion" of docking panes
/// to secondary screens. Panes are distributed as native floating Dock windows
/// so they remain redockable and connected to the global message bus (Clock, TimeSync).
/// SplitToWindow expects position and size in logical pixels (DIPs); WorkingArea is device pixels so we scale by Screen.Scaling.
/// </summary>
public class MonitorManager
{
    private readonly Window _mainWindow;

    public MonitorManager(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    /// <summary>True if there are secondary screens available for explosion.</summary>
    public bool HasSecondaryScreens
    {
        get
        {
            var currentScreen = _mainWindow.Screens.ScreenFromWindow(_mainWindow)
                                ?? _mainWindow.Screens.Primary;
            return _mainWindow.Screens.All
                .Any(s => currentScreen is null || s.WorkingArea != currentScreen.WorkingArea);
        }
    }

    /// <summary>
    /// Distributes all documents (except the first) from the active Dock layout to secondary monitors
    /// as floating Dock windows. Uses <c>SplitToWindow</c> to create native redockable windows.
    /// Round-robins across available screens.
    /// </summary>
    public void ExplodeToMonitors(WorkspaceViewModel workspace, NovaLogDockFactory factory)
    {
        if (workspace.Layout is null) return;

        var currentScreen = _mainWindow.Screens.ScreenFromWindow(_mainWindow)
                            ?? _mainWindow.Screens.Primary;
        var secondaryScreens = _mainWindow.Screens.All
            .Where(s => currentScreen is null || s.WorkingArea != currentScreen.WorkingArea)
            .ToList();

        if (secondaryScreens.Count == 0) return;

        var docs = DockLayoutHelper.GetAllDocuments(workspace.Layout);
        if (docs.Count <= 1) return; // Keep at least one in main window

        // DockService.DockDockableIntoWindow passes root.ActiveDockable (targetWindowOwner) to SplitToWindow, not doc.Owner.
        var root = workspace.Layout;
        if (root.ActiveDockable is not IDock targetWindowOwner)
            return;

        // Iterate in REVERSE to avoid stale references when SplitToWindow mutates the layout.
        int screenIndex = 0;
        for (int i = docs.Count - 1; i >= 1; i--)
        {
            var doc = docs[i];
            if (doc.Owner is null) continue;

            var screen = secondaryScreens[screenIndex % secondaryScreens.Count];
            var wa = screen.WorkingArea;
            var scale = screen.Scaling;

            var x = (wa.X + wa.Width * 0.05) / scale;
            var y = (wa.Y + wa.Height * 0.05) / scale;
            var w = wa.Width * 0.9 / scale;
            var h = wa.Height * 0.9 / scale;

            // Manual hoist: use same signature as DockService.DockDockableIntoWindow so the library creates and shows the host.
            factory.SplitToWindow(targetWindowOwner, doc, x, y, w, h);

            screenIndex++;
        }
    }
}
