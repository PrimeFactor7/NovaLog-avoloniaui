using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class SearchEngineTests
{
    // ── Literal mode ──────────────────────────────────────────────

    [Fact]
    public void Literal_CaseSensitive_MatchesExact()
    {
        var matcher = SearchEngine.Compile("Error", SearchMode.Literal, caseSensitive: true);

        Assert.True(matcher.IsMatch("An Error occurred"));
        Assert.False(matcher.IsMatch("An error occurred"));
    }

    [Fact]
    public void Literal_CaseInsensitive_MatchesBothCases()
    {
        var matcher = SearchEngine.Compile("error", SearchMode.Literal, caseSensitive: false);

        Assert.True(matcher.IsMatch("An Error occurred"));
        Assert.True(matcher.IsMatch("An ERROR occurred"));
        Assert.True(matcher.IsMatch("An error occurred"));
    }

    [Fact]
    public void Literal_FindMatches_ReturnsAllPositions()
    {
        var matcher = SearchEngine.Compile("ab", SearchMode.Literal, caseSensitive: true);
        var matches = matcher.FindMatches("abcabc").ToList();

        Assert.Equal(2, matches.Count);
        Assert.Equal((0, 2), matches[0]);
        Assert.Equal((3, 2), matches[1]);
    }

    [Fact]
    public void Literal_NoMatch_ReturnsEmpty()
    {
        var matcher = SearchEngine.Compile("xyz", SearchMode.Literal, caseSensitive: true);

        Assert.False(matcher.IsMatch("hello world"));
        Assert.Empty(matcher.FindMatches("hello world"));
    }

    // ── Wildcard mode ─────────────────────────────────────────────

    [Fact]
    public void Wildcard_StarMatchesAnything()
    {
        var matcher = SearchEngine.Compile("err*", SearchMode.Wildcard, caseSensitive: false);

        Assert.True(matcher.IsMatch("error occurred"));
        Assert.True(matcher.IsMatch("erroneous"));
        Assert.False(matcher.IsMatch("no match"));
    }

    [Fact]
    public void Wildcard_QuestionMarkMatchesSingleChar()
    {
        var matcher = SearchEngine.Compile("err?r", SearchMode.Wildcard, caseSensitive: false);

        Assert.True(matcher.IsMatch("error"));
        Assert.True(matcher.IsMatch("errXr"));
        Assert.True(matcher.IsMatch("errrr"));
        Assert.False(matcher.IsMatch("er_r"));
    }

    [Fact]
    public void Wildcard_SpecialCharsEscaped()
    {
        var matcher = SearchEngine.Compile("file.log", SearchMode.Wildcard, caseSensitive: true);

        Assert.True(matcher.IsMatch("file.log"));
        Assert.False(matcher.IsMatch("fileXlog"));
    }

    // ── Regex mode ────────────────────────────────────────────────

    [Fact]
    public void Regex_PatternMatches()
    {
        var matcher = SearchEngine.Compile(@"\berror\b", SearchMode.Regex, caseSensitive: false);

        Assert.True(matcher.IsMatch("An error occurred"));
        Assert.False(matcher.IsMatch("An erroneous thing"));
    }

    [Fact]
    public void Regex_FindMatches_ReturnsPositions()
    {
        var matcher = SearchEngine.Compile(@"\d+", SearchMode.Regex, caseSensitive: true);
        var matches = matcher.FindMatches("abc 123 def 456").ToList();

        Assert.Equal(2, matches.Count);
        Assert.Equal((4, 3), matches[0]);
        Assert.Equal((12, 3), matches[1]);
    }

    [Fact]
    public void Regex_CaseSensitive_RespectedCorrectly()
    {
        var sensitive = SearchEngine.Compile("Error", SearchMode.Regex, caseSensitive: true);
        var insensitive = SearchEngine.Compile("Error", SearchMode.Regex, caseSensitive: false);

        Assert.False(sensitive.IsMatch("error"));
        Assert.True(insensitive.IsMatch("error"));
    }

    // ── TryCompile ────────────────────────────────────────────────

    [Fact]
    public void TryCompile_InvalidRegex_ReturnsNull()
    {
        var matcher = SearchEngine.TryCompile("[invalid", SearchMode.Regex, caseSensitive: true);
        Assert.Null(matcher);
    }

    [Fact]
    public void TryCompile_ValidPattern_ReturnsMatcher()
    {
        var matcher = SearchEngine.TryCompile("hello", SearchMode.Literal, caseSensitive: true);
        Assert.NotNull(matcher);
        Assert.True(matcher!.IsMatch("hello world"));
    }

    // ── Thread safety ─────────────────────────────────────────────

    [Fact]
    public async Task CompiledMatcher_IsThreadSafe()
    {
        var matcher = SearchEngine.Compile(@"\berror\b", SearchMode.Regex, caseSensitive: false);
        var tasks = Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    Assert.True(matcher.IsMatch("An error occurred"));
                    Assert.False(matcher.IsMatch("No problems here"));
                }
            }));

        await Task.WhenAll(tasks);
    }
}
