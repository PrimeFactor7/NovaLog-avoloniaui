# Drag-Drop Test Application

This is a minimal Avalonia application to test and verify drag-drop functionality.

## Purpose

This test app was created to isolate and verify drag-drop behavior before integrating into the main NovaLog application.

## How to Run

```bash
cd NovaLog-avoloniaui/DragDropTest
dotnet run
```

## How to Test

1. Launch the application
2. You'll see two panels:
   - **Left Panel (DRAG SOURCE)**: Contains a green box labeled "DRAG ME"
   - **Right Panel (DROP TARGET)**: Contains a gray box labeled "DROP HERE"

3. Click and hold on the green "DRAG ME" box
4. Move your mouse (drag) towards the right panel
5. The status bar will show progress messages
6. When you hover over the "DROP HERE" box, it should turn green
7. Release the mouse button to complete the drop
8. If successful, the drop box will stay green and display "✓ DROPPED!"

## What It Tests

- Custom data format drag-drop using `DataFormat.CreateStringApplicationFormat()`
- Fallback to plain text format
- DragEnter, DragOver, DragLeave, and Drop events
- Visual feedback during drag operations
- Status messages for debugging

## Implementation Details

### Drag Source
- Uses PointerPressed, PointerMoved, and PointerReleased events
- Requires 4px movement threshold to initiate drag
- Creates custom format: `application/x-dragdroptest-item`
- Also adds text format as fallback: `dragdroptest:test-item-id-12345`

### Drop Target
- Handles all drag-drop events (DragEnter, DragOver, DragLeave, Drop)
- Checks for custom format first, falls back to text format
- Provides visual feedback (green highlight on valid drop)
- Shows status messages in real-time

## Expected Output

Console output should show:
```
Ready. Drag the green box to the drop zone.
Mouse down - move to start dragging...
Starting drag operation...
Data prepared. Starting DoDragDrop...
DragEnter fired!
DragEnter - HasCustom: True, HasText: True
DROP EVENT FIRED!
Custom format found: test-item-id-12345
SUCCESS! Received item: test-item-id-12345
```

## Key Findings

This test proves that:
1. Custom DataFormat objects work correctly when created with `DataFormat.CreateStringApplicationFormat()`
2. The format ID must match EXACTLY between source and target
3. Using `e.Data` (though deprecated) provides synchronous access to drag data
4. Both custom format and text fallback work correctly
