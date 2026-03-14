# NovaLog Master Specification

## Section 11: Pane-Level Sync & Highlight

- **Interaction:** Clicking any row in a synced pane broadcasts its `DateTime` to the workspace.
- **Highlighting:** Synced panes must highlight the row with the mathematically closest timestamp (nearest-neighbor).
- **Scroll Logic:** Use **Time-Anchored Scrolling.** Viewports stay aligned by timestamp center, not pixel ratio. When you scroll Pane A, other linked panes scroll so the center-most row of their viewport matches the timestamp of the center-most row in Pane A. When you click a line in Pane A, other linked panes scroll to bring the nearest-timestamp line into view and center it (BringIntoView).
- **Sync Toggle:** ToggleButton in pane header with Codicon **link** (`&#xea5b;`) when synced (accent) and **link-external** (`&#xea5c;`) when unlinked (dim). Ctrl+L toggles local sync; Global Follow button toggles all.
- **Keyboard:** Ctrl+L toggles local sync; Global Follow button toggles all panes.

### Pane Header Components

| Feature | Logic | Icon (Codicon) |
|--------|--------|----------------|
| **Local Search** | Filter just the current pane (search within a search). | `&#xeb0f;` |
| **Freeze Frame** | Stop the UI stream for this pane for analysis without affecting other panes. | `&#xea71;` |
| **Grid/Text Switch** | Toggle forensic Grid Mode vs high-speed Text View. | `&#xea73;` |
| **Pop-out** | Explode this pane to a new window/monitor (TradingView style). | `&#xeb2d;` |

### Nearest-Neighbor Timestamp

When Pane A is at 12:00:01.500 and Pane B only has 12:00:01.490 and 12:00:01.510, the sync logic picks the closest line (12:00:01.510 in this case) to highlight and scroll to.
