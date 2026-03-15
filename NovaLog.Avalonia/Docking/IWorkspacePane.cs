namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Abstraction for a workspace pane that has a LogView, used for both the legacy
/// split-tree panes and Dock document panes.
/// </summary>
public interface IWorkspacePane
{
    ViewModels.LogViewViewModel LogView { get; }
}
