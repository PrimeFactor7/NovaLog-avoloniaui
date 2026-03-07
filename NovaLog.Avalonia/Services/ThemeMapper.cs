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
        var theme = _themeService.CurrentTheme;
        var res = app.Resources;

        // Switch Avalonia's base theme for standard controls (TextBox, CheckBox, etc.)
        app.RequestedThemeVariant = theme.Name == LogThemeData.Light.Name ? ThemeVariant.Light : ThemeVariant.Dark;

        res["BackgroundBrush"] = BrushFrom(theme.Background);
        res["PanelBgBrush"] = BrushFrom(theme.PanelBg);
        res["ToolBarBgBrush"] = BrushFrom(theme.ToolBarBg);
        res["StatusBarBgBrush"] = BrushFrom(theme.StatusBarBg);
        res["TabActiveBgBrush"] = BrushFrom(theme.TabActiveBg);
        res["TabHoverBgBrush"] = BrushFrom(theme.TabHoverBg);
        res["SeparatorBrush"] = BrushFrom(theme.Separator);
        res["SplitterBgBrush"] = BrushFrom(theme.SplitterBg);

        res["TextTraceBrush"] = BrushFrom(theme.TextTrace);
        res["TextVerboseBrush"] = BrushFrom(theme.TextVerbose);
        res["TextDebugBrush"] = BrushFrom(theme.TextDebug);
        res["TextInfoBrush"] = BrushFrom(theme.TextInfo);
        res["TextWarnBrush"] = BrushFrom(theme.TextWarn);
        res["TextErrorBrush"] = BrushFrom(theme.TextError);
        res["TextFatalBrush"] = BrushFrom(theme.TextFatal);
        res["TextDefaultBrush"] = BrushFrom(theme.TextDefault);

        res["AccentBrush"] = BrushFrom(theme.Accent);
        res["TimestampBrush"] = BrushFrom(theme.Timestamp);
        res["DimTextBrush"] = BrushFrom(theme.DimText);

        res["WarnLineBgBrush"] = BrushFrom(theme.WarnLineBg);
        res["ErrorLineBgBrush"] = BrushFrom(theme.ErrorLineBg);
        res["FatalLineBgBrush"] = BrushFrom(theme.FatalLineBg);

        res["JsonKeyBrush"] = BrushFrom(theme.JsonKey);
        res["JsonStringBrush"] = BrushFrom(theme.JsonString);
        res["JsonNumberBrush"] = BrushFrom(theme.JsonNumber);
        res["JsonBoolBrush"] = BrushFrom(theme.JsonBool);
        res["JsonPunctuationBrush"] = BrushFrom(theme.JsonPunctuation);
        res["JsonBraceBrush"] = BrushFrom(theme.JsonBrace);

        res["SqlKeywordBrush"] = BrushFrom(theme.SqlKeyword);
        res["SqlTableBrush"] = BrushFrom(theme.SqlTable);
        res["SqlValueBrush"] = BrushFrom(theme.SqlValue);
        res["SqlOperatorBrush"] = BrushFrom(theme.SqlOperator);

        res["StackMethodBrush"] = BrushFrom(theme.StackMethod);
        res["StackFilePathBrush"] = BrushFrom(theme.StackFilePath);
        res["StackLineNumberBrush"] = BrushFrom(theme.StackLineNumber);
        res["StackKeywordBrush"] = BrushFrom(theme.StackKeyword);

        res["NumberLiteralBrush"] = BrushFrom(theme.NumberLiteral);
        res["BookmarkMarkerBrush"] = BrushFrom(theme.BookmarkMarker);
        res["SearchHitMarkerBrush"] = BrushFrom(theme.SearchHitMarker);
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
}
