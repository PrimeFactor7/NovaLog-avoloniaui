using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class MessageFormatterTests
{
    // ── JSON ───────────────────────────────────────────────────────────

    [Fact]
    public void FormatJson_CompactObject_PrettyPrints()
    {
        var result = MessageFormatter.FormatJson("{\"a\":1,\"b\":\"hello\"}");
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
        Assert.Equal(SyntaxFlavor.Json, result[0].Flavor);
        Assert.Contains("\"a\": 1", result.First(l => l.Text.Contains("\"a\"")).Text);
    }

    [Fact]
    public void FormatJson_WithPrefix_PrefixOnFirstLine()
    {
        var result = MessageFormatter.FormatJson("INFO: {\"key\":\"val\"}");
        Assert.NotNull(result);
        Assert.StartsWith("INFO: {", result[0].Text);
        Assert.False(result[0].IsContinuation);
        Assert.True(result[1].IsContinuation);
    }

    [Fact]
    public void FormatJson_InvalidJson_ReturnsNull()
    {
        var result = MessageFormatter.FormatJson("{not json at all");
        Assert.Null(result);
    }

    [Fact]
    public void FormatJson_IndentSize4_Uses4Spaces()
    {
        var result = MessageFormatter.FormatJson("{\"a\":1}", indentSize: 4);
        Assert.NotNull(result);
        var keyLine = result.First(l => l.Text.Contains("\"a\""));
        Assert.StartsWith("    ", keyLine.Text);
    }

    [Fact]
    public void FormatJson_IndentSize2_Uses2Spaces()
    {
        var result = MessageFormatter.FormatJson("{\"a\":1}", indentSize: 2);
        Assert.NotNull(result);
        var keyLine = result.First(l => l.Text.Contains("\"a\""));
        Assert.StartsWith("  ", keyLine.Text);
        Assert.False(keyLine.Text.StartsWith("    ")); // not 4 spaces
    }

    [Fact]
    public void FormatJson_ExceedsMaxLines_Truncates()
    {
        var json = "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":1}}}},\"f\":2,\"g\":3}";
        var result = MessageFormatter.FormatJson(json, maxLines: 5);
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Contains("more lines", result.Last().Text);
        Assert.Equal(SyntaxFlavor.None, result.Last().Flavor);
    }

    [Fact]
    public void FormatJson_Array_PrettyPrints()
    {
        var result = MessageFormatter.FormatJson("[1,2,3]");
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
    }

    [Fact]
    public void FormatJson_EmptyString_ReturnsNull()
    {
        Assert.Null(MessageFormatter.FormatJson(""));
        Assert.Null(MessageFormatter.FormatJson(null!));
    }

    [Fact]
    public void FormatJson_NoJsonContent_ReturnsNull()
    {
        Assert.Null(MessageFormatter.FormatJson("just plain text"));
    }

    [Fact]
    public void FormatJson_NoTabsInOutput()
    {
        var result = MessageFormatter.FormatJson("{\"a\":{\"b\":1}}");
        Assert.NotNull(result);
        Assert.DoesNotContain(result, l => l.Text.Contains('\t'));
    }

    [Fact]
    public void FormatJson_NestedObject_IndentsCorrectly()
    {
        var result = MessageFormatter.FormatJson("{\"outer\":{\"inner\":1}}", indentSize: 2);
        Assert.NotNull(result);
        // Should have lines at depth 0, 1, and 2
        var innerLine = result.First(l => l.Text.Contains("\"inner\""));
        Assert.StartsWith("    ", innerLine.Text); // 2 levels × 2 spaces = 4 spaces
    }

    [Fact]
    public void FormatJson_SpaceAfterColon()
    {
        var result = MessageFormatter.FormatJson("{\"key\":\"value\"}");
        Assert.NotNull(result);
        var keyLine = result.First(l => l.Text.Contains("\"key\""));
        Assert.Contains(": ", keyLine.Text);
    }

    // ── SQL — Compact Regex Injection ─────────────────────────────

    [Fact]
    public void FormatSql_SimpleSelect_IndentedClauses()
    {
        var result = MessageFormatter.FormatSql("select id, name from users where id = 1");
        Assert.NotNull(result);

        var texts = result.Select(l => l.Text).ToList();
        // Major clauses indented 1 level (2 spaces), content on same line as keyword
        Assert.Equal("  SELECT id, name", texts[0]);
        Assert.Equal("  FROM users", texts[1]);
        Assert.Equal("  WHERE id = 1", texts[2]);
    }

    [Fact]
    public void FormatSql_SimpleSelect_FirstLineNotContinuation()
    {
        var result = MessageFormatter.FormatSql("select id from users");
        Assert.NotNull(result);
        Assert.False(result[0].IsContinuation); // SELECT line is first
        Assert.True(result[1].IsContinuation);   // FROM line is continuation
    }

    [Fact]
    public void FormatSql_KeywordsUppercased()
    {
        var result = MessageFormatter.FormatSql("select id from users where id = 1");
        Assert.NotNull(result);
        var allText = string.Join("\n", result.Select(l => l.Text));
        Assert.Contains("SELECT", allText);
        Assert.Contains("FROM", allText);
        Assert.Contains("WHERE", allText);
    }

    [Fact]
    public void FormatSql_AndOrIndentedUnderWhere()
    {
        var result = MessageFormatter.FormatSql(
            "select * from users where id = 1 and name = 'test' or age > 20");
        Assert.NotNull(result);

        var texts = result.Select(l => l.Text).ToList();
        // Major clauses indented 1 level, sub-clauses (AND/OR) indented 2 levels
        Assert.Equal("  SELECT *", texts[0]);
        Assert.Equal("  FROM users", texts[1]);
        Assert.Equal("  WHERE id = 1", texts[2]);
        Assert.Equal("    AND name = 'test'", texts[3]);
        Assert.Equal("    OR age > 20", texts[4]);
    }

    [Fact]
    public void FormatSql_JoinWithOn_StructuralIndentation()
    {
        var result = MessageFormatter.FormatSql(
            "select u.id from users u inner join orders o on u.id = o.user_id where o.total > 100");
        Assert.NotNull(result);

        var texts = result.Select(l => l.Text).ToList();
        // Major clauses indented 1 level, ON indented 2 levels
        Assert.Equal("  SELECT u.id", texts[0]);
        Assert.Equal("  FROM users u", texts[1]);
        Assert.Equal("  INNER JOIN orders o", texts[2]);
        Assert.Equal("    ON u.id = o.user_id", texts[3]);
        Assert.Equal("  WHERE o.total > 100", texts[4]);
    }

    [Fact]
    public void FormatSql_WithPrefix_PrefixOnLine1()
    {
        var result = MessageFormatter.FormatSql("Executing query: select id from users");
        Assert.NotNull(result);

        Assert.Equal("Executing query:", result[0].Text);
        Assert.False(result[0].IsContinuation);
        Assert.StartsWith("  SELECT", result[1].Text); // indented under prefix
        Assert.True(result[1].IsContinuation); // SQL lines are continuation after prefix
    }

    [Fact]
    public void FormatSql_ComplexQuery_CompactHierarchy()
    {
        var sql = "select u.name, count(o.id) from users u " +
                  "left join orders o on u.id = o.user_id " +
                  "where u.active = 1 and o.status = 'complete' " +
                  "group by u.name order by count(o.id) desc";
        var result = MessageFormatter.FormatSql(sql);
        Assert.NotNull(result);

        var texts = result.Select(l => l.Text).ToList();

        // Major clauses indented 1 level with content on same line
        Assert.StartsWith("  SELECT", texts[0]);
        Assert.True(texts.Any(t => t.StartsWith("  LEFT JOIN")));
        Assert.True(texts.Any(t => t.StartsWith("  WHERE")));
        Assert.True(texts.Any(t => t.StartsWith("  GROUP BY")));
        Assert.True(texts.Any(t => t.StartsWith("  ORDER BY")));

        // Sub-clauses indented 2 levels
        var onLine = texts.First(t => t.TrimStart().StartsWith("ON"));
        Assert.StartsWith("    ", onLine);
        var andLine = texts.First(t => t.TrimStart().StartsWith("AND"));
        Assert.StartsWith("    ", andLine);
    }

    [Fact]
    public void FormatSql_AllFlavorIsSql()
    {
        var result = MessageFormatter.FormatSql("select id from users where id = 1");
        Assert.NotNull(result);
        Assert.All(result, l => Assert.Equal(SyntaxFlavor.Sql, l.Flavor));
    }

    [Fact]
    public void FormatSql_EmptyString_ReturnsNull()
    {
        Assert.Null(MessageFormatter.FormatSql(""));
        Assert.Null(MessageFormatter.FormatSql(null!));
    }

    [Fact]
    public void FormatSql_NoSqlKeywords_ReturnsNull()
    {
        Assert.Null(MessageFormatter.FormatSql("just plain text no sql here"));
    }

    [Fact]
    public void FormatSql_ExceedsMaxLines_Truncates()
    {
        var sql = "select a from b where c = 1 and d = 2 and e = 3 and f = 4 and g = 5 and h = 6";
        var result = MessageFormatter.FormatSql(sql, maxLines: 3);
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains("more lines", result.Last().Text);
    }

    [Fact]
    public void FormatSql_IndentSize4_Uses4Spaces()
    {
        var result = MessageFormatter.FormatSql(
            "select id from users where id = 1 and name = 'test'", indentSize: 4);
        Assert.NotNull(result);
        // AND line should be indented with 8 spaces (2 levels × 4 spaces)
        var andLine = result.First(l => l.Text.TrimStart().StartsWith("AND"));
        Assert.StartsWith("        ", andLine.Text);
    }

    [Fact]
    public void FormatSql_NormalizesExistingNewlines()
    {
        var result = MessageFormatter.FormatSql("select id\nfrom users\nwhere id = 1");
        Assert.NotNull(result);
        // Newlines normalized to spaces, then reformatted
        var texts = result.Select(l => l.Text).ToList();
        Assert.StartsWith("  SELECT", texts[0]);
        Assert.StartsWith("  FROM", texts[1]);
        Assert.StartsWith("  WHERE", texts[2]);
    }

    // ── TruncateLines ─────────────────────────────────────────────────

    [Fact]
    public void TruncateLines_UnderLimit_ReturnsOriginal()
    {
        var lines = new List<FormattedSubLine>
        {
            new() { Text = "line1" },
            new() { Text = "line2" },
        };
        var result = MessageFormatter.TruncateLines(lines, 10);
        Assert.Same(lines, result);
    }

    [Fact]
    public void TruncateLines_OverLimit_AddsIndicator()
    {
        var lines = Enumerable.Range(0, 20)
            .Select(i => new FormattedSubLine { Text = $"line{i}" })
            .ToList();
        var result = MessageFormatter.TruncateLines(lines, 5);
        Assert.Equal(5, result.Count);
        Assert.Contains("16 more lines", result.Last().Text);
        Assert.True(result.Last().IsContinuation);
    }

    // ── Format dispatch ───────────────────────────────────────────────

    [Fact]
    public void Format_JsonDisabled_ReturnsNull()
    {
        var result = MessageFormatter.Format("{\"a\":1}", SyntaxFlavor.Json,
            jsonEnabled: false, sqlEnabled: true);
        Assert.Null(result);
    }

    [Fact]
    public void Format_SqlDisabled_ReturnsNull()
    {
        var result = MessageFormatter.Format("SELECT 1", SyntaxFlavor.Sql,
            jsonEnabled: true, sqlEnabled: false);
        Assert.Null(result);
    }

    [Fact]
    public void Format_PlainText_ReturnsNull()
    {
        var result = MessageFormatter.Format("hello world", SyntaxFlavor.None,
            jsonEnabled: true, sqlEnabled: true);
        Assert.Null(result);
    }

    [Fact]
    public void Format_StackTrace_ReturnsNull()
    {
        var result = MessageFormatter.Format("at Foo.Bar()", SyntaxFlavor.StackTrace,
            jsonEnabled: true, sqlEnabled: true);
        Assert.Null(result);
    }

    [Fact]
    public void Format_JsonEnabled_Formats()
    {
        var result = MessageFormatter.Format("{\"x\":42}", SyntaxFlavor.Json,
            jsonEnabled: true, sqlEnabled: false);
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
    }

    [Fact]
    public void Format_SqlEnabled_Formats()
    {
        var result = MessageFormatter.Format(
            "select id from users where id = 1", SyntaxFlavor.Sql,
            jsonEnabled: false, sqlEnabled: true);
        Assert.NotNull(result);
        Assert.True(result.Count > 1);
    }
}
