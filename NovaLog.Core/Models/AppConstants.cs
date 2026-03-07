namespace NovaLog.Core.Models;

/// <summary>Application-wide constants.</summary>
public static class AppConstants
{
    // Drag-drop format ID for source items
    // Must be a simple identifier without special characters (no slashes, colons, etc.)
    public const string DragDropSourceFormatId = "NovaLogSource";

    public const string DefaultTabId = "default";
    public const string DefaultTabDisplayName = "Workspace";
    public const string RotationStrategyAuditJson = "AuditJson";
    public const string RotationStrategyDirectoryScan = "DirectoryScan";
    public const string RotationStrategyFileCreation = "FileCreation";
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";
}
