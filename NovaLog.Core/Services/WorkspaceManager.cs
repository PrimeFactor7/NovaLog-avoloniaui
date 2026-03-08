using System.Text.Json;
using System.Text.Json.Serialization;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

public sealed class WorkspaceData
{
    [JsonPropertyName("sources")]
    public List<LogSource> Sources { get; set; } = [];

    [JsonPropertyName("recentHistory")]
    public List<RecentSourceEntry>? RecentHistory { get; set; }

    [JsonPropertyName("workspaceTabs")]
    public List<WorkspaceTabLayout>? WorkspaceTabs { get; set; }
}

public sealed class WorkspaceManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private List<LogSource> _sources = [];
    private string? _workspacePath;

    public IReadOnlyList<LogSource> Sources => _sources;
    public List<RecentSourceEntry> RecentHistory { get; set; } = [];
    public List<WorkspaceTabLayout> WorkspaceTabs { get; set; } = [];

    public event Action? SourcesChanged;

    private string WorkspacePath
    {
        get
        {
            if (_workspacePath != null) return _workspacePath;

            var appDir = AppContext.BaseDirectory;
            var portablePath = Path.Combine(appDir, "workspace.json");

            try
            {
                var probe = Path.Combine(appDir, ".workspace_probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                _workspacePath = portablePath;
            }
            catch
            {
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var novaDir = Path.Combine(roaming, "NovaLog");
                Directory.CreateDirectory(novaDir);
                _workspacePath = Path.Combine(novaDir, "workspace.json");
            }

            return _workspacePath;
        }
    }

    public void AddSource(LogSource source)
    {
        if (_sources.Any(s => s.PhysicalPath == source.PhysicalPath && s.Kind == source.Kind)) return;
        _sources.Add(source);
        SourcesChanged?.Invoke();
        Save();
    }

    public void RemoveSource(string id)
    {
        _sources.RemoveAll(s => s.Id == id);
        SourcesChanged?.Invoke();
        Save();
    }

    public void ClearSources()
    {
        _sources.Clear();
        SourcesChanged?.Invoke();
    }

    public void SetSources(IEnumerable<LogSource> sources)
    {
        _sources.Clear();
        _sources.AddRange(sources);
        SourcesChanged?.Invoke();
    }

    public void Save()
    {
        try
        {
            var data = new WorkspaceData
            {
                Sources = _sources,
                RecentHistory = RecentHistory,
                WorkspaceTabs = WorkspaceTabs
            };
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var dir = Path.GetDirectoryName(WorkspacePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(WorkspacePath, json);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"WorkspaceManager.Save failed: {ex.Message}"); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(WorkspacePath)) return;
            var json = File.ReadAllText(WorkspacePath);
            var data = JsonSerializer.Deserialize<WorkspaceData>(json, JsonOpts);
            if (data != null)
            {
                _sources = data.Sources ?? [];
                RecentHistory = data.RecentHistory ?? [];
                WorkspaceTabs = data.WorkspaceTabs ?? [];
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"WorkspaceManager.Load failed: {ex.Message}"); }
    }
}
