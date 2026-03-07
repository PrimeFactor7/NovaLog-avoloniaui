using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class JsonHighlightTokenizerTests
{
    // ── Basic tokenization ───────────────────────────────────────

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_SimpleBraces_ReturnsPunctuation()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{}");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(JsonHighlightKind.Punctuation, tokens[0].Kind);
        Assert.Equal(JsonHighlightKind.Punctuation, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_QuotedKey_ReturnsKey()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{\"name\": \"Alice\"}");

        var keyToken = tokens.First(t => t.Kind == JsonHighlightKind.Key);
        Assert.Equal("\"name\"", ExtractText("{\"name\": \"Alice\"}", keyToken));
    }

    [Fact]
    public void Tokenize_QuotedString_ReturnsString()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{\"name\": \"Alice\"}");

        var strToken = tokens.First(t => t.Kind == JsonHighlightKind.String);
        Assert.Equal("\"Alice\"", ExtractText("{\"name\": \"Alice\"}", strToken));
    }

    [Fact]
    public void Tokenize_Number_ReturnsNumber()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{\"age\": 42}");

        var numToken = tokens.First(t => t.Kind == JsonHighlightKind.Number);
        Assert.Equal("42", ExtractText("{\"age\": 42}", numToken));
    }

    [Fact]
    public void Tokenize_Boolean_ReturnsBool()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{\"active\": true}");

        var boolToken = tokens.First(t => t.Kind == JsonHighlightKind.Bool);
        Assert.Equal("true", ExtractText("{\"active\": true}", boolToken));
    }

    [Fact]
    public void Tokenize_Null_ReturnsBool()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{\"value\": null}");

        var nullToken = tokens.First(t => t.Kind == JsonHighlightKind.Bool);
        Assert.Equal("null", ExtractText("{\"value\": null}", nullToken));
    }

    // ── Prefix handling ──────────────────────────────────────────

    [Fact]
    public void Tokenize_WithPrefix_SplitsPrefix()
    {
        var msg = "Payload: {\"key\": 1}";
        int jsonStart = msg.IndexOf('{');
        var tokens = JsonHighlightTokenizer.Tokenize(msg, jsonStart);

        var prefix = tokens.First(t => t.Kind == JsonHighlightKind.Prefix);
        Assert.Equal("Payload: ", ExtractText(msg, prefix));
    }

    // ── Unquoted keys ────────────────────────────────────────────

    [Fact]
    public void Tokenize_UnquotedKey_DetectsAsKey()
    {
        var tokens = JsonHighlightTokenizer.Tokenize("{broadcast: true}");

        var keyToken = tokens.First(t => t.Kind == JsonHighlightKind.Key);
        // Unquoted keys include the colon
        Assert.Contains("broadcast", ExtractText("{broadcast: true}", keyToken));
    }

    // ── Nested JSON ──────────────────────────────────────────────

    [Fact]
    public void Tokenize_NestedObject_TokenizesAll()
    {
        var msg = "{\"outer\": {\"inner\": 42}}";
        var tokens = JsonHighlightTokenizer.Tokenize(msg);

        Assert.True(tokens.Count >= 7); // braces, keys, colon, number
        Assert.Contains(tokens, t => t.Kind == JsonHighlightKind.Key);
        Assert.Contains(tokens, t => t.Kind == JsonHighlightKind.Number);
    }

    // ── Escaped strings ──────────────────────────────────────────

    [Fact]
    public void Tokenize_EscapedQuote_HandledCorrectly()
    {
        var msg = "{\"msg\": \"He said \\\"hello\\\"\"}";
        var tokens = JsonHighlightTokenizer.Tokenize(msg);

        var strings = tokens.Where(t => t.Kind == JsonHighlightKind.String).ToList();
        Assert.Single(strings);
    }

    // ── Decimal numbers ──────────────────────────────────────────

    [Fact]
    public void Tokenize_DecimalNumber_RecognizesWholeSpan()
    {
        var msg = "{\"price\": 19.99}";
        var tokens = JsonHighlightTokenizer.Tokenize(msg);

        var num = tokens.First(t => t.Kind == JsonHighlightKind.Number);
        Assert.Equal("19.99", ExtractText(msg, num));
    }

    [Fact]
    public void Tokenize_ScientificNotation_RecognizesWholeSpan()
    {
        var msg = "{\"val\": 1.5e10}";
        var tokens = JsonHighlightTokenizer.Tokenize(msg);

        var num = tokens.First(t => t.Kind == JsonHighlightKind.Number);
        Assert.Equal("1.5e10", ExtractText(msg, num));
    }

    // ── Token coverage ───────────────────────────────────────────

    [Fact]
    public void Tokenize_CoversEntireInput()
    {
        var msg = "{\"name\": \"Alice\", \"age\": 30, \"active\": true}";
        var tokens = JsonHighlightTokenizer.Tokenize(msg);

        // Ensure no gaps — all characters are accounted for
        int totalCovered = tokens.Sum(t => t.Length);
        Assert.Equal(msg.Length, totalCovered);
    }

    // ── Helper ───────────────────────────────────────────────────

    private static string ExtractText(string source, (int Start, int Length, JsonHighlightKind Kind) token)
        => source.Substring(token.Start, token.Length);
}
