namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Wraps a Dock document's LogView so it can be used wherever IWorkspacePane is needed (e.g. GetAllPanes).
/// </summary>
public sealed class DockPaneWrapper : IWorkspacePane
{
    public ViewModels.LogViewViewModel LogView { get; }

    public DockPaneWrapper(ViewModels.LogViewViewModel logView)
    {
        LogView = logView;
    }
}
