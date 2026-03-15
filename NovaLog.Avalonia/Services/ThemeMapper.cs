using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using NovaLog.Core.Theme;

namespace NovaLog.Avalonia.Services;

/// <summary>
/// Bridges LogThemeData hex strings to Avalonia DynamicResource brushes.
/// UI binds to resource keys; Skia handles transitions/opacity natively.
/// </summary>
public sealed class ThemeMapper
{
    private readonly ThemeService _themeService;

    public ThemeMapper(ThemeService themeService)
    {
        _themeService = themeService;
        _themeService.ThemeChanged += OnThemeChanged;
    }

    public void ApplyTheme(Application app)
    {
        var appTheme = _themeService.AppTheme;
        var logTheme = _themeService.LogTheme;
        var res = app.Resources;

        // Avalonia base theme from app UI theme
        app.RequestedThemeVariant = appTheme.Name == LogThemeData.Light.Name ? ThemeVariant.Light : ThemeVariant.Dark;

        // ── App UI only (Apply to App) ──
        res["BackgroundBrush"] = BrushFrom(appTheme.Background);
        res["PanelBgBrush"] = BrushFrom(appTheme.PanelBg);
        res["ToolBarBgBrush"] = BrushFrom(appTheme.ToolBarBg);
        res["StatusBarBgBrush"] = BrushFrom(appTheme.StatusBarBg);
        res["TabActiveBgBrush"] = BrushFrom(appTheme.TabActiveBg);
        res["TabHoverBgBrush"] = BrushFrom(appTheme.TabHoverBg);
        res["SeparatorBrush"] = BrushFrom(appTheme.Separator);
        res["SplitterBgBrush"] = BrushFrom(appTheme.SplitterBg);
        res["AccentBrush"] = BrushFrom(appTheme.Accent);

        // ── Log / syntax (Apply to Logs): level text, timestamp, line bg, JSON/SQL/stack ──
        res["TextTraceBrush"] = BrushFrom(logTheme.TextTrace);
        res["TextVerboseBrush"] = BrushFrom(logTheme.TextVerbose);
        res["TextDebugBrush"] = BrushFrom(logTheme.TextDebug);
        res["TextInfoBrush"] = BrushFrom(logTheme.TextInfo);
        res["TextWarnBrush"] = BrushFrom(logTheme.TextWarn);
        res["TextErrorBrush"] = BrushFrom(logTheme.TextError);
        res["TextFatalBrush"] = BrushFrom(logTheme.TextFatal);
        res["TextDefaultBrush"] = BrushFrom(logTheme.TextDefault);
        res["TimestampBrush"] = BrushFrom(logTheme.Timestamp);
        res["DimTextBrush"] = BrushFrom(logTheme.DimText);
        res["WarnLineBgBrush"] = BrushFrom(logTheme.WarnLineBg);
        res["ErrorLineBgBrush"] = BrushFrom(logTheme.ErrorLineBg);
        res["FatalLineBgBrush"] = BrushFrom(logTheme.FatalLineBg);

        res["JsonKeyBrush"] = BrushFrom(logTheme.JsonKey);
        res["JsonStringBrush"] = BrushFrom(logTheme.JsonString);
        res["JsonNumberBrush"] = BrushFrom(logTheme.JsonNumber);
        res["JsonBoolBrush"] = BrushFrom(logTheme.JsonBool);
        res["JsonPunctuationBrush"] = BrushFrom(logTheme.JsonPunctuation);
        res["JsonBraceBrush"] = BrushFrom(logTheme.JsonBrace);

        res["SqlKeywordBrush"] = BrushFrom(logTheme.SqlKeyword);
        res["SqlTableBrush"] = BrushFrom(logTheme.SqlTable);
        res["SqlValueBrush"] = BrushFrom(logTheme.SqlValue);
        res["SqlOperatorBrush"] = BrushFrom(logTheme.SqlOperator);

        res["StackMethodBrush"] = BrushFrom(logTheme.StackMethod);
        res["StackFilePathBrush"] = BrushFrom(logTheme.StackFilePath);
        res["StackLineNumberBrush"] = BrushFrom(logTheme.StackLineNumber);
        res["StackKeywordBrush"] = BrushFrom(logTheme.StackKeyword);

        res["NumberLiteralBrush"] = BrushFrom(logTheme.NumberLiteral);
        res["BookmarkMarkerBrush"] = BrushFrom(logTheme.BookmarkMarker);
        res["SearchHitMarkerBrush"] = BrushFrom(logTheme.SearchHitMarker);
    }

    private void OnThemeChanged(LogThemeData _)
    {
        if (Application.Current is { } app)
            ApplyTheme(app);
    }

    public static SolidColorBrush BrushFrom(string hex)
    {
        return new SolidColorBrush(ColorFrom(hex));
    }

    public static Color ColorFrom(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.White;
        hex = hex.TrimStart('#');
        try
        {
            return hex.Length switch
            {
                6 => Color.FromRgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16)),
                _ => Colors.White
            };
        }
        catch (FormatException)
        {
            return Colors.White;
        }
    }
}
