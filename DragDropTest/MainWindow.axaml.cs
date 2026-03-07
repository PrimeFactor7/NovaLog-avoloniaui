using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;

namespace DragDropTest;

public partial class MainWindow : Window
{
    private Point _dragStartPoint;
    private bool _isDragPending;
    private PointerPressedEventArgs? _dragPressArgs;

    // Custom format ID for our drag data
    // Must be a simple identifier without special characters or slashes
    private const string CustomFormatId = "DragDropTestItem";

    public MainWindow()
    {
        InitializeComponent();

        // Setup drop target event handlers
        DropRect.AddHandler(DragDrop.DragEnterEvent, OnDropRectDragEnter);
        DropRect.AddHandler(DragDrop.DragOverEvent, OnDropRectDragOver);
        DropRect.AddHandler(DragDrop.DragLeaveEvent, OnDropRectDragLeave);
        DropRect.AddHandler(DragDrop.DropEvent, OnDropRectDrop);

        UpdateStatus("Ready. Drag the green box to the drop zone.");
    }

    // === DRAG SOURCE EVENTS ===

    private void OnDragRectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragPending = true;
            _dragPressArgs = e;

            UpdateStatus("Mouse down - move to start dragging...");
        }
    }

    private async void OnDragRectPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragPending || _dragPressArgs == null)
            return;

        var currentPoint = e.GetPosition(this);
        var diff = _dragStartPoint - currentPoint;

        // Check if we've moved enough to start a drag operation
        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            _isDragPending = false;
            var pressArgs = _dragPressArgs;
            _dragPressArgs = null;

            UpdateStatus("Starting drag operation...");

            try
            {
                // Create the data transfer object
                var dataTransfer = new DataTransfer();

                // Method 1: Add custom format with DataFormat object
                var customFormat = DataFormat.CreateStringApplicationFormat(CustomFormatId);
                dataTransfer.Add(DataTransferItem.Create(customFormat, "test-item-id-12345"));

                // Method 2: Also add as plain text for debugging
                dataTransfer.Add(DataTransferItem.CreateText("dragdroptest:test-item-id-12345"));

                UpdateStatus("Data prepared. Starting DoDragDrop...");

                // Perform the drag-drop operation
                var result = await DragDrop.DoDragDropAsync(pressArgs, dataTransfer, DragDropEffects.Copy);

                UpdateStatus($"Drag completed with result: {result}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"ERROR during drag: {ex.Message}");
            }
        }
    }

    private void OnDragRectPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragPending = false;
        _dragPressArgs = null;
    }

    // === DROP TARGET EVENTS ===

#pragma warning disable CS0618 // Type or member is obsolete
    private void OnDropRectDragEnter(object? sender, DragEventArgs e)
    {
        UpdateStatus("DragEnter fired!");

        // Check if we have our custom format
        bool hasCustomFormat = e.Data.Contains(CustomFormatId);
        bool hasText = !string.IsNullOrWhiteSpace(e.Data.GetText());

        UpdateStatus($"DragEnter - HasCustom: {hasCustomFormat}, HasText: {hasText}");

        if (hasCustomFormat || hasText)
        {
            e.DragEffects = DragDropEffects.Copy;
            DropRect.Background = new SolidColorBrush(Color.Parse("#00FF41"));
            DropRect.BorderBrush = new SolidColorBrush(Color.Parse("#00FF41"));
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDropRectDragOver(object? sender, DragEventArgs e)
    {
        // Check if we can accept the drop
        bool hasCustomFormat = e.Data.Contains(CustomFormatId);
        bool hasText = !string.IsNullOrWhiteSpace(e.Data.GetText());

        if (hasCustomFormat || hasText)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDropRectDragLeave(object? sender, DragEventArgs e)
    {
        UpdateStatus("DragLeave fired!");

        DropRect.Background = new SolidColorBrush(Color.Parse("#404040"));
        DropRect.BorderBrush = new SolidColorBrush(Color.Parse("#666666"));

        e.Handled = true;
    }

    private void OnDropRectDrop(object? sender, DragEventArgs e)
    {
        UpdateStatus("DROP EVENT FIRED!");

        try
        {
            // Try to get custom format first
            string? itemId = null;

            if (e.Data.Contains(CustomFormatId))
            {
                var customData = e.Data.Get(CustomFormatId);
                itemId = customData as string;
                UpdateStatus($"Custom format found: {itemId}");
            }

            // Fallback to text
            if (string.IsNullOrEmpty(itemId))
            {
                var text = e.Data.GetText();
                if (!string.IsNullOrWhiteSpace(text) && text.StartsWith("dragdroptest:"))
                {
                    itemId = text.Substring("dragdroptest:".Length);
                    UpdateStatus($"Text format found: {itemId}");
                }
            }

            if (!string.IsNullOrEmpty(itemId))
            {
                // Success!
                DropRect.Background = new SolidColorBrush(Color.Parse("#00FF41"));
                DropRect.BorderBrush = new SolidColorBrush(Color.Parse("#00FF41"));
                DropText.Text = "✓ DROPPED!";
                DropText.Foreground = new SolidColorBrush(Colors.Black);

                UpdateStatus($"SUCCESS! Received item: {itemId}");
            }
            else
            {
                UpdateStatus("No valid data found in drop!");
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            UpdateStatus($"ERROR in drop handler: {ex.Message}");
        }
    }
#pragma warning restore CS0618 // Type or member is obsolete

    // === HELPER METHODS ===

    private void UpdateStatus(string message)
    {
        StatusText.Text = $"Status: {message}";

        // Also log to console for debugging
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }
}
