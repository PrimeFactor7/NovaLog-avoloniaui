using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaLog.Core.Theme;

/// <summary>
/// Fetches VS Code marketplace theme data in-process: search and VSIX download + extraction.
/// No proxy or browser required. Single-language, single-binary friendly.
/// </summary>
public sealed class VSCodeThemeService
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Add("Accept", "application/json;api-version=3.0-preview.1");
        return client;
    }

    private const string GalleryQueryUrl = "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery";

    /// <summary>Search the marketplace for theme extensions.</summary>
    /// <param name="searchTerm">Search query.</param>
    /// <param name="sortBy">Sort order (0 = relevance).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>First page of extensions (each has extensionId, publisher, displayName, etc.).</returns>
    public async Task<IReadOnlyList<VSCodeExtensionSummary>> SearchAsync(
        string searchTerm,
        int sortBy = 0,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            filters = new[]
            {
                new
                {
                    criteria = new[]
                    {
                        new { filterType = 8, value = "Microsoft.VisualStudio.Code" },
                        new { filterType = 5, value = "Themes" },
                        new { filterType = 10, value = searchTerm ?? "" }
                    },
                    pageNumber = 1,
                    pageSize = 15,
                    sortBy,
                    sortOrder = 0
                }
            },
            assetTypes = Array.Empty<string>(),
            flags = 914
        };

        using var response = await SharedHttp.PostAsJsonAsync(GalleryQueryUrl, payload, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return Array.Empty<VSCodeExtensionSummary>();

        var first = results[0];
        if (!first.TryGetProperty("extensions", out var extensions))
            return Array.Empty<VSCodeExtensionSummary>();

        var list = new List<VSCodeExtensionSummary>();
        foreach (var ext in extensions.EnumerateArray())
        {
            var extensionName = ext.TryGetProperty("extensionName", out var en) ? en.GetString() : null;
            var publisherName = ext.TryGetProperty("publisher", out var pub) ? pub.TryGetProperty("publisherName", out var pn) ? pn.GetString() : null : null;
            var displayName = ext.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            var version = ext.TryGetProperty("versions", out var v) && v.GetArrayLength() > 0 && v[0].TryGetProperty("version", out var ver)
                ? ver.GetString()
                : null;
            if (!string.IsNullOrEmpty(extensionName) && !string.IsNullOrEmpty(publisherName))
                list.Add(new VSCodeExtensionSummary(extensionName, publisherName, displayName ?? extensionName, version));
        }
        return list;
    }

    /// <summary>Fetch theme variants from an extension's VSIX (download, unzip, parse theme JSON).</summary>
    public async Task<IReadOnlyList<VSCodeThemeVariant>> FetchThemesAsync(
        string publisher,
        string extension,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        var versionSegment = string.IsNullOrWhiteSpace(version) ? "" : $"/{Uri.EscapeDataString(version)}";
        var vsixUrl = $"https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{Uri.EscapeDataString(publisher)}/vsextensions/{Uri.EscapeDataString(extension)}{versionSegment}/vspackage";

        var bytes = await SharedHttp.GetByteArrayAsync(vsixUrl, cancellationToken).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var pkgEntry = archive.GetEntry("extension/package.json");
        if (pkgEntry == null)
            throw new InvalidOperationException("VSIX invalid: extension/package.json not found.");

        JsonNode? pkgDoc;
        await using (var pkgStream = pkgEntry.Open())
        {
            pkgDoc = await JsonNode.ParseAsync(pkgStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var themesArray = pkgDoc?["contributes"]?["themes"]?.AsArray();
        if (themesArray == null || themesArray.Count == 0)
            throw new InvalidOperationException("Extension does not contribute any themes.");

        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        var resultList = new List<VSCodeThemeVariant>();

        foreach (var theme in themesArray)
        {
            var path = theme?["path"]?.GetValue<string>()?.Replace("./", "", StringComparison.Ordinal) ?? "";
            if (string.IsNullOrEmpty(path)) continue;

            var themeEntry = archive.GetEntry("extension/" + path);
            if (themeEntry == null) continue;

            try
            {
                await using var themeStream = themeEntry.Open();
                using var themeDoc = await JsonDocument.ParseAsync(themeStream, options, cancellationToken).ConfigureAwait(false);
                var root = themeDoc.RootElement;
                var colorsElement = root.TryGetProperty("colors", out var colors) ? colors : root;
                var colorsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in colorsElement.EnumerateObject())
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(val))
                        colorsDict[prop.Name] = val;
                }
                var tokenColorsList = new List<(string Scope, string Foreground)>();
                if (root.TryGetProperty("tokenColors", out var tokenColorsArr))
                {
                    foreach (var entry in tokenColorsArr.EnumerateArray())
                    {
                        var foreground = entry.TryGetProperty("settings", out var settings) && settings.TryGetProperty("foreground", out var fg)
                            ? fg.GetString()
                            : null;
                        if (string.IsNullOrWhiteSpace(foreground)) continue;
                        if (entry.TryGetProperty("scope", out var scopeEl))
                        {
                            if (scopeEl.ValueKind == JsonValueKind.String)
                            {
                                var s = scopeEl.GetString();
                                if (!string.IsNullOrEmpty(s))
                                    tokenColorsList.Add((s.Trim(), foreground));
                            }
                            else if (scopeEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in scopeEl.EnumerateArray())
                                {
                                    var s = item.GetString();
                                    if (!string.IsNullOrEmpty(s))
                                        tokenColorsList.Add((s.Trim(), foreground));
                                }
                            }
                        }
                    }
                }
                var label = theme?["label"]?.GetValue<string>() ?? "Unnamed Theme";
                var uiTheme = theme?["uiTheme"]?.GetValue<string>();
                var kind = VSCodeThemeMapping.GetThemeKind(colorsDict, tokenColorsList);
                resultList.Add(new VSCodeThemeVariant(label, uiTheme, kind, colorsDict, tokenColorsList));
            }
            catch (JsonException)
            {
                // Skip invalid theme file
            }
        }

        if (resultList.Count == 0)
            throw new InvalidOperationException("Could not parse any theme files from the extension.");

        return resultList;
    }
}

public sealed record VSCodeExtensionSummary(string ExtensionName, string Publisher, string DisplayName, string? Version);

/// <summary>UI only, syntax only (tokenColors), or both.</summary>
public enum VSCodeThemeKind { UIOnly, SyntaxOnly, Full }

public sealed record VSCodeThemeVariant(
    string Label,
    string? UiTheme,
    VSCodeThemeKind Kind,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<(string Scope, string Foreground)> TokenColors)
{
    public string KindDisplay => Kind switch
    {
        VSCodeThemeKind.Full => "Full (UI + syntax)",
        VSCodeThemeKind.UIOnly => "UI only",
        VSCodeThemeKind.SyntaxOnly => "Syntax only",
        _ => Kind.ToString()
    };
}
