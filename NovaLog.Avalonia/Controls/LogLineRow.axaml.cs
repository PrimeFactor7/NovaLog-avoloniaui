using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaLog.Avalonia.Services;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Core.Theme;
using AvaloniaApplication = Avalonia.Application;
using System.Text.RegularExpressions;
using Avalonia.VisualTree;

namespace NovaLog.Avalonia.Controls;

public partial class LogLineRow : Control
{
    private static readonly Typeface MonoTypeface = new("Cascadia Mono, Consolas, Courier New");
    private const double LogFontSize = 12;
    private const double RowHeight = 18;
    private const double CharWidth = 7.2;
    private const double TimestampChars = 23;
    private const double LevelCharsMax = 8;
    private const double GapChars = 2;
    private const double LeftPad = 4;

    private LogLineViewModel? _vm;

    public LogLineRow()
    {
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as LogLineViewModel;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double desiredWidth = 1200; // Safe default
        if (_vm is not null)
        {
            int chars = (_vm.TimestampText?.Length ?? 0) + 
                        (_vm.LevelText?.Length ?? 0) + 
                        (_vm.Message?.Length ?? 0) + 10;
            desiredWidth = LeftPad + (chars * CharWidth);
        }
        double width = double.IsInfinity(availableSize.Width) ? desiredWidth : Math.Max(desiredWidth, availableSize.Width);
        return new Size(width, RowHeight);
    }

    public void ResetVisualState()
    {
        _vm = null;
    }

