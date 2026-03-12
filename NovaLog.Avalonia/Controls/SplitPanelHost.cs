using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Markup.Xaml.MarkupExtensions;
using global::Avalonia.VisualTree;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Avalonia.Views;
using System;

namespace NovaLog.Avalonia.Controls;

/// <summary>
/// Recursively renders a <see cref="SplitNodeViewModel"/> tree.
/// Leaf nodes become <see cref="LogViewPanel"/> instances.
/// Branch nodes become Grid + GridSplitter + two child SplitPanelHosts.
/// </summary>
public class SplitPanelHost : ContentControl
{
    public static readonly StyledProperty<SplitNodeViewModel?> NodeProperty =
        AvaloniaProperty.Register<SplitPanelHost, SplitNodeViewModel?>(nameof(Node));

    private SplitNodeViewModel? _subscribedNode;
    private System.ComponentModel.PropertyChangedEventHandler? _nodeHandler;

    public SplitNodeViewModel? Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    static SplitPanelHost()
    {
        NodeProperty.Changed.AddClassHandler<SplitPanelHost>((host, _) => host.RebuildContent());
    }

    private LogViewPanel? _subscribedPanel;

    private void UnsubscribePrevious()
    {
        if (_subscribedNode is not null && _nodeHandler is not null)
        {
            _subscribedNode.PropertyChanged -= _nodeHandler;
            _nodeHandler = null;
            _subscribedNode = null;
        }
        if (_subscribedPanel is not null)
        {
            _subscribedPanel.NewFileDropped -= _onNewFileDropped;
            _subscribedPanel.SplitRequested -= _onSplitRequested;
            _subscribedPanel.SourceIdDropped -= _onSourceIdDropped;
            _subscribedPanel.SourceIdSplitRequested -= _onSourceIdSplitRequested;
            _subscribedPanel = null;
        }
        if (_subscribedLogView is not null && _onCloseRequested is not null)
        {
            _subscribedLogView.CloseRequested -= _onCloseRequested;
            _onCloseRequested = null;
            _subscribedLogView = null;
        }
    }

    // Event handler delegates for cleanup
    private Action<string>? _onNewFileDropped;
    private Action<string, bool>? _onSplitRequested;
    private Action<string>? _onSourceIdDropped;
    private Action<string, bool>? _onSourceIdSplitRequested;
    private Action? _onCloseRequested;
    private LogViewViewModel? _subscribedLogView;

