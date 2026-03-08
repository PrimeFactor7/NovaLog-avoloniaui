using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using NovaLog.Avalonia.ViewModels;

namespace NovaLog.Avalonia.Views;

public partial class FilterPanel : UserControl
{
    private ScrollViewer? _resultsScroller;
    private FilterPanelViewModel? _attachedViewModel;
    private bool _resultsScrollerHooked;

    public FilterPanel()
    {
        InitializeComponent();

        var input = this.FindControl<TextBox>("SearchInput");
        if (input is not null)
        {
            input.KeyDown += OnSearchInputKeyDown;
        }

        var results = this.FindControl<ItemsControl>("FilterResultsControl");
        results?.AddHandler(InputElement.PointerPressedEvent, OnFilterResultPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_attachedViewModel is not null)
            _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // Unsubscribe from old scroll handler before resetting
        if (_resultsScroller is not null && _resultsScrollerHooked)
        {
            _resultsScroller.ScrollChanged -= OnResultsScrollChanged;
        }

        _attachedViewModel = DataContext as FilterPanelViewModel;
        if (_attachedViewModel is not null)
        {
            _attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _resultsScroller = null;
            _resultsScrollerHooked = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not FilterPanelViewModel vm)
            return;

        if (e.PropertyName == nameof(FilterPanelViewModel.IsFollowMode) && vm.IsFollowMode)
        {
            Dispatcher.UIThread.Post(ScrollResultsToEnd);
            return;
        }

        if (e.PropertyName == nameof(FilterPanelViewModel.ResultCount))
        {
            if (vm.IsFollowMode && vm.ResultCount > 0)
            {
                Dispatcher.UIThread.Post(() => ScrollResultsToEnd());
            }
        }
    }

    private void ScrollResultsToEnd()
    {
        EnsureResultsScroller();

        _resultsScroller?.ScrollToEnd();
    }

    private void EnsureResultsScroller()
    {
        if (_resultsScroller is null)
        {
            var itemsControl = this.FindControl<ItemsControl>("FilterResultsControl");
            if (itemsControl is null)
            {
                _resultsScroller = this.FindDescendantOfType<ScrollViewer>();
            }
            else
            {
                _resultsScroller = itemsControl.FindDescendantOfType<ScrollViewer>();
            }
        }

        if (_resultsScroller is not null && !_resultsScrollerHooked)
        {
            _resultsScroller.ScrollChanged += OnResultsScrollChanged;
            _resultsScrollerHooked = true;
        }
    }

    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_attachedViewModel is null || !_attachedViewModel.IsFollowMode || _resultsScroller is null)
            return;

        if (_resultsScroller.Extent.Height <= 0)
            return;

        double maxScroll = _resultsScroller.Extent.Height - _resultsScroller.Viewport.Height;
        double currentScroll = _resultsScroller.Offset.Y;
        if (maxScroll - currentScroll > 36.0)
            _attachedViewModel.IsFollowMode = false;
    }

    private void OnSearchInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (DataContext is FilterPanelViewModel vm)
            {
                vm.IsFollowMode = true;
                ScrollResultsToEnd();
                e.Handled = true;
            }
        }
    }

    /// <summary>Focus the search input when the panel becomes visible.</summary>
    public void FocusSearch()
    {
        var input = this.FindControl<TextBox>("SearchInput");
        input?.Focus();
    }

    private void OnFilterResultPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_attachedViewModel is null || e.Source is not Visual element)
            return;

        Visual? current = element;
        while (current is not null)
        {
            if (current is StyledElement styled && styled.DataContext is LogLineViewModel line)
            {
                _attachedViewModel.ActivateResult(line);
                e.Handled = true;
                return;
            }

            current = current.GetVisualParent();
        }
    }
}
