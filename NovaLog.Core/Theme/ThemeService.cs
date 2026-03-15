using NovaLog.Core.Models;

namespace NovaLog.Core.Theme;

/// <summary>
/// Non-static theme service. Replaces StarLogColors static accessor.
/// Returns hex strings; UI layer converts to platform-specific color objects.
/// Supports runtime color overrides for specific log elements.
/// </summary>
public sealed class ThemeService
{
    private LogThemeData _appTheme = LogThemeData.Dark;
    private LogThemeData _logTheme = LogThemeData.Dark;

    // ── Overrides ────────────────────────────────────────────────
    private string? _overrideTimestamp;
    private string? _overrideMessage;
    private readonly Dictionary<LogLevel, string?> _overrideLevelFg = new();
    private readonly Dictionary<LogLevel, string?> _overrideLevelBg = new();

    /// <summary>Theme for app UI (sidebar, tabs, panels).</summary>
    public LogThemeData AppTheme => _appTheme;
    /// <summary>Theme for log content and syntax (levels, JSON/SQL, stack traces).</summary>
    public LogThemeData LogTheme => _logTheme;
    /// <summary>Same as AppTheme for backward compatibility.</summary>
    public LogThemeData CurrentTheme => _appTheme;
    public bool IsDark => _appTheme.Name == LogThemeData.Dark.Name;

    public event Action<LogThemeData>? ThemeChanged;

    public void SetTheme(string themeId)
    {
        var theme = themeId == AppConstants.ThemeLight ? LogThemeData.Light : LogThemeData.Dark;
        _appTheme = theme;
        _logTheme = theme;
        ThemeChanged?.Invoke(_appTheme);
    }

    public void SetTheme(LogThemeData theme)
    {
        _appTheme = theme;
        _logTheme = theme;
        ThemeChanged?.Invoke(_appTheme);
    }

    /// <summary>Apply theme only to app UI (panels, tabs, sidebar).</summary>
    public void SetAppTheme(LogThemeData theme)
    {
        _appTheme = theme;
        ThemeChanged?.Invoke(_appTheme);
    }

    /// <summary>Apply theme only to log/syntax (levels, message, JSON/SQL highlighting).</summary>
    public void SetLogTheme(LogThemeData theme)
    {
        _logTheme = theme;
        ThemeChanged?.Invoke(_logTheme);
    }

    public void CycleTheme()
    {
        var next = _appTheme.Name == LogThemeData.Dark.Name ? LogThemeData.Light : LogThemeData.Dark;
        _appTheme = next;
        _logTheme = next;
        ThemeChanged?.Invoke(_appTheme);
    }

    public bool LevelEntireLineEnabled { get; set; }
    public bool JsonHighlightEnabled { get; set; } = true;
    public bool SqlHighlightEnabled { get; set; } = true;
    public bool StackTraceHighlightEnabled { get; set; } = true;
    public bool NumberHighlightEnabled { get; set; } = true;

    // ── Override API ─────────────────────────────────────────────

    public void SetTimestampOverride(string? hex) { _overrideTimestamp = hex; ThemeChanged?.Invoke(_appTheme); }
    public void SetMessageOverride(string? hex) { _overrideMessage = hex; ThemeChanged?.Invoke(_appTheme); }
    public void SetLevelFgOverride(LogLevel level, string? hex) { _overrideLevelFg[level] = hex; ThemeChanged?.Invoke(_appTheme); }
    public void SetLevelBgOverride(LogLevel level, string? hex) { _overrideLevelBg[level] = hex; ThemeChanged?.Invoke(_appTheme); }

    public void ClearOverrides()
    {
        _overrideTimestamp = null;
        _overrideMessage = null;
        _overrideLevelFg.Clear();
        _overrideLevelBg.Clear();
        ThemeChanged?.Invoke(_appTheme);
    }

    public string GetTimestampColor() => _overrideTimestamp ?? _logTheme.Timestamp;
    public string GetMessageColor() => _overrideMessage ?? _logTheme.TextDefault;

    public string GetLevelColorHex(LogLevel level)
    {
        if (_overrideLevelFg.TryGetValue(level, out var hex) && !string.IsNullOrEmpty(hex))
            return hex;
        return _logTheme.GetLevelColorHex(level);
    }

    public string? GetLevelBgColorHex(LogLevel level)
    {
        if (_overrideLevelBg.TryGetValue(level, out var hex) && !string.IsNullOrEmpty(hex))
            return hex;
        return level switch
        {
            LogLevel.Warn => _logTheme.WarnLineBg,
            LogLevel.Error => _logTheme.ErrorLineBg,
            LogLevel.Fatal => _logTheme.FatalLineBg,
            _ => null
        };
    }
}
