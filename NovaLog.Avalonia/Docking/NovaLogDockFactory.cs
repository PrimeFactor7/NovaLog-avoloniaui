using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Orientation = Dock.Model.Core.Orientation;

namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Factory that builds Dock layouts with one main document area. Creates documents
/// that wrap <see cref="ViewModels.LogViewViewModel"/> for display in <see cref="Views.LogViewPanel"/>.
/// </summary>
public class NovaLogDockFactory : Factory
{
    /// <summary>
    /// Creates the default layout: one root with one document dock containing one log view document.
    /// </summary>
    public override IRootDock CreateLayout()
    {
        var document = CreateDocument();
        var documentDock = CreateDocumentDock();
        documentDock.VisibleDockables = CreateList<IDockable>(document);
        documentDock.ActiveDockable = document;
        documentDock.DefaultDockable = document;

        // Root with a single DocumentDock so the document area fills the content (no proportional splitter taking space).
        var root = CreateRootDock();
        root.VisibleDockables = CreateList<IDockable>(documentDock);
        root.ActiveDockable = documentDock;
        root.DefaultDockable = documentDock;

        return root;
    }

    /// <summary>
    /// Creates a single document with a new LogViewViewModel. The caller must initialize
    /// the LogView (Clock, SourceManager, Theme) when the workspace is ready.
    /// </summary>
    public override IDocument CreateDocument()
    {
        var logView = new ViewModels.LogViewViewModel();
        return new LogViewDocument(logView);
    }

    public override void OnWindowOpened(IDockWindow? dockWindow)
    {
        base.OnWindowOpened(dockWindow);
        if (dockWindow?.Host is not HostWindow host || host.Window is not Window win)
            return;
        Dispatcher.UIThread.Post(() => AttachFloatingWindowChrome(win, dockWindow));
    }

    /// <summary>Adds Always on Top toggle and Transparency slider to a floating dock window.</summary>
    private static void AttachFloatingWindowChrome(Window win, IDockWindow dockWindow)
    {
        if (win.Content is not Control existingContent)
            return;
        var topmostToggle = new global::Avalonia.Controls.Primitives.ToggleButton
        {
            Content = "Pin",
            IsChecked = win.Topmost
        };
        ToolTip.SetTip(topmostToggle, "Always on top");
        topmostToggle.IsCheckedChanged += (_, _) =>
        {
            if (topmostToggle.IsChecked == true)
                win.Topmost = true;
            else if (topmostToggle.IsChecked == false)
                win.Topmost = false;
        };
        win.GetObservable(Window.TopmostProperty).Subscribe(v => topmostToggle.IsChecked = v);

        var opacitySlider = new Slider
        {
            Minimum = 0.2,
            Maximum = 1,
            Value = win.Opacity,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center
        };
        opacitySlider.GetObservable(Slider.ValueProperty).Subscribe(v => win.Opacity = v);

        var bar = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 2),
            Child = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    topmostToggle,
                    new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) },
                    opacitySlider
                }
            }
        };
        var topBar = new Border { Child = bar, MinHeight = 28 };
        DockPanel.SetDock(topBar, global::Avalonia.Controls.Dock.Top);
        win.Content = new DockPanel
        {
            LastChildFill = true,
            Children = { topBar, existingContent }
        };
    }
}
