# Explode to Monitors — Implementation Summary

This document describes how **Explode to Monitors** is implemented so you can review and debug why panes disappear when using it.

---

## 1. User action and entry point

- **UI:** Toolbar button in [MainWindow.axaml](NovaLog.Avalonia/Views/MainWindow.axaml) (around line 88):
  - `Command="{Binding ExplodeCommand}"`
  - Tooltip: "Explode to monitors — Distribute panes to secondary screens"

- **Command:** [MainWindowViewModel.cs](NovaLog.Avalonia/ViewModels/MainWindowViewModel.cs):
  - `ExplodeCommand` calls `Explode()`.
  - `Explode()` requires `_monitorManager != null` and `Workspace.DockFactory != null`, then calls:
    - `_monitorManager.ExplodeToMonitors(Workspace, Workspace.DockFactory)`.

- **MonitorManager creation:** `_monitorManager` is set in `SetMainWindow(Window)`, which is invoked from **MainWindow.OnWindowOpened** (MainWindow.axaml.cs, ~line 283). So Explode only works after the main window has fired `Opened`.

---

## 2. MonitorManager.ExplodeToMonitors

**File:** [MonitorManager.cs](NovaLog.Avalonia/Services/MonitorManager.cs)

**Steps:**

1. **Guard:** If `workspace.Layout` is null, return.
2. **Screens:** Resolve current screen (`ScreenFromWindow(mainWindow)` or Primary). Build list of *secondary* screens (all screens whose `WorkingArea` is not the current screen’s).
3. **Guard:** If there are no secondary screens, return (no change).
4. **Documents:** `docs = DockLayoutHelper.GetAllDocuments(workspace.Layout)` — recursive collect of all `LogViewDocument` in the layout tree.
5. **Guard:** If `docs.Count <= 1`, return (keep at least one in main window).
6. **Loop (reverse order, index `docs.Count - 1` down to `1`):**
   - `doc = docs[i]`
   - `ownerDock = doc.Owner as IDock`; if null, skip.
   - Pick a secondary screen (round-robin via `screenIndex`).
   - From `screen.WorkingArea` (device pixels) and `screen.Scaling`, compute logical (DIP) position and size:
     - `x, y`: offset 5% from top-left, then divided by scale.
     - `w, h`: 90% of working area width/height, divided by scale.
   - Call **`factory.SplitToWindow(ownerDock, doc, x, y, w, h)`**.

So our code only:
- Decides *which* documents to move (all but the first),
- Picks *which* secondary screen for each,
- Converts *coordinates* to DIPs,
- Calls the Dock library’s **`SplitToWindow`** once per document.

Everything about *removing* the document from the layout and *creating/showing* a floating window is inside **Dock.Model** / **Dock.Avalonia** (we do not implement it).

---

## 3. Dock layout and document collection

- **Layout:** `Workspace.Layout` is an `IRootDock` (from [NovaLogDockFactory.CreateLayout](NovaLog.Avalonia/Docking/NovaLogDockFactory.cs)). Structure is: Root → one or more children (e.g. `IDocumentDock`, `IProportionalDock`). Documents live inside `IDocumentDock.VisibleDockables` (and possibly nested docks if the user has split panes).

- **GetAllDocuments:** [DockLayoutHelper.GetAllDocuments](NovaLog.Avalonia/Docking/DockLayoutHelper.cs) walks the layout tree (via `VisibleDockables`), collects every `LogViewDocument`, and returns them in tree order. So `docs[0]` is the “first” document in that order; we leave it in place and explode `docs[1]` … `docs[n-1]`.

- **Owner:** For each `doc`, `doc.Owner` is the `IDock` that currently contains it (e.g. an `IDocumentDock`). We pass that as `ownerDock` to `SplitToWindow` so the library knows where to remove the document from.

---

## 4. Factory and DockControl setup

- **Factory:** [NovaLogDockFactory](NovaLog.Avalonia/Docking/NovaLogDockFactory.cs) extends Dock.Model’s **`Factory`** (from `Dock.Model.Mvvm`). We do **not** override `SplitToWindow`; we use the base implementation.

- **DockControl:** In [MainWindow.axaml](NovaLog.Avalonia/Views/MainWindow.axaml), the main content is a **DockControl** with:
  - `Layout="{Binding Workspace.Layout}"`
  - `Factory="{Binding Workspace.DockFactory}"`
  - `InitializeLayout="True"`
  - **`EnableManagedWindowLayer="False"`**  
  So floating windows are intended to be **native** `HostWindow`s, not hosted in the managed layer.

- **Host window creation:** When the Dock library creates a floating window, it uses a **HostWindowLocator** (and related) that the **DockControl** sets on the Factory during its `Initialize` (when Layout is set). So the same `Workspace.DockFactory` instance that we pass to `ExplodeToMonitors` is the one the DockControl has already configured. Floating windows are created by the Dock library via that locator (e.g. `new HostWindow(...)` when not using the managed layer).

