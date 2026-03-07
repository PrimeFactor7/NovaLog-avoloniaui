using System.Text.RegularExpressions;

namespace NovaLog.Core.Services;

public enum SearchMode
{
    Literal,
    Wildcard,
    Regex
}

/// <summary>
/// Immutable, thread-safe compiled matcher. Once created, can be used from any thread.
/// </summary>
public sealed class CompiledMatcher
{
    private readonly Func<string, bool> _isMatch;
    private readonly Regex? _regex;
    private readonly string? _literal;
    private readonly StringComparison _comparison;

    internal CompiledMatcher(Func<string, bool> isMatch, Regex? regex,
        string? literal = null, StringComparison comparison = default)
    {
        _isMatch = isMatch;
        _regex = regex;
        _literal = literal;
        _comparison = comparison;
    }

    /// <summary>Returns true if the input string matches the pattern.</summary>
    public bool IsMatch(string input) => _isMatch(input);

    /// <summary>
    /// Returns all match positions within the input string.
    /// Used by highlight rendering to know where to paint.
    /// </summary>
    public IEnumerable<(int Index, int Length)> FindMatches(string input)
    {
        if (_regex != null)
        {
            foreach (Match m in _regex.Matches(input))
                yield return (m.Index, m.Length);
        }
        else if (_literal != null)
        {
            int pos = 0;
            while (pos < input.Length)
            {
                int idx = input.IndexOf(_literal, pos, _comparison);
                if (idx < 0) break;
                yield return (idx, _literal.Length);
                pos = idx + _literal.Length;
            }
        }
    }
}

/// <summary>
/// Factory for creating compiled matchers from patterns in different search modes.
/// </summary>
public static class SearchEngine
{
    /// <summary>
    /// Compiles a pattern into a reusable, thread-safe matcher.
    /// Throws ArgumentException or RegexParseException if the pattern is invalid.
    /// </summary>
    public static CompiledMatcher Compile(string pattern, SearchMode mode, bool caseSensitive)
    {
        return mode switch
        {
            SearchMode.Literal => CompileLiteral(pattern, caseSensitive),
            SearchMode.Wildcard => CompileWildcard(pattern, caseSensitive),
            SearchMode.Regex => CompileRegex(pattern, caseSensitive),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Non-throwing variant — returns null if the pattern is invalid.
    /// Preferred for live-typing scenarios.
    /// </summary>
    public static CompiledMatcher? TryCompile(string pattern, SearchMode mode, bool caseSensitive)
    {
        try { return Compile(pattern, mode, caseSensitive); }
        catch (Exception ex) when (ex is ArgumentException or RegexParseException) { return null; }
    }

    private static CompiledMatcher CompileLiteral(string pattern, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return new CompiledMatcher(
            input => input.Contains(pattern, comparison),
            regex: null,
            literal: pattern,
            comparison: comparison);
    }

    private static CompiledMatcher CompileWildcard(string pattern, bool caseSensitive)
    {
        // Escape regex special chars, then convert wildcard tokens
        var regexPattern = Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        var opts = RegexOptions.Compiled | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var regex = new Regex(regexPattern, opts);
        return new CompiledMatcher(input => regex.IsMatch(input), regex);
    }

    private static CompiledMatcher CompileRegex(string pattern, bool caseSensitive)
    {
        var opts = RegexOptions.Compiled | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var regex = new Regex(pattern, opts);
        return new CompiledMatcher(input => regex.IsMatch(input), regex);
    }
}
