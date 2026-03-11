using System.Text.Json.Serialization;

namespace NovaLog.Core.Models;

/// <summary>
/// Persisted application settings. Serialized to/from JSON.
/// All color values stored as "#RRGGBB" or "#AARRGGBB" hex strings.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = AppConstants.ThemeDark;

    [JsonPropertyName("timestampColorEnabled")]
    public bool TimestampColorEnabled { get; set; }

    [JsonPropertyName("timestampColor")]
    public string TimestampColor { get; set; } = "#5A5A82";

    [JsonPropertyName("messageColorEnabled")]
    public bool MessageColorEnabled { get; set; }

    [JsonPropertyName("messageColor")]
    public string MessageColor { get; set; } = "#808080";

    [JsonPropertyName("levelColors")]
    public Dictionary<string, LevelColorEntry> LevelColors { get; set; } = new();

    [JsonPropertyName("levelEntireLineEnabled")]
    public bool LevelEntireLineEnabled { get; set; }

    [JsonPropertyName("highlightRules")]
    public List<HighlightRuleEntry> HighlightRules { get; set; } = [];

    [JsonPropertyName("mainFollowEnabled")]
    public bool MainFollowEnabled { get; set; } = true;

    [JsonPropertyName("filterFollowEnabled")]
    public bool FilterFollowEnabled { get; set; } = false;

    [JsonPropertyName("minimapShowSearch")]
    public bool MinimapShowSearch { get; set; } = true;

    [JsonPropertyName("minimapShowErrors")]
    public bool MinimapShowErrors { get; set; } = false;

    [JsonPropertyName("minimapVisible")]
    public bool MinimapVisible { get; set; } = true;

    [JsonPropertyName("filterPanelVisible")]
    public bool FilterPanelVisible { get; set; } = false;

    [JsonPropertyName("searchResultCap")]
    public int SearchResultCap { get; set; } = 500;

    [JsonPropertyName("searchNewestFirst")]
    public bool SearchNewestFirst { get; set; } = true;

    [JsonPropertyName("sectionColumnColorsExpanded")]
    public bool SectionColumnColorsExpanded { get; set; } = false;

    [JsonPropertyName("sectionLogLevelsExpanded")]
    public bool SectionLogLevelsExpanded { get; set; } = false;

    [JsonPropertyName("sectionHighlightRulesExpanded")]
    public bool SectionHighlightRulesExpanded { get; set; } = false;

    [JsonPropertyName("sectionMinimapExpanded")]
    public bool SectionMinimapExpanded { get; set; } = false;

    [JsonPropertyName("sectionThemeExpanded")]
    public bool SectionThemeExpanded { get; set; } = false;

    [JsonPropertyName("sectionFollowExpanded")]
    public bool SectionFollowExpanded { get; set; } = false;

    [JsonPropertyName("sectionDisplayExpanded")]
    public bool SectionDisplayExpanded { get; set; } = true;

    [JsonPropertyName("sectionSyntaxHighlightingExpanded")]
    public bool SectionSyntaxHighlightingExpanded { get; set; } = true;

    [JsonPropertyName("fontSize")]
    public float FontSize { get; set; } = 10f;

    [JsonPropertyName("lineHeight")]
    public int LineHeight { get; set; } = 18;

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 1280;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 800;

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    [JsonPropertyName("lastDirectory")]
    public string? LastDirectory { get; set; }

    [JsonPropertyName("rotationStrategy")]
    public string RotationStrategy { get; set; } = AppConstants.RotationStrategyAuditJson;

    [JsonPropertyName("jsonHighlightEnabled")]
    public bool JsonHighlightEnabled { get; set; } = true;

    [JsonPropertyName("sqlHighlightEnabled")]
    public bool SqlHighlightEnabled { get; set; } = true;

    [JsonPropertyName("stackTraceHighlightEnabled")]
    public bool StackTraceHighlightEnabled { get; set; } = true;

    [JsonPropertyName("numberHighlightEnabled")]
    public bool NumberHighlightEnabled { get; set; } = true;

    [JsonPropertyName("sourceManagerVisible")]
    public bool SourceManagerVisible { get; set; } = true;

    [JsonPropertyName("sourceManagerWidthPct")]
    public double SourceManagerWidthPct { get; set; } = 0.203;

    [JsonPropertyName("recentSources")]
    public List<RecentSourceEntry> RecentSources { get; set; } = [];

    [JsonPropertyName("bookmarks")]
    public Dictionary<string, List<long>> Bookmarks { get; set; } = new();

    // Grid View
    [JsonPropertyName("defaultGridMode")]
    public bool DefaultGridMode { get; set; } = true;

    [JsonPropertyName("gridLinesVisible")]
    public bool GridLinesVisible { get; set; } = true;

    [JsonPropertyName("gridMultiline")]
    public bool GridMultiline { get; set; } = true;

    [JsonPropertyName("sectionGridExpanded")]
    public bool SectionGridExpanded { get; set; } = false;

    // Formatting (auto-format in Span Lines mode)
    [JsonPropertyName("jsonFormatEnabled")]
    public bool JsonFormatEnabled { get; set; }

    [JsonPropertyName("sqlFormatEnabled")]
    public bool SqlFormatEnabled { get; set; }

    [JsonPropertyName("formatIndentSize")]
    public int FormatIndentSize { get; set; } = 2;

    [JsonPropertyName("maxRowLines")]
    public int MaxRowLines { get; set; } = 50;

    [JsonPropertyName("sectionFormattingExpanded")]
    public bool SectionFormattingExpanded { get; set; }
}

public sealed class LevelColorEntry
{
    [JsonPropertyName("foreground")]
    public string Foreground { get; set; } = "#FFFFFF";

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("backgroundEnabled")]
    public bool BackgroundEnabled { get; set; }
}

public sealed class HighlightRuleEntry
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [JsonPropertyName("foreground")]
    public string Foreground { get; set; } = "#FFFF00";

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("ruleType")]
    public string RuleType { get; set; } = "MatchHighlight";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class RecentSourceEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "File";

    [JsonPropertyName("lastAccessed")]
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}
