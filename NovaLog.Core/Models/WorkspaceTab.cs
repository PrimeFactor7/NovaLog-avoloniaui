namespace NovaLog.Core.Models;

/// <summary>
/// Represents a workspace tab — an independent investigation session
/// containing its own split-pane layout with multiple log sources.
/// UI-agnostic: the actual panel is created by the UI layer.
/// </summary>
public sealed class WorkspaceTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = "Workspace";
}
