using System.Text.RegularExpressions;

namespace NovaLog.Core.Models;

public enum HighlightRuleType
{
    /// <summary>Only the matched characters are highlighted.</summary>
    MatchHighlight,

    /// <summary>The entire row background/foreground changes.</summary>
    LineHighlight
}

/// <summary>
/// A user-defined highlight rule: regex pattern with foreground/background hex colors.
/// </summary>
public sealed class HighlightRule
{
    public string Pattern { get; set; } = "";
    public string ForegroundHex { get; set; } = "#FFFF00";
    public string? BackgroundHex { get; set; }
    public bool Enabled { get; set; } = true;
    public HighlightRuleType RuleType { get; set; } = HighlightRuleType.MatchHighlight;

    private Regex? _compiled;

    /// <summary>Returns the compiled regex, or null if the pattern is invalid.</summary>
    public Regex? CompiledRegex
    {
        get
        {
            if (_compiled != null) return _compiled;
            if (string.IsNullOrWhiteSpace(Pattern)) return null;
            try
            {
                _compiled = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                return _compiled;
            }
            catch (RegexParseException) { return null; }
        }
    }

    /// <summary>Call after changing Pattern to recompile.</summary>
    public void Invalidate() => _compiled = null;
}
