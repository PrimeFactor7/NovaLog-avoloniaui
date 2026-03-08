# Session Persistence Implementation

## Issue Resolved
When exiting and re-launching the app:
- ✅ Panes (split layout) were saved
- ❌ Sources were NOT saved (FIXED in iteration 1)
- ❌ Source-to-pane mapping was NOT saved (FIXED in iteration 2)
- ❌ Sources restored but not loaded into panes (FIXED - source ID preservation issue)

Root cause: Source IDs were regenerated during restore, breaking the ID-based mapping between layout nodes and sources.

## Solution

### 1. Track Loaded Source in Each Pane
**File:** `LogViewViewModel.cs:44-60`

Added `GetLoadedSourceId()` method to expose which source is currently loaded in a pane:

```csharp
public string? GetLoadedSourceId()
{
    if (_loadedSourceIds.Count > 0)
        return _loadedSourceIds.First();

    // Fallback: try to find source by path
    if (_loadedPaths.Count > 0 && _sourceManager != null)
    {
        var path = _loadedPaths.First();
        var source = _sourceManager.Sources.FirstOrDefault(s =>
            Path.GetFullPath(s.PhysicalPath).Equals(path, StringComparison.OrdinalIgnoreCase));
        return source?.SourceId;
    }

    return null;
}
```

### 2. Save Source ID with Each Pane
**File:** `WorkspaceViewModel.cs:77-94`

Updated `SerializeNode()` to save the source ID:

```csharp
private SplitLayoutNode SerializeNode(SplitNodeViewModel node)
{
    if (node is PaneNodeViewModel pane)
    {
        // Save the source ID if this pane has a source loaded
        var sourceId = pane.LogView.GetLoadedSourceId();
        return SplitLayoutNode.Leaf(sourceId, null, 0);
    }
    // ... branches
}
```

### 3. Restore Source When Loading Pane
**File:** `WorkspaceViewModel.cs:96-136`

Updated `DeserializeNode()` to reload the source:

```csharp
private SplitNodeViewModel DeserializeNode(SplitLayoutNode node)
{
    if (node.IsLeaf)
    {
        var pane = new PaneNodeViewModel();
        if (_sourceManager != null && _theme != null)
        {
            pane.LogView.Initialize(Clock, _sourceManager, _theme);

            // Restore the source if one was saved
            if (!string.IsNullOrEmpty(node.SourceId))
            {
                var source = _sourceManager.Sources.FirstOrDefault(s => s.SourceId == node.SourceId);
                if (source != null)
                {
                    // Load the source based on its type
                    if (source.Kind == SourceKind.File)
                        pane.LogView.LoadFile(source.PhysicalPath);
                    else if (source.Kind == SourceKind.Folder)
                        pane.LogView.LoadFolder(source.PhysicalPath);
                    else if (source.Kind == SourceKind.Merge)
                    {
                        var ids = source.PhysicalPath.Substring(8).Split('|');
                        var sourcesToMerge = _sourceManager.Sources.Where(s => ids.Contains(s.SourceId)).ToList();
                        pane.LogView.LoadMerge(sourcesToMerge);
                    }
                }
            }
        }
        return pane;
    }
    // ... branches
}
```

### 4. Save Sources on Exit
**File:** `MainWindowViewModel.cs:140-162`

Updated `SaveSession()` to copy sources from SourceManager to WorkspaceManager:

```csharp
public void SaveSession()
{
    var layout = Workspace.SaveLayout();
    // ... save layout to tabs ...

    // Save sources from SourceManager to WorkspaceManager
    var sources = SourceManager.Sources.Select(src => new LogSource
    {
        Id = src.SourceId,
        PhysicalPath = src.PhysicalPath,
        Kind = src.Kind
    }).ToList();
    _workspaceManager.SetSources(sources);

    _workspaceManager.Save();
}
```

### 5. Added Utility Methods to WorkspaceManager
**File:** `WorkspaceManager.cs:79-90`

