using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaLog.Core.Models;

namespace NovaLog.Avalonia.Controls;

public partial class DropZoneOverlay : Control
{
    // Cached brushes to prevent allocations on every render
    private static readonly SolidColorBrush CenterFillBrush =
        new SolidColorBrush(Color.FromArgb(60, 0, 120, 255));
    private static readonly SolidColorBrush CenterBorderBrush =
        new SolidColorBrush(Color.FromArgb(180, 0, 120, 255));
    private static readonly SolidColorBrush EdgeFillBrush =
        new SolidColorBrush(Color.FromArgb(60, 0, 255, 65));
    private static readonly SolidColorBrush EdgeBorderBrush =
        new SolidColorBrush(Color.FromArgb(180, 0, 255, 65));
    private static readonly Pen CenterPen = new Pen(CenterBorderBrush, 2);
    private static readonly Pen EdgePen = new Pen(EdgeBorderBrush, 2);

    public static readonly DirectProperty<DropZoneOverlay, DropZone> ActiveZoneProperty =
        AvaloniaProperty.RegisterDirect<DropZoneOverlay, DropZone>(
            nameof(ActiveZone),
            o => o.ActiveZone,
            (o, v) => o.ActiveZone = v);

    private DropZone _activeZone = DropZone.None;
    private DropZone _lastRenderedZone = DropZone.None;

    public DropZone ActiveZone
    {
        get => _activeZone;
        set
        {
            if (SetAndRaise(ActiveZoneProperty, ref _activeZone, value))
            {
                // Only invalidate if zone actually changed
                if (_lastRenderedZone != _activeZone)
                {
                    InvalidateVisual();
                }
            }
        }
    }

    /// <summary>Minimum pane dimension. Edge splits that would produce panes below this threshold force Center (tab-merge).</summary>
    private const double MinPaneDimension = 250;

    public DropZone CalculateZone(Point clientPos)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return DropZone.None;

        double marginX = w / 4;
        double marginY = h / 4;

        DropZone zone;
        if (clientPos.X < marginX) zone = DropZone.Left;
        else if (clientPos.X > w - marginX) zone = DropZone.Right;
        else if (clientPos.Y < marginY) zone = DropZone.Top;
        else if (clientPos.Y > h - marginY) zone = DropZone.Bottom;
        else return DropZone.Center;

        // Tab Fairness: if splitting would create a pane below 250px, force Center (tab-merge visual)
        if (zone is DropZone.Left or DropZone.Right && w < MinPaneDimension * 2)
            return DropZone.Center;
        if (zone is DropZone.Top or DropZone.Bottom && h < MinPaneDimension * 2)
            return DropZone.Center;

        return zone;
    }

    public override void Render(DrawingContext context)
    {
        _lastRenderedZone = _activeZone;

        if (_activeZone == DropZone.None) return;

        double w = Bounds.Width;
        double h = Bounds.Height;

        Rect rect = _activeZone switch
        {
            DropZone.Center => new Rect(w / 4, h / 4, w / 2, h / 2),
            DropZone.Left => new Rect(0, 0, w / 4, h),
            DropZone.Right => new Rect(w - w / 4, 0, w / 4, h),
            DropZone.Top => new Rect(0, 0, w, h / 4),
            DropZone.Bottom => new Rect(0, h - h / 4, w, h / 4),
            _ => new Rect()
        };

        if (rect.Width <= 0) return;

        // Use cached brushes and pens
        bool isCenter = _activeZone == DropZone.Center;
        var fillBrush = isCenter ? CenterFillBrush : EdgeFillBrush;
        var pen = isCenter ? CenterPen : EdgePen;

        context.FillRectangle(fillBrush, rect);
        context.DrawRectangle(pen, rect);
    }
}
