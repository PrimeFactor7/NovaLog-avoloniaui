using System.Diagnostics;
using System.Text.Json;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Loads and saves AppSettings to a JSON file.
/// Portable: next to .exe. Fallback: %APPDATA%/NovaLog/.
/// </summary>
public static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string? _settingsPath;

    /// <summary>Fired when Load fails. Message is suitable for status bar or log.</summary>
    public static event Action<string>? LoadFailed;
    /// <summary>Fired when Save fails. Message is suitable for status bar or log.</summary>
    public static event Action<string>? SaveFailed;

    public static string SettingsPath
    {
        get
        {
            if (_settingsPath != null) return _settingsPath;

            var appDir = AppContext.BaseDirectory;
            var portablePath = Path.Combine(appDir, "novalog-settings.json");

            try
            {
                var probe = Path.Combine(appDir, ".novalog_probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                _settingsPath = portablePath;
            }
            catch
            {
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var novaDir = Path.Combine(roaming, "NovaLog");
                Directory.CreateDirectory(novaDir);
                _settingsPath = Path.Combine(novaDir, "novalog-settings.json");
            }

            return _settingsPath;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return CreateDefaults();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (settings == null) return CreateDefaults();
            
            // Ensure collections are populated if JSON was missing them
            if (settings.LevelColors.Count == 0)
                PopulateDefaultLevelColors(settings);

            return settings;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[Settings] Failed to load: {ex.Message}");
            LoadFailed?.Invoke(ex.Message);
            return CreateDefaults();
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[Settings] Failed to load (invalid JSON): {ex.Message}");
            LoadFailed?.Invoke(ex.Message);
            return CreateDefaults();
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[Settings] Failed to load: {ex.Message}");
            LoadFailed?.Invoke(ex.Message);
            return CreateDefaults();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, json);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[Settings] Failed to save: {ex.Message}");
            SaveFailed?.Invoke(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[Settings] Failed to save: {ex.Message}");
            SaveFailed?.Invoke(ex.Message);
        }
    }

    public static AppSettings CreateDefaults()
    {
        var s = new AppSettings();
        PopulateDefaultLevelColors(s);
        PopulateDefaultHighlightRules(s);
        return s;
    }

    private static void PopulateDefaultLevelColors(AppSettings s)
    {
        s.LevelColors["Trace"]   = new LevelColorEntry { Foreground = "#606060" };
        s.LevelColors["Verbose"] = new LevelColorEntry { Foreground = "#6A6A8A" };
        s.LevelColors["Debug"]   = new LevelColorEntry { Foreground = "#808080" };
        s.LevelColors["Info"]    = new LevelColorEntry { Foreground = "#00D4FF" };
        s.LevelColors["Warn"]    = new LevelColorEntry { Foreground = "#FFB000", Background = "#12FFB000", BackgroundEnabled = true };
        s.LevelColors["Error"]   = new LevelColorEntry { Foreground = "#FF3E3E", Background = "#1CFF3E3E", BackgroundEnabled = true };
        s.LevelColors["Fatal"]   = new LevelColorEntry { Foreground = "#FF2060", Background = "#30FF2060", BackgroundEnabled = true };
        s.LevelColors["Unknown"] = new LevelColorEntry { Foreground = "#00FF41" };
    }

    private static void PopulateDefaultHighlightRules(AppSettings s)
    {
        s.HighlightRules.Add(new HighlightRuleEntry
        {
            Pattern = @"\berror\b",
            Foreground = "#FF5050",
            Background = "#28FF3C3C",
            RuleType = "LineHighlight",
            Enabled = true
        });
        s.HighlightRules.Add(new HighlightRuleEntry
        {
            Pattern = @"\bwarn\b",
            Foreground = "#FFC83C",
            Background = "#1EFFC800",
            RuleType = "LineHighlight",
            Enabled = false
        });
        s.HighlightRules.Add(new HighlightRuleEntry
        {
            Pattern = @"\bdebug\b",
            Foreground = "#787878",
            RuleType = "MatchHighlight",
            Enabled = false
        });
    }
}
