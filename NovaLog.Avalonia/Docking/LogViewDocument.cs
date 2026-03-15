using System.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Dock document that wraps a single LogViewViewModel. The Dock UI shows this as a tab;
/// the view template binds to <see cref="Context"/> to render <see cref="Views.LogViewPanel"/>.
/// </summary>
public sealed class LogViewDocument : Document
{
    private readonly PropertyChangedEventHandler _titleSyncHandler;

    /// <summary>
    /// The log view view model for this pane. Set before adding to the layout and call
    /// LogView.Initialize(clock, sourceManager, theme) when the workspace is initialized.
    /// </summary>
    public ViewModels.LogViewViewModel LogView => (ViewModels.LogViewViewModel)Context!;

    public LogViewDocument(ViewModels.LogViewViewModel logView)
    {
        Title = logView.Title;
        Context = logView;
        _titleSyncHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.LogViewViewModel.Title))
                Title = logView.Title;
        };
        logView.PropertyChanged += _titleSyncHandler;
    }

    /// <summary>Unsubscribes the title-sync handler. Call when removing this document from the layout.</summary>
    public void Detach()
    {
        LogView.PropertyChanged -= _titleSyncHandler;
    }
}
