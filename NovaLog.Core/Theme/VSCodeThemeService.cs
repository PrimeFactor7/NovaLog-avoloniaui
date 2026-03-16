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
    /// <param name="sortBy">Sort order (0 = relevance, 4 = downloads).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>First page of extensions (each has extensionId, publisher, displayName, etc.).</returns>
    public async Task<IReadOnlyList<VSCodeExtensionSummary>> SearchAsync(
        string? searchTerm,
        int sortBy = 0,
        CancellationToken cancellationToken = default)
    {
        // If no search term, default to popular themes
        int effectiveSortBy = string.IsNullOrWhiteSpace(searchTerm) ? 4 : sortBy;
        
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
                    pageSize = 24,
                    sortBy = effectiveSortBy,
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

        var resultList = new List<VSCodeThemeVariant>();

        foreach (var theme in themesArray)
        {
            var path = theme?["path"]?.GetValue<string>()?.Replace("./", "", StringComparison.Ordinal).Replace("\\", "/", StringComparison.Ordinal) ?? "";
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                var (colors, tokens) = await ParseThemeRecursivelyAsync(archive, "extension/" + path, cancellationToken);
                
                var label = theme?["label"]?.GetValue<string>() ?? "Unnamed Theme";
                var uiTheme = theme?["uiTheme"]?.GetValue<string>();
                var kind = VSCodeThemeMapping.GetThemeKind(colors, tokens);
                resultList.Add(new VSCodeThemeVariant(label, uiTheme, kind, colors, tokens));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VSCodeThemeService] Failed to parse theme '{path}': {ex.Message}");
            }
        }

        if (resultList.Count == 0)
            throw new InvalidOperationException("Could not parse any theme files from the extension. They might use unsupported formats or missing includes.");

        return resultList;
    }

    private async Task<(Dictionary<string, string> Colors, List<(string Scope, string Foreground)> Tokens)> ParseThemeRecursivelyAsync(
        ZipArchive archive, string entryPath, CancellationToken ct)
    {
        var entry = archive.GetEntry(entryPath);
        if (entry == null) return (new(), new());

        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        await using var stream = entry.Open();
        using var doc = await JsonDocument.ParseAsync(stream, options, ct).ConfigureAwait(false);
        var root = doc.RootElement;

        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new List<(string Scope, string Foreground)>();

        // 1. Handle Include
        if (root.TryGetProperty("include", out var includeEl) && includeEl.ValueKind == JsonValueKind.String)
        {
            var includePath = includeEl.GetString();
            if (!string.IsNullOrEmpty(includePath))
            {
                var baseDir = Path.GetDirectoryName(entryPath)?.Replace("\\", "/") ?? "";
                var targetPath = Path.Combine(baseDir, includePath).Replace("\\", "/");
                var (baseColors, baseTokens) = await ParseThemeRecursivelyAsync(archive, targetPath, ct);
                foreach (var kv in baseColors) colors[kv.Key] = kv.Value;
                tokens.AddRange(baseTokens);
            }
        }

        // 2. Handle Variables
        if (root.TryGetProperty("variables", out var variablesElement))
        {
            foreach (var prop in variablesElement.EnumerateObject())
            {
                var val = prop.Value.GetString();
                if (!string.IsNullOrEmpty(val))
                    colors[prop.Name] = val;
            }
        }

        // 3. Handle Colors
        if (root.TryGetProperty("colors", out var colorsElement))
        {
            foreach (var prop in colorsElement.EnumerateObject())
            {
                var val = prop.Value.GetString();
                if (!string.IsNullOrEmpty(val))
                    colors[prop.Name] = VSCodeThemeMapping.TryResolveVariable(val, colors);
            }
        }

        // 4. Handle TokenColors
        if (root.TryGetProperty("tokenColors", out var tokenColorsArr))
        {
            foreach (var item in tokenColorsArr.EnumerateArray())
            {
                var foreground = item.TryGetProperty("settings", out var settings) && settings.TryGetProperty("foreground", out var fg)
                    ? fg.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(foreground)) continue;
                foreground = VSCodeThemeMapping.TryResolveVariable(foreground, colors);

                if (item.TryGetProperty("scope", out var scopeEl))
                {
                    if (scopeEl.ValueKind == JsonValueKind.String)
                    {
                        var s = scopeEl.GetString();
                        if (!string.IsNullOrEmpty(s)) tokens.Add((s.Trim(), foreground));
                    }
                    else if (scopeEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sEl in scopeEl.EnumerateArray())
                        {
                            var s = sEl.GetString();
                            if (!string.IsNullOrEmpty(s)) tokens.Add((s.Trim(), foreground));
                        }
                    }
                }
            }
        }

        return (colors, tokens);
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