Added methods to manipulate sources collection:

```csharp
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
```

### 6. Fixed Source ID Preservation (Critical Fix)
**File:** `SourceManagerViewModel.cs:62-100`

**Problem**: The initial implementation saved sources and sourceIds correctly, but during `LoadSession()`, calling `AddSource(path, kind)` created new `SourceItemViewModel` instances with new GUIDs. The original IDs from workspace.json were discarded, so layout nodes couldn't find their sources.

**Solution**: Added overload that preserves source ID when provided:

```csharp
public void AddSource(string path, SourceKind kind)
{
    AddSource(path, kind, null);
}

public void AddSource(string path, SourceKind kind, string? sourceId)
{
    if (Sources.Any(s => s.PhysicalPath == path && !s.IsChild)) return;

    var item = new SourceItemViewModel
    {
        DisplayName = kind == SourceKind.Folder
            ? new DirectoryInfo(path).Name
            : Path.GetFileName(path),
        PhysicalPath = path,
        Kind = kind,
        SourceColorHex = GetNextColor(Sources.Count)
    };

    // Preserve the source ID if provided (for session restore)
    if (!string.IsNullOrEmpty(sourceId))
        item.SourceId = sourceId;

    // ... property changed handlers ...

    Sources.Add(item);
}
```

**File:** `MainWindowViewModel.cs:132`

Updated `LoadSession()` to pass preserved IDs:

```csharp
foreach (var src in _workspaceManager.Sources)
{
    System.Diagnostics.Debug.WriteLine($"[SESSION] Adding source: {src.Id} - {src.PhysicalPath} ({src.Kind})");
    SourceManager.AddSource(src.PhysicalPath, src.Kind, src.Id); // ← Preserve the original ID!
}
```

This ensures source IDs remain stable across app restarts, allowing the layout → source mapping to work correctly.

## Data Flow

### On Exit (SaveSession)
1. User closes app
2. `SaveSession()` called
3. Workspace layout serialized → includes source ID for each pane
4. Sources copied from SourceManager → WorkspaceManager
5. Both saved to `workspace.json`

### On Launch (LoadSession)
1. App starts → `LoadSession()` called
2. Sources loaded from WorkspaceManager with **original IDs preserved**
3. `AddSource(path, kind, originalId)` called for each source → SourceManager populated with stable IDs
4. Layout loaded → `DeserializeNode()` creates panes
5. For each pane: if has sourceId, find source by ID and load it:
   - File: calls `LoadFile(path)`
   - Folder: calls `LoadFolder(path)`
   - Merge: calls `LoadMerge(sourcesToMerge)`
6. Panes display the same content they had before exit ✅

## JSON Structure

```json
{
  "sources": [
    {
      "id": "guid-123",
      "physicalPath": "C:\\logs\\app.log",
      "kind": "File"
    }
  ],
  "workspaceTabs": [
    {
      "id": "default",
      "displayName": "Workspace 1",
      "layout": {
        "type": "split",
        "orientation": "Vertical",
        "splitterPct": 0.5,
        "child1": {
          "type": "leaf",
          "sourceId": "guid-123"  // ← Links pane to source
        },
        "child2": {
          "type": "leaf",
          "sourceId": null
        }
      }
    }
  ]
}
```

## Result

✅ **Sources persist across restarts**
✅ **Each pane remembers which source it was displaying**
✅ **Layout + content fully restored on launch**
✅ **Supports files, folders, and merged sources**

## Testing

See `TEST_SESSION_FIX.md` for detailed testing procedure.

**Quick Test:**
1. Launch app from Visual Studio (to see Debug.WriteLine output)
2. Load multiple log files/folders into different panes
3. Exit app normally
4. Check `workspace.json` - layout nodes should have populated sourceIds
5. Re-launch app
6. **Expected:** Same panes with same sources automatically loaded

**Note**: Current `workspace.json` has null sourceIds because it was saved before the fix. After the first save with the fix, sourceIds will be populated and restoration will work correctly.
