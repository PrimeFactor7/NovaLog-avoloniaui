namespace NovaLog.Core.Theme;

/// <summary>
/// Maps VS Code theme color keys and tokenColors scopes to LogThemeData property names.
/// Used to build a NovaLog theme from a VS Code theme's colors and syntax tokens.
/// </summary>
public static class VSCodeThemeMapping
{
    /// <summary>Scope selector (prefix match) → LogThemeData syntax property. Order: more specific first.</summary>
    private static readonly (string ScopePrefix, string Property)[] ScopeToSyntaxProperty =
    {
        ("support.type.property-name.json", "JsonKey"),
        ("entity.name.tag", "JsonKey"),
        ("variable.other.key", "JsonKey"),
        ("keyword.control.sql", "SqlKeyword"),
        ("keyword.other.sql", "SqlKeyword"),
        ("storage.type.sql", "SqlKeyword"),
        ("keyword", "SqlKeyword"),
        ("string.quoted.double.json", "JsonString"),
        ("string.quoted.single.sql", "SqlValue"),
        ("string.quoted", "JsonString"),
        ("string", "JsonString"),
        ("constant.numeric", "JsonNumber"),
        ("constant.numeric", "NumberLiteral"),
        ("constant.language.boolean", "JsonBool"),
        ("constant.character", "JsonString"),
        ("punctuation.separator.key-value", "JsonPunctuation"),
        ("punctuation.definition.string", "JsonPunctuation"),
        ("punctuation.brace", "JsonBrace"),
        ("punctuation", "JsonPunctuation"),
        ("entity.name.table", "SqlTable"),
        ("entity.name.type", "SqlTable"),
        ("variable.other", "SqlValue"),
        ("keyword.operator", "SqlOperator"),
        ("comment", "DimText"),
        ("meta.structure.dictionary.json", "JsonKey"),
        ("support.function", "StackMethod"),
        ("entity.name.function", "StackMethod"),
    };

    /// <summary>VS Code theme key → LogThemeData property name (for WithOverrides).</summary>
    private static readonly Dictionary<string, string> VSCodeToProperty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor.background"] = "Background",
        ["editor.foreground"] = "TextDefault",
        ["foreground"] = "TextDefault",
        ["activityBar.background"] = "PanelBg",
        ["sideBar.background"] = "PanelBg",
        ["titleBar.activeBackground"] = "ToolBarBg",
        ["statusBar.background"] = "StatusBarBg",
        ["tab.activeBackground"] = "TabActiveBg",
        ["tab.inactiveBackground"] = "TabHoverBg",
        ["tab.border"] = "Separator",
        ["sideBar.border"] = "Separator",
        ["panel.border"] = "Separator",
        ["focusBorder"] = "Accent",
        ["button.background"] = "Accent",
        ["button.foreground"] = "TextDefault",
        ["input.background"] = "PanelBg",
        ["input.foreground"] = "TextDefault",
        ["input.border"] = "Separator",
        ["widget.background"] = "PanelBg",
        ["panel.background"] = "Background",
        ["editor.lineHighlightBackground"] = "PanelBg",
        ["list.activeSelectionBackground"] = "TabActiveBg",
        ["list.activeSelectionForeground"] = "TextDefault",
        ["list.hoverBackground"] = "TabHoverBg",
        ["list.inactiveSelectionBackground"] = "TabHoverBg",
        ["scrollbarSlider.background"] = "Separator",
        ["scrollbarSlider.hoverBackground"] = "DimText",
        ["scrollbarSlider.activeBackground"] = "Accent",
        ["statusBar.foreground"] = "DimText",
        ["titleBar.activeForeground"] = "TextDefault",
        ["sideBar.foreground"] = "TextDefault",
        ["sideBarTitle.foreground"] = "DimText",
        ["activityBar.foreground"] = "DimText",
        ["tab.activeForeground"] = "TextDefault",
        ["tab.inactiveForeground"] = "DimText",
        ["editorCursor.foreground"] = "Accent",
        ["editorLineNumber.foreground"] = "DimText",
        ["editorLineNumber.activeForeground"] = "Accent",
        ["editor.selectionBackground"] = "Accent",
    };

    /// <summary>Convert VS Code theme colors to a dictionary of LogThemeData property names → hex.</summary>
    public static IReadOnlyDictionary<string, string> ToThemeOverrides(IReadOnlyDictionary<string, string> vsCodeColors)
    {
        if (vsCodeColors == null || vsCodeColors.Count == 0)
            return new Dictionary<string, string>();

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vsKey, hex) in vsCodeColors)
        {
            if (string.IsNullOrWhiteSpace(hex)) continue;
            if (VSCodeToProperty.TryGetValue(vsKey, out var prop))
            {
                var raw = hex.TrimStart('#');
                overrides[prop] = (raw.Length == 6 || raw.Length == 8) ? "#" + raw : hex;
            }
        }
        return overrides;
    }

    /// <summary>Convert tokenColors (scope → foreground) to LogThemeData syntax property overrides. Uses first matching scope per property.</summary>
    public static IReadOnlyDictionary<string, string> ToSyntaxOverrides(IReadOnlyList<(string Scope, string Foreground)>? tokenColors)
    {
        if (tokenColors == null || tokenColors.Count == 0)
            return new Dictionary<string, string>();

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (scopePrefix, property) in ScopeToSyntaxProperty)
        {
            if (overrides.ContainsKey(property)) continue;
            foreach (var (scope, foreground) in tokenColors)
            {
                if (string.IsNullOrWhiteSpace(foreground)) continue;
                if (scope.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var raw = foreground.TrimStart('#');
                    overrides[property] = (raw.Length == 6 || raw.Length == 8) ? "#" + raw : foreground;
                    break;
                }
            }
        }
        return overrides;
    }

    /// <summary>Classify theme as Full (UI + syntax), UIOnly, or SyntaxOnly.</summary>
    public static VSCodeThemeKind GetThemeKind(
        IReadOnlyDictionary<string, string> colors,
        IReadOnlyList<(string Scope, string Foreground)> tokenColors)
    {
        var hasUi = colors != null && colors.Count > 0;
        var hasSyntax = tokenColors != null && tokenColors.Count > 0;
        if (hasUi && hasSyntax) return VSCodeThemeKind.Full;
        if (hasSyntax) return VSCodeThemeKind.SyntaxOnly;
        return VSCodeThemeKind.UIOnly;
    }

    public static string TryResolveVariable(string value, IReadOnlyDictionary<string, string> colors)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('$')) return value;
        var varName = value.TrimStart('$');
        if (colors.TryGetValue(varName, out var resolved)) return resolved;
        return value;
    }
}
