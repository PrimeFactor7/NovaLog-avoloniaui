using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using NovaLog.Avalonia.Services;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Core.Theme;
using AvaloniaApplication = Avalonia.Application;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NovaLog.Avalonia.Controls;

public partial class LogLineRow : Control
{
    private static readonly Typeface MonoTypeface = new("Cascadia Mono, Consolas, Courier New");
    private const double LogFontSize = 12;
    internal const double RowHeight = 18;
    private const double CharWidth = 7.2;
    private const double TimestampChars = 23;
    private const double LevelCharsMax = 8;
    private const double GapChars = 2;
    private const double LeftPad = 4;
    private const double MergeColorBarWidth = 6;
    private const double MergeGutterGap = 2;
    private const double BookmarkMarkerWidth = 3;
    private static readonly IBrush SelectedLineBrush = new SolidColorBrush(Color.FromArgb(0x24, 0x4F, 0xC3, 0xF7));
    private static readonly IPen SelectedLinePen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x4F, 0xC3, 0xF7)), 1);
    private static readonly ConcurrentDictionary<string, IBrush> ParsedBrushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex StackMethodPattern = new(@"(?<atkw>at)\s+(?<method>[\w.+<>\[\]`,]+)\((?<args>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex StackFilePattern = new(@"(?:(?<inkw>in)\s+)?(?<path>\w:[\\\/][^\s:]+|[\\\/][^\s:]+):(?:line\s+)?(?<line>\d+)", RegexOptions.Compiled);
    private static readonly Regex StackExceptionPattern = new(@"(?<extype>[\w.]+Exception)\b", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"\b\d+\.?\d*(?:[eE][-+]?\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex GuidPattern = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IpPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"\b(?:https?|ftp)://[^\s]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SqlKeywordPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|CROSS|ON|AND|OR|NOT|IN|INTO|VALUES|SET|CREATE|DROP|ALTER|TABLE|INDEX|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|AS|DISTINCT|COUNT|SUM|AVG|MIN|MAX|BETWEEN|LIKE|IS|NULL|EXISTS|UNION|CASE|WHEN|THEN|ELSE|END|EXEC|EXECUTE|TOP|ASC|DESC)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SqlStringPattern = new(@"'(?:[^'\\]|\\.)*'", RegexOptions.Compiled);
    private static readonly Regex SqlOperatorPattern = new(@"[=<>!]+|[(),;*]", RegexOptions.Compiled);
    private static readonly Regex SqlNumberPattern = new(@"\b\d+\.?\d*\b", RegexOptions.Compiled);
    private static readonly IBrush FallbackText = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x41));
    private static readonly IBrush FallbackDim = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x96));
    private static readonly IBrush FallbackTimestamp = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x82));
    private static readonly IBrush FallbackStackArgs = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly IBrush FallbackStackPath = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
    private static readonly IBrush FallbackGuidBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6));
    private static readonly IBrush FallbackUrlBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF));
    private static readonly IBrush FallbackIpBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
    private static readonly IBrush FallbackHexBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
    private static readonly IBrush FallbackJsonBraceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
    private static readonly IBrush FallbackJsonBracketBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6));
    private static readonly IBrush FallbackSqlKeywordBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF));
    private static readonly IBrush FallbackBookmarkBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xFF));
    private static readonly IBrush SearchHighlightBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xE0, 0x00));

    private LogLineViewModel? _vm;
    public static readonly StyledProperty<LogViewViewModel?> OwnerLogViewProperty =
        AvaloniaProperty.Register<LogLineRow, LogViewViewModel?>(nameof(OwnerLogView));

    static LogLineRow()
    {
        OwnerLogViewProperty.Changed.AddClassHandler<LogLineRow>((row, _) => row.InvalidateVisual());
    }

    public LogLineRow()
    {
    }

    public LogViewViewModel? OwnerLogView
    {
        get => GetValue(OwnerLogViewProperty);
        set => SetValue(OwnerLogViewProperty, value);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as LogLineViewModel;
        EnsureOwnerLogView();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureOwnerLogView();
    }

    private void EnsureOwnerLogView()
    {
        if (OwnerLogView is not null) return;

        for (var current = this.GetVisualParent(); current is not null; current = current.GetVisualParent())
        {
            if (current is NovaLog.Avalonia.Views.LogViewPanel panel && panel.DataContext is LogViewViewModel vm)
            {
                OwnerLogView = vm;
                return;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double desiredWidth = 1200; // Safe default
        if (_vm is not null)
        {
            int chars = (_vm.TimestampText?.Length ?? 0) + 
                        (_vm.LevelText?.Length ?? 0) + 
                        (_vm.Message?.Length ?? 0) + 10;
            desiredWidth = LeftPad +
                           (!string.IsNullOrWhiteSpace(_vm.MergeSourceColorHex) ? MergeColorBarWidth + MergeGutterGap : 0) +
                           (chars * CharWidth);
        }
        double width = double.IsInfinity(availableSize.Width) ? desiredWidth : Math.Max(desiredWidth, availableSize.Width);
        return new Size(width, RowHeight);
    }

    public void ResetVisualState()
    {
        _vm = null;
    }

    private (IEnumerable<HighlightRule>? Rules, ThemeService? Theme, int? SelectedLineIndex, NavigationIndex? NavIndex) GetContext()
    {
        var owner = OwnerLogView;
        return owner is null
            ? (null, null, null, null)
            : (owner.HighlightRules, owner.Theme, owner.SelectedLineIndex, owner.NavIndex);
    }

    public override void Render(DrawingContext context)
    {
        // Use local rect (0,0,w,h) — Bounds includes parent offset which is wrong for Render's local coordinate space
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (_vm is null) return;

        var (rules, theme, selectedLineIndex, navIndex) = GetContext();
        bool hasMergeSource = !string.IsNullOrWhiteSpace(_vm.MergeSourceColorHex);
        bool isSelectedLine = selectedLineIndex == _vm.GlobalIndex;
        bool isBookmarked = navIndex?.IsBookmarked(_vm.GlobalIndex) == true;

        double y = (bounds.Height - LogFontSize) / 2;
        if (y < 1) y = 1;

        // 1. Log Level Row background tint (only when setting is enabled)
        if (theme?.LevelEntireLineEnabled == true)
        {
            string? bgHex = theme.GetLevelBgColorHex(_vm.Level);
            if (!string.IsNullOrEmpty(bgHex))
                context.FillRectangle(ParseBrush(bgHex), new Rect(0, 0, bounds.Width, bounds.Height));
        }

        // 2. Custom Highlight Rule Line Backgrounds
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled || rule.RuleType != HighlightRuleType.LineHighlight || string.IsNullOrEmpty(rule.BackgroundHex)) continue;
                if (rule.CompiledRegex?.IsMatch(_vm.RawText) == true)
                {
                    var brush = ParseBrush(rule.BackgroundHex);
                    context.FillRectangle(brush, new Rect(0, 0, bounds.Width, bounds.Height));
                }
            }
        }

        if (isSelectedLine)
            context.FillRectangle(SelectedLineBrush, bounds);

        if (_vm.IsFileSeparator)
        {
            // Draw header bar background
            var headerBg = GetBrush("ToolBarBgBrush") ?? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C));
            context.FillRectangle(headerBg, bounds);

            // Bottom separator line
            var sepBrush = GetBrush("SeparatorBrush") ?? Brushes.Gray;
            context.DrawLine(new Pen(sepBrush, 1), new Point(0, bounds.Height - 0.5), new Point(bounds.Width, bounds.Height - 0.5));

            if (!string.IsNullOrEmpty(_vm.Message))
            {
                double xPos = LeftPad + 4;

                // File icon (codicon)
                var iconFt = new FormattedText("\uEA7B", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("codicon"), LogFontSize, GetBrush("DimTextBrush") ?? Brushes.Gray);
                context.DrawText(iconFt, new Point(xPos, y));
                xPos += iconFt.Width + 6;

                // Filename in accent color
                var accentBrush = GetBrush("AccentBrush") ?? Brushes.Cyan;
                var nameFt = new FormattedText(_vm.Message, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Cascadia Mono, Consolas, Courier New", FontStyle.Normal, FontWeight.Bold),
                    LogFontSize, accentBrush);
                context.DrawText(nameFt, new Point(xPos, y));
                xPos += nameFt.Width + 12;

                // File size (if available)
                if (!string.IsNullOrEmpty(_vm.FileSizeText))
                {
                    var sizeFt = CreateFormattedText(_vm.FileSizeText, GetBrush("DimTextBrush") ?? Brushes.Gray);
                    context.DrawText(sizeFt, new Point(xPos, y));
                }
            }
            return;
        }

        if (hasMergeSource)
            context.FillRectangle(ParseBrush(_vm.MergeSourceColorHex!), new Rect(0, 0, MergeColorBarWidth, bounds.Height));

        if (isBookmarked)
        {
            var bookmarkBrush = GetBrush("BookmarkMarkerBrush") ?? FallbackBookmarkBrush;
            context.FillRectangle(bookmarkBrush, new Rect(0, 0, BookmarkMarkerWidth, bounds.Height));
        }

        double x = LeftPad + (hasMergeSource ? MergeColorBarWidth + MergeGutterGap : 0);

        // 3. Timestamp
        if (!_vm.IsContinuation && !string.IsNullOrEmpty(_vm.TimestampText))
        {
            IBrush tsBrush = theme != null 
                ? ParseBrush(theme.GetTimestampColor())
                : GetBrush("TimestampBrush") ?? FallbackTimestamp;
            
            var ft = CreateFormattedText(_vm.TimestampText, tsBrush);
            context.DrawText(ft, new Point(x, y));
        }
        x += (TimestampChars + GapChars) * CharWidth;

        // 4. Level
        if (!_vm.IsContinuation && !string.IsNullOrEmpty(_vm.LevelText))
        {
            IBrush levelBrush = theme != null
                ? ParseBrush(theme.GetLevelColorHex(_vm.Level))
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
                        var bg = ParseBrush(rule.BackgroundHex);
                        context.FillRectangle(bg, new Rect(xMessageStart + xOff, 0, mWidth, RowHeight));
                    }

                    var fg = string.IsNullOrEmpty(rule.ForegroundHex)
                        ? GetBrush("TextDefaultBrush") ?? FallbackText
                        : ParseBrush(rule.ForegroundHex);
                    var ft = CreateFormattedText(m.Value, fg);
                    context.DrawText(ft, new Point(xMessageStart + xOff, y));
                }
            }
        }

        // 6. Search match highlights
        var owner = OwnerLogView;
        if (owner?.ActiveSearchMatcher is { } searchMatcher && !string.IsNullOrEmpty(_vm.Message))
        {
            foreach (var (idx, len) in searchMatcher.FindMatches(_vm.Message))
            {
                double hx = xMessageStart + idx * CharWidth;
                double hw = len * CharWidth;
                context.FillRectangle(SearchHighlightBrush, new Rect(hx, 0, hw, RowHeight));
            }
        }

        if (isSelectedLine)
            context.DrawRectangle(SelectedLinePen, bounds.Deflate(0.5));
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
                        ? ParseBrush(theme.GetMessageColor())
                        : GetBrush("TextDefaultBrush") ?? Brushes.Green;
                }
                RenderMessageWithHighlights(context, x, y, vm.Message, brush);
                break;
        }
    }

    private void RenderStackTraceMessage(DrawingContext context, double startX, double y, string message)
    {
        var tokens = new List<(int Index, int Length, IBrush Brush, string Type)>();

        // Exception types
        foreach (Match m in StackExceptionPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("StackExceptionBrush") ?? Brushes.Red, "exception"));

        // Method calls
        foreach (Match m in StackMethodPattern.Matches(message))
        {
            if (m.Groups["atkw"].Success)
                tokens.Add((m.Groups["atkw"].Index, m.Groups["atkw"].Length, GetBrush("StackKeywordBrush") ?? Brushes.Gray, "keyword"));
            if (m.Groups["method"].Success)
                tokens.Add((m.Groups["method"].Index, m.Groups["method"].Length, GetBrush("StackMethodBrush") ?? Brushes.Cyan, "method"));
            if (m.Groups["args"].Success && m.Groups["args"].Length > 0)
                tokens.Add((m.Groups["args"].Index, m.Groups["args"].Length, GetBrush("StackArgsBrush") ?? FallbackStackArgs, "args"));
        }

        // File paths and line numbers
        foreach (Match m in StackFilePattern.Matches(message))
        {
            if (m.Groups["inkw"].Success)
                tokens.Add((m.Groups["inkw"].Index, m.Groups["inkw"].Length, GetBrush("StackKeywordBrush") ?? Brushes.Gray, "keyword"));
            if (m.Groups["path"].Success)
                tokens.Add((m.Groups["path"].Index, m.Groups["path"].Length, GetBrush("StackPathBrush") ?? FallbackStackPath, "path"));
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
        var tokens = new List<(int Index, int Length, IBrush Brush)>();

        // GUIDs (highest priority)
        foreach (Match m in GuidPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("GuidBrush") ?? FallbackGuidBrush));

        // URLs
        foreach (Match m in UrlPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("UrlBrush") ?? FallbackUrlBrush));
        }

        // IP addresses
        foreach (Match m in IpPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("IpAddressBrush") ?? FallbackIpBrush));
        }

        // Hex values (before regular numbers)
        foreach (Match m in HexPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index < t.Index + t.Length && m.Index + m.Length > t.Index);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("HexBrush") ?? FallbackHexBrush));
        }

        // Regular numbers
        foreach (Match m in NumberPattern.Matches(message))
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
        // Collect all tokens
        var tokens = new List<(int Index, int Length, IBrush Brush)>();

        foreach (Match m in SqlKeywordPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("SqlKeywordBrush") ?? FallbackSqlKeywordBrush));
        foreach (Match m in SqlStringPattern.Matches(message))
            tokens.Add((m.Index, m.Length, GetBrush("SqlStringBrush") ?? Brushes.Green));
        foreach (Match m in SqlOperatorPattern.Matches(message))
        {
            bool overlaps = tokens.Any(t => m.Index >= t.Index && m.Index < t.Index + t.Length);
            if (!overlaps)
                tokens.Add((m.Index, m.Length, GetBrush("SqlOperatorBrush") ?? Brushes.Gray));
        }
        foreach (Match m in SqlNumberPattern.Matches(message))
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
                '{' or '}' => GetBrush("JsonBraceBrush") ?? FallbackJsonBraceBrush,
                '[' or ']' => GetBrush("JsonBracketBrush") ?? FallbackJsonBracketBrush,
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
            "BookmarkMarkerBrush" => FallbackBookmarkBrush,
            _ => null
        };
    }

    private static IBrush ParseBrush(string hex)
        => ParsedBrushes.GetOrAdd(hex, static value => Brush.Parse(value));

    private static FormattedText CreateFormattedText(string text, IBrush foreground)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoTypeface, LogFontSize, foreground);
    }
}
