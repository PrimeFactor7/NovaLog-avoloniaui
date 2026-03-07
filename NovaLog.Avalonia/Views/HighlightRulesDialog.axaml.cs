using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaLog.Avalonia.Views;

public partial class HighlightRulesDialog : Window
{
    public HighlightRulesDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
