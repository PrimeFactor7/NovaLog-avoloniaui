# NovaLog Avalonia — Full Code Review

## Context
Comprehensive code review of the entire NovaLog Avalonia codebase across all layers: Core (Models/Services/Theme), UI (Controls/Views/Converters/Services), ViewModels, and Tests. Findings organized by severity.

---

## CRITICAL (5)

| # | File | Line | Issue |
|---|------|------|-------|
| 1 | `Core/Services/LogStreamer.cs` | 73 | **Resource leak**: GZipStream not disposed if exception occurs between creation and explicit Dispose() call. Use `using` for both branches. |
| 2 | `Core/Services/ChronoMergeEngine.cs` | 153 | **Race condition**: `_isIndexing` checked BEFORE acquiring `_appendLock`. Lines can be lost if they arrive while Build() is completing. Move check inside lock. |
| 3 | `Core/Services/SourceContext.cs` | 47 | **ObjectDisposedException**: `LevelScanCts?.Cancel()` on already-disposed CTS throws. Wrap in try-catch. |
| 4 | `Avalonia/Controls/LogMinimap.cs` | 81 | **Render allocation**: `new Pen(brush, tickHeight)` on every render frame. Cache as static readonly. |
| 5 | `Avalonia/Controls/LogLineRow.axaml.cs` | 179, 184 | **Render allocation**: `new SolidColorBrush(...)` and `new Pen(...)` created on every separator row render. Cache as static fields. |

## HIGH (14)

| # | File | Line | Issue |
|---|------|------|-------|
| 6 | `Core/Services/BigFileLineIndex.cs` | 67 | **Type cast**: `long lineIndex` cast to `int` without overflow check. |
| 7 | `Core/Services/BigFileLogProvider.cs` | 104 | **Overflow risk**: `MaxDisplayLineLength * 4` could overflow for large values. Use safe constant. |
| 8 | `Core/Services/AuditLogManager.cs` | 182 | **Null safety**: `Path.GetDirectoryName()!` null-forgiving without validation. |
| 9 | `Core/Services/SettingsManager.cs` | 96 | **Null safety**: `Path.GetDirectoryName()` result used in `Directory.CreateDirectory()` without null check. |
| 10 | `Avalonia/Controls/GridMessageCell.cs` | 121-138 | **O(n²) overlap check**: `tokens.Any()` called per regex match in SQL rendering. Use interval set. |
| 11 | `Avalonia/Controls/SplitPanelHost.cs` | 64-108 | **Memory leak**: Panel event handlers (NewFileDropped, SplitRequested, etc.) attached in `RebuildContent()` but never unsubscribed. |
| 12 | `Avalonia/Views/LogViewPanel.axaml.cs` | 101-105 | **Stale ref**: `ElementClearing` resets `_vm` but not `OwnerLogView`, causing stale visual tree references on row reuse. |
| 13 | `Avalonia/Views/LogViewPanel.axaml.cs` | 308-355 | **Memory leak**: Minimap scroll handler unsubscribe skipped if `_minimap` is null at detach time. |
| 14 | `Avalonia/Views/FilterPanel.axaml.cs` | 77-96 | **Memory leak**: ScrollViewer `ScrollChanged` handler subscribed once but never unsubscribed on DataContext change. |
| 15 | `Avalonia/ViewModels/LogViewViewModel.cs` | 646-652 | **Memory leak**: `provider.LinesAppended` and `provider.IndexingCompleted` lambda subscriptions never unsubscribed in `DisposeProvider()`. |
| 16 | `Avalonia/ViewModels/MainWindowViewModel.cs` | entire | **Missing IDisposable**: Simulators, workspace, event subscriptions never cleaned up. |
| 17 | `Avalonia/ViewModels/LogViewViewModel.cs` | 126-225 | **Race condition**: `_currentMatcher` set before search task completes. Rapid re-searches can produce out-of-order results. |
| 18 | `Avalonia/ViewModels/LogViewViewModel.cs` | 456-477 | **Null deref**: SeekToTimestamp binary search uses `ts!.Value.Ticks` without validating probe found valid timestamp. All-null region causes crash. |
| 19 | `Avalonia/ViewModels/FilterPanelViewModel.cs` | 101-114 | **Thread safety**: `ResultCount` modified without atomic update; `SearchHitsChanged` fired with potentially stale data. |

