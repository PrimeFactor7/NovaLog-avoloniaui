using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NovaLog.Avalonia.Views;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;

        BtnOk.Click += (_, _) => Close(InputBox.Text);
        BtnCancel.Click += (_, _) => Close(null);

        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Close(InputBox.Text);
            if (e.Key == Key.Escape) Close(null);
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InputBox.Focus();
        InputBox.SelectAll();
    }
}
