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
    internal static readonly Typeface MonoTypeface = new("Cascadia Mono, Consolas, Courier New");
    internal const double LogFontSize = 12;
    internal static double RowHeight { get; set; } = 18;
    internal const double CharWidth = 7.2;
    private const double TimestampChars = 23;
    private const double LevelCharsMax = 8;
    private const double GapChars = 2;
    private const double LeftPad = 4;
    private const double MergeColorBarWidth = 6;
    private const double MergeGutterGap = 2;
    private const double BookmarkMarkerWidth = 3;
    private static readonly IBrush SelectedLineBrush = new SolidColorBrush(Color.FromArgb(0x24, 0x4F, 0xC3, 0xF7));
    private static readonly IPen SelectedLinePen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x4F, 0xC3, 0xF7)), 1);
    internal static readonly ConcurrentDictionary<string, IBrush> ParsedBrushes = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly IBrush FallbackText = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x41));
    internal static readonly IBrush FallbackDim = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x96));
    internal static readonly IBrush FallbackTimestamp = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x82));
    internal static readonly IBrush FallbackStackArgs = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    internal static readonly IBrush FallbackStackPath = new SolidColorBrush(Color.FromRgb(0x90, 0xEE, 0x90));
    internal static readonly IBrush FallbackGuidBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6));
    internal static readonly IBrush FallbackUrlBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF));
    internal static readonly IBrush FallbackIpBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
    internal static readonly IBrush FallbackHexBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
    internal static readonly IBrush FallbackJsonBraceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
    internal static readonly IBrush FallbackJsonBracketBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6));
    internal static readonly IBrush FallbackSqlKeywordBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBF, 0xFF));
    private static readonly IBrush FallbackBookmarkBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xFF));
    private static readonly IBrush SearchHighlightBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xE0, 0x00));
    private static readonly IBrush FallbackHeaderBg = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C));
    private static readonly IPen SeparatorPen = new Pen(Brushes.Gray, 1);

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
        OwnerLogView = null;
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
            var headerBg = GetBrush("ToolBarBgBrush") ?? FallbackHeaderBg;
            context.FillRectangle(headerBg, bounds);

            // Bottom separator line
            context.DrawLine(SeparatorPen, new Point(0, bounds.Height - 0.5), new Point(bounds.Width, bounds.Height - 0.5));

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
                : ResolveLevelBrush(_vm.Level);

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
        double currentX = x;
        foreach (var token in vm.MessageTokens)
        {
            if (token.Length <= 0) continue;
            
            var text = vm.Message.Substring(token.Index, token.Length);
            var brush = ResolveTokenBrush(token, theme);
            var ft = CreateFormattedText(text, brush);
            context.DrawText(ft, new Point(currentX, y));
            currentX += text.Length * CharWidth;
        }
    }

    private IBrush ResolveTokenBrush(HighlightToken token, ThemeService? theme)
    {
        return token.Type switch
        {
            HighlightType.TextDefault => theme != null ? ParseBrush(theme.GetMessageColor()) : GetBrush("TextDefaultBrush") ?? Brushes.Green,
            HighlightType.DimText => GetBrush("DimTextBrush") ?? Brushes.Gray,
            
            HighlightType.StackKeyword => GetBrush("StackKeywordBrush") ?? Brushes.Gray,
            HighlightType.StackMethod => GetBrush("StackMethodBrush") ?? Brushes.Cyan,
            HighlightType.StackArgs => GetBrush("StackArgsBrush") ?? FallbackStackArgs,
            HighlightType.StackPath => GetBrush("StackPathBrush") ?? FallbackStackPath,
            HighlightType.StackLineNumber => GetBrush("StackLineNumberBrush") ?? Brushes.Orange,
            HighlightType.StackException => GetBrush("StackExceptionBrush") ?? Brushes.Red,
            
            HighlightType.JsonKey => ResolveBrush("JsonKeyBrush") ?? Brushes.Cyan,
            HighlightType.JsonString => ResolveBrush("JsonStringBrush") ?? Brushes.Green,
            HighlightType.JsonNumber => ResolveBrush("JsonNumberBrush") ?? Brushes.Orange,
            HighlightType.JsonBool => ResolveBrush("JsonBoolBrush") ?? Brushes.Red,
            HighlightType.JsonPunctuation => ResolveBrush("JsonPunctuationBrush") ?? Brushes.Gray,
            HighlightType.JsonBrace => ResolveBrush("JsonBraceBrush") ?? FallbackJsonBraceBrush,
            HighlightType.JsonBracket => ResolveBrush("JsonBracketBrush") ?? FallbackJsonBracketBrush,
            
            HighlightType.SqlKeyword => GetBrush("SqlKeywordBrush") ?? FallbackSqlKeywordBrush,
            HighlightType.SqlString => GetBrush("SqlStringBrush") ?? Brushes.Green,
            HighlightType.SqlOperator => GetBrush("SqlOperatorBrush") ?? Brushes.Gray,
            HighlightType.SqlNumber => GetBrush("SqlNumberBrush") ?? Brushes.Orange,
            
            HighlightType.Guid => GetBrush("GuidBrush") ?? FallbackGuidBrush,
            HighlightType.Url => GetBrush("UrlBrush") ?? FallbackUrlBrush,
            HighlightType.IpAddress => GetBrush("IpAddressBrush") ?? FallbackIpBrush,
            HighlightType.Hex => GetBrush("HexBrush") ?? FallbackHexBrush,
            HighlightType.Number => GetBrush("NumberBrush") ?? Brushes.Orange,
            
            HighlightType.CustomRule => !string.IsNullOrEmpty(token.CustomColorHex) ? ParseBrush(token.CustomColorHex) : Brushes.White,
            _ => Brushes.White
        };
    }

    private void RenderPlainMessage(DrawingContext context, double x, double y, string message, IBrush? brush)
    {
        brush ??= Brushes.White;
        var ft = CreateFormattedText(message, brush);
        context.DrawText(ft, new Point(x, y));
    }

    private IBrush GetJsonBrush(JsonHighlightKind kind, string segment)
        => ResolveJsonBrush(kind, segment);

    internal static IBrush ResolveJsonBrush(JsonHighlightKind kind, string segment)
    {
        if (kind == JsonHighlightKind.Punctuation && segment.Length == 1)
        {
            return segment[0] switch
            {
                '{' or '}' => ResolveBrush("JsonBraceBrush") ?? FallbackJsonBraceBrush,
                '[' or ']' => ResolveBrush("JsonBracketBrush") ?? FallbackJsonBracketBrush,
                ':' or ',' => ResolveBrush("JsonPunctuationBrush") ?? Brushes.Gray,
                _ => ResolveBrush("JsonPunctuationBrush") ?? Brushes.Gray
            };
        }

        return kind switch
        {
            JsonHighlightKind.Key         => ResolveBrush("JsonKeyBrush") ?? Brushes.Cyan,
            JsonHighlightKind.String      => ResolveBrush("JsonStringBrush") ?? Brushes.Green,
            JsonHighlightKind.Number      => ResolveBrush("JsonNumberBrush") ?? Brushes.Orange,
            JsonHighlightKind.Bool        => ResolveBrush("JsonBoolBrush") ?? Brushes.Red,
            JsonHighlightKind.Punctuation => ResolveBrush("JsonPunctuationBrush") ?? Brushes.Gray,
            JsonHighlightKind.Prefix      => ResolveBrush("DimTextBrush") ?? Brushes.Gray,
            _                             => ResolveBrush("TextDefaultBrush") ?? Brushes.White
        };
    }

    internal static IBrush ResolveLevelBrush(LogLevel level) => level switch
    {
        LogLevel.Trace   => ResolveBrush("TextTraceBrush") ?? Brushes.Gray,
        LogLevel.Verbose => ResolveBrush("TextVerboseBrush") ?? Brushes.Gray,
        LogLevel.Debug   => ResolveBrush("TextDebugBrush") ?? Brushes.Gray,
        LogLevel.Info    => ResolveBrush("TextInfoBrush") ?? Brushes.Cyan,
        LogLevel.Warn    => ResolveBrush("TextWarnBrush") ?? Brushes.Orange,
        LogLevel.Error   => ResolveBrush("TextErrorBrush") ?? Brushes.Red,
        LogLevel.Fatal   => ResolveBrush("TextFatalBrush") ?? Brushes.Magenta,
        _                => ResolveBrush("TextDefaultBrush") ?? Brushes.Green
    };

    private IBrush? GetBrush(string key) => ResolveBrush(key);

    internal static IBrush? ResolveBrush(string key)
    {
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

    internal static IBrush ParseBrush(string hex)
        => ParsedBrushes.GetOrAdd(hex, static value => Brush.Parse(value));

    internal static FormattedText CreateFormattedText(string text, IBrush foreground)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoTypeface, LogFontSize, foreground);
    }
}
