using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public NovaLogDockFactory()
    {
        // Maps the platform-agnostic IDockWindow to the Avalonia-specific HostWindow.
        // Without this, SplitToWindow detaches the pane but has no host and it vanishes.
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };
    }

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
        // HostWindow IS the Avalonia Window (extends Window).
        // host.Window is IDockWindow? (the model), NOT an Avalonia Window.
        if (dockWindow?.Host is not HostWindow host)
            return;
        Dispatcher.UIThread.Post(() => AttachFloatingWindowChrome(host));
    }

    /// <summary>
    /// Adds Pin/Opacity/Close controls to a floating dock window without replacing
    /// the existing content (preserves Dock.Avalonia's template parts for native drag-to-dock).
    /// Chrome removal (SystemDecorations=None) is handled via the HostWindow style in DockNeon.axaml.
    /// </summary>
    private static void AttachFloatingWindowChrome(HostWindow host)
    {
        // Find the existing HostWindowTitleBar inside the HostWindow template
        var titleBar = host.GetVisualDescendants()
            .OfType<HostWindowTitleBar>()
            .FirstOrDefault();

        if (titleBar is null)
            return;

        // Build our controls and insert them into the existing title bar
        var topmostToggle = new global::Avalonia.Controls.Primitives.ToggleButton
        {
            Content = "Pin",
            IsChecked = host.Topmost,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2),
            Margin = new Thickness(4, 0)
        };
        ToolTip.SetTip(topmostToggle, "Always on top");
        topmostToggle.IsCheckedChanged += (_, _) => host.Topmost = topmostToggle.IsChecked == true;
        host.GetObservable(Window.TopmostProperty).Subscribe(v => topmostToggle.IsChecked = v);

        var opacitySlider = new Slider
        {
            Minimum = 0.2,
            Maximum = 1,
            Value = host.Opacity,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        };
        opacitySlider.GetObservable(Slider.ValueProperty).Subscribe(v => host.Opacity = v);

        var closeBtn = new Button
        {
            Content = "\u2715",
            FontSize = 12,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(closeBtn, "Close floating window");
        closeBtn.Click += (_, _) => host.Close();

        // Add chrome without replacing host.Content (avoids breaking Dock template bindings).
        // If Content is already a Panel (e.g. Grid from the theme), add our bar as a child; otherwise skip overlay.
        var controlBar = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 2),
            MinHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 100,
            Child = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { topmostToggle, new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center }, opacitySlider, closeBtn }
            }
        };

        // Only inject into existing Panel (e.g. theme Grid); never replace host.Content.
        if (host.Content is Panel panel)
            panel.Children.Add(controlBar);
    }
}
