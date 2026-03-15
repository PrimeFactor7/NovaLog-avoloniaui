using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using Dock.Model.Core;
using NovaLog.Avalonia.Docking;
using NovaLog.Avalonia.ViewModels;

namespace NovaLog.Avalonia.Services;

/// <summary>
/// Handles detection of monitor layouts and the "Explosion" of docking panes
/// or workspaces to secondary screens.
/// </summary>
public class MonitorManager
{
    private readonly Window _mainWindow;

    public MonitorManager(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    /// <summary>True if there are secondary screens available for explosion.</summary>
    public bool HasSecondaryScreens => GetSecondaryScreens().Count > 0;

    private List<Screen> GetSecondaryScreens()
    {
        var currentScreen = _mainWindow.Screens.ScreenFromWindow(_mainWindow)
                            ?? _mainWindow.Screens.Primary;
        return _mainWindow.Screens.All
            .Where(s => currentScreen is null || s.WorkingArea != currentScreen.WorkingArea)
            .ToList();
    }

    /// <summary>
    /// Distributes all document panes (except the first one) of the ACTIVE workspace
    /// to secondary monitors as floating Dock windows.
    /// </summary>
    public void ExplodePanesToMonitors(WorkspaceViewModel workspace, NovaLogDockFactory factory)
    {
        if (workspace.Layout is null) return;

        var secondaryScreens = GetSecondaryScreens();
        if (secondaryScreens.Count == 0) return;

        var docs = DockLayoutHelper.GetAllDocuments(workspace.Layout);
        if (docs.Count <= 1) return;

        int screenIndex = 0;
        // Iterate backwards so indices don't shift when removing items.
        for (int i = docs.Count - 1; i >= 1; i--)
        {
            var doc = docs[i];
            if (doc.Owner is not IDock ownerDock) continue;

            var screen = secondaryScreens[screenIndex % secondaryScreens.Count];
            var wa = screen.WorkingArea;
            var scale = screen.Scaling;

            var x = (wa.X + wa.Width * 0.05) / scale;
            var y = (wa.Y + wa.Height * 0.05) / scale;
            var w = wa.Width * 0.9 / scale;
            var h = wa.Height * 0.9 / scale;

            factory.SplitToWindow(ownerDock, doc, x, y, w, h);
            screenIndex++;
        }
    }

    /// <summary>
    /// Distributes all workspaces (tabs) EXCEPT the active one to secondary monitors.
    /// Each exploded workspace gets its own native Window hosting the layout.
    /// </summary>
    public void ExplodeTabsToMonitors(WorkspaceViewModel workspace, SourceManagerViewModel sourceManager, Core.Theme.ThemeService theme)
    {
        var secondaryScreens = GetSecondaryScreens();
        if (secondaryScreens.Count == 0) return;

        var tabsToExplode = workspace.Tabs.Where(t => !t.IsActive).ToList();
        if (tabsToExplode.Count == 0) return;

        int screenIndex = 0;
        foreach (var tab in tabsToExplode)
        {
            var screen = secondaryScreens[screenIndex % secondaryScreens.Count];
            var wa = screen.WorkingArea;

            // Create secondary workspace VM that shares services but only has THIS layout
            var secondaryWorkspace = new WorkspaceViewModel();
            secondaryWorkspace.Initialize(sourceManager, theme);
            secondaryWorkspace.DockFactory = workspace.DockFactory;
            
            if (tab.SavedDockLayout != null)
                secondaryWorkspace.Layout = tab.SavedDockLayout;
            else if (tab.SavedLayout != null)
                secondaryWorkspace.RootNode = tab.SavedLayout;

            var win = new Views.WorkspaceWindow
            {
                DataContext = secondaryWorkspace,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            win.Show();
            win.Position = new Avalonia.PixelPoint(wa.X, wa.Y);
            win.WindowState = WindowState.Maximized;

            // Remove from the main tab list
            workspace.Tabs.Remove(tab);
            screenIndex++;
        }
    }
}
