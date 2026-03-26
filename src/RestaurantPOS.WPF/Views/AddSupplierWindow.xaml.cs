using System.Windows;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class AddSupplierWindow : Window
{
    public string SupplierName { get; private set; } = string.Empty;
    public string ContactPerson { get; private set; } = string.Empty;
    public string SupplierPhone { get; private set; } = string.Empty;
    public string SupplierEmail { get; private set; } = string.Empty;
    public string SupplierAddress { get; private set; } = string.Empty;
    public string SupplierCity { get; private set; } = string.Empty;
    public string SupplierNotes { get; private set; } = string.Empty;

    public AddSupplierWindow(Supplier? existing = null)
    {
        InitializeComponent();
        if (existing != null)
        {
            Title = "Edit Supplier";
            TxtName.Text = existing.Name;
            TxtContact.Text = existing.ContactPerson ?? "";
            TxtPhone.Text = existing.Phone ?? "";
            TxtEmail.Text = existing.Email ?? "";
            TxtCity.Text = existing.City ?? "";
            TxtAddress.Text = existing.Address ?? "";
            TxtNotes.Text = existing.Notes ?? "";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        { MessageBox.Show("Supplier name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        SupplierName = TxtName.Text.Trim();
        ContactPerson = TxtContact.Text.Trim();
        SupplierPhone = TxtPhone.Text.Trim();
        SupplierEmail = TxtEmail.Text.Trim();
        SupplierAddress = TxtAddress.Text.Trim();
        SupplierCity = TxtCity.Text.Trim();
        SupplierNotes = TxtNotes.Text.Trim();
        DialogResult = true; Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
