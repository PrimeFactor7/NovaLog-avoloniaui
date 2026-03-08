using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// State-machine tokenizer for JSON-like log content. No regex.
/// </summary>
public static class JsonHighlightTokenizer
{
    private const string BoolTrue = "true";
    private const string BoolFalse = "false";
    private const string BoolNull = "null";
    private const string BoolUndefined = "undefined";

    public static List<(int Start, int Length, JsonHighlightKind Kind)> Tokenize(string message, int jsonStart = 0)
    {
        var spans = new List<(int Start, int Length, JsonHighlightKind Kind)>();
        if (message.Length == 0) return spans;

        // If jsonStart was not explicitly provided, find first brace
        if (jsonStart == 0)
        {
            jsonStart = message.IndexOfAny(['{', '[', '}', ']']);
            if (jsonStart < 0) jsonStart = 0;
            else if (jsonStart > 0)
            {
                // Check if the prefix before the first brace looks like JSON content
                // (e.g., "  commandParams: " on a continuation line).
                // If it only contains whitespace, word chars, colons, commas, quotes — start from beginning.
                var prefix = message.AsSpan(0, jsonStart);
                bool looksLikeJsonContent = true;
                for (int j = 0; j < prefix.Length; j++)
                {
                    char c = prefix[j];
                    if (c == ' ' || c == '\t' || c == ',' || c == ':' || c == '"' || c == '\''
                        || char.IsLetterOrDigit(c) || c == '_' || c == '$')
                        continue;
                    looksLikeJsonContent = false;
                    break;
                }
                if (looksLikeJsonContent)
                    jsonStart = 0;
            }
        }

        if (jsonStart > 0)
            spans.Add((0, jsonStart, JsonHighlightKind.Prefix));

        var slice = message.AsSpan(jsonStart);
        int i = 0;
        while (i < slice.Length)
        {
            var (start, length, kind) = ReadNext(slice, i);
            if (length <= 0) break;
            spans.Add((jsonStart + start, length, kind));
            i = start + length;
        }
        return spans;
    }

    private static (int start, int length, JsonHighlightKind kind) ReadNext(ReadOnlySpan<char> s, int i)
    {
        if (i >= s.Length) return (i, 0, JsonHighlightKind.Gap);

        char c = s[i];

        if (c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == ':')
            return (i, 1, JsonHighlightKind.Punctuation);

        if (c == '"') return ReadQuotedString(s, i, '"');
        if (c == '\'') return ReadQuotedString(s, i, '\'');
        if (IsNumberStart(s, i)) return ReadNumber(s, i);
        if (IsWordStart(c)) return ReadWord(s, i);

        int gapStart = i;
        while (i < s.Length && !IsStructural(s[i]) && !IsQuote(s[i]) && !IsNumberStart(s, i) && !IsWordStart(s[i]))
            i++;
        return (gapStart, i - gapStart, JsonHighlightKind.Gap);
    }

    private static (int start, int length, JsonHighlightKind kind) ReadQuotedString(ReadOnlySpan<char> s, int i, char quote)
    {
        int start = i;
        i++;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\')
            {
                if (i + 1 < s.Length && s[i + 1] == 'u')
                    i += 6; // \uXXXX
                else
                    i += 2;
                if (i > s.Length) i = s.Length;
                continue;
            }
            if (c == quote)
            {
                i++;
                int j = i;
                while (j < s.Length && IsJsonWhitespace(s[j]))
                {
                    j++;
                    if (j < s.Length && s[j - 1] == '\r' && s[j] == '\n') j++;
                }
                if (j < s.Length && s[j] == ':')
                    return (start, i - start, JsonHighlightKind.Key);
                return (start, i - start, JsonHighlightKind.String);
            }
            i++;
        }
        return (start, s.Length - start, JsonHighlightKind.String);
    }

    private static bool IsQuote(char c) => c == '"' || c == '\'';
    private static bool IsJsonWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';
    private static bool IsStructural(char c) => c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == ':';

    private static bool IsNumberStart(ReadOnlySpan<char> s, int i)
    {
        if (i >= s.Length) return false;
        char c = s[i];
        if (c >= '0' && c <= '9') return true;
        if (c == '.' && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '9') return true;
        if ((c == '+' || c == '-') && i + 1 < s.Length && (s[i + 1] >= '0' && s[i + 1] <= '9' || s[i + 1] == '.')) return true;
        return false;
    }

    private static (int start, int length, JsonHighlightKind kind) ReadNumber(ReadOnlySpan<char> s, int i)
    {
        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i < s.Length && s[i] == '.' && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '9')
        {
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        return (start, i - start, JsonHighlightKind.Number);
    }

    private static bool IsWordStart(char c) =>
        c == '_' || c == '$' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static (int start, int length, JsonHighlightKind kind) ReadWord(ReadOnlySpan<char> s, int i)
    {
        int start = i;
        while (i < s.Length && (s[i] == '_' || s[i] == '$' || char.IsLetterOrDigit(s[i]))) i++;
        int len = i - start;
        var word = s.Slice(start, len);

        int j = i;
        while (j < s.Length && IsJsonWhitespace(s[j]))
        {
            j++;
            if (j < s.Length && s[j - 1] == '\r' && s[j] == '\n') j++;
        }
        if (j < s.Length && s[j] == ':')
        {
            int end = j + 1;
            return (start, end - start, JsonHighlightKind.Key);
        }

        if (Matches(word, BoolTrue) || Matches(word, BoolFalse) || Matches(word, BoolNull) || Matches(word, BoolUndefined))
            return (start, len, JsonHighlightKind.Bool);

        return (start, len, JsonHighlightKind.Gap);
    }

    private static bool Matches(ReadOnlySpan<char> a, string b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i])) return false;
        return true;
    }
}
