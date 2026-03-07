# First File 30-Second Delay Investigation

## Issue Description

**Symptom:** The first log file loaded after app launch takes ~30 seconds, but subsequent files load quickly. This happens even if you close all files and reload the same file first.

**This suggests:** JIT compilation, static initialization, or thread pool warm-up delays.

## Changes Made

### 1. Added Comprehensive Diagnostic Logging

**Files:** `LogViewViewModel.cs:297-358, 419-430, 474-485`

Added timing diagnostics to trace exactly where the delay occurs:

```csharp
[LOAD] Start loading: filename
[LOAD] File size: X bytes (Yms)
[LOAD] Using BigFileLogProvider/LogStreamer (Yms)
[LOAD] Calling provider.Open() (Yms)
[LOAD] provider.Open() returned (Yms)
[LOAD] LoadFromProvider completed (Yms)
[LOAD] Total load time: Yms
```

For folders (gen logs):
```csharp
[LOAD FOLDER] Start loading: foldername
[LOAD FOLDER] Creating AuditLogManager (Yms)
[LOAD FOLDER] Calling auditManager.Refresh() (Yms)
[LOAD FOLDER] auditManager.Refresh() returned (Yms)
[LOAD FOLDER] LoadHistoryAndStream: calling streamer.LoadHistory() (Yms)
[LOAD FOLDER] LoadHistoryAndStream: LoadHistory returned N lines (Yms)
[LOAD FOLDER] LoadHistoryAndStream: LoadFromLines completed (Yms)
[LOAD FOLDER] LoadHistoryAndStream: completed (Yms)
```

### 2. Added Pre-Warming at App Startup

**File:** `App.axaml.cs:23-88`

Added background task that pre-warms critical code paths:
- LogLineParser (regex compilation)
- BigFileLogProvider (MMF initialization, first chunk indexing)
- LogStreamer (file reading, line parsing)

This triggers JIT compilation and first-time allocations during app startup instead of during first file load.

```csharp
[PREWARM] Starting pre-warm
[PREWARM] Warming LogLineParser (Yms)
[PREWARM] Warming BigFileLogProvider (Yms)
[PREWARM] Warming LogStreamer (Yms)
[PREWARM] Completed in Yms
```

## Next Steps - USER ACTION REQUIRED

**Please test and report the diagnostic output:**

1. **Restart the app** with the new build
2. **Watch the debug console** (Visual Studio Output window or debugger)
3. **Click Gen → Fast** to create simulator logs
4. **Load the first gen log** (note the time)
5. **Copy and send ALL the diagnostic output** that appears:
   - `[PREWARM]` messages
   - `[LOAD]` or `[LOAD FOLDER]` messages
   - Any other debug output

## What We're Looking For

The diagnostics will show exactly which step takes 30 seconds:

### Possible Culprits

| Step | If This Is Slow | Cause |
|------|----------------|-------|
| `auditManager.Refresh()` | File system scanning | Antivirus? Network drive? |
| `streamer.LoadHistory()` | Reading/parsing lines | File I/O slow? Parsing overhead? |
| `provider.Open()` | MMF initialization | First-time MMF allocation? |
| `LoadFromLines()` | UI binding | ObservableCollection slow adds? |
| Entire load | JIT compilation | Pre-warming didn't cover something |

### Expected Results

**With pre-warming:**
- `[PREWARM]` should complete in < 1 second during startup
- First file load should be < 1 second (same as subsequent files)

**If still slow:**
- The timing breakdown will show which specific operation is the bottleneck
- We can then optimize that specific operation

## Potential Fixes (depending on findings)

1. **If `LoadHistory()` is slow:** Optimize line parsing, use parallel parsing
2. **If `auditManager.Refresh()` is slow:** Cache discovered files, reduce I/O
3. **If `LoadFromLines()` is slow:** Batch ObservableCollection adds
4. **If MMF is slow:** Defer MMF creation, show placeholder first
5. **If still JIT-related:** Add more pre-warming, or use AOT compilation

## Build Status

✅ **Build succeeded** - new diagnostics and pre-warming are ready to test
⏳ **Waiting for user test results** with diagnostic output
