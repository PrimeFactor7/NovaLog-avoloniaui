using System.Text.Json.Serialization;

namespace NovaLog.Core.Models;

/// <summary>Platform-agnostic orientation enum (replaces System.Windows.Forms.Orientation).</summary>
public enum SplitOrientation { Horizontal, Vertical }

public sealed class WorkspaceTabLayout
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "Workspace";

    [JsonPropertyName("layout")]
    public SplitLayoutNode? Layout { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public sealed class SplitLayoutNode
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "leaf";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("tabKey")]
    public string? TabKey { get; set; }

    [JsonPropertyName("scrollIndex")]
    public long ScrollIndex { get; set; }

    [JsonPropertyName("isFollowMode")]
    public bool IsFollowMode { get; set; } = true;

    [JsonPropertyName("orientation")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SplitOrientation Orientation { get; set; }

    [JsonPropertyName("splitterPct")]
    public double SplitterPct { get; set; } = 0.5;

    [JsonPropertyName("child1")]
    public SplitLayoutNode? Child1 { get; set; }

    [JsonPropertyName("child2")]
    public SplitLayoutNode? Child2 { get; set; }

    [JsonIgnore]
    public bool IsLeaf => Type == "leaf";

    public static SplitLayoutNode Leaf(string? sourceId, string? tabKey, long scrollIndex = 0, bool isFollowMode = true) => new()
    {
        Type = "leaf", SourceId = sourceId, TabKey = tabKey, ScrollIndex = scrollIndex, IsFollowMode = isFollowMode
    };

    public static SplitLayoutNode Branch(SplitOrientation orientation, double pct,
        SplitLayoutNode child1, SplitLayoutNode child2) => new()
    {
        Type = "split", Orientation = orientation, SplitterPct = pct, Child1 = child1, Child2 = child2
    };
}
