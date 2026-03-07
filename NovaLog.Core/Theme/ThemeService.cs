using NovaLog.Core.Models;

namespace NovaLog.Core.Theme;

/// <summary>
/// Non-static theme service. Replaces StarLogColors static accessor.
/// Returns hex strings; UI layer converts to platform-specific color objects.
/// Supports runtime color overrides for specific log elements.
/// </summary>
public sealed class ThemeService
{
    private LogThemeData _theme = LogThemeData.Dark;

    // ── Overrides ────────────────────────────────────────────────
    private string? _overrideTimestamp;
    private string? _overrideMessage;
    private readonly Dictionary<LogLevel, string?> _overrideLevelFg = new();
    private readonly Dictionary<LogLevel, string?> _overrideLevelBg = new();

    public LogThemeData CurrentTheme => _theme;
    public bool IsDark => _theme.Name == LogThemeData.Dark.Name;

    public event Action<LogThemeData>? ThemeChanged;

    public void SetTheme(string themeId)
    {
        _theme = themeId == AppConstants.ThemeLight ? LogThemeData.Light : LogThemeData.Dark;
        ThemeChanged?.Invoke(_theme);
    }

    public void SetTheme(LogThemeData theme)
    {
        _theme = theme;
        ThemeChanged?.Invoke(_theme);
    }

    public void CycleTheme()
    {
        _theme = _theme.Name == LogThemeData.Dark.Name ? LogThemeData.Light : LogThemeData.Dark;
        ThemeChanged?.Invoke(_theme);
    }

    public bool LevelEntireLineEnabled { get; set; }
    public bool JsonHighlightEnabled { get; set; } = true;
    public bool SqlHighlightEnabled { get; set; } = true;
    public bool StackTraceHighlightEnabled { get; set; } = true;
    public bool NumberHighlightEnabled { get; set; } = true;

    // ── Override API ─────────────────────────────────────────────

    public void SetTimestampOverride(string? hex) { _overrideTimestamp = hex; ThemeChanged?.Invoke(_theme); }
    public void SetMessageOverride(string? hex) { _overrideMessage = hex; ThemeChanged?.Invoke(_theme); }
    public void SetLevelFgOverride(LogLevel level, string? hex) { _overrideLevelFg[level] = hex; ThemeChanged?.Invoke(_theme); }
    public void SetLevelBgOverride(LogLevel level, string? hex) { _overrideLevelBg[level] = hex; ThemeChanged?.Invoke(_theme); }

    public void ClearOverrides()
    {
        _overrideTimestamp = null;
        _overrideMessage = null;
        _overrideLevelFg.Clear();
        _overrideLevelBg.Clear();
        ThemeChanged?.Invoke(_theme);
    }

    public string GetTimestampColor() => _overrideTimestamp ?? _theme.Timestamp;
    public string GetMessageColor() => _overrideMessage ?? _theme.TextDefault;

    public string GetLevelColorHex(LogLevel level)
    {
        if (_overrideLevelFg.TryGetValue(level, out var hex) && !string.IsNullOrEmpty(hex))
            return hex;
        return _theme.GetLevelColorHex(level);
    }

    public string? GetLevelBgColorHex(LogLevel level)
    {
        if (_overrideLevelBg.TryGetValue(level, out var hex) && !string.IsNullOrEmpty(hex))
            return hex;
        return level switch
        {
            LogLevel.Warn => _theme.WarnLineBg,
            LogLevel.Error => _theme.ErrorLineBg,
            LogLevel.Fatal => _theme.FatalLineBg,
            _ => null
        };
    }
}
