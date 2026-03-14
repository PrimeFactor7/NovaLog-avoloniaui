# Code Review: Time Sync Updates & Text vs Grid Behavior

## Scope

- Time sync: Sync toggle (Codicon), `BroadcastSelectedLineTimestamp`, `SeekToTimestamp` (nearest-neighbor), `HandleTimeChanged`, scroll broadcast.
- Text view vs grid view: center-line detection, scroll-to-line, minimap, span/variable-height rows.

---

## 1. Time Sync Logic — No Critical Bugs Found

### 1.1 Broadcast source and click-to-sync

- **Only the focused pane** is the broadcast source (`SetAsBroadcastSource(true)` in `FocusPane`). Scroll-induced broadcast (`NotifyViewportScrolled`) correctly requires `_isBroadcastSource && IsLinked`, so no cascade when a linked-but-unfocused pane is scrolled by sync.
- **Click-to-sync**: `BroadcastSelectedLineTimestamp()` is invoked only from the view after `SelectLine` on row click (log and grid). It is not called from `NavigateToLine`/`SeekToTimestamp`, so no feedback loop when other panes update selection/scroll.

### 1.2 HandleTimeChanged

- Sender is excluded (`if (pane.LogView == sender) continue`), so the originating pane does not call `SeekToTimestamp` on itself.
- Only panes with `IsLinked` receive the timestamp. Correct.

### 1.3 SeekToTimestamp and empty / no source

- If no source is loaded: memory path returns `0`, provider path uses `atOrBefore` from callback. `NavigateToLine(0)` runs; `TryNormalizeLineIndex` with `lineCount == 0` returns false and no scroll/selection occurs. Safe.
- If only one pane has a source and the other is empty, the empty pane does nothing when it receives the timestamp (provider/memory branches not taken or NavigateToLine no-ops). Acceptable.

### 1.4 Nearest-neighbor

- **Memory**: Binary search for “at or before,” then compare `best` and `best+1` by `Math.Abs(ts.Ticks - target.Ticks)`. Null timestamps handled; tie-break prefers earlier index. Correct.
- **Provider**: Uses “at or before” index from provider, then compares with `atOrBefore+1` via `GetLine()`. Uses `GetLoadedLineCount()` for bounds. Correct.

### 1.5 Threading

- `HandleTimeChanged` is invoked on the UI thread (via `Clock.TimeChanged` and `SynchronizationContext.Post` in `GlobalClockService`). `SeekToTimestamp` and provider callbacks run on that thread. `ScrollToLineRequested` is handled with `Dispatcher.UIThread.Post` in the view. No cross-thread access issues identified.

---

## 2. Text View vs Grid View — Differences and Risks

### 2.1 Center-line detection (what gets broadcast on scroll)

| Mode   | How center line is computed | Variable height? |
|--------|-----------------------------|-------------------|
| **Text** | `topIdx = Offset.Y / LogLineRow.RowHeight`, `visibleCount = Viewport.Height / RowHeight`, `center = topIdx + visibleCount/2` | **No** — assumes fixed row height. |
| **Grid** | `FindCenterVisibleGridRow()`: walk materialized `TreeDataGridRow`s, find row whose vertical center is closest to viewport center; fallback to `ratio = Offset.Y / maxScroll`, `line = ratio * TotalLineCount`. | **Yes** for primary path; fallback is ratio-based (same caveat as below). |

- **Text**: With **span lines** (multi-line rows), rows have height `LogLineRow.RowHeight * LineCount`. The scroll extent is the sum of these heights, not `TotalLineCount * RowHeight`. So:
  - `topIdx` and `visibleCount` are wrong whenever any row has `LineCount > 1`.
  - The “center” line used for time sync can be off; other panes may sync to the wrong timestamp.
- **Grid**: Center detection is correct for variable-height rows when materialized rows are available. When no rows are materialized (e.g. right after switch or before layout), the ratio fallback assumes linear mapping from scroll Y to line index, which is only approximate with variable row heights.

**Recommendation**: Treat text view as “best effort” for time sync when span lines exist. Longer term, text view could compute center line by mapping scroll offset to cumulative line heights (or by walking visible items similar to grid).

### 2.2 ScrollToLine (where synced pane scrolls to)

| Mode   | How target Y is computed | Variable height? |
|--------|---------------------------|-------------------|
| **Text** | `targetY = lineIndex * LogLineRow.RowHeight - halfViewport` | **No** — assumes uniform height. |
| **Grid** | `ratio = lineIndex / TotalLineCount`, `targetY = ratio * scroll.Extent.Height - viewport.Height/2` | **Approximate** — extent is sum of row heights; ratio assumes linear distribution. |

- **Text**: With span lines, the true offset for a line index is the sum of heights of lines 0..lineIndex-1. Using `lineIndex * RowHeight` is wrong and can place the “centered” line off-screen or in the wrong place.
- **Grid**: `ratio * scroll.Extent.Height` assumes line index is linear in scroll space. With variable row heights this is only approximate; the synced line may not be exactly centered.

**Recommendation**: Same as 2.1: document that with span/variable-height content, synced scroll position is approximate in both modes; grid is better due to `FindCenterVisibleGridRow` for *reading* the center; *setting* scroll remains ratio-based.

### 2.3 Click-to-line index (text view)

- **Text**: `OnLogPointerPressed` uses `lineIndex = (int)(pos.Y / LogLineRow.RowHeight)`. With variable-height rows, `pos.Y` does not map linearly to line index; the user can click one logical line but the formula may select another.
- **Grid**: Uses the actual row’s `DataContext` (`GridRowViewModel.Line.GlobalIndex`), so the correct line is always selected. No issue.

