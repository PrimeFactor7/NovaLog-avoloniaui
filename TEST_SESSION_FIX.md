# Session Persistence Fix - Test Validation

## Fix Implemented

**Issue**: Sources were restored but not loaded into their prior panes
**Root Cause**: Source IDs were regenerated on restore, breaking the ID-based mapping

**Solution Applied**:
1. `SourceManagerViewModel.cs:62-100` - Added `AddSource(path, kind, sourceId)` overload that preserves IDs
2. `MainWindowViewModel.cs:132` - Updated `LoadSession()` to pass original IDs: `AddSource(src.PhysicalPath, src.Kind, src.Id)`

## Testing Steps

### 1. Initial State Check
Current `workspace.json` has:
- ✅ Sources array with proper IDs (26615ec3..., acca0ece..., etc.)
- ❌ Layout nodes with `"sourceId": null` (saved before fix)

### 2. Test Procedure

1. **Launch App**
   - Run from Visual Studio with debugger attached
   - Check Output window for `[SESSION]` logs showing source restoration
   - Verify: "Loaded 4 sources from workspace"
   - Verify: Each source shows preserved ID in log

2. **Load Sources into Panes**
   - Use existing session OR manually load sources into different panes
   - Split panes and load different sources into each

3. **Exit App**
   - Close the application normally
   - This triggers `SaveSession()` which now captures source IDs

4. **Verify workspace.json Updated**
   - Open `NovaLog.Avalonia\bin\Debug\net10.0\workspace.json`
   - Check that leaf nodes now have populated `"sourceId"` fields
   - Example of what to look for:
   ```json
   {
     "type": "leaf",
     "sourceId": "26615ec3-15f3-4c6b-a522-afd9578ac0c9",  // ← Should be populated now
     "orientation": "Horizontal",
     "splitterPct": 0.5,
     "child1": null,
     "child2": null
   }
   ```

5. **Re-launch App**
   - Start the app again
   - Check Output window for `[RESTORE]` logs
   - Verify: Each pane finds its source by ID and loads it
   - Expected logs:
     ```
     [RESTORE] Deserializing leaf node, sourceId: 26615ec3-15f3-4c6b-a522-afd9578ac0c9
     [RESTORE] Looking for source with ID: 26615ec3-15f3-4c6b-a522-afd9578ac0c9
     [RESTORE] Found source, loading File: D:\dev\src\via\via-gateway\logs\gw-2026-03-05-11.log
     ```

6. **Visual Verification**
   - Each pane should automatically display the same source it had before exit
   - Split layout should be preserved
   - No empty panes with sources in sidebar

## Expected Results

✅ **Sources list restored** (already working)
✅ **Source IDs preserved during restore** (fixed)
✅ **Source IDs saved with layout** (fixed)
✅ **Sources automatically load into correct panes** (fixed)

## Diagnostic Logs

If issues occur, check these debug statements:
- `[SESSION]` - During workspace load/save
- `[SAVE]` - When serializing panes (should show sourceId)
- `[RESTORE]` - When deserializing panes (should find and load source)

## Notes

- Debug.WriteLine only outputs to Visual Studio Output window, not `dotnet run` console
- To see logs, must run from Visual Studio with debugger attached
- The fix is in the compiled DLLs, ready for testing
