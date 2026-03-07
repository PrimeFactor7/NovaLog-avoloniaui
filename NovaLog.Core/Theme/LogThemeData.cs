using NovaLog.Core.Models;

namespace NovaLog.Core.Theme;

/// <summary>
/// Color-agnostic theme preset. All colors stored as "#RRGGBB" or "#AARRGGBB" hex strings.
/// No dependency on System.Drawing or Avalonia.Media.
/// </summary>
public sealed class LogThemeData
{
    public required string Name { get; init; }

    // Backgrounds
    public required string Background { get; init; }
    public required string PanelBg { get; init; }
    public required string ToolBarBg { get; init; }
    public required string StatusBarBg { get; init; }
    public required string TabActiveBg { get; init; }
    public required string TabHoverBg { get; init; }
    public required string Separator { get; init; }
    public required string SplitterBg { get; init; }

    // Log level text
    public required string TextTrace { get; init; }
    public required string TextVerbose { get; init; }
    public required string TextDebug { get; init; }
    public required string TextInfo { get; init; }
    public required string TextWarn { get; init; }
    public required string TextError { get; init; }
    public required string TextFatal { get; init; }
    public required string TextDefault { get; init; }

    // UI accent / chrome
    public required string Accent { get; init; }
    public required string Timestamp { get; init; }
    public required string DimText { get; init; }

    // Line backgrounds (semi-transparent tints)
    public required string WarnLineBg { get; init; }
    public required string ErrorLineBg { get; init; }
    public required string FatalLineBg { get; init; }

    // JSON token colors
    public required string JsonKey { get; init; }
    public required string JsonString { get; init; }
    public required string JsonNumber { get; init; }
    public required string JsonBool { get; init; }
    public required string JsonPunctuation { get; init; }
    public required string JsonBrace { get; init; }

    // SQL token colors
    public required string SqlKeyword { get; init; }
    public required string SqlTable { get; init; }
    public required string SqlValue { get; init; }
    public required string SqlOperator { get; init; }

    // Stack trace colors
    public required string StackMethod { get; init; }
    public required string StackFilePath { get; init; }
    public required string StackLineNumber { get; init; }
    public required string StackKeyword { get; init; }

    // Standalone number color
    public required string NumberLiteral { get; init; }

    // Navigation markers
    public required string BookmarkMarker { get; init; }
    public required string SearchHitMarker { get; init; }

    public string GetLevelColorHex(LogLevel level) => level switch
    {
        LogLevel.Trace   => TextTrace,
        LogLevel.Verbose => TextVerbose,
        LogLevel.Debug   => TextDebug,
        LogLevel.Info    => TextInfo,
        LogLevel.Warn    => TextWarn,
        LogLevel.Error   => TextError,
        LogLevel.Fatal   => TextFatal,
        _                => TextDefault
    };

    // ── Presets ─────────────────────────────────────────────────

    public static LogThemeData Dark { get; } = new()
    {
        Name            = "Dark",
        Background      = "#020205",
        PanelBg         = "#0E0E14",
        ToolBarBg       = "#14141C",
        StatusBarBg     = "#0A0A10",
        TabActiveBg     = "#232332",
        TabHoverBg      = "#2D2D3C",
        Separator       = "#323241",
        SplitterBg      = "#282837",
        TextTrace       = "#606060",
        TextVerbose     = "#6A6A8A",
        TextDebug       = "#808080",
        TextInfo        = "#00D4FF",
        TextWarn        = "#FFB000",
        TextError       = "#FF3E3E",
        TextFatal       = "#FF2060",
        TextDefault     = "#00FF41",
        Accent          = "#00D4FF",
        Timestamp       = "#5A5A82",
        DimText         = "#787896",
        WarnLineBg      = "#12FFB000",
        ErrorLineBg     = "#1CFF3E3E",
        FatalLineBg     = "#30FF2060",
        JsonKey         = "#00D4FF",
        JsonString      = "#00FF41",
        JsonNumber      = "#FFB000",
        JsonBool        = "#FF3E3E",
        JsonPunctuation = "#A0A0A8",
        JsonBrace       = "#FFD700",
        SqlKeyword      = "#FF00FF",
        SqlTable        = "#00D4FF",
        SqlValue        = "#00FF41",
        SqlOperator     = "#808080",
        StackMethod     = "#FF3E3E",
        StackFilePath   = "#00D4FF",
        StackLineNumber = "#FFB000",
        StackKeyword    = "#A0A0A8",
        NumberLiteral   = "#FFB000",
        BookmarkMarker  = "#0078FF",
        SearchHitMarker = "#00FF41"
    };

    public static LogThemeData Light { get; } = new()
    {
        Name            = "Light",
        Background      = "#FAFAFD",
        PanelBg         = "#F0F0F5",
        ToolBarBg       = "#E8E8F0",
        StatusBarBg     = "#E2E2EA",
        TabActiveBg     = "#D2D2DE",
        TabHoverBg      = "#DCDCE6",
        Separator       = "#C8C8D4",
        SplitterBg      = "#BEBEBC",
        TextTrace       = "#A0A0A0",
        TextVerbose     = "#646482",
        TextDebug       = "#828282",
        TextInfo        = "#0070AA",
        TextWarn        = "#AA6E00",
        TextError       = "#BE1919",
        TextFatal       = "#B4003C",
        TextDefault     = "#005A00",
        Accent          = "#0064B4",
        Timestamp       = "#64648C",
        DimText         = "#5A5A70",
        WarnLineBg      = "#24FFC832",
        ErrorLineBg     = "#24FF5050",
        FatalLineBg     = "#28C8003C",
        JsonKey         = "#7832B4",
        JsonString      = "#A31515",
        JsonNumber      = "#098658",
        JsonBool        = "#0000C8",
        JsonPunctuation = "#707080",
        JsonBrace       = "#B48C00",
        SqlKeyword      = "#AF00DB",
        SqlTable        = "#0070AA",
        SqlValue        = "#008000",
        SqlOperator     = "#828282",
        StackMethod     = "#BE1919",
        StackFilePath   = "#0070AA",
        StackLineNumber = "#AA6E00",
        StackKeyword    = "#828282",
        NumberLiteral   = "#AA6E00",
        BookmarkMarker  = "#0050C8",
        SearchHitMarker = "#00A028"
    };
}
