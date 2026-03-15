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
    /// <summary>Main document dock in the primary window; used by "Join" to re-dock floating documents.</summary>
    public IDocumentDock? MainDocumentDock { get; private set; }

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
        MainDocumentDock = documentDock;
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
        if (dockWindow?.Host is not HostWindow host)
            return;
        // Pass factory and dockWindow so the Join button knows where to return panes.
        Dispatcher.UIThread.Post(() => AttachFloatingWindowChrome(this, host, dockWindow));
    }

    /// <summary>
    /// Adds Join, Pin, Opacity, Minimize, Maximize/Restore, Close and enables dragging. Non-destructive overlay onto root Panel.
    /// </summary>
    private static void AttachFloatingWindowChrome(NovaLogDockFactory factory, HostWindow host, IDockWindow dockWindow)
    {
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

        var codiconFont = Application.Current?.Resources["CodiconFont"] as FontFamily ?? new FontFamily("Segoe UI Symbol");
        var joinBtn = new Button
        {
            Content = "\uEAE0", // log-in: box with arrow in (re-join / dock)
            FontSize = 16,
            FontFamily = codiconFont,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(joinBtn, "Join back to main window");
        joinBtn.Click += (_, _) =>
        {
            var floatingDock = dockWindow.Layout?.VisibleDockables?.OfType<IDock>().FirstOrDefault();
            if (floatingDock is not null && factory.MainDocumentDock is not null)
            {
                var docsToMove = floatingDock.VisibleDockables?.ToList() ?? new List<IDockable>();
                foreach (var d in docsToMove)
                {
                    floatingDock.VisibleDockables?.Remove(d);
                    factory.MainDocumentDock.VisibleDockables?.Add(d);
                    d.Owner = factory.MainDocumentDock;
                }
                factory.MainDocumentDock.ActiveDockable = docsToMove.LastOrDefault() ?? factory.MainDocumentDock.ActiveDockable;
            }
            host.Close();
        };

        var minBtn = new Button
        {
            Content = "\uEABF",
            FontSize = 14,
            FontFamily = codiconFont,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(minBtn, "Minimize");
        minBtn.Click += (_, _) => host.WindowState = WindowState.Minimized;

        var maxBtn = new Button
        {
            Content = host.WindowState == WindowState.Maximized ? "\uEAC1" : "\uEAC0",
            FontSize = 14,
            FontFamily = codiconFont,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(maxBtn, host.WindowState == WindowState.Maximized ? "Restore" : "Maximize");
        maxBtn.Click += (_, _) =>
        {
            host.WindowState = host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        host.GetObservable(Window.WindowStateProperty).Subscribe(state =>
        {
            if (state == WindowState.Maximized)
            {
                maxBtn.Content = "\uEAC1";
                ToolTip.SetTip(maxBtn, "Restore");
            }
            else
            {
                maxBtn.Content = "\uEAC0";
                ToolTip.SetTip(maxBtn, "Maximize");
            }
        });

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

        var controlBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Padding = new Thickness(6, 2),
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 1000,
            Child = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { joinBtn, topmostToggle, new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center }, opacitySlider, minBtn, maxBtn, closeBtn }
            }
        };

        controlBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                if (host.WindowState != WindowState.Maximized)
                    host.BeginMoveDrag(e);
                e.Handled = true;
            }
        };

        Dispatcher.UIThread.Post(() =>
        {
            var rootVisual = host.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
            if (rootVisual is not null && !rootVisual.Children.Contains(controlBar))
                rootVisual.Children.Add(controlBar);
        }, DispatcherPriority.Loaded);
    }
}