**Recommendation**: For text view with span lines, consider mapping `pos.Y` to line index via cumulative heights (or hit-testing the repeater’s children) so click-to-sync selects the correct line.

### 2.4 Minimap

- **UpdateMinimapViewport** correctly branches:
  - Grid: `_logGrid?.Scroll` (extent, viewport, offset).
  - Text: `_scroller` (extent, viewport, offset).
- **ScrollToLine** calls `UpdateMinimapViewport(_minimap)` after changing offset in both modes, so the minimap reflects the new position after sync.
- Minimap uses `ViewportTopRatio` / `ViewportHeightRatio` and assumes a linear mapping of scroll to “line space” for drawing the viewport lens; with variable height this is a proportional representation of scroll position, not of exact line range. Acceptable for a minimap.

No bugs found in minimap wiring for time sync.

### 2.5 Grid: ScrollViewer vs TreeDataGrid.Scroll

- Grid scroll is driven by `_logGrid.Scroll` (TreeDataGrid’s scroll API). `EnsureGridScroller()` gets the internal `ScrollViewer` and subscribes `OnGridScrollChanged` / `OnGridScrollSizeChanged`. When the grid’s `Source` or layout changes, `_gridScroller` can be nulled but the handler remains on the actual control; the code uses `s as ScrollViewer` in the handler and re-resolves the grid scroll in `ScrollToLine` via `_logGrid?.Scroll`. Consistent and correct.

---

## 3. Edge Cases and Small Improvements

### 3.1 SeekToTimestamp when not loaded

- If `_provider` is non-null but `GetLoadedLineCount()` is 0 (e.g. still loading), the provider’s `ScrollToTimestamp` may still call the callback with some index. `GetNearestTimestampIndexFromProvider` returns `atOrBefore` when `count <= 0`, so we could call `NavigateToLine(0)`. `TryNormalizeLineIndex(0)` with count 0 returns false, so no-op. Safe.

### 3.2 Grid center-line throttle

- `FindCenterVisibleGridRow` is throttled (50 ms). During fast scroll, the same center line can be reused. That’s acceptable for debounced time sync; the final scroll position will still broadcast the correct center once scrolling stops (and the global clock already debounces).

### 3.3 Optional: Prevent double broadcast on click

- Currently: view calls `SelectLine` then `BroadcastSelectedLineTimestamp()`. `SelectLine` sets `SelectedLineIndex` and `SetCurrentLine`. So when the user clicks, we broadcast. No need to change; just noting that if in the future selection could be set programmatically from the view in other places, ensure we don’t call `BroadcastSelectedLineTimestamp` from those paths.

---

## 4. Summary Table

| Area | Text view | Grid view | Notes |
|------|-----------|-----------|--------|
| Center line for broadcast | Fixed row height formula | Row-based or ratio fallback | Text wrong with span lines; grid good when rows materialized. |
| ScrollToLine (sync target) | `lineIndex * RowHeight` | `ratio * Extent.Height` | Both approximate with variable height; text worse with span. |
| Row click → line index | `pos.Y / RowHeight` | Row DataContext | Text wrong with span lines; grid correct. |
| Minimap viewport source | _scroller | _logGrid.Scroll | Correct per mode. |
| Minimap update after sync | Yes (in ScrollToLine) | Yes | Correct. |

---

## 5. Recommendations

1. **Document** in product or spec: time sync (scroll and click) is “time-anchored” and nearest-neighbor; with **span lines / variable row height**, text view center and scroll position are approximate; grid view is more accurate for center detection.
2. **Optional follow-ups**:
   - Text view: map scroll offset to center line using cumulative row heights (or visible items) when span lines are enabled.
   - Text view: map click `pos.Y` to line index using cumulative heights or hit-testing for correct click-to-sync with span lines.
   - Grid view: if TreeDataGrid exposes or we can compute “scroll offset for line index,” use it in `ScrollToLine` instead of ratio for exact centering with variable height.
3. **Tests**: Add unit tests for `FindNearestTimestampIndexInMemory` and `GetNearestTimestampIndexFromProvider` (tie-break, nulls, single line, two lines before/after target). Consider an integration test: two panes linked, click row in one, assert the other’s `SelectedLineIndex` is the nearest-neighbor line for that timestamp.

No blocking issues were found in the time sync flow; the main residual risks are the known text-view assumptions (fixed row height and linear Y→line mapping) when span lines or variable height are used.

---

## 6. Fixes Applied (post-review)

1. **SeekToTimestamp**: Early return when `GetLoadedLineCount() == 0` to avoid no-op NavigateToLine.
2. **GetNearestTimestampIndexFromProvider**: Clamp `atOrBefore` to `[0, count - 1]`; return `0` when `count <= 0`.
3. **Text view center line**: Use `FindCenterVisibleLogRow()` when realized rows exist (viewport center from actual row positions); fallback to uniform-height formula when no rows realized.
4. **Text view click**: `GetLogLineIndexAtPosition(pos)` resolves the line from the row under the pointer (realized rows + bounds); fallback to `pos.Y / RowHeight`.
5. **Grid ScrollToLine**: After ratio-based scroll and `SelectGridRowByLineIndex`, call `BringGridRowIntoView(lineIndex)` so the correct row is brought into view (corrects for variable row height).
6. **Comments**: Documented text view uniform RowHeight assumption in ScrollToLine.