    private void RebuildContent()
    {
        UnsubscribePrevious();

        var node = Node;
        if (node is null)
        {
            Content = null;
            return;
        }

        if (node is PaneNodeViewModel pane)
        {
            var panel = new LogViewPanel { DataContext = pane.LogView };

            // Wire drag-drop events (store delegates for cleanup)
            _onNewFileDropped = path =>
            {
                pane.LogView.LoadFile(path);
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                    mvm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.File);
            };
            panel.NewFileDropped += _onNewFileDropped;

            _onSplitRequested = (path, horizontal) =>
            {
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                {
                    var newPane = mvm.Workspace.SplitTarget(pane, horizontal);
                    newPane?.LogView.LoadFile(path);
                    mvm.SourceManager.AddSource(path, NovaLog.Core.Models.SourceKind.File);
                }
            };
            panel.SplitRequested += _onSplitRequested;

            _onSourceIdDropped = id =>
            {
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                {
                    var src = mvm.SourceManager.Sources.FirstOrDefault(s => s.SourceId == id);
                    if (src != null)
                        LoadSourceIntoPane(mvm, pane.LogView, src);
                }
            };
            panel.SourceIdDropped += _onSourceIdDropped;

            _onSourceIdSplitRequested = (id, horizontal) =>
            {
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                {
                    var src = mvm.SourceManager.Sources.FirstOrDefault(s => s.SourceId == id);
                    if (src != null)
                    {
                        var newPane = mvm.Workspace.SplitTarget(pane, horizontal);
                        if (newPane != null)
                            LoadSourceIntoPane(mvm, newPane.LogView, src);
                    }
                }
            };
            panel.SourceIdSplitRequested += _onSourceIdSplitRequested;
            _subscribedPanel = panel;

            _onCloseRequested = () =>
            {
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                    mvm.Workspace.ClosePane(pane);
            };
            _subscribedLogView = pane.LogView;
            pane.LogView.CloseRequested += _onCloseRequested;

            // Wrap in a Border that shows focus state
            var border = new Border
            {
                BorderThickness = new Thickness(2),
                Child = panel
            };

            // Bind border color to focus state
            _nodeHandler = (_, e) =>
            {
                if (e.PropertyName == nameof(PaneNodeViewModel.IsFocused))
                    UpdateBorderBrush(border, pane.IsFocused);
            };
            _subscribedNode = pane;
            pane.PropertyChanged += _nodeHandler;

            if (pane.IsFocused)
                UpdateBorderBrush(border, true);

            // Focus on pointer press — walk up visual tree to find WorkspaceViewModel
            border.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                var window = this.FindAncestorOfType<MainWindow>();
                if (window?.DataContext is MainWindowViewModel mvm)
                    mvm.Workspace.FocusPane(pane);
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            Content = border;
        }
        else if (node is SplitBranchViewModel branch)
        {
            var grid = new Grid();

            if (branch.IsHorizontal)
            {
                grid.ColumnDefinitions = new ColumnDefinitions("*,3,*");

                var child1 = new SplitPanelHost { Node = branch.Child1 };
                Grid.SetColumn(child1, 0);

                var splitter = new GridSplitter
                {
                    Width = 3,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                splitter[!GridSplitter.BackgroundProperty] = new DynamicResourceExtension("SplitterBgBrush");
                Grid.SetColumn(splitter, 1);

                var child2 = new SplitPanelHost { Node = branch.Child2 };
                Grid.SetColumn(child2, 2);

                grid.Children.Add(child1);
                grid.Children.Add(splitter);
                grid.Children.Add(child2);
            }
            else
            {
                grid.RowDefinitions = new RowDefinitions("*,3,*");

                var child1 = new SplitPanelHost { Node = branch.Child1 };
                Grid.SetRow(child1, 0);

                var splitter = new GridSplitter
                {
                    Height = 3,
                    VerticalAlignment = VerticalAlignment.Center
                };
                splitter[!GridSplitter.BackgroundProperty] = new DynamicResourceExtension("SplitterBgBrush");
                Grid.SetRow(splitter, 1);

                var child2 = new SplitPanelHost { Node = branch.Child2 };
                Grid.SetRow(child2, 2);

                grid.Children.Add(child1);
                grid.Children.Add(splitter);
                grid.Children.Add(child2);
            }

            Content = grid;

            _nodeHandler = (_, e) =>
            {
                if (e.PropertyName is nameof(SplitBranchViewModel.Child1)
                    or nameof(SplitBranchViewModel.Child2)
                    or nameof(SplitBranchViewModel.IsHorizontal))
                {
                    RebuildContent();
                }
            };
            _subscribedNode = branch;
            branch.PropertyChanged += _nodeHandler;
        }
    }

    private static void UpdateBorderBrush(Border border, bool isFocused)
    {
        if (isFocused)
            border[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        else
            border.BorderBrush = null;
    }

    private static void LoadSourceIntoPane(
        MainWindowViewModel mvm,
        LogViewViewModel logView,
        SourceItemViewModel src)
    {
        switch (src.Kind)
        {
            case NovaLog.Core.Models.SourceKind.File:
                logView.LoadFile(src.PhysicalPath);
                break;
            case NovaLog.Core.Models.SourceKind.Folder:
                logView.LoadFolder(src.PhysicalPath);
                break;
            case NovaLog.Core.Models.SourceKind.Merge:
            {
                var sourceIds = GetMergeSourceIds(src);
                var sourcesToMerge = mvm.SourceManager.Sources
                    .Where(s => sourceIds.Contains(s.SourceId))
                    .ToList();

                if (sourcesToMerge.Count >= 2)
                    logView.LoadMerge(sourcesToMerge);
                break;
            }
        }
    }

    private static HashSet<string> GetMergeSourceIds(SourceItemViewModel src)
    {
        if (src.ChildSourceIds.Count > 0)
            return src.ChildSourceIds.ToHashSet(StringComparer.Ordinal);

        const string mergePrefix = "merge://";
        if (src.PhysicalPath.StartsWith(mergePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return src.PhysicalPath[mergePrefix.Length..]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
        }

        return [];
    }
}
