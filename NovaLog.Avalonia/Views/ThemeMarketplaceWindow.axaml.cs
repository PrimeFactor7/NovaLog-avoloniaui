using Avalonia.Controls;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Theme;

namespace NovaLog.Avalonia.Views;

public partial class ThemeMarketplaceWindow : Window
{
    public ThemeMarketplaceWindow(ThemeService themeService)
    {
        InitializeComponent();
        var vm = new ThemeMarketplaceViewModel(themeService);
        DataContext = vm;
        Closing += (_, _) => vm.CancelPending();
    }
}