## MEDIUM (21)

| # | File | Line | Issue |
|---|------|------|-------|
| 20 | `Core/Models/NavigationIndex.cs` | 139 | Backward navigation wrap semantics ambiguous when at first bookmark. |
| 21 | `Core/Services/JsonHighlightTokenizer.cs` | 88 | Escape sequence handling skips exactly 2 chars; `\uXXXX` sequences mishandled. |
| 22 | `Core/Services/LogLineParser.cs` | 41 | DateTime.TryParse without span length validation; partial match possible. |
| 23 | `Core/Services/WorkspaceManager.cs` | 107, 124 | Silent `catch { }` swallows all exceptions including permissions errors. |
| 24 | `Core/Services/RotationStrategies.cs` | 94 | Hard-coded `Thread.Sleep(50)` with no justification. |
| 25 | `Core/Services/ChronoMergeEngine.cs` | 229 | PriorityQueue created without initial capacity hint. |
| 26 | `Avalonia/Controls/LogLineRow.axaml.cs` | Render() | `CreateFormattedText()` allocates per token per frame — no caching. |
| 27 | `Avalonia/Controls/GridMessageCell.cs` | Render() | `message.Substring()` allocations per token per frame. |
| 28 | `Avalonia/Views/SourceManagerPanel.axaml.cs` | 28-34 | Pointer handlers attached in constructor without cleanup on detach. |
| 29 | `Avalonia/ViewModels/LogViewViewModel.cs` | 126-231 | **MVVM violation**: ViewModel creates TreeDataGridSource, TextBlocks, FuncDataTemplate (UI elements). |
| 30 | `Avalonia/ViewModels/LogViewViewModel.cs` | 517-552 | `catch { return null; }` swallows all exceptions including OutOfMemory. |
| 31 | `Avalonia/ViewModels/LogViewViewModel.cs` | 596, 165, 246 | `NotifyRowVisualsChanged()` called inconsistently on/off UI thread. |
| 32 | `Avalonia/ViewModels/LogViewViewModel.cs` | 81-93 | Full `RebuildGridSource()` triggered on any formatting property change. |
| 33 | `Avalonia/ViewModels/FilterPanelViewModel.cs` | 66, 87, 97 | `_lastProcessedIndex` accessed from multiple threads with no synchronization. |
| 34 | `Avalonia/ViewModels/FilterPanelViewModel.cs` | 83-95 | Re-parses raw text via `LogLineParser.Parse()` when pre-parsed data available. |
| 35 | `Avalonia/ViewModels/MainWindowViewModel.cs` | 39-50 | Workspace initialization happens AFTER event subscription — init events could fire before defaults set. |
| 36 | `Avalonia/ViewModels/MainWindowViewModel.cs` | 78-84 | Fire-and-forget async lambda on `AliasInputRequested`; exceptions unobserved. |
| 37 | `Avalonia/ViewModels/HighlightRulesViewModel.cs` | 36-56 | Full list Replace instead of per-property change notification on HighlightRule. |
| 38 | `Avalonia/ViewModels/SourceManagerViewModel.cs` | 278-303 | No validation that sources still exist between UI selection and merge execution. |
| 39 | `Avalonia/ViewModels/SourceManagerViewModel.cs` | 604-650 | Converters defined inside ViewModel file instead of Converters folder. |
| 40 | `Avalonia/ViewModels/` | Multiple | Cyclic dependency web: MainWindow → Workspace → Panes → LogView → SourceManager → back. |

## LOW (20)

