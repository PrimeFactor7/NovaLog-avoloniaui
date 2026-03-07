using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Media;
using NovaLog.Core.Models;
using NovaLog.Core.Services;

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

    public static readonly StyledProperty<int> ViewportLineProperty =
        AvaloniaProperty.Register<LogMinimap, int>(nameof(ViewportLine));

    public static readonly StyledProperty<int> ViewportHeightLinesProperty =
        AvaloniaProperty.Register<LogMinimap, int>(nameof(ViewportHeightLines), 50);

    public int TotalLines { get => GetValue(TotalLinesProperty); set => SetValue(TotalLinesProperty, value); }
    public NavigationIndex? NavIndex { get => GetValue(NavIndexProperty); set => SetValue(NavIndexProperty, value); }
    public int ViewportLine { get => GetValue(ViewportLineProperty); set => SetValue(ViewportLineProperty, value); }
    public int ViewportHeightLines { get => GetValue(ViewportHeightLinesProperty); set => SetValue(ViewportHeightLinesProperty, value); }

    /// <summary>Fired when user clicks/drags to a line index.</summary>
    public event Action<int>? ScrollRequested;

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF3E3E"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#FFB000"));
    private static readonly IBrush SearchBrush = new SolidColorBrush(Color.Parse("#00D4FF"));
    private static readonly IBrush BookmarkBrush = new SolidColorBrush(Color.Parse("#00FF41"));
    private static readonly IBrush ViewportBrush = new SolidColorBrush(Color.Parse("#20FFFFFF"));
    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#1A1A2E"));

    static LogMinimap()
    {
        AffectsRender<LogMinimap>(TotalLinesProperty, NavIndexProperty, ViewportLineProperty, ViewportHeightLinesProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(BgBrush, bounds);

        if (TotalLines <= 0 || NavIndex is null) return;

        double h = bounds.Height;
        double w = bounds.Width;

        // Viewport lens
        double vpTop = (double)ViewportLine / TotalLines * h;
        double vpHeight = Math.Max(4, (double)ViewportHeightLines / TotalLines * h);
        context.FillRectangle(ViewportBrush, new Rect(0, vpTop, w, vpHeight));

        // Draw ticks for errors
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.Error), ErrorBrush, w, h, 2);

        // Draw ticks for search hits
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.SearchHit), SearchBrush, w * 0.6, h, 1);

        // Draw ticks for bookmarks
        DrawTicks(context, NavIndex.GetAll(NavigationCategory.Bookmark), BookmarkBrush, w, h, 3);
    }

    private void DrawTicks(DrawingContext context, IReadOnlyList<long> indices, IBrush brush,
        double tickWidth, double height, double tickHeight)
    {
        if (indices.Count == 0 || TotalLines <= 0) return;

        var pen = new Pen(brush, tickHeight);
        foreach (var idx in indices)
        {
            double y = (double)idx / TotalLines * height;
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

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(40, availableSize.Height);
    }
}
