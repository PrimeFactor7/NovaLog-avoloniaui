using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class SyntaxResolverTests
{
    // ── None flavor ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("Application started")]
    [InlineData("User logged in successfully")]
    [InlineData("Processing request #42")]
    public void Detect_PlainText_ReturnsNone(string message)
    {
        Assert.Equal(SyntaxFlavor.None, SyntaxResolver.Detect(message));
    }

    [Fact]
    public void Detect_Null_ReturnsNone()
    {
        Assert.Equal(SyntaxFlavor.None, SyntaxResolver.Detect(null!));
        Assert.Equal(SyntaxFlavor.None, SyntaxResolver.Detect(""));
    }

    // ── JSON detection ───────────────────────────────────────────

    [Theory]
    [InlineData("{\"key\": \"value\"}")]
    [InlineData("{key: value}")]
    [InlineData("Response: {\"status\": 200}")]
    [InlineData("{\"nested\": {\"deep\": true}}")]
    public void Detect_JsonContent_ReturnsJson(string message)
    {
        Assert.Equal(SyntaxFlavor.Json, SyntaxResolver.Detect(message));
    }

    [Fact]
    public void Detect_RegexQuantifier_NotJson()
    {
        // {3} is a regex quantifier, not JSON
        Assert.Equal(SyntaxFlavor.None, SyntaxResolver.Detect("pattern matched {3} times"));
    }

    [Fact]
    public void Detect_RegexQuantifierFollowedByJson_ReturnsJson()
    {
        // Brace after quantifier means there's real JSON later
        Assert.Equal(SyntaxFlavor.Json, SyntaxResolver.Detect("matched {3} times, data: {key: true}"));
    }

    // ── SQL detection ────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM Users")]
    [InlineData("INSERT INTO Logs (msg) VALUES ('test')")]
    [InlineData("UPDATE Settings SET value = 1")]
    [InlineData("DELETE FROM TempData WHERE expired = 1")]
    [InlineData("EXEC sp_GetUsers @id = 5")]
    [InlineData("EXECUTE dbo.UpdateRecord")]
    public void Detect_SqlStatements_ReturnsSql(string message)
    {
        Assert.Equal(SyntaxFlavor.Sql, SyntaxResolver.Detect(message));
    }

    [Fact]
    public void Detect_SqlCaseInsensitive()
    {
        Assert.Equal(SyntaxFlavor.Sql, SyntaxResolver.Detect("select id from users"));
    }

    // ── StackTrace detection ─────────────────────────────────────

    [Theory]
    [InlineData("at MyApp.Program.Main(String[] args)")]
    [InlineData("   at System.IO.File.ReadAllLines(String path)")]
    [InlineData("NullReferenceException: Object reference not set")]
    [InlineData("System.InvalidOperationException: Failed")]
    public void Detect_StackTrace_ReturnsStackTrace(string message)
    {
        Assert.Equal(SyntaxFlavor.StackTrace, SyntaxResolver.Detect(message));
    }

    [Fact]
    public void Detect_ExceptionAtEnd_ReturnsStackTrace()
    {
        Assert.Equal(SyntaxFlavor.StackTrace, SyntaxResolver.Detect("System.ArgumentException"));
    }

    [Fact]
    public void Detect_ExceptionWordAlone_NotStackTrace()
    {
        // "Exception" without preceding letter/dot or trailing colon
        Assert.Equal(SyntaxFlavor.None, SyntaxResolver.Detect("An exception occurred in module"));
    }

    // ── Priority: StackTrace > JSON > SQL ────────────────────────

    [Fact]
    public void Detect_StackTraceWithJson_PrefersStackTrace()
    {
        var msg = "at MyApp.Handler.Process(String json) {\"error\": true}";
        Assert.Equal(SyntaxFlavor.StackTrace, SyntaxResolver.Detect(msg));
    }

    [Fact]
    public void Detect_JsonWithSqlKeyword_PrefersJson()
    {
        var msg = "{\"query\": \"SELECT * FROM users\"}";
        Assert.Equal(SyntaxFlavor.Json, SyntaxResolver.Detect(msg));
    }
}
