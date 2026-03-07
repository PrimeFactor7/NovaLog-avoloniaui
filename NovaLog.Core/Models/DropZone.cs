namespace NovaLog.Core.Models;

/// <summary>
/// Describes where in a pane a drag-drop will occur.
/// </summary>
public enum DropZone
{
    None,
    Center,    // Replace pane content
    Left,      // Split left
    Right,     // Split right
    Top,       // Split top
    Bottom     // Split bottom
}