    private (IEnumerable<HighlightRule>? Rules, ThemeService? Theme) GetContext()
    {
        var parent = this.GetVisualParent();
        while (parent != null && parent is not Views.LogViewPanel)
            parent = parent.GetVisualParent();
        
        if (parent is Views.LogViewPanel panel && panel.DataContext is LogViewViewModel lvm)
            return (lvm.HighlightRules, lvm.Theme);
        
        return (null, null);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (_vm is null) return;

        var (rules, theme) = GetContext();

        double y = (bounds.Height - LogFontSize) / 2;
        if (y < 1) y = 1;

        // 1. Log Level Row background tint (prioritize theme override)
        string? bgHex = theme?.GetLevelBgColorHex(_vm.Level);
        if (!string.IsNullOrEmpty(bgHex))
        {
            context.FillRectangle(Brush.Parse(bgHex), new Rect(0, 0, bounds.Width, bounds.Height));
        }

        // 2. Custom Highlight Rule Line Backgrounds
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled || rule.RuleType != HighlightRuleType.LineHighlight || string.IsNullOrEmpty(rule.BackgroundHex)) continue;
                if (rule.CompiledRegex?.IsMatch(_vm.RawText) == true)
                {
                    var brush = Brush.Parse(rule.BackgroundHex);
                    context.FillRectangle(brush, new Rect(0, 0, bounds.Width, bounds.Height));
                }
            }
        }

        if (_vm.IsFileSeparator)
        {
            var sepBrush = GetBrush("SeparatorBrush") ?? Brushes.Gray;
            context.DrawLine(new Pen(sepBrush, 1), new Point(0, bounds.Height / 2), new Point(bounds.Width, bounds.Height / 2));
            if (!string.IsNullOrEmpty(_vm.Message))
            {
                var ft = CreateFormattedText(_vm.Message, GetBrush("DimTextBrush") ?? Brushes.Gray);
                context.DrawText(ft, new Point(LeftPad, y));
            }
            return;
        }

        double x = LeftPad;

        // 3. Timestamp
        if (!_vm.IsContinuation && !string.IsNullOrEmpty(_vm.TimestampText))
        {
            IBrush tsBrush = theme != null 
                ? Brush.Parse(theme.GetTimestampColor())
                : GetBrush("TimestampBrush") ?? FallbackTimestamp;
            
            var ft = CreateFormattedText(_vm.TimestampText, tsBrush);
            context.DrawText(ft, new Point(x, y));
        }
        x += (TimestampChars + GapChars) * CharWidth;

        // 4. Level
        if (!_vm.IsContinuation && !string.IsNullOrEmpty(_vm.LevelText))
        {
            IBrush levelBrush = theme != null
                ? Brush.Parse(theme.GetLevelColorHex(_vm.Level))
                : GetLevelBrush(_vm.Level);

            var ft = CreateFormattedText(_vm.LevelText, levelBrush);
            context.DrawText(ft, new Point(x, y));
        }
        x += (LevelCharsMax + GapChars) * CharWidth;

        double xMessageStart = x;
        RenderMessage(context, x, y, _vm, theme);

        // 5. Custom Highlight Rule Match Highlights (Overlays)
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled || string.IsNullOrEmpty(rule.Pattern)) continue;
                var regex = rule.CompiledRegex;
                if (regex == null) continue;

                foreach (Match m in regex.Matches(_vm.Message))
                {
                    if (m.Length == 0) continue;
                    
                    double xOff = m.Index * CharWidth;
                    double mWidth = m.Length * CharWidth;

                    if (!string.IsNullOrEmpty(rule.BackgroundHex))
                    {
                        var bg = Brush.Parse(rule.BackgroundHex);
                        context.FillRectangle(bg, new Rect(xMessageStart + xOff, 0, mWidth, RowHeight));
                    }

                    var fg = Brush.Parse(rule.ForegroundHex);
                    var ft = CreateFormattedText(m.Value, fg);
                    context.DrawText(ft, new Point(xMessageStart + xOff, y));
                }
            }
        }
    }

    private void RenderMessage(DrawingContext context, double x, double y, LogLineViewModel vm, ThemeService? theme)
    {
        var flavor = vm.Flavor;
        if (theme != null)
        {
            if (flavor == SyntaxFlavor.Json && !theme.JsonHighlightEnabled) flavor = SyntaxFlavor.None;
            if (flavor == SyntaxFlavor.Sql && !theme.SqlHighlightEnabled) flavor = SyntaxFlavor.None;
            if (flavor == SyntaxFlavor.StackTrace && !theme.StackTraceHighlightEnabled) flavor = SyntaxFlavor.None;
            // Note: Number highlighting isn't fully separated into a flavor yet in this port, 
            // but the infrastructure is now there to support it.
        }

        switch (flavor)
        {
            case SyntaxFlavor.Json:
                RenderJsonMessage(context, x, y, vm.Message);
                break;
            case SyntaxFlavor.Sql:
                RenderSqlMessage(context, x, y, vm.Message);
                break;
            case SyntaxFlavor.StackTrace:
                RenderStackTraceMessage(context, x, y, vm.Message);
                break;
            default:
                IBrush brush;
                if (vm.IsContinuation)
                {
                    brush = GetBrush("DimTextBrush") ?? Brushes.Gray;
                }
                else
                {
                    brush = theme != null 
                        ? Brush.Parse(theme.GetMessageColor())
                        : GetBrush("TextDefaultBrush") ?? Brushes.Green;
                }
                RenderMessageWithHighlights(context, x, y, vm.Message, brush);
                break;
        }
    }

    private void RenderStackTraceMessage(DrawingContext context, double startX, double y, string message)
    {
        // Regex patterns for stack trace highlighting
        var stackMethodPattern = new Regex(@"(?<atkw>at)\s+(?<method>[\w.+<>\[\]`,]+)\((?<args>[^)]*)\)");
        var stackFilePattern = new Regex(@"(?:(?<inkw>in)\s+)?(?<path>\w:[\\\/][^\s:]+|[\\\/][^\s:]+):(?:line\s+)?(?<line>\d+)");
        var stackExceptionPattern = new Regex(@"(?<extype>[\w.]+Exception)\b");

        var tokens = new List<(int Index, int Length, IBrush Brush, string Type)>();

        // Exception types
        foreach (Match m in stackExceptionPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("StackExceptionBrush") ?? Brushes.Red, "exception"));

        // Method calls
        foreach (Match m in stackMethodPattern.Matches(message))
        {
            if (m.Groups["atkw"].Success)
                tokens.Add((m.Groups["atkw"].Index, m.Groups["atkw"].Length, GetBrush("StackKeywordBrush") ?? Brushes.Gray, "keyword"));
            if (m.Groups["method"].Success)
                tokens.Add((m.Groups["method"].Index, m.Groups["method"].Length, GetBrush("StackMethodBrush") ?? Brushes.Cyan, "method"));
            if (m.Groups["args"].Success && m.Groups["args"].Length > 0)
                tokens.Add((m.Groups["args"].Index, m.Groups["args"].Length, GetBrush("StackArgsBrush") ?? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), "args"));
        }

        // File paths and line numbers
        foreach (Match m in stackFilePattern.Matches(message))
        {
            if (m.Groups["inkw"].Success)
                tokens.Add((m.Groups["inkw"].Index, m.Groups["inkw"].Length, GetBrush("StackKeywordBrush") ?? Brushes.Gray, "keyword"));
            if (m.Groups["path"].Success)
                tokens.Add((m.Groups["path"].Index, m.Groups["path"].Length, GetBrush("StackPathBrush") ?? new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90)), "path"));
            if (m.Groups["line"].Success)
                tokens.Add((m.Groups["line"].Index, m.Groups["line"].Length, GetBrush("StackLineNumberBrush") ?? Brushes.Orange, "line"));
        }

        if (tokens.Count == 0)
        {
            var ft = CreateFormattedText(message, GetBrush("TextDefaultBrush") ?? Brushes.White);
            context.DrawText(ft, new Point(startX, y));
            return;
        }

        tokens.Sort((a, b) => a.Index.CompareTo(b.Index));

        double x = startX;
        int pos = 0;
        var fallbackBrush = GetBrush("TextDefaultBrush") ?? Brushes.White;

        foreach (var (index, length, brush, type) in tokens)
        {
            // Draw gap before token
            if (pos < index)
            {
                var gap = message.Substring(pos, index - pos);
                var ft = CreateFormattedText(gap, fallbackBrush);
                context.DrawText(ft, new Point(x, y));
                x += ft.Width;
            }

            // Draw token
            var token = message.Substring(index, length);
            var tokenFt = CreateFormattedText(token, brush);
            context.DrawText(tokenFt, new Point(x, y));
            x += tokenFt.Width;
            pos = index + length;
        }

        // Draw remaining text
        if (pos < message.Length)
        {
            var remaining = message.Substring(pos);
            var ft = CreateFormattedText(remaining, fallbackBrush);
            context.DrawText(ft, new Point(x, y));
        }
    }

    private void RenderMessageWithHighlights(DrawingContext context, double startX, double y, string message, IBrush defaultBrush)
    {
        // Patterns for numbers, hex, GUIDs, IP addresses, URLs
        var hexPattern = new Regex(@"\b0x[0-9a-fA-F]+\b");
        var numberPattern = new Regex(@"\b\d+\.?\d*(?:[eE][-+]?\d+)?\b");
        var guidPattern = new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.IgnoreCase);
        var ipPattern = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
        var urlPattern = new Regex(@"\b(?:https?|ftp)://[^\s]+\b", RegexOptions.IgnoreCase);

        var tokens = new List<(int Index, int Length, IBrush Brush)>();

        // GUIDs (highest priority)
        foreach (Match m in guidPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("GuidBrush") ?? new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6)))); // Orchid

        // URLs
        foreach (Match m in urlPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("UrlBrush") ?? new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF)))); // Deep Sky Blue
        }

        // IP addresses
        foreach (Match m in ipPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("IpAddressBrush") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)))); // Gold
        }

        // Hex values (before regular numbers)
        foreach (Match m in hexPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("HexBrush") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)))); // Orange
        }

        // Regular numbers
        foreach (Match m in numberPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("NumberBrush") ?? Brushes.Orange));
        }

        if (tokens.Count == 0)
        {
            var ft = CreateFormattedText(message, defaultBrush);
            context.DrawText(ft, new Point(startX, y));
            return;
        }

        tokens.Sort((a, b) => a.Index.CompareTo(b.Index));

        double x = startX;
        int pos = 0;

        foreach (var (index, length, brush) in tokens)
        {
            // Draw gap before token
            if (pos < index)
            {
                var gap = message.Substring(pos, index - pos);
                var ft = CreateFormattedText(gap, defaultBrush);
                context.DrawText(ft, new Point(x, y));
                x += ft.Width;
            }

            // Draw token
            var token = message.Substring(index, length);
            var tokenFt = CreateFormattedText(token, brush);
            context.DrawText(tokenFt, new Point(x, y));
            x += tokenFt.Width;
            pos = index + length;
        }

        // Draw remaining text
        if (pos < message.Length)
        {
            var remaining = message.Substring(pos);
            var ft = CreateFormattedText(remaining, defaultBrush);
            context.DrawText(ft, new Point(x, y));
        }
    }

    private void RenderJsonMessage(DrawingContext context, double startX, double y, string message)
    {
        var tokens = JsonHighlightTokenizer.Tokenize(message);
        double x = startX;

        foreach (var (start, length, kind) in tokens)
        {
            if (length <= 0) continue;
            var segment = message.Substring(start, length);
            var brush = GetJsonBrush(kind, segment);
            var ft = CreateFormattedText(segment, brush);
            context.DrawText(ft, new Point(x, y));
            x += ft.Width;
        }
    }

    private void RenderSqlMessage(DrawingContext context, double startX, double y, string message)
    {
        // Regex patterns for SQL highlighting
        var sqlKeywordPattern = new Regex(
            @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|CROSS|ON|AND|OR|NOT|IN|INTO|VALUES|SET|CREATE|DROP|ALTER|TABLE|INDEX|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|AS|DISTINCT|COUNT|SUM|AVG|MIN|MAX|BETWEEN|LIKE|IS|NULL|EXISTS|UNION|CASE|WHEN|THEN|ELSE|END|EXEC|EXECUTE|TOP|ASC|DESC)\b",
            RegexOptions.IgnoreCase);
        var sqlStringPattern = new Regex(@"'(?:[^'\\]|\\.)*'");
        var sqlOperatorPattern = new Regex(@"[=<>!]+|[(),;*]");
        var sqlNumberPattern = new Regex(@"\b\d+\.?\d*\b");

        // Collect all tokens
        var tokens = new List<(int Index, int Length, IBrush Brush)>();

        foreach (Match m in sqlKeywordPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("SqlKeywordBrush") ?? new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF)))); // Deep Sky Blue
        foreach (Match m in sqlStringPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("SqlStringBrush") ?? Brushes.Green));
        foreach (Match m in sqlOperatorPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index >= t.Index && m.Index < t.Index + t.Length);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("SqlOperatorBrush") ?? Brushes.Gray));
        }
        foreach (Match m in sqlNumberPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("SqlNumberBrush") ?? Brushes.Orange));
        }

        if (tokens.Count == 0)
        {
            var ft = CreateFormattedText(message, GetBrush("TextDefaultBrush") ?? Brushes.White);
            context.DrawText(ft, new Point(startX, y));
            return;
        }

        tokens.Sort((a, b) => a.Index.CompareTo(b.Index));

        double x = startX;
        int pos = 0;
        var fallbackBrush = GetBrush("TextDefaultBrush") ?? Brushes.White;

        foreach (var (index, length, brush) in tokens)
        {
            // Draw gap before token
            if (pos < index)
            {
                var gap = message.Substring(pos, index - pos);
                var ft = CreateFormattedText(gap, fallbackBrush);
                context.DrawText(ft, new Point(x, y));
                x += ft.Width;
            }

            // Draw token
            var token = message.Substring(index, length);
            var tokenFt = CreateFormattedText(token, brush);
            context.DrawText(tokenFt, new Point(x, y));
            x += tokenFt.Width;
            pos = index + length;
        }

        // Draw remaining text
        if (pos < message.Length)
        {
            var remaining = message.Substring(pos);
            var ft = CreateFormattedText(remaining, fallbackBrush);
            context.DrawText(ft, new Point(x, y));
        }
    }

    private void RenderPlainMessage(DrawingContext context, double x, double y, string message, IBrush? brush)
    {
        brush ??= Brushes.White;
        var ft = CreateFormattedText(message, brush);
        context.DrawText(ft, new Point(x, y));
    }

    private IBrush GetJsonBrush(JsonHighlightKind kind, string segment)
    {
        if (kind == JsonHighlightKind.Punctuation && segment.Length == 1)
        {
            return segment[0] switch
            {
                '{' or '}' => GetBrush("JsonBraceBrush") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), // Gold
                '[' or ']' => GetBrush("JsonBracketBrush") ?? new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6)), // Orchid
                ':' or ',' => GetBrush("JsonPunctuationBrush") ?? Brushes.Gray,
                _ => GetBrush("JsonPunctuationBrush") ?? Brushes.Gray
            };
        }

        return kind switch
        {
            JsonHighlightKind.Key         => GetBrush("JsonKeyBrush") ?? Brushes.Cyan,
            JsonHighlightKind.String      => GetBrush("JsonStringBrush") ?? Brushes.Green,
            JsonHighlightKind.Number      => GetBrush("JsonNumberBrush") ?? Brushes.Orange,
            JsonHighlightKind.Bool        => GetBrush("JsonBoolBrush") ?? Brushes.Red,
            JsonHighlightKind.Punctuation => GetBrush("JsonPunctuationBrush") ?? Brushes.Gray,
            JsonHighlightKind.Prefix      => GetBrush("DimTextBrush") ?? Brushes.Gray,
            _                             => GetBrush("TextDefaultBrush") ?? Brushes.White
        };
    }

    private IBrush GetLevelBrush(LogLevel level) => level switch
    {
        LogLevel.Trace   => GetBrush("TextTraceBrush") ?? Brushes.Gray,
        LogLevel.Verbose => GetBrush("TextVerboseBrush") ?? Brushes.Gray,
        LogLevel.Debug   => GetBrush("TextDebugBrush") ?? Brushes.Gray,
        LogLevel.Info    => GetBrush("TextInfoBrush") ?? Brushes.Cyan,
        LogLevel.Warn    => GetBrush("TextWarnBrush") ?? Brushes.Orange,
        LogLevel.Error   => GetBrush("TextErrorBrush") ?? Brushes.Red,
        LogLevel.Fatal   => GetBrush("TextFatalBrush") ?? Brushes.Magenta,
        _                => GetBrush("TextDefaultBrush") ?? Brushes.Green
    };

    private static readonly IBrush FallbackText = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x41));
    private static readonly IBrush FallbackDim = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x96));
    private static readonly IBrush FallbackTimestamp = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x82));

    private IBrush? GetBrush(string key)
    {
        if (this.TryFindResource(key, out var resource) && resource is IBrush brush)
            return brush;
        if (AvaloniaApplication.Current?.TryGetResource(key, null, out var appRes) == true && appRes is IBrush appBrush)
            return appBrush;
        return key switch
        {
            "TextDefaultBrush" => FallbackText,
            "DimTextBrush" => FallbackDim,
            "TimestampBrush" => FallbackTimestamp,
            _ => null
        };
    }

    private static FormattedText CreateFormattedText(string text, IBrush foreground)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoTypeface, LogFontSize, foreground);
    }
}
