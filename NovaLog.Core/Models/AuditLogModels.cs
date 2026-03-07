using System.Text.Json.Serialization;

namespace NovaLog.Core.Models;

/// <summary>
/// Root object of a Winston .audit.json file.
/// </summary>
public sealed class AuditLog
{
    [JsonPropertyName("keep")]
    public AuditKeepPolicy? Keep { get; set; }

    [JsonPropertyName("auditLog")]
    public string? AuditLogPath { get; set; }

    [JsonPropertyName("files")]
    public List<AuditFileEntry> Files { get; set; } = [];

    [JsonPropertyName("hashType")]
    public string? HashType { get; set; }
}

public sealed class AuditKeepPolicy
{
    [JsonPropertyName("days")]
    public bool Days { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public sealed class AuditFileEntry
{
    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Resolved local path (filename from Name, resolved against the audit file's directory).
    /// </summary>
    [JsonIgnore]
    public string ResolvedPath { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(Date);
}
