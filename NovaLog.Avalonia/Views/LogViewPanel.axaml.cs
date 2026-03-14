#pragma warning disable CS0618
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using global::Avalonia.Interactivity;
using System.ComponentModel;
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
    private LogMinimap? _minimap;
    private global::Avalonia.Controls.TreeDataGrid? _logGrid;
    private ScrollViewer? _gridScroller;
    private ScrollViewer? _gridScrollerHookedInstance;
    private LogViewViewModel? _attachedViewModel;
    private FilterPanelViewModel? _attachedFilterViewModel;
    private bool _minimapRefreshPending;
    private double _filterPanelHeight = 220;

    // Performance throttling for center row detection
    private DateTime _lastCenterDetection = DateTime.MinValue;
    private int _lastDetectedCenterLine = -1;
    private const int CenterDetectionThrottleMs = 50; // Max 20 detections/sec

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

        // Grid context menu handlers (mirror text mode context menu)
        GridMenuCopyLine.Click += async (_, _) =>
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
        GridMenuCopyFormattedJson.Click += async (_, _) =>
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
        GridMenuToggleBookmark.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm) vm.ToggleBookmark();
        };
        GridMenuGoToTimestamp.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm) ShowGoToTimestampDialog(vm);
        };
        GridMenuTimeTravel.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm) vm.TimeTravelCommand.Execute(null);
        };
        GridMenuPinTimestamp.Click += (_, _) =>
        {
            if (DataContext is LogViewViewModel vm) vm.PinCurrentTimestamp();
        };

        _repeater = this.FindControl<ItemsRepeater>("LogRepeater");
        _scroller = this.FindControl<ScrollViewer>("LogScroller");
        _minimap = this.FindControl<LogMinimap>("Minimap");
        _logGrid = this.FindControl<global::Avalonia.Controls.TreeDataGrid>("LogGrid");
        if (_logGrid is not null)
        {
            _logGrid.SizeChanged += (_, _) => FitMessageColumn();
            _logGrid.TemplateApplied += (_, _) => EnsureGridScroller();
            _logGrid.AddHandler(InputElement.PointerPressedEvent, OnGridPointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            // Mouse wheel up on grid → disable follow
            _logGrid.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                if (e.Delta.Y > 0 && DataContext is LogViewViewModel { IsFollowMode: true } vm)
                    vm.IsFollowMode = false;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        FilterPanelView.SizeChanged += OnFilterPanelSizeChanged;

        if (_repeater is not null)
        {
            _repeater.ElementClearing += (s, e) =>
            {
                if (e.Element is LogLineRow row)
                    row.ResetVisualState();
            };
        }

        // Register on the ScrollViewer — it has a Background so it always receives
        // hit tests, even in gaps between rows where the ItemsRepeater is transparent.
        if (_scroller is not null)
        {
            _scroller.AddHandler(InputElement.PointerPressedEvent, OnLogPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            // Mouse wheel up → disable follow (fires before ScrollChanged, reliable direction)
            _scroller.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                if (e.Delta.Y > 0 && DataContext is LogViewViewModel { IsFollowMode: true } vm)
                    vm.IsFollowMode = false;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        if (_scroller is not null)
        {
            _scroller.ScrollChanged += (s, e) =>
            {
                if (DataContext is LogViewViewModel vm)
                {
                    int topIdx = (int)(_scroller.Offset.Y / LogLineRow.RowHeight);
                    int visibleCount = (int)(_scroller.Viewport.Height / LogLineRow.RowHeight);
                    vm.SetCurrentLine(topIdx + visibleCount / 2);

                    if (_minimap is not null)
                        UpdateMinimapViewport(_minimap);

                    // Scrollbar drag / keyboard scroll up → disable follow
                    if (vm.IsFollowMode && e.OffsetDelta.Y < -0.1)
                    {
                        double maxScroll = _scroller.Extent.Height - _scroller.Viewport.Height;
                        if (maxScroll - _scroller.Offset.Y > 0.5)
                            vm.IsFollowMode = false;
                    }

                    // Time sync: broadcast center timestamp to linked panes
                    vm.NotifyViewportScrolled();
                }
            };

            _scroller.SizeChanged += (_, _) =>
            {
                if (_minimap is not null)
                    UpdateMinimapViewport(_minimap);
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

        if (_attachedViewModel is not null)
        {
            _attachedViewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            _attachedViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            _attachedViewModel.NavIndex.IndicesChanged -= OnNavIndexChanged;
            _attachedViewModel.SelectedLineChanged -= OnSelectedLineChanged;
            _attachedViewModel.RowVisualsChanged -= OnRowVisualsChanged;
            _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_attachedFilterViewModel is not null)
            _attachedFilterViewModel.PropertyChanged -= OnFilterViewModelPropertyChanged;

        if (_minimap is not null)
        {
            _minimap.ScrollRequested -= OnMinimapScrollRequested;
            _minimap.WheelScrollRequested -= OnMinimapWheelScroll;
        }

        _attachedViewModel = DataContext as LogViewViewModel;
        _attachedFilterViewModel = _attachedViewModel?.Filter;

        if (_attachedViewModel is not null)
        {
            _attachedViewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _attachedViewModel.ScrollToLineRequested += OnScrollToLineRequested;
            _attachedViewModel.NavIndex.IndicesChanged += OnNavIndexChanged;
            _attachedViewModel.SelectedLineChanged += OnSelectedLineChanged;
            _attachedViewModel.RowVisualsChanged += OnRowVisualsChanged;
            _attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _attachedFilterViewModel?.PropertyChanged += OnFilterViewModelPropertyChanged;

            if (_minimap is not null)
            {
                _minimap.NavIndex = _attachedViewModel.NavIndex;
                UpdateMinimapViewport(_minimap);
                _minimap.ScrollRequested += OnMinimapScrollRequested;
                _minimap.WheelScrollRequested += OnMinimapWheelScroll;
            }

            UpdateFilterLayout(_attachedFilterViewModel?.IsVisible == true);
        }
        else if (_minimap is not null)
        {
            _minimap.NavIndex = null;
            UpdateFilterLayout(false);
        }
    }

    /// <summary>
    /// Finds the line index of the row closest to the vertical center of the grid viewport.
    /// Uses actual materialized rows instead of pixel ratio math, so it works correctly
    /// with variable-height rows (multiline, hierarchical headers).
    /// Falls back to ratio-based estimation if no materialized rows are found.
    /// Throttled to max 20 calls/sec to prevent performance issues during rapid scrolling.
    /// </summary>
    private int FindCenterVisibleGridRow()
    {
        if (_logGrid is null || DataContext is not LogViewViewModel vm)
            return -1;

        // Throttle expensive visual tree walk to max 20 calls/sec (50ms between calls)
        var now = DateTime.UtcNow;
        if ((now - _lastCenterDetection).TotalMilliseconds < CenterDetectionThrottleMs)
            return _lastDetectedCenterLine; // Return cached result

        _lastCenterDetection = now;

        // Get all materialized TreeDataGridRow containers with actual log lines (skip file headers)
        var rows = _logGrid.GetVisualDescendants()
            .OfType<global::Avalonia.Controls.Primitives.TreeDataGridRow>()
            .Where(r => r.DataContext is GridRowViewModel { Line: not null, IsFileHeader: false })
            .ToList();

        if (rows.Count == 0)
        {
            // Fallback: use ratio-based estimation (works for flat grids, approximate for hierarchical)
            var scroll = _logGrid.Scroll;
            if (scroll is not null && scroll.Extent.Height > 0 && vm.TotalLineCount > 0)
            {
                double maxScroll = scroll.Extent.Height - scroll.Viewport.Height;
                if (maxScroll <= 0)
                {
                    _lastDetectedCenterLine = 0; // All content visible
                    return 0;
                }
                double ratio = Math.Clamp(scroll.Offset.Y / maxScroll, 0.0, 1.0);
                int fallbackResult = (int)(ratio * vm.TotalLineCount);
                _lastDetectedCenterLine = fallbackResult;
                return fallbackResult;
            }
            _lastDetectedCenterLine = -1;
            return -1;
        }

        // Find viewport center point
        var gridScroll = _logGrid.Scroll;
        if (gridScroll is null)
            return -1;

        double viewportCenterY = gridScroll.Offset.Y + gridScroll.Viewport.Height / 2.0;

        // Find the row whose vertical center is closest to the viewport center
        global::Avalonia.Controls.Primitives.TreeDataGridRow? centerRow = null;
        double minDistance = double.MaxValue;

        foreach (var row in rows)
        {
            // Get row position relative to scroll content
            var bounds = row.Bounds;
            var transformMatrix = row.TransformToVisual(_logGrid);
            if (!transformMatrix.HasValue)
                continue;

            var topLeft = transformMatrix.Value.Transform(new global::Avalonia.Point(0, 0));
            // topLeft.Y is already in grid coordinates (transform accounts for scroll position)
            double rowCenterY = topLeft.Y + bounds.Height / 2.0;

            double distance = Math.Abs(rowCenterY - viewportCenterY);
            if (distance < minDistance)
            {
                minDistance = distance;
                centerRow = row;
            }
        }

        // Extract line index from the center row
        int result = -1;
        if (centerRow?.DataContext is GridRowViewModel { Line: { } line })
            result = (int)line.GlobalIndex;

        _lastDetectedCenterLine = result;
        return result;
    }

    private void OnGridScrollChanged(object? s, ScrollChangedEventArgs e)
    {
        if (DataContext is not LogViewViewModel { IsGridMode: true } vm)
            return;

        // Use sender instead of _gridScroller field — the field gets nulled on GridDataSource change
        // but the handler stays attached to the actual ScrollViewer object.
        var sv = s as ScrollViewer;
        if (sv is null) return;

        // Track center line for grid mode using precise row detection
        var centerLineIndex = FindCenterVisibleGridRow();
        if (centerLineIndex >= 0)
            vm.SetCurrentLine(centerLineIndex);

        if (_minimap is not null)
            UpdateMinimapViewport(_minimap);

        // Scrollbar drag / keyboard scroll up → disable follow
        if (vm.IsFollowMode && e.OffsetDelta.Y < -0.1)
        {
            var scroll = _logGrid?.Scroll;
            if (scroll is not null && scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height > 0.5)
                vm.IsFollowMode = false;
        }

        // Time sync: broadcast center timestamp to linked panes
        vm.NotifyViewportScrolled();
    }

    private void OnGridScrollSizeChanged(object? s, SizeChangedEventArgs e)
    {
        if (_minimap is not null)
            UpdateMinimapViewport(_minimap);
    }

    private ScrollViewer? EnsureGridScroller()
    {
        if (_gridScroller is not null) return _gridScroller;
        _gridScroller = _logGrid?.FindDescendantOfType<ScrollViewer>();
        if (_gridScroller is not null && _gridScroller != _gridScrollerHookedInstance)
        {
            // Unsubscribe old handlers to prevent memory leak
            if (_gridScrollerHookedInstance is not null)
            {
                _gridScrollerHookedInstance.ScrollChanged -= OnGridScrollChanged;
                _gridScrollerHookedInstance.SizeChanged -= OnGridScrollSizeChanged;
            }

            _gridScrollerHookedInstance = _gridScroller;
            _gridScroller.ScrollChanged += OnGridScrollChanged;
            _gridScroller.SizeChanged += OnGridScrollSizeChanged;
        }
        return _gridScroller;
    }

    private void OnScrollToEndRequested()
    {
        Dispatcher.UIThread.Post(() => ScrollToEndAfterLayout(retryCount: 0), DispatcherPriority.Loaded);
    }

    private const int MaxScrollRetries = 5;

    private void ScrollToEndAfterLayout(int retryCount = 0)
    {
        // Grid mode: use TreeDataGrid.Scroll API (not internal ScrollViewer)
        if (DataContext is LogViewViewModel { IsGridMode: true })
        {
            var scroll = _logGrid?.Scroll;
            if (scroll is null)
            {
                if (retryCount < MaxScrollRetries)
                    Dispatcher.UIThread.Post(() => ScrollToEndAfterLayout(retryCount + 1), DispatcherPriority.Loaded);
                return;
            }
            double extentH = scroll.Extent.Height;
            double viewportH = scroll.Viewport.Height;
            if (extentH <= 0 && retryCount < MaxScrollRetries)
            {
                Dispatcher.UIThread.Post(() => ScrollToEndAfterLayout(retryCount + 1), DispatcherPriority.Loaded);
                return;
            }
            if (extentH <= viewportH) return;
            scroll.Offset = scroll.Offset.WithY(extentH - viewportH);
            if (_minimap is not null) UpdateMinimapViewport(_minimap);
            return;
        }

        // Text mode: use ScrollViewer directly
        if (_scroller is null)
        {
            if (retryCount < MaxScrollRetries)
                Dispatcher.UIThread.Post(() => ScrollToEndAfterLayout(retryCount + 1), DispatcherPriority.Loaded);
            return;
        }
        double extH = _scroller.Extent.Height;
        double vpH = _scroller.Viewport.Height;
        if (extH <= 0 && retryCount < MaxScrollRetries)
        {
            Dispatcher.UIThread.Post(() => ScrollToEndAfterLayout(retryCount + 1), DispatcherPriority.Loaded);
            return;
        }
        if (extH <= vpH) return;
        double targetY = Math.Max(0, extH - vpH);
        _scroller.Offset = new global::Avalonia.Vector(_scroller.Offset.X, targetY);
        if (_minimap is not null)
            UpdateMinimapViewport(_minimap);
    }

    private void OnScrollToLineRequested(int lineIndex)
    {
        Dispatcher.UIThread.Post(() => ScrollToLine(lineIndex));
    }

    /// <summary>Scroll the active log view to center the given line index.</summary>
    private void ScrollToLine(int lineIndex)
    {
        if (_attachedViewModel is null) return;

        if (_attachedViewModel.IsGridMode)
        {
            var scroll = _logGrid?.Scroll;
            if (scroll is null || _attachedViewModel.TotalLineCount <= 0) return;
            double ratio = (double)lineIndex / _attachedViewModel.TotalLineCount;
            double targetY = Math.Max(0, ratio * scroll.Extent.Height - scroll.Viewport.Height / 2);
            targetY = Math.Min(targetY, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            scroll.Offset = scroll.Offset.WithY(targetY);
            // Post selection after layout so virtualized rows are realized
            Dispatcher.UIThread.Post(() => SelectGridRowByLineIndex(lineIndex),
                DispatcherPriority.Render);
        }
        else
        {
            if (_scroller is null) return;
            double halfViewport = _scroller.Viewport.Height / 2;
            double targetY = Math.Max(0, lineIndex * LogLineRow.RowHeight - halfViewport);
            _scroller.Offset = new global::Avalonia.Vector(_scroller.Offset.X, targetY);
        }

        if (_minimap is not null) UpdateMinimapViewport(_minimap);
    }

    /// <summary>Programmatically select the grid row matching a line index.</summary>
    private void SelectGridRowByLineIndex(int lineIndex)
    {
        // Use the control's own source reference to ensure we're operating on
        // the same source the TreeDataGrid is displaying.
        var source = _logGrid?.Source;
        if (source is null) return;

        if (source is global::Avalonia.Controls.FlatTreeDataGridSource<GridRowViewModel> flat)
        {
            var sel = flat.RowSelection;
            if (sel is null) return;
            int i = 0;
            foreach (var item in flat.Items)
            {
                if (item.Line?.GlobalIndex == lineIndex)
                {
                    sel.SelectedIndex = new global::Avalonia.Controls.IndexPath(i);
                    return;
                }
                i++;
            }
        }
        else if (source is global::Avalonia.Controls.HierarchicalTreeDataGridSource<GridRowViewModel> hier)
        {
            var sel = hier.RowSelection;
            if (sel is null) return;
            int g = 0;
            foreach (var group in hier.Items)
            {
                if (group.Children is { } children)
                {
                    int c = 0;
                    foreach (var child in children)
                    {
                        if (child.Line?.GlobalIndex == lineIndex)
                        {
                            hier.Expand(new global::Avalonia.Controls.IndexPath(g));
                            sel.SelectedIndex = new global::Avalonia.Controls.IndexPath(g, c);
                            return;
                        }
                        c++;
                    }
                }
                if (group.Line?.GlobalIndex == lineIndex)
                {
                    sel.SelectedIndex = new global::Avalonia.Controls.IndexPath(g);
                    return;
                }
                g++;
            }
        }
    }

    private void OnMinimapWheelScroll(PointerWheelEventArgs e)
    {
        // Scrolling up on minimap disables follow
        if (e.Delta.Y > 0 && _attachedViewModel is { IsFollowMode: true })
            _attachedViewModel.IsFollowMode = false;

        // Forward mousewheel over minimap to the active scroller
        double delta = e.Delta.Y * 48; // ~3 lines per notch
        if (_attachedViewModel is { IsGridMode: true } && _logGrid?.Scroll is { } scroll)
        {
            double newY = Math.Clamp(scroll.Offset.Y - delta, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            scroll.Offset = scroll.Offset.WithY(newY);
        }
        else if (_scroller is not null)
        {
            double newY = Math.Clamp(_scroller.Offset.Y - delta, 0, Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height));
            _scroller.Offset = new global::Avalonia.Vector(_scroller.Offset.X, newY);
        }
        if (_minimap is not null)
            UpdateMinimapViewport(_minimap);
    }

    private void OnMinimapScrollRequested(int line)
    {
        if (_attachedViewModel is null) return;
        _attachedViewModel.IsFollowMode = false;
        ScrollToLine(line);
    }

    private void OnNavIndexChanged()
    {
        bool refreshMinimap = _minimap is not null && !_minimapRefreshPending;
        if (refreshMinimap)
            _minimapRefreshPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            if (!refreshMinimap)
                return;

            _minimapRefreshPending = false;
            _minimap?.InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_attachedViewModel is null) return;

        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed && !currentPoint.Properties.IsRightButtonPressed)
            return;

        // Walk up from clicked element to find the TreeDataGridRow and its data context
        var row = FindVisualAncestorOrSelf<global::Avalonia.Controls.Primitives.TreeDataGridRow>(e.Source);
        if (row?.DataContext is GridRowViewModel gridRow && gridRow.Line is { } line)
        {
            _attachedViewModel.SelectLine((int)line.GlobalIndex);
        }
    }

    private void OnLogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_attachedViewModel is null || _repeater is null)
            return;

        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed && !currentPoint.Properties.IsRightButtonPressed)
            return;

        // Calculate line index from Y position — works for direct hits and gap clicks alike
        var pos = e.GetPosition(_repeater);
        int lineIndex = (int)(pos.Y / LogLineRow.RowHeight);
        if (lineIndex >= 0 && lineIndex < _attachedViewModel.TotalLineCount)
            _attachedViewModel.SelectLine(lineIndex);
    }

    private static T? FindVisualAncestorOrSelf<T>(object? source)
        where T : class
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private void OnSelectedLineChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_repeater is null) return;

            foreach (var row in _repeater.GetVisualDescendants().OfType<LogLineRow>())
                row.InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    private void OnRowVisualsChanged() => OnSelectedLineChanged();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewViewModel.GridDataSource))
        {
            // Grid source rebuild may invalidate the cached scroller —
            // re-discover and re-hook after the visual tree settles.
            // Keep _gridScrollerHookedInstance so we don't double-subscribe if same ScrollViewer reappears.
            _gridScroller = null;
            Dispatcher.UIThread.Post(() => EnsureGridScroller(), DispatcherPriority.Loaded);
        }

        if (e.PropertyName is nameof(LogViewViewModel.IsGridMode) or nameof(LogViewViewModel.GridDataSource))
        {
            // After grid becomes visible or source changes, fit the Message column
            Dispatcher.UIThread.Post(FitMessageColumn, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Manually sizes the last (Message) column to fill remaining width.
    /// Star sizing on TemplateColumn is broken in TreeDataGrid v11.0.10.
    /// </summary>
    private void FitMessageColumn()
    {
        if (_logGrid is null || DataContext is not LogViewViewModel { IsGridMode: true } vm)
            return;
        var source = vm.GridDataSource;
        if (source is null) return;

        double gridWidth = _logGrid.Bounds.Width;
        if (gridWidth <= 0) return;

        var columns = source.Columns;
        if (columns.Count < 2) return;

        // Sum widths of all columns except the last one
        double fixedWidth = 0;
        for (int i = 0; i < columns.Count - 1; i++)
        {
            double w = columns[i].ActualWidth;
            if (double.IsNaN(w))
                w = columns[i].Width.IsAbsolute ? columns[i].Width.Value : 200;
            fixedWidth += w;
        }

        // Account for scrollbar (~18px) and small margin
        double margin = 20;
        double remaining = gridWidth - fixedWidth - margin;
        if (remaining < 100) remaining = 100;

        int lastIdx = columns.Count - 1;
        columns.SetColumnWidth(lastIdx, new global::Avalonia.Controls.GridLength(remaining));
    }

    private void OnFilterViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterPanelViewModel.IsVisible))
        {
            Dispatcher.UIThread.Post(() =>
                UpdateFilterLayout(_attachedFilterViewModel?.IsVisible == true));
        }
    }

    private void OnFilterPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_attachedFilterViewModel?.IsVisible == true && e.NewSize.Height >= FilterPanelView.MinHeight)
            _filterPanelHeight = e.NewSize.Height;
    }

    private void UpdateFilterLayout(bool isVisible)
    {
        if (RootGrid.RowDefinitions.Count < 4)
            return;

        RootGrid.RowDefinitions[2].Height = new GridLength(isVisible ? 4 : 0, GridUnitType.Pixel);
        RootGrid.RowDefinitions[3].Height = new GridLength(isVisible ? Math.Max(FilterPanelView.MinHeight, _filterPanelHeight) : 0, GridUnitType.Pixel);
        FilterSplitter.IsVisible = isVisible;
        FilterPanelView.IsVisible = isVisible;
    }

    private void UpdateMinimapViewport(LogMinimap minimap)
    {
        double extentHeight, viewportHeight, offsetY;

        if (DataContext is LogViewViewModel { IsGridMode: true } && _logGrid?.Scroll is { } scroll)
        {
            extentHeight = scroll.Extent.Height;
            viewportHeight = scroll.Viewport.Height;
            offsetY = scroll.Offset.Y;
        }
        else if (_scroller is not null)
        {
            extentHeight = _scroller.Extent.Height;
            viewportHeight = _scroller.Viewport.Height;
            offsetY = _scroller.Offset.Y;
        }
        else
        {
            return;
        }

        if (extentHeight <= 0 || viewportHeight <= 0)
        {
            minimap.ViewportTopRatio = 0;
            minimap.ViewportHeightRatio = 1;
            return;
        }

        double maxOffset = Math.Max(0.0, extentHeight - viewportHeight);
        double clampedOffset = Math.Clamp(offsetY, 0.0, maxOffset);
        double heightRatio = viewportHeight / extentHeight;
        bool pinToBottom = _attachedViewModel?.IsFollowMode == true && maxOffset > 0;
        double topRatio = pinToBottom
            ? Math.Max(0.0, 1.0 - heightRatio)
            : clampedOffset / extentHeight;

        minimap.ViewportTopRatio = topRatio;
        minimap.ViewportHeightRatio = heightRatio;
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
        else if (e.Key == Key.G && !ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.ToggleViewModeCommand.Execute(null);
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }
}

