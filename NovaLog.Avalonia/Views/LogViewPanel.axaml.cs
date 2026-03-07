#pragma warning disable CS0618
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using global::Avalonia.Interactivity;
using NovaLog.Avalonia.Controls;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;
using AvClipboard = global::Avalonia.Input.Platform.IClipboard;
using System.Linq;
using System;

namespace NovaLog.Avalonia.Views;

public partial class LogViewPanel : UserControl
{
    private ItemsRepeater? _repeater;
    private ScrollViewer? _scroller;

    public event Action<string>? NewFileDropped;
    public event Action<string, bool>? SplitRequested;
    
    public event Action<string>? SourceIdDropped;
    public event Action<string, bool>? SourceIdSplitRequested;

    public LogViewPanel()
    {
        InitializeComponent();

        MenuToggleBookmark.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
                vm.ToggleBookmark();
        };
        MenuCopyLine.Click += async (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
            {
                var text = vm.GetCurrentLineText();
                if (text is not null)
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard is not null)
                        await clipboard.SetTextAsync(text);
                }
            }
        };
        MenuCopyFormattedJson.Click += async (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
            {
                var json = vm.GetFormattedJson();
                if (json is not null)
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard is not null)
                        await clipboard.SetTextAsync(json);
                }
            }
        };
        MenuGoToTimestamp.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
                ShowGoToTimestampDialog(vm);
        };
        MenuTimeTravel.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
                vm.TimeTravelCommand.Execute(null);
        };
        MenuPinTimestamp.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
                vm.PinCurrentTimestamp();
        };
        MenuHighlightRules.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm)
                ShowHighlightRulesDialog(vm);
        };

        _repeater = this.FindControl<ItemsRepeater>("LogRepeater");
        _scroller = this.FindControl<ScrollViewer>("LogScroller");

        if (_repeater is not null)
        {
            _repeater.ElementClearing += (s, e) =>
            {
                if (e.Element is LogLineRow row)
                    row.ResetVisualState();
            };
        }

        if (_scroller is not null)
        {
            _scroller.ScrollChanged += (s, e) =>
            {
                if (DataContext is LogViewViewModel vm)
                {
                    int topIdx = (int)(_scroller.Offset.Y / 18.0);
                    int visibleCount = (int)(_scroller.Viewport.Height / 18.0);
                    vm.SetCurrentLine(topIdx + visibleCount / 2);

                    var minimap = this.FindControl<LogMinimap>("Minimap");
                    if (minimap is not null)
                    {
                        minimap.ViewportLine = topIdx;
                        minimap.ViewportHeightLines = visibleCount;
                    }

                    // Auto-disable follow mode when user manually scrolls up/away from end
                    if (vm.IsFollowMode && _scroller.Extent.Height > 0)
                    {
                        double maxScroll = _scroller.Extent.Height - _scroller.Viewport.Height;
                        double currentScroll = _scroller.Offset.Y;
                        // If user is scrolled more than 2 rows away from the bottom, disable follow mode
                        if (maxScroll - currentScroll > 36.0) // 2 rows * 18px
                        {
                            vm.IsFollowMode = false;
                        }
                    }
                }
            };
        }

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DROP] OnDragEnter fired");

        var sourceId = TryExtractSourceId(e.Data);
        var hasFiles = e.Data.Contains(DataFormats.Files);

        if (hasFiles || sourceId != null)
        {
            // Show overlay ONCE on enter
            if (!DropOverlay.IsVisible)
            {
                DropOverlay.IsVisible = true;
            }
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var sourceId = TryExtractSourceId(e.Data);
        var hasFiles = e.Data.Contains(DataFormats.Files);

        if (hasFiles || sourceId != null)
        {
            // Only update zone position - don't touch visibility
            var pt = e.GetPosition(DropOverlay);
            DropOverlay.ActiveZone = DropOverlay.CalculateZone(pt);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DROP] OnDragLeave fired");

        DropOverlay.IsVisible = false;
        DropOverlay.ActiveZone = DropZone.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var zone = DropOverlay.ActiveZone;
        DropOverlay.ActiveZone = DropZone.None;

        if (zone == DropZone.None) zone = DropZone.Center;

        bool horizontal = zone is DropZone.Left or DropZone.Right;

        var sourceId = TryExtractSourceId(e.Data);
        if (!string.IsNullOrEmpty(sourceId))
        {
            if (zone == DropZone.Center)
                SourceIdDropped?.Invoke(sourceId);
            else
                SourceIdSplitRequested?.Invoke(sourceId, horizontal);

            e.Handled = true;
            return;
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files == null) return;
            var firstFile = files.FirstOrDefault();
            if (firstFile == null) return;
            var path = firstFile.Path.LocalPath;

            if (zone == DropZone.Center)
            {
                NewFileDropped?.Invoke(path);
            }
            else
            {
                SplitRequested?.Invoke(path, horizontal);
            }

            e.Handled = true;
        }
    }

    private static string? TryExtractSourceId(IDataObject data)
    {
        System.Diagnostics.Debug.WriteLine($"[DROP] TryExtractSourceId called");

        var hasCustomFormat = data.Contains(AppConstants.DragDropSourceFormatId);
        System.Diagnostics.Debug.WriteLine($"[DROP] HasCustomFormat ({AppConstants.DragDropSourceFormatId}): {hasCustomFormat}");

        if (hasCustomFormat
            && data.Get(AppConstants.DragDropSourceFormatId) is string sourceId
            && !string.IsNullOrWhiteSpace(sourceId))
        {
            System.Diagnostics.Debug.WriteLine($"[DROP] Extracted from custom format: {sourceId}");
            return sourceId;
        }

        var text = data.GetText();
        System.Diagnostics.Debug.WriteLine($"[DROP] Text data: {text}");

        const string prefix = "novalog-source:";
        if (!string.IsNullOrWhiteSpace(text) && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            var extracted = text[prefix.Length..].Trim();
            System.Diagnostics.Debug.WriteLine($"[DROP] Extracted from text: {extracted}");
            return extracted;
        }

        System.Diagnostics.Debug.WriteLine($"[DROP] No source ID found");
        return null;
    }

    private async void ShowGoToTimestampDialog(LogViewViewModel vm)
    {
        var dialog = new GoToTimestampDialog();
        var parent = TopLevel.GetTopLevel(this) as Window;
        if (parent != null)
        {
            var result = await dialog.ShowDialog<DateTime?>(parent);
            if (result.HasValue)
                vm.SeekToTimestamp(result.Value);
        }
    }

    private async void ShowHighlightRulesDialog(LogViewViewModel vm)
    {
        var dialog = new HighlightRulesDialog
        {
            DataContext = new HighlightRulesViewModel(vm.HighlightRules)
        };
        var parent = TopLevel.GetTopLevel(this) as Window;
        if (parent != null)
        {
            await dialog.ShowDialog(parent);
            vm.HighlightRules.Clear();
            foreach (var r in (dialog.DataContext as HighlightRulesViewModel)!.Rules)
                vm.HighlightRules.Add(r);
            
            InvalidateVisual();
            _repeater?.InvalidateVisual();
        }
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LogViewViewModel vm)
        {
            vm.ScrollToEndRequested += () =>
            {
                Dispatcher.UIThread.Post(() => _scroller?.ScrollToEnd());
            };

            vm.ScrollToLineRequested += lineIndex =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_scroller is null) return;
                    _scroller.Offset = new global::Avalonia.Vector(
                        _scroller.Offset.X,
                        lineIndex * 18.0);
                });
            };

            var minimap = this.FindControl<LogMinimap>("Minimap");
            if (minimap is not null)
            {
                minimap.NavIndex = vm.NavIndex;
                minimap.ScrollRequested += line =>
                {
                    vm.IsFollowMode = false;
                    vm.RequestScrollToLine(line);
                };

                vm.NavIndex.IndicesChanged += () =>
                    Dispatcher.UIThread.Post(() => minimap.InvalidateVisual());
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not LogViewViewModel vm)
        {
            base.OnKeyDown(e);
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.F)
        {
            vm.ToggleFilter();
            if (vm.Filter.IsVisible)
                FilterPanelView?.FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.Filter.IsVisible)
        {
            vm.Filter.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            bool forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            vm.NavigateSearchHit(forward);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            bool forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            vm.NavigateBookmark(forward);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.B)
        {
            vm.ToggleBookmark();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.G)
        {
            ShowGoToTimestampDialog(vm);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.T)
        {
            vm.TimeTravelCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.L)
        {
            // Ctrl+L — toggle sync link
            vm.IsLinked = !vm.IsLinked;
            e.Handled = true;
        }
        else if (ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.P)
        {
            // Ctrl+Shift+P — pin current timestamp for sync
            vm.PinCurrentTimestamp();
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.NavigateError(forward: true);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.NavigateError(forward: false);
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }
}

