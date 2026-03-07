using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// Base class for nodes in the binary split-pane tree.
/// Either a leaf (PaneNode) containing a LogViewViewModel,
/// or a branch (SplitBranch) containing two children with a splitter.
/// </summary>
public abstract class SplitNodeViewModel : ObservableObject
{
    public SplitBranchViewModel? Parent { get; set; }
}

/// <summary>
/// Leaf node wrapping a single LogViewViewModel pane.
/// </summary>
public sealed partial class PaneNodeViewModel : SplitNodeViewModel
{
    public LogViewViewModel LogView { get; } = new();

    /// <summary>Whether this pane is the currently focused pane (shown with accent border).</summary>
    [ObservableProperty] private bool _isFocused;
}

/// <summary>
/// Branch node containing two children separated by a splitter.
/// </summary>
public sealed partial class SplitBranchViewModel : SplitNodeViewModel
{
    /// <summary>True = side-by-side (vertical splitter), False = stacked (horizontal splitter).</summary>
    [ObservableProperty] private bool _isHorizontal;

    [ObservableProperty] private SplitNodeViewModel _child1;
    [ObservableProperty] private SplitNodeViewModel _child2;

    /// <summary>Splitter position as fraction 0.0–1.0.</summary>
    [ObservableProperty] private double _splitterRatio = 0.5;

    public SplitBranchViewModel(SplitNodeViewModel child1, SplitNodeViewModel child2, bool isHorizontal)
    {
        _child1 = child1;
        _child2 = child2;
        _isHorizontal = isHorizontal;
        child1.Parent = this;
        child2.Parent = this;
    }
}
