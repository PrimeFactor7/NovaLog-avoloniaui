# NovaLog Migration Plan: Advanced Docking & Multi-Monitor HUD

**Status:** Implemented. Layout persistence uses per-tab Dock JSON in workspace.json; Explode uses `SplitToWindow` (DIPs); temporal sync is timestamp-center; theme uses DockNeon.axaml (splitters, HostWindow, ContextMenu/MenuFlyout overlay safety).

## I. Architectural Overhaul (The Plumbing)
* **Framework:** Integrate `Dock.Avalonia` using the Factory Pattern to separate layout state from `LogViewViewModel`.
* **Theme:** Use `Dock.Avalonia.Themes.Simple` as the base to minimize legacy "WinForms" styling and allow for custom "Deep Space" neon skinning.
* **Persistence:** Implement `DockSerializer` to save/load coordinates ($X, Y$) and docking states to `layout.json`.

## II. "Deep Space" Visual Standards (The Aesthetic)
* **Zero-Chrome Floating Windows:** Use `ExtendClientAreaToDecorationsHint="True"` and `NoChrome` for all "torn-out" panes.
* **Neon Partitions:** Override `ProportionalStackPanelSplitter` to be a 2px translucent line that glows **Neon Cyan** on hover.
* **Tab Fairness:** Enforce a **250px minimum width**; any pane smaller than this will force new logs into a **Tab Merge** instead of a new split.
* **Overlay Safety:** All spillover menus must use `MenuFlyout` with `ZIndex="100"` and `ClipToBounds="False"` to prevent clipping in the dock layer.

## III. Pane-Level HUD Integration (The Control)
* **Hierarchical Sync:** Move **Follow** and **Time-Sync** toggles from the global toolbar to the individual **Pane Headers**.
* **Local Filter:** Add a localized regex filter to each header for pane-specific forensic analysis.
* **Temporal Anchoring:** Synced panes scroll to match the **timestamp center** of the active pane rather than a pixel-ratio.

## IV. Multi-Monitor "Explosion" (The Workflow)
* **Auto-Detection:** Use the `MonitorManager` to identify all connected hardware and their `WorkingArea`.
* **The Explode Trigger:** A single button click iterates through the workspace and distributes unpinned panes to secondary monitors.
* **Window Hoisting:** Floating windows maintain their connection to the global **Ollama RCA** and **Time-Sync** message buses regardless of monitor position.

### Implementation notes (current API)

* **Explosion:** Uses Dock.Model's `Factory.SplitToWindow(ownerDock, doc, x, y, w, h)`. There is no `CreateHostWindow(pane)`; tear-off is done via `SplitToWindow`. Position and size must be in **logical pixels (DIPs)**; `WorkingArea` is device pixels so coordinates are scaled by `Screen.Scaling`.
* **Factory type:** The app uses `NovaLogDockFactory` (extends Dock.Model `Factory`), not `IDockFactory`.
* **Persistence:** Per-tab Dock layout is stored in `WorkspaceTabLayout.DockLayoutJson` (full Dock JSON per tab in workspace.json). Single layout.json still used for the active layout path where applicable.
* **Temporal anchoring:** Implemented: active pane broadcasts viewport center line's timestamp; linked panes call `SeekToTimestamp` and scroll the nearest line to center (see `WorkspaceViewModel.HandleTimeChanged` and `LogViewViewModel.SeekToTimestamp`).

