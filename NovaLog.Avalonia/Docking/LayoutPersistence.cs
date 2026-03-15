using System.IO;
using Dock.Model.Controls;
using Dock.Serializer.SystemTextJson;

namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Saves and loads the Dock layout to/from %AppData%/NovaLog/layout.json.
/// Uses atomic write (temp file + move). If load fails or file is missing, returns null.
/// </summary>
public static class LayoutPersistence
{
    /// <summary>Full path to layout.json. Deleting this file forces a fresh layout on next run (troubleshooting empty document pane).</summary>
    public static string GetLayoutPath()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(roaming, "NovaLog");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "layout.json");
    }

    private static string LayoutPath => GetLayoutPath();

    private static DockSerializer CreateSerializer()
    {
        // Include our custom document type so it can be serialized/deserialized
        return new DockSerializer();
    }

    /// <summary>Serializes the current root layout to layout.json (atomic write).</summary>
    public static void Save(IRootDock layout)
    {
        try
        {
            var serializer = CreateSerializer();
            var json = serializer.Serialize(layout);
            var path = LayoutPath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LayoutPersistence.Save failed: {ex.Message}");
        }
    }

    /// <summary>Deserializes layout from layout.json. Returns null if file missing or invalid.</summary>
    public static IRootDock? Load()
    {
        try
        {
            var path = LayoutPath;
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            var serializer = CreateSerializer();
            return serializer.Deserialize<IRootDock>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LayoutPersistence.Load failed: {ex.Message}");
            return null;
        }
    }
}
