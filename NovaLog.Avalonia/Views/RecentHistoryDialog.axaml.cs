using Avalonia.Controls;
using Avalonia.Input;
using NovaLog.Avalonia.ViewModels;

namespace NovaLog.Avalonia.Views;

public partial class RecentHistoryDialog : Window
{
    private readonly SourceManagerViewModel? _sourceManager;

    public RecentHistoryDialog()
    {
        InitializeComponent();
        DataContext = new RecentHistoryDialogViewModel([]);

        BtnAdd.Click += (_, _) => AddSelectedAndClose();
        BtnClose.Click += (_, _) => Close();
    }

    public RecentHistoryDialog(SourceManagerViewModel sourceManager)
        : this()
    {
        _sourceManager = sourceManager;
        DataContext = new RecentHistoryDialogViewModel(sourceManager.BuildRecentHistoryItems());
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RecentListBox.Focus();
    }

    private void OnRecentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RecentHistoryDialogViewModel vm || vm.SelectedItem is null)
            return;

        _sourceManager?.PreviewRecent(vm.SelectedItem.Entry, trackUsage: false);
    }

    private void OnRecentDoubleTapped(object? sender, TappedEventArgs e)
    {
        AddSelectedAndClose();
    }

    private void AddSelectedAndClose()
    {
        if (DataContext is not RecentHistoryDialogViewModel vm || vm.SelectedItem is null)
            return;

        if (_sourceManager?.AddRecentToSources(vm.SelectedItem.Entry) == true)
            Close(vm.SelectedItem.Entry);
    }
}
