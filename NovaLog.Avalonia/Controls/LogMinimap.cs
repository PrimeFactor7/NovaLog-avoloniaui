using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Media;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using System.Collections.Generic;

namespace NovaLog.Avalonia.Controls;

/// <summary>
/// Vertical minimap showing error/warning density, search hit ticks, and bookmark ticks.
/// Click to scroll, drag to scrub. Right-docked per pane, 40px wide.
/// </summary>
public class LogMinimap : Control
{
    public static readonly StyledProperty<int> TotalLinesProperty =
        AvaloniaProperty.Register<LogMinimap, int>(nameof(TotalLines));

    public static readonly StyledProperty<NavigationIndex?> NavIndexProperty =
        AvaloniaProperty.Register<LogMinimap, NavigationIndex?>(nameof(NavIndex));

    public static readonly StyledProperty<double> ViewportTopRatioProperty =
        AvaloniaProperty.Register<LogMinimap, double>(nameof(ViewportTopRatio));

    public static readonly StyledProperty<double> ViewportHeightRatioProperty =
        AvaloniaProperty.Register<LogMinimap, double>(nameof(ViewportHeightRatio), 1.0);

    public int TotalLines { get => GetValue(TotalLinesProperty); set => SetValue(TotalLinesProperty, value); }
    public NavigationIndex? NavIndex { get => GetValue(NavIndexProperty); set => SetValue(NavIndexProperty, value); }
    public double ViewportTopRatio { get => GetValue(ViewportTopRatioProperty); set => SetValue(ViewportTopRatioProperty, value); }
    public double ViewportHeightRatio { get => GetValue(ViewportHeightRatioProperty); set => SetValue(ViewportHeightRatioProperty, value); }

    /// <summary>Fired when user clicks/drags to a line index.</summary>
    public event Action<int>? ScrollRequested;

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF3E3E"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#FFB000"));
    private static readonly IBrush SearchBrush = new SolidColorBrush(Color.Parse("#00D4FF"));
    private static readonly IBrush BookmarkBrush = new SolidColorBrush(Color.Parse("#00FF41"));
    private static readonly IPen ErrorPen = new Pen(ErrorBrush, 2);
    private static readonly IPen SearchPen = new Pen(SearchBrush, 1);
    private static readonly IPen BookmarkPen = new Pen(BookmarkBrush, 3);
    private static readonly IBrush ViewportBrush = new SolidColorBrush(Color.Parse("#20FFFFFF"));
    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#1A1A2E"));

    static LogMinimap()
    {
        AffectsRender<LogMinimap>(TotalLinesProperty, NavIndexProperty, ViewportTopRatioProperty, ViewportHeightRatioProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(BgBrush, bounds);

        if (TotalLines <= 0 || NavIndex is null) return;

        double h = bounds.Height;
        double w = bounds.Width;

        // Viewport lens
        double vpTop = Math.Clamp(ViewportTopRatio, 0.0, 1.0) * h;
        double vpHeight = Math.Clamp(ViewportHeightRatio, 0.0, 1.0) * h;
        vpHeight = Math.Clamp(vpHeight, Math.Min(4.0, h), h);
        vpTop = Math.Clamp(vpTop, 0.0, Math.Max(0.0, h - vpHeight));
        context.FillRectangle(ViewportBrush, new Rect(0, vpTop, w, vpHeight));

        // Draw ticks for errors
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.Error), ErrorPen, w, h);

        // Draw ticks for search hits
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.SearchHit), SearchPen, w * 0.6, h);

        // Draw ticks for bookmarks
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.Bookmark), BookmarkPen, w, h);
    }

    private void DrawTicks(DrawingContext context, IReadOnlyList<long> indices, IPen pen,
        double tickWidth, double height)
    {
        if (indices.Count == 0 || TotalLines <= 0) return;
        int bucketCount = Math.Max(1, (int)Math.Ceiling(height));
        var occupiedRows = new HashSet<int>();

        foreach (var idx in indices)
        {
            int row = (int)Math.Round((double)idx / TotalLines * (bucketCount - 1));
            if (!occupiedRows.Add(Math.Clamp(row, 0, bucketCount - 1)))
                continue;

            double y = row + 0.5;
            context.DrawLine(pen, new Point(0, y), new Point(tickWidth, y));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ScrollToPointer(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            ScrollToPointer(e);
    }

    private void ScrollToPointer(PointerEventArgs e)
    {
        if (TotalLines <= 0) return;
        var pos = e.GetPosition(this);
        int line = (int)(pos.Y / Bounds.Height * TotalLines);
        line = Math.Clamp(line, 0, TotalLines - 1);
        ScrollRequested?.Invoke(line);
    }

    /// <summary>Fired when user scrolls the mousewheel over the minimap.</summary>
    public event Action<PointerWheelEventArgs>? WheelScrollRequested;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        WheelScrollRequested?.Invoke(e);
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(40, availableSize.Height);
    }
}
