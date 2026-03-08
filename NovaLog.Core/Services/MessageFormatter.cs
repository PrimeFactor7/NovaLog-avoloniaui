using System.Text.Json;
using System.Text.RegularExpressions;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// A single line produced by auto-formatting (JSON pretty-print, SQL clause splitting).
/// </summary>
public sealed class FormattedSubLine
{
    public string Text { get; init; } = "";
    public SyntaxFlavor Flavor { get; init; }
    public bool IsContinuation { get; init; }
}

/// <summary>
/// Auto-formats compact JSON / SQL messages into indented multiline text
/// for "Span Lines" grid mode. Pure string transforms — no UI dependencies.
/// </summary>
public static class MessageFormatter
{
    // Major clauses that start at column 0 on their own line
    private static readonly Regex SqlMajorClausePattern = new(
        @"\b(SELECT|FROM|WHERE|(?:LEFT|RIGHT|INNER|OUTER|CROSS|FULL)\s+JOIN|JOIN|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|OFFSET|UNION(?:\s+ALL)?|SET|VALUES|INTO|INSERT\s+INTO|UPDATE|DELETE\s+FROM|DELETE|EXEC(?:UTE)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Sub-clauses that get indented (AND, OR, ON)
    private static readonly Regex SqlSubClausePattern = new(
        @"\b(AND|OR|ON)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // All SQL keywords for uppercasing
    private static readonly Regex SqlKeywordPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|CROSS|FULL|ON|AND|OR|NOT|IN|INTO|VALUES|SET|CREATE|DROP|ALTER|TABLE|INDEX|ORDER|BY|GROUP|HAVING|LIMIT|OFFSET|AS|DISTINCT|COUNT|SUM|AVG|MIN|MAX|BETWEEN|LIKE|IS|NULL|EXISTS|UNION|ALL|CASE|WHEN|THEN|ELSE|END|EXEC|EXECUTE|TOP|ASC|DESC|WITH|USING)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Detects the start of SQL in a log message
    private static readonly Regex SqlStartPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|EXEC(?:UTE)?|WITH)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Format a message according to its flavor. Returns null if no formatting was applied
    /// (e.g. disabled, or plain text flavor, or parse failure).
    /// </summary>
    public static List<FormattedSubLine>? Format(
        string message, SyntaxFlavor flavor,
        bool jsonEnabled, bool sqlEnabled,
        int indentSize = 2, int maxLines = 50)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        return flavor switch
        {
            SyntaxFlavor.Json when jsonEnabled => FormatJson(message, indentSize, maxLines),
            SyntaxFlavor.Sql when sqlEnabled => FormatSql(message, indentSize, maxLines),
            _ => null,
        };
    }

    /// <summary>
    /// Pretty-print a JSON message. Finds the first { or [ and re-serializes with indentation.
    /// Text before the JSON becomes a prefix on the first line.
    /// Returns null if JSON parsing fails.
    /// </summary>
    public static List<FormattedSubLine>? FormatJson(string text, int indentSize = 2, int maxLines = 50)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        int braceIdx = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' || text[i] == '[')
            {
                braceIdx = i;
                break;
            }
        }
        if (braceIdx < 0) return null;

        string prefix = braceIdx > 0 ? text[..braceIdx] : "";
        string jsonPart = text[braceIdx..];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonPart, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException)
        {
            return null;
        }

        string pretty;
        using (doc)
        {
            pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true,
                IndentSize = indentSize,
            });
        }

        // Safety net: convert any tabs to spaces for consistent grid rendering
        if (pretty.Contains('\t'))
            pretty = pretty.Replace("\t", new string(' ', indentSize));

        var rawLines = pretty.Split('\n');
        var result = new List<FormattedSubLine>(rawLines.Length);
        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i].TrimEnd('\r');
            if (i == 0 && prefix.Length > 0)
                line = prefix + line;

            result.Add(new FormattedSubLine
            {
                Text = line,
                Flavor = SyntaxFlavor.Json,
                IsContinuation = i > 0,
            });
        }

        return TruncateLines(result, maxLines);
    }

    /// <summary>
    /// Format SQL via regex injection: major clauses at column 0 with content
    /// on the same line, AND/OR/ON as indented sub-clause lines.
    /// Prefix text (e.g. "Executing query:") goes on its own line.
    /// </summary>
    public static List<FormattedSubLine>? FormatSql(string text, int indentSize = 2, int maxLines = 50)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        string indent = new(' ', indentSize);

        // 1. Normalize: strip existing newlines to spaces
        string normalized = text.Replace("\r\n", " ").Replace("\n", " ");

        // 2. Extract prefix (text before first SQL keyword)
        string prefix = "";
        var sqlStart = SqlStartPattern.Match(normalized);
        if (sqlStart.Success && sqlStart.Index > 0)
        {
            prefix = normalized[..sqlStart.Index].TrimEnd();
            normalized = normalized[sqlStart.Index..];
        }

        // 3. Uppercase all SQL keywords
        string upper = SqlKeywordPattern.Replace(normalized, m => m.Value.ToUpperInvariant());

        // 4. Inject newline + indent before each major clause (content stays on same line as keyword)
        string formatted = SqlMajorClausePattern.Replace(upper, m => "\n" + indent + m.Value);

        // 5. Inject newline + double indent before each sub-clause (AND/OR/ON)
        formatted = SqlSubClausePattern.Replace(formatted, m => "\n" + indent + indent + m.Value);

        // 6. Split on newlines, trim trailing whitespace, skip empty
        var rawLines = formatted.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<FormattedSubLine>();
        bool hasPrefix = prefix.Length > 0;

        if (hasPrefix)
        {
            result.Add(new FormattedSubLine
            {
                Text = prefix,
                Flavor = SyntaxFlavor.Sql,
                IsContinuation = false,
            });
        }

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].TrimEnd();
            if (string.IsNullOrEmpty(line))
                continue;

            result.Add(new FormattedSubLine
            {
                Text = line,
                Flavor = SyntaxFlavor.Sql,
                IsContinuation = hasPrefix || i > 0,
            });
        }

        // Don't format if it would be just 1 line (no expansion benefit)
        if (result.Count <= 1)
            return null;

        return TruncateLines(result, maxLines);
    }

    /// <summary>
    /// Truncate to maxLines. If truncated, the last line becomes an indicator.
    /// </summary>
    public static List<FormattedSubLine> TruncateLines(List<FormattedSubLine> lines, int maxLines)
    {
        if (maxLines <= 0 || lines.Count <= maxLines)
            return lines;

        int remaining = lines.Count - (maxLines - 1);
        var truncated = new List<FormattedSubLine>(maxLines);
        for (int i = 0; i < maxLines - 1; i++)
            truncated.Add(lines[i]);

        truncated.Add(new FormattedSubLine
        {
            Text = $"... ({remaining} more lines)",
            Flavor = SyntaxFlavor.None,
            IsContinuation = true,
        });

        return truncated;
    }
}
