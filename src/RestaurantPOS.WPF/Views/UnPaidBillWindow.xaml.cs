using System.Windows;

namespace RestaurantPOS.WPF.Views;

public partial class UnPaidBillWindow : Window
{
    public string Reason { get; private set; } = string.Empty;

    public UnPaidBillWindow()
    {
        InitializeComponent();
        TxtReason.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtReason.Text))
        {
            MessageBox.Show("Reason is required to mark bill as Un-Paid.",
                "Reason Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtReason.Focus();
            return;
        }

        Reason = TxtReason.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
