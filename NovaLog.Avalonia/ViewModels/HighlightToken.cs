namespace NovaLog.Avalonia.ViewModels;

public enum HighlightType
{
    None,
    // Message general
    TextDefault,
    DimText,
    // StackTrace
    StackKeyword,
    StackMethod,
    StackArgs,
    StackPath,
    StackLineNumber,
    StackException,
    // JSON
    JsonKey,
    JsonString,
    JsonNumber,
    JsonBool,
    JsonPunctuation,
    JsonBrace,
    JsonBracket,
    // SQL
    SqlKeyword,
    SqlString,
    SqlOperator,
    SqlNumber,
    // Common patterns
    Guid,
    Url,
    IpAddress,
    Hex,
    Number,
    // Dynamic
    SearchHighlight,
    CustomRule
}

public readonly record struct HighlightToken(int Index, int Length, HighlightType Type, string? CustomColorHex = null);
