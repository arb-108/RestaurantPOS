using System.Windows;

namespace RestaurantPOS.WPF.Views;

public partial class AddCustomerWindow : Window
{
    public string CustomerName => TxtName.Text.Trim();
    public string CustomerPhone => TxtMobile.Text.Trim();
    public string CustomerEmail => TxtEmail.Text.Trim();
    public string CustomerAddress => TxtAddress.Text.Trim();

    public AddCustomerWindow(string prefillPhone = "")
    {
        InitializeComponent();
        TxtMobile.Text = prefillPhone;

        // Focus name field on load
        Loaded += (_, _) => TxtName.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtMobile.Text))
        {
            MessageBox.Show("Mobile number is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtMobile.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtAddress.Text))
        {
            MessageBox.Show("Address is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAddress.Focus();
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
