using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using System;
using System.Linq;

namespace NovaLog.Avalonia.Views;

public partial class SourceManagerPanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? AddFileRequested;
    public event EventHandler<RoutedEventArgs>? AddFolderRequested;

    private Point _dragStartPoint;
    private bool _dragPending;
    private PointerPressedEventArgs? _dragPressArgs;
    private string? _dragSourceId;

    public SourceManagerPanel()
    {
        InitializeComponent();

        // Attach drag handlers with handledEventsToo to intercept events before ListBox consumes them
        var listBox = this.FindControl<ListBox>("SourceListBox");
        if (listBox != null)
        {
            listBox.AddHandler(PointerPressedEvent, OnSourcePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            listBox.AddHandler(PointerMovedEvent, OnSourcePointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            listBox.AddHandler(PointerReleasedEvent, OnSourcePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    public void OnAddFileClick(object? sender, RoutedEventArgs e) => AddFileRequested?.Invoke(this, e);
    public void OnAddFolderClick(object? sender, RoutedEventArgs e) => AddFolderRequested?.Invoke(this, e);

    private void OnSourceDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SourceManagerViewModel vm && vm.SelectedSource != null)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                vm.LoadSelectedCommand.Execute(null);
            else
                vm.OpenInNewTabCommand.Execute(null);
        }
    }

    private void OnSourcePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DRAG] PointerPressed fired, sender={sender?.GetType().Name}");

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragPending = true;
            _dragPressArgs = e;
            _dragSourceId = null;

            System.Diagnostics.Debug.WriteLine($"[DRAG] Left button pressed, dragStartPoint={_dragStartPoint}");

            if (DataContext is SourceManagerViewModel vm)
            {
                var hitSource = TryGetSourceFromPointerEvent(e);
                if (hitSource != null)
                {
                    vm.SelectedSource = hitSource;
                    _dragSourceId = hitSource.SourceId;
                    System.Diagnostics.Debug.WriteLine($"[DRAG] Hit source: {hitSource.DisplayName}, ID={_dragSourceId}");
                }
                else if (vm.SelectedSource != null)
                {
                    _dragSourceId = vm.SelectedSource.SourceId;
                    System.Diagnostics.Debug.WriteLine($"[DRAG] Using selected source: {vm.SelectedSource.DisplayName}, ID={_dragSourceId}");
                }
            }
        }

        // Don't mark as handled - let ListBox process for selection
        // e.Handled = false;
    }

    private async void OnSourcePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragPending || _dragPressArgs == null)
        {
            // Not in drag mode, don't log spam
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[DRAG] PointerMoved fired, pending={_dragPending}");

        var point = e.GetPosition(this);
        var diff = _dragStartPoint - point;

        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            System.Diagnostics.Debug.WriteLine($"[DRAG] Movement threshold exceeded, starting drag operation");

            _dragPending = false;
            var pressArgs = _dragPressArgs;
            _dragPressArgs = null;

            if (DataContext is SourceManagerViewModel vm)
            {
                var sourceId = _dragSourceId ?? vm.SelectedSource?.SourceId;
                var source = sourceId != null
                    ? vm.Sources.FirstOrDefault(s => s.SourceId == sourceId)
                    : null;

                System.Diagnostics.Debug.WriteLine($"[DRAG] Source: {source?.DisplayName}, IsMissing: {source?.IsMissing}");

                if (source != null && !source.IsMissing)
                {
                    try
                    {
                        var transfer = new DataTransfer();
                        // Add custom format using string-based format ID
                        var customFormat = DataFormat.CreateStringApplicationFormat(AppConstants.DragDropSourceFormatId);
                        System.Diagnostics.Debug.WriteLine($"[DRAG] Created format with ID: {AppConstants.DragDropSourceFormatId}");

                        transfer.Add(DataTransferItem.Create(customFormat, source.SourceId));
                        // Add text fallback
                        transfer.Add(DataTransferItem.CreateText($"novalog-source:{source.SourceId}"));

                        System.Diagnostics.Debug.WriteLine($"[DRAG] Starting DoDragDropAsync for source: {source.SourceId}");
                        var result = await DragDrop.DoDragDropAsync(pressArgs, transfer, DragDropEffects.Copy);
                        System.Diagnostics.Debug.WriteLine($"[DRAG] DoDragDropAsync completed with result: {result}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DRAG] ERROR: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[DRAG] Stack: {ex.StackTrace}");
                    }
                }
            }
        }
    }

    private void OnSourcePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DRAG] PointerReleased fired");
        _dragPending = false;
        _dragPressArgs = null;
        _dragSourceId = null;

        // Don't mark as handled - let ListBox process
        // e.Handled = false;
    }

    private static SourceItemViewModel? TryGetSourceFromPointerEvent(PointerEventArgs e)
    {
        if (e.Source is not Visual sourceVisual)
            return null;

        var listBoxItem = sourceVisual.FindAncestorOfType<ListBoxItem>();
        return listBoxItem?.DataContext as SourceItemViewModel;
    }
}

