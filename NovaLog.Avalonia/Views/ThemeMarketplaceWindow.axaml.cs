using Avalonia.Controls;
using NovaLog.Core.Theme;

namespace NovaLog.Avalonia.Views;

public partial class ThemeMarketplaceWindow : Window
{
    public ThemeMarketplaceWindow(ThemeService themeService)
    {
        InitializeComponent();
        DataContext = new ViewModels.ThemeMarketplaceViewModel(themeService);
    }
}
