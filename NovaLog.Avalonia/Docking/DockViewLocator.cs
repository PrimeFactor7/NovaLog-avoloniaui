using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Avalonia.Views;

namespace NovaLog.Avalonia.Docking;

/// <summary>
/// Resolves Dock document/tool content to views so the document pane can display
/// <see cref="LogViewPanel"/> when the theme presents a dockable or its Context.
/// </summary>
public class DockViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;
        if (data is LogViewDocument doc)
            return new LogViewPanel { DataContext = doc.Context };
        if (data is LogViewViewModel vm)
            return new LogViewPanel { DataContext = vm };
        return null;
    }

    public bool Match(object? data)
    {
        return data is LogViewDocument or LogViewViewModel;
    }
}
