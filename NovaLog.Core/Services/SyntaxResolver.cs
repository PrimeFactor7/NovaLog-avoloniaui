using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Picks the best syntax highlighter flavor for a log message line.
/// Uses cheap Span-based substring checks (no regex, no allocations).
/// Priority: StackTrace > JSON > SQL > None.
/// </summary>
public static class SyntaxResolver
{
    private static readonly string[] SqlTriggers =
        ["SELECT ", "INSERT ", "UPDATE ", "DELETE ", "EXEC ", "EXECUTE "];

    public static SyntaxFlavor Detect(string message)
    {
        if (string.IsNullOrEmpty(message))
            return SyntaxFlavor.None;

        var span = message.AsSpan();

        if (IsStackTrace(span))
            return SyntaxFlavor.StackTrace;
        if (LooksLikeJson(span))
            return SyntaxFlavor.Json;
        if (IsSql(span))
            return SyntaxFlavor.Sql;

        return SyntaxFlavor.None;
    }

    private static bool IsStackTrace(ReadOnlySpan<char> span)
    {
        int atIdx = span.IndexOf("at ".AsSpan(), StringComparison.Ordinal);
        if (atIdx >= 0)
        {
            var after = span[(atIdx + 3)..];
            if (after.Contains('.') && after.Contains('('))
                return true;
        }

        int idx = span.IndexOf("Exception".AsSpan(), StringComparison.Ordinal);
        if (idx >= 0)
        {
            int endIdx = idx + "Exception".Length;

            bool followedByColon = endIdx < span.Length && span[endIdx] == ':';
            bool atEnd = endIdx == span.Length;

            if (followedByColon || atEnd)
            {
                if (idx > 0 && (char.IsLetter(span[idx - 1]) || span[idx - 1] == '.'))
                    return true;
                if (idx == 0 && followedByColon)
                    return true;
            }
        }

        return false;
    }

    private static bool LooksLikeJson(ReadOnlySpan<char> span)
    {
        int braceIdx = span.IndexOf('{');
        if (braceIdx < 0) return false;

        if (braceIdx + 1 < span.Length)
        {
            var afterBrace = span[(braceIdx + 1)..];
            if (afterBrace.Length > 0 && char.IsDigit(afterBrace[0]))
            {
                int closeIdx = afterBrace.IndexOf('}');
                if (closeIdx >= 0 && closeIdx < 10)
                {
                    bool isQuantifier = true;
                    for (int i = 0; i < closeIdx; i++)
                    {
                        if (!char.IsDigit(afterBrace[i]) && afterBrace[i] != ',')
                        { isQuantifier = false; break; }
                    }
                    if (isQuantifier)
                    {
                        var remaining = braceIdx + 1 + closeIdx + 1 < span.Length
                            ? span[(braceIdx + 1 + closeIdx + 1)..]
                            : ReadOnlySpan<char>.Empty;
                        if (!remaining.Contains('{'))
                            return false;
                    }
                }
            }
        }
        return true;
    }

    private static bool IsSql(ReadOnlySpan<char> span)
    {
        foreach (var keyword in SqlTriggers)
        {
            if (span.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