- **OnWindowOpened:** Our factory overrides `OnWindowOpened(IDockWindow)`. When the Dock library opens a new floating window, it calls this; we use it only to attach custom chrome (Pin/Opacity/Close) to the `HostWindow`. We do **not** create or show the window ourselves.

---

## 5. What we do *not* control (Dock library)

- **SplitToWindow implementation:** We never see the source of `Factory.SplitToWindow`. It is in **Dock.Model** (NuGet package). Expected behavior (from typical Dock designs):
  - Remove `doc` from `ownerDock.VisibleDockables`.
  - Create an `IDockWindow` (or similar) for the floating window and set its content/layout to that document.
  - Obtain a host (e.g. `HostWindow`) via the Factory’s **HostWindowLocator** (set by DockControl).
  - Initialize the host with the window model and **show** it (e.g. `Show()` on the Avalonia `Window`).

- **Why panes might “disappear forever”:**
  - The document is removed from the main layout (so it’s gone from the main window).
  - If the library never creates a host, or creates it but never calls `Show()`, or the host is created in a way that doesn’t appear (e.g. wrong parent, zero size, off-screen), the pane would vanish with no visible floating window.
  - With `EnableManagedWindowLayer="False"`, the library should use native `HostWindow`s; if there is a bug or misconfiguration in the library’s path from `SplitToWindow` to `HostWindow.Show()`, that would match “panes disappear forever.”

---

## 6. Single-monitor behavior

- If there are **no secondary screens**, `ExplodeToMonitors` returns immediately and does nothing.
- On a **single monitor**, `secondaryScreens` is empty, so Explode never runs. So the “disappear forever” behavior you see is presumably on a **multi-monitor** setup (or a case where `secondaryScreens` is non-empty).

---

## 7. Files to review when debugging

| File | Role |
|------|------|
| [MainWindow.axaml](NovaLog.Avalonia/Views/MainWindow.axaml) | Explode button, DockControl, `EnableManagedWindowLayer="False"` |
| [MainWindowViewModel.cs](NovaLog.Avalonia/ViewModels/MainWindowViewModel.cs) | `ExplodeCommand`, `SetMainWindow`, `Explode()` |
| [MainWindow.axaml.cs](NovaLog.Avalonia/Views/MainWindow.axaml.cs) | `OnWindowOpened` → `SetMainWindow(this)` |
| [MonitorManager.cs](NovaLog.Avalonia/Services/MonitorManager.cs) | Screen selection, DIP conversion, loop, `SplitToWindow` calls |
| [DockLayoutHelper.cs](NovaLog.Avalonia/Docking/DockLayoutHelper.cs) | `GetAllDocuments` (who gets exploded) |
| [NovaLogDockFactory.cs](NovaLog.Avalonia/Docking/NovaLogDockFactory.cs) | Factory bound to DockControl; `OnWindowOpened` for chrome only |
| **Dock.Model** (NuGet 11.3.11.22) | Defines `SplitToWindow`; actual remove/create/show logic |

---

## 8. Suggested next steps for debugging

1. **Confirm multi-monitor:** Add temporary debug (e.g. `System.Diagnostics.Debug.WriteLine`) in `ExplodeToMonitors` to log: `secondaryScreens.Count`, `docs.Count`, and each `(ownerDock, doc, x, y, w, h)` before calling `SplitToWindow`. That verifies we’re calling the library as intended.
2. **Inspect Dock.Model:** From the Dock GitHub repo or NuGet package, open the **Factory** implementation (e.g. `Factory.cs` or equivalent) and trace **SplitToWindow**: does it add an entry to `IRootDock.Windows` (or similar), and who is responsible for creating the Avalonia `Window` and calling `Show()`?
3. **DockControl and HostWindowLocator:** In Dock.Avalonia’s **DockControl**, find where it sets `Factory.HostWindowLocator` / `DefaultHostWindowLocator` and where it reacts to new entries in the root’s Windows collection (if any). Confirm that when `SplitToWindow` adds a window, the DockControl (or a service) creates a `HostWindow` and shows it.
4. **Break on SplitToWindow:** Set a breakpoint in our code on `factory.SplitToWindow(...)`. Step into the Dock.Model implementation and see whether it creates a host and calls `Show()`, or only updates the model.

---

## 9. Package versions (for reference)

- **Dock.Avalonia**, **Dock.Model**, **Dock.Model.Mvvm**, **Dock.Avalonia.Themes.Simple**: 11.3.11.22  
- **Avalonia**: 11.3.12  

If you later try a different Dock version, note it here when reviewing.
