# Dock Explode and Docking Stabilization

Single reference for issues and resolutions.

## Issue / resolution table

| Issue                      | Root cause                                                | Resolution                                                                                    |
| -------------------------- | --------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Panes vanish on Explode    | SplitToWindow detaches doc; host may not be created/shown | Manual hoist: pass root.ActiveDockable (targetWindowOwner) to SplitToWindow; no Content replace |
| Z-order / dropdown clipped | Header Border clips overflow                              | HeaderBorder: ClipToBounds="False" in LogViewPanel.axaml                                      |
| Invisible splitters        | Splitter background matches panel background              | DockNeon: 1px Neon Cyan line for ProportionalStackPanelSplitter (and optional laser on hover) |

## Explosion logic

Explode maps the list of log-view documents to physical screens and, per document, calls the Dock factory’s `SplitToWindow(targetWindowOwner, doc, x, y, w, h)` where `targetWindowOwner` is the root’s `ActiveDockable` (same as `DockService.DockDockableIntoWindow`). Position and size are computed from each target screen’s working area in DIPs (scaled by `Screen.Scaling`). Documents are processed in reverse order to avoid stale owner references when the layout mutates. The first document remains in the main window; the rest are moved to native floating HostWindows so panes stay visible and redockable.

## Chrome (floating windows)

Floating HostWindows no longer have their `Content` replaced in code. Chrome (Pin, Opacity, Close) is injected only when `host.Content` is already a `Panel` (e.g. theme Grid), by adding the control bar as a child. This avoids breaking the Dock template and HostWindowLocator bindings.

## Next steps (HUD aesthetic)

- **Laser splitters:** ProportionalStackPanelSplitter can be made nearly invisible until hover (e.g. transparent or very low opacity by default, AccentBrush on `:pointerover`) if a “laser” look is desired.
- **Synced highlighting:** Ensure clicking a line in a torn-out window still triggers `NotifyViewportScrolled` so WorkspaceViewModel and time-sync stay in sync across floating panes.
