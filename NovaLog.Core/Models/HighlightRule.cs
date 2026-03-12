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

    private volatile Regex? _compiled;

    /// <summary>Returns the compiled regex, or null if the pattern is invalid.</summary>
    public Regex? CompiledRegex
    {
        get
        {
            var cached = _compiled;
            if (cached != null) return cached;
            if (string.IsNullOrWhiteSpace(Pattern)) return null;
            try
            {
                cached = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _compiled = cached;
                return cached;
            }
            catch (RegexParseException) { return null; }
        }
    }

    /// <summary>Call after changing Pattern to recompile.</summary>
    public void Invalidate() => _compiled = null;
}
