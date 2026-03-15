namespace NovaLog.Core.Theme;

/// <summary>
/// Maps VS Code theme color keys to LogThemeData property names.
/// Used to build a NovaLog theme from a VS Code theme's colors.
/// </summary>
public static class VSCodeThemeMapping
{
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
}
