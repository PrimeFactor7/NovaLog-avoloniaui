using System.Text.Json.Serialization;

namespace NovaLog.Core.Models;

public enum SourceStatus { Active, Inactive, Missing }
public enum SourceKind { Folder, File, Merge }

public sealed class LogSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("physicalPath")]
    public string PhysicalPath { get; set; } = "";

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("sourceColor")]
    public string SourceColorHex { get; set; } = "#00FF41";

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; } = true;

    [JsonPropertyName("isSelectedForMerge")]
    public bool IsSelectedForMerge { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SourceStatus Status { get; set; } = SourceStatus.Active;

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SourceKind Kind { get; set; } = SourceKind.Folder;

    [JsonPropertyName("childSourceIds")]
    public List<string>? ChildSourceIds { get; set; }

    [JsonPropertyName("isExpanded")]
    public bool IsExpanded { get; set; } = true;

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(Alias)
        ? Alias
        : Kind switch
        {
            SourceKind.Folder => new DirectoryInfo(PhysicalPath).Name,
            SourceKind.File => Path.GetFileName(PhysicalPath),
            SourceKind.Merge => "Merged View",
            _ => PhysicalPath
        };
}
