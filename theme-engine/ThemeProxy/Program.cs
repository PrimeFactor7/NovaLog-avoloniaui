using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseCors();

// ==========================================
// 1. SEARCH ENDPOINT (Marketplace Search)
// ==========================================
app.MapPost("/api/search-themes", async ([FromBody] SearchRequest? request, IHttpClientFactory clientFactory) =>
{
    var searchTerm = request?.SearchTerm ?? "";
    var sortBy = request?.SortBy ?? 0;

    var payload = new
    {
        filters = new[]
        {
            new
            {
                criteria = new[]
                {
                    new { filterType = 8, value = "Microsoft.VisualStudio.Code.Extension" },
                    new { filterType = 5, value = "category:\"Themes\"" },
                    new { filterType = 10, value = searchTerm }
                },
                pageNumber = 1,
                pageSize = 15,
                sortBy,
                sortOrder = 0
            }
        },
        flags = 914
    };

    try
    {
        var client = clientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery",
            payload);

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (content.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
        {
            var first = results[0];
            if (first.TryGetProperty("extensions", out var extensions))
                return Results.Ok(extensions);
        }
        return Results.Ok(JsonDocument.Parse("[]").RootElement);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ==========================================
// 2. FETCH ENDPOINT (Binary VSIX Extraction)
// ==========================================
app.MapGet("/api/fetch-vscode-theme", async (
    [FromQuery] string? publisher,
    [FromQuery] string? extension,
    [FromQuery] string? version,
    IHttpClientFactory clientFactory) =>
{
    if (string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(extension))
        return Results.BadRequest(new { error = "Missing params", message = "Required: publisher, extension. Optional: version." });

    var versionSegment = string.IsNullOrWhiteSpace(version) ? "" : $"/{Uri.EscapeDataString(version)}";
    var vsixUrl = $"https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{Uri.EscapeDataString(publisher)}/vsextensions/{Uri.EscapeDataString(extension)}{versionSegment}/vspackage";

    try
    {
        var client = clientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        var bytes = await client.GetByteArrayAsync(vsixUrl);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var pkgEntry = archive.GetEntry("extension/package.json");
        if (pkgEntry == null)
            return Results.NotFound(new { error = "Invalid VSIX", message = "extension/package.json not found." });

        JsonNode? pkgDoc;
        await using (var pkgStream = pkgEntry.Open())
        {
            pkgDoc = await JsonNode.ParseAsync(pkgStream);
        }

        var themesArray = pkgDoc?["contributes"]?["themes"]?.AsArray();
        if (themesArray == null || themesArray.Count == 0)
            return Results.NotFound(new { error = "No themes found", message = "This extension does not contribute any themes." });

        var resultList = new List<object>();
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        foreach (var theme in themesArray)
        {
            var path = theme?["path"]?.GetValue<string>()?.Replace("./", "", StringComparison.Ordinal) ?? "";
            if (string.IsNullOrEmpty(path)) continue;

            var themeEntry = archive.GetEntry("extension/" + path);
            if (themeEntry == null) continue;

            try
            {
                await using var themeStream = themeEntry.Open();
                using var themeDoc = await JsonDocument.ParseAsync(themeStream, options);
                var root = themeDoc.RootElement;
                var tokens = root.TryGetProperty("colors", out var colors) ? colors : root;
                resultList.Add(new
                {
                    label = theme?["label"]?.GetValue<string>() ?? "Unnamed Theme",
                    uiTheme = theme?["uiTheme"]?.GetValue<string>(),
                    tokens
                });
            }
            catch (JsonException)
            {
                // Skip invalid theme file
            }
        }

        if (resultList.Count == 0)
            return Results.UnprocessableEntity(new { error = "No valid themes", message = "Could not parse any theme files." });

        return Results.Ok(new { extension, publisher, version, themes = resultList });
    }
    catch (HttpRequestException ex)
    {
        var status = ex.StatusCode;
        if (status == System.Net.HttpStatusCode.NotFound)
            return Results.NotFound(new { error = "Extension or version not found", message = ex.Message });
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

public record SearchRequest(string? SearchTerm = "", int SortBy = 0);
