using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NovaLog.Avalonia.Views;

public partial class GoToTimestampDialog : Window
{
    public GoToTimestampDialog()
    {
        InitializeComponent();
        BtnGo.Click += (_, _) => OnGo();
        BtnCancel.Click += (_, _) => Close(null);
        TimestampBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) OnGo();
            if (e.Key == Key.Escape) Close(null);
        };
    }

    private void OnGo()
    {
        if (DateTime.TryParse(TimestampBox.Text, out var dt))
            Close(dt);
        else
            Close(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TimestampBox.Focus();
    }
}