| # | File | Line | Issue |
|---|------|------|-------|
| 41 | `Core/Models/AppSettings.cs` | 36-75 | Many boolean fields; consider flags/groups. |
| 42 | `Core/Services/SyntaxResolver.cs` | 36 | Redundant double IndexOf (Contains then IndexOf). |
| 43 | `Core/Services/GlobalClockService.cs` | 31 | Timer fires with stale pending sender if BroadcastTime never called. |
| 44 | `Core/Services/LogSimulator.cs` | 143 | `_currentFile!` force unwrap without null guard. |
| 45 | `Core/Models/HighlightRule.cs` | 36 | Silent regex parse failure — no error message surfaced to user. |
| 46 | `Core/Services/AuditLogManager.cs` | 141-147 | DateTime component parsing (int.Parse) not validated; bad data throws. |
| 47 | `Avalonia/Controls/LogLineRow.axaml.cs` | 620-632 | Inconsistent brush resolution pattern (ResolveBrush vs GetBrush). |
| 48 | `Avalonia/Converters/TabConverters.cs` | 38-45 | Bare `catch { }` in HexToColorConverter catches all exceptions. |
| 49 | `Avalonia/Controls/PanePickerOverlay.axaml.cs` | 19 | No null-check on `ConfirmCommand` before Execute. |
| 50 | `Avalonia/Services/ThemeMapper.cs` | 84-106 | Hex color parsing throws FormatException with no catch. |
| 51 | `Avalonia/Views/MainWindow.axaml.cs` | 42 | TabBar click handler attached in OnLoaded, never detached (low risk — root window). |
| 52 | `Avalonia/Controls/LogLineRow.axaml.cs` | All | Hard-coded fallback colors won't adapt to high-contrast mode. |
| 53 | Multiple AXAML files | — | Icon glyphs lack AutomationProperties.Name for screen readers. |
| 54 | `Avalonia/ViewModels/WorkspaceViewModel.cs` | 48-58 | Clock.TimeChanged subscription never unsubscribed. |
| 55 | `Avalonia/ViewModels/SettingsViewModel.cs` | 102-111 | Color.TryParse failures silently ignored — UI gets wrong colors. |
| 56 | `Avalonia/ViewModels/LogViewViewModel.cs` | 679+ | Debug.WriteLine calls left in production code. |
| 57 | `Avalonia/ViewModels/LogLineViewModel.cs` | 40-42 | Timestamp formatted without timezone indicator. |
| 58 | All test files | — | Test classes not sealed (xUnit best practice). |
| 59 | Test files | — | Tests use real file I/O instead of mocks — slower, flakier. |
| 60 | `Avalonia/ViewModels/` | — | Inconsistent null-check patterns (`!= null` vs `is { }`). |

## TEST COVERAGE GAPS

| Area | Missing |
|------|---------|
| FilterPanelViewModel | Zero test coverage for search, incremental updates, race conditions |
| MainWindowViewModel | Event subscriptions, simulator lifecycle untested |
| LogViewViewModel | `LoadMerge()`, `SeekToTimestamp()` edge cases, concurrent search+load |
| NavigationIndex | Empty collection, single item, boundary bookmarks |

---

## SUMMARY

| Severity | Count |
|----------|-------|
| Critical | 5 |
| High | 14 |
| Medium | 21 |
| Low | 20 |
| **Total** | **60** |

## RECOMMENDED PRIORITY

**Immediate (bugs/crashes):**
1. #2 — ChronoMergeEngine race condition (move `_isIndexing` check inside lock)
2. #1 — LogStreamer GZipStream resource leak (use proper `using`)
3. #3 — SourceContext double-cancellation (wrap in try-catch)
4. #18 — SeekToTimestamp null deref (validate probe result)
5. #17 — Search matcher race condition (don't set matcher until task completes)

**Short-term (memory leaks):**
6. #11 — SplitPanelHost event handler leak
7. #15 — BigFileLogProvider event lambda leak
8. #14 — FilterPanel scroll handler leak
9. #16 — MainWindowViewModel missing IDisposable

**Performance (render hot path):**
10. #4, #5 — Cache Pen/Brush allocations in Render methods
11. #10 — O(n²) token overlap → interval set
