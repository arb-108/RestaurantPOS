using System.Windows;

namespace RestaurantPOS.WPF.Views;

public partial class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public InputDialog(string prompt, string title = "Input", string defaultValue = "")
    {
        InitializeComponent();
        TxtPrompt.Text = prompt;
        Title = title;
        TxtInput.Text = defaultValue;
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        InputText = TxtInput.Text.Trim();
        if (string.IsNullOrEmpty(InputText))
        {
            TxtInput.Focus();
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
