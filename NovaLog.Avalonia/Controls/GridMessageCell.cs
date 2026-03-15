using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using R = NovaLog.Avalonia.Controls.LogLineRow;

namespace NovaLog.Avalonia.Controls;

/// <summary>
/// Custom-drawn cell for TreeDataGrid that renders log messages with full syntax highlighting.
/// Reuses the same tokenization and brush resolution as LogLineRow.
/// </summary>
public sealed class GridMessageCell : Control
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var row = DataContext as GridRowViewModel;
        var text = row?.Message ?? "";
        double width = text.Length * 7.2 + 8;
        double height = R.RowHeight * (row?.LineCount ?? 1);
        if (double.IsInfinity(availableSize.Width))
            return new Size(width, height);
        return new Size(Math.Max(width, availableSize.Width), height);
    }

    private static double TextY => (R.RowHeight - R.LogFontSize) / 2.0;

    public override void Render(DrawingContext context)
    {
        if (DataContext is not GridRowViewModel row) return;

        if (row.IsFileHeader)
        {
            var brush = R.ResolveBrush("AccentBrush") ?? Brushes.Cyan;
            var ft = R.CreateFormattedText(row.FileName, brush);
            context.DrawText(ft, new Point(2, TextY));
            return;
        }

        // Auto-formatted lines (JSON pretty-print, SQL formatting)
        if (row.FormattedLines is { Count: > 0 } fmtLines)
        {
            for (int i = 0; i < fmtLines.Count; i++)
            {
                double y = TextY + i * R.RowHeight;
                var fl = fmtLines[i];
                RenderLine(context, fl.Text, fl.Flavor, fl.IsContinuation, null, y);
            }
            return;
        }

        // Multiline span mode: render each sub-line at its own Y offset
        if (row.SubLines is { Count: > 0 } subLines)
        {
            for (int i = 0; i < subLines.Count; i++)
            {
                double y = TextY + i * R.RowHeight;
                var sub = subLines[i];
                RenderLine(context, sub.Message, sub.Flavor, sub.IsContinuation, sub, y);
            }
            return;
        }

        var line = row.Line;
        if (line is null) return;
        RenderLine(context, row.Message, row.Flavor, row.IsContinuation, line, TextY);
    }

    private static void RenderLine(DrawingContext context, string message,
        SyntaxFlavor flavor, bool isContinuation, LogLineViewModel? vm, double y)
    {
        if (string.IsNullOrEmpty(message)) return;

        IReadOnlyList<HighlightToken> tokens;
        if (vm != null && vm.Message == message)
        {
            tokens = vm.MessageTokens;
        }
        else
        {
            tokens = SyntaxHighlighter.Tokenize(message, flavor, isContinuation);
        }

        double x = 2;
        foreach (var token in tokens)
        {
            if (token.Length <= 0) continue;
            var text = message.Substring(token.Index, token.Length);
            var brush = R.ResolveTokenBrushStatic(token);
            var ft = R.CreateFormattedText(text, brush);
            context.DrawText(ft, new Point(x, y));
            x += text.Length * R.CharWidth;
        }
    }
}
