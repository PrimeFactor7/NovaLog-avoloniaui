using Avalonia.Controls;
using Avalonia.Input;

namespace NovaLog.Avalonia.Controls;

public partial class PanePickerOverlay : UserControl
{
    public PanePickerOverlay()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is ViewModels.PanePickerViewModel vm)
        {
            if (e.Key == Key.Enter)
            {
                vm.ConfirmCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }
}
