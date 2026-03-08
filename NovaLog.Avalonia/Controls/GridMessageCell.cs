using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using System.Text.RegularExpressions;
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
                RenderLine(context, fl.Text, fl.Flavor, fl.IsContinuation, y);
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
                RenderLine(context, sub.Message, sub.Flavor, sub.IsContinuation, y);
            }
            return;
        }

        var line = row.Line;
        if (line is null) return;
        RenderLine(context, row.Message, row.Flavor, row.IsContinuation, TextY);
    }

    private static void RenderLine(DrawingContext context, string message,
        SyntaxFlavor flavor, bool isContinuation, double y)
    {
        if (string.IsNullOrEmpty(message)) return;
        switch (flavor)
        {
            case SyntaxFlavor.Json:
                RenderJsonMessage(context, message, y);
                break;
            case SyntaxFlavor.Sql:
                RenderSqlMessage(context, message, y);
                break;
            case SyntaxFlavor.StackTrace:
                RenderStackTraceMessage(context, message, y);
                break;
            default:
                IBrush brush;
                if (isContinuation)
                    brush = R.ResolveBrush("DimTextBrush") ?? Brushes.Gray;
                else
                    brush = R.ResolveBrush("TextDefaultBrush") ?? R.FallbackText;
                RenderMessageWithHighlights(context, message, brush, y);
                break;
        }
    }

    private static void RenderJsonMessage(DrawingContext context, string message, double y)
    {
        var tokens = JsonHighlightTokenizer.Tokenize(message);
        double x = 2;
        foreach (var (start, length, kind) in tokens)
        {
            if (length <= 0) continue;
            var segment = message.Substring(start, length);
            var brush = R.ResolveJsonBrush(kind, segment);
            var ft = R.CreateFormattedText(segment, brush);
            context.DrawText(ft, new Point(x, y));
            x += segment.Length * R.CharWidth;
        }
    }

    private static void RenderSqlMessage(DrawingContext context, string message, double y)
    {
        var tokens = new List<(int Index, int Length, IBrush Brush)>();
        foreach (Match m in R.SqlKeywordPattern.Matches(message))
            tokens.Add((m.Index, m.Length, R.ResolveBrush("SqlKeywordBrush") ?? R.FallbackSqlKeywordBrush));
        foreach (Match m in R.SqlStringPattern.Matches(message))
            tokens.Add((m.Index, m.Length, R.ResolveBrush("SqlStringBrush") ?? Brushes.Green));
        foreach (Match m in R.SqlOperatorPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index >= t.Index && m.Index < t.Index + t.Length);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("SqlOperatorBrush") ?? Brushes.Gray));
        }
        foreach (Match m in R.SqlNumberPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("SqlNumberBrush") ?? Brushes.Orange));
        }

        RenderTokens(context, message, tokens, R.ResolveBrush("TextDefaultBrush") ?? Brushes.White, y);
    }

    private static void RenderStackTraceMessage(DrawingContext context, string message, double y)
    {
        var tokens = new List<(int Index, int Length, IBrush Brush)>();
        foreach (Match m in R.StackExceptionPattern.Matches(message))
            tokens.Add((m.Index, m.Length, R.ResolveBrush("StackExceptionBrush") ?? Brushes.Red));
        foreach (Match m in R.StackMethodPattern.Matches(message))
        {
            if (m.Groups["atkw"].Success)
                tokens.Add((m.Groups["atkw"].Index, m.Groups["atkw"].Length, R.ResolveBrush("StackKeywordBrush") ?? Brushes.Gray));
            if (m.Groups["method"].Success)
                tokens.Add((m.Groups["method"].Index, m.Groups["method"].Length, R.ResolveBrush("StackMethodBrush") ?? Brushes.Cyan));
            if (m.Groups["args"].Success && m.Groups["args"].Length > 0)
                tokens.Add((m.Groups["args"].Index, m.Groups["args"].Length, R.ResolveBrush("StackArgsBrush") ?? R.FallbackStackArgs));
        }
        foreach (Match m in R.StackFilePattern.Matches(message))
        {
            if (m.Groups["inkw"].Success)
                tokens.Add((m.Groups["inkw"].Index, m.Groups["inkw"].Length, R.ResolveBrush("StackKeywordBrush") ?? Brushes.Gray));
            if (m.Groups["path"].Success)
                tokens.Add((m.Groups["path"].Index, m.Groups["path"].Length, R.ResolveBrush("StackPathBrush") ?? R.FallbackStackPath));
            if (m.Groups["line"].Success)
                tokens.Add((m.Groups["line"].Index, m.Groups["line"].Length, R.ResolveBrush("StackLineNumberBrush") ?? Brushes.Orange));
        }

        RenderTokens(context, message, tokens, R.ResolveBrush("TextDefaultBrush") ?? Brushes.White, y);
    }

    private static void RenderMessageWithHighlights(DrawingContext context, string message, IBrush defaultBrush, double y)
    {
        var tokens = new List<(int Index, int Length, IBrush Brush)>();

        foreach (Match m in R.GuidPattern.Matches(message))
            tokens.Add((m.Index, m.Length, R.ResolveBrush("GuidBrush") ?? R.FallbackGuidBrush));
        foreach (Match m in R.UrlPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("UrlBrush") ?? R.FallbackUrlBrush));
        }
        foreach (Match m in R.IpPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("IpAddressBrush") ?? R.FallbackIpBrush));
        }
        foreach (Match m in R.HexPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("HexBrush") ?? R.FallbackHexBrush));
        }
        foreach (Match m in R.NumberPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, R.ResolveBrush("NumberBrush") ?? Brushes.Orange));
        }

        RenderTokens(context, message, tokens, defaultBrush, y);
    }

    private static void RenderTokens(DrawingContext context, string message,
        List<(int Index, int Length, IBrush Brush)> tokens, IBrush fallbackBrush, double y)
    {
        if (tokens.Count == 0)
        {
            var ft = R.CreateFormattedText(message, fallbackBrush);
            context.DrawText(ft, new Point(2, y));
            return;
        }

        tokens.Sort((a, b) => a.Index.CompareTo(b.Index));
        double x = 2;
        int pos = 0;

        foreach (var (index, length, brush) in tokens)
        {
            if (index < pos) continue;
            if (pos < index)
            {
                var gap = message.Substring(pos, index - pos);
                var ft = R.CreateFormattedText(gap, fallbackBrush);
                context.DrawText(ft, new Point(x, y));
                x += gap.Length * R.CharWidth;
            }
            var token = message.Substring(index, length);
            var tokenFt = R.CreateFormattedText(token, brush);
            context.DrawText(tokenFt, new Point(x, y));
            x += token.Length * R.CharWidth;
            pos = index + length;
        }

        if (pos < message.Length)
        {
            var remaining = message.Substring(pos);
            var ft = R.CreateFormattedText(remaining, fallbackBrush);
            context.DrawText(ft, new Point(x, y));
        }
    }
}
