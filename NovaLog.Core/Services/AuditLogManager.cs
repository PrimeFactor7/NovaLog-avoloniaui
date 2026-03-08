using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Discovers and parses Winston .audit.json files in a logs directory.
/// Identifies the current active log file per audit and provides chronological ordering.
/// </summary>
public sealed partial class AuditLogManager
{
    private readonly string _logsDirectory;
    private readonly Dictionary<string, AuditLog> _auditLogs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Matches Winston-style filenames: {prefix}-{yyyy}-{MM}-{dd}-{HH}.{log|gz}
    /// </summary>
    [GeneratedRegex(
        @"^(?<prefix>.+?)-(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})-(?<hour>\d{2})\.(?<ext>log|gz)$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex FileNamePattern();

    public AuditLogManager(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        _logsDirectory = Path.GetFullPath(logsDirectory);
    }

    public string LogsDirectory => _logsDirectory;

    /// <summary>
    /// All parsed audit logs keyed by their file path.
    /// </summary>
    public IReadOnlyDictionary<string, AuditLog> AuditLogs => _auditLogs;

    // ── Discovery & Refresh ─────────────────────────────────────────

    /// <summary>
    /// Scans the directory for every *-audit.json file and parses them.
    /// </summary>
    public void Refresh()
    {
        _auditLogs.Clear();

        if (!Directory.Exists(_logsDirectory))
            return;

        foreach (var file in Directory.GetFiles(_logsDirectory, "*-audit.json", SearchOption.TopDirectoryOnly))
        {
            var audit = ParseAuditFile(file);
            if (audit != null)
                _auditLogs[file] = audit;
        }
    }

    /// <summary>
    /// Re-parses a single audit file (call after a watcher notification).
    /// </summary>
    public AuditLog? RefreshSingle(string auditFilePath)
    {
        var key = Path.GetFullPath(auditFilePath);
        var audit = ParseAuditFile(key);
        if (audit != null)
            _auditLogs[key] = audit;
        else
            _auditLogs.Remove(key);
        return audit;
    }

    // ── Queries ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the active (most recent / tail-target) entry, or null.
    /// </summary>
    public AuditFileEntry? GetActiveFile(string auditFilePath)
    {
        var key = Path.GetFullPath(auditFilePath);
        return _auditLogs.TryGetValue(key, out var a) && a.Files.Count > 0
            ? a.Files[^1]
            : null;
    }

    /// <summary>
    /// Returns every file entry in chronological order (oldest → newest).
    /// </summary>
    public IReadOnlyList<AuditFileEntry> GetFilesInOrder(string auditFilePath)
    {
        var key = Path.GetFullPath(auditFilePath);
        return _auditLogs.TryGetValue(key, out var a) ? a.Files : [];
    }

    /// <summary>
    /// Extracts the log prefix (e.g. "sf" or "sf-error") from an audit's first entry.
    /// </summary>
    public string? GetLogPrefix(string auditFilePath)
    {
        var files = GetFilesInOrder(auditFilePath);
        if (files.Count == 0) return null;

        var match = FileNamePattern().Match(Path.GetFileName(files[0].ResolvedPath));
        return match.Success ? match.Groups["prefix"].Value : null;
    }

    /// <summary>
    /// Maps each discovered prefix → its audit file path. Useful for building tabs.
    /// </summary>
    public Dictionary<string, string> GetPrefixToAuditMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _auditLogs)
        {
            var prefix = GetLogPrefix(kvp.Key);
            if (prefix != null) map[prefix] = kvp.Key;
        }
        return map;
    }

    // ── Pattern-based fallback (no audit file) ──────────────────────

    /// <summary>
    /// Groups and orders log files purely by their filename timestamp.
    /// Returns prefix → ordered list of full paths.
    /// </summary>
    public static Dictionary<string, List<string>> DiscoverByPattern(string directory)
    {
        try
        {
            var groups = new Dictionary<string, List<(string Path, DateTime Ts)>>(StringComparer.OrdinalIgnoreCase);
            var regex = FileNamePattern();

            var logFiles = Directory.GetFiles(directory, "*.log")
                .Concat(Directory.GetFiles(directory, "*.gz"));

            foreach (var file in logFiles)
            {
                var match = regex.Match(Path.GetFileName(file));
                if (!match.Success) continue;

                var prefix = match.Groups["prefix"].Value;
                var ts = new DateTime(
                    int.Parse(match.Groups["year"].Value),
                    int.Parse(match.Groups["month"].Value),
                    int.Parse(match.Groups["day"].Value),
                    int.Parse(match.Groups["hour"].Value),
                    0, 0, DateTimeKind.Local);

                if (!groups.TryGetValue(prefix, out var list))
                    groups[prefix] = list = [];
                list.Add((file, ts));
            }

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in groups)
            {
                result[kvp.Key] = kvp.Value
                    .OrderBy(x => x.Ts)
                    .Select(x => x.Path)
                    .ToList();
            }
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuditLogManager] DiscoverByPattern({directory}): {ex.Message}");
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ── Internal ────────────────────────────────────────────────────

    private static AuditLog? ParseAuditFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var audit = JsonSerializer.Deserialize<AuditLog>(stream);
            if (audit == null) return null;

            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir)) return null;
            foreach (var entry in audit.Files)
            {
                // Resolve to local directory — the Name field may be an absolute path
                // from the original machine, so we just take the filename portion.
                entry.ResolvedPath = Path.Combine(dir, Path.GetFileName(entry.Name));
            }
            return audit;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"AuditLogManager: failed to parse '{filePath}': {ex.Message}");
            return null;
        }
    }
}
