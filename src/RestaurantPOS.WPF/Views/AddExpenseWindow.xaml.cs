using System.Collections.Generic;
using System.Windows;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class AddExpenseWindow : Window
{
    public Supplier? SelectedSupplier { get; private set; }
    public string ExpenseDescription { get; private set; } = string.Empty;
    public decimal ExpenseAmount { get; private set; }
    public DateTime ExpenseDate { get; private set; } = DateTime.Today;
    public string InvoiceNumber { get; private set; } = string.Empty;
    public string ExpenseCategory { get; private set; } = string.Empty;
    public bool IsPaid { get; private set; }
    public string ExpenseNotes { get; private set; } = string.Empty;

    public AddExpenseWindow(IEnumerable<Supplier> suppliers)
    {
        InitializeComponent();
        CmbSupplier.ItemsSource = suppliers;
        CmbCategory.ItemsSource = new[] { "Raw Material", "Equipment", "Packaging", "Utilities", "Maintenance", "Other" };
        DpDate.SelectedDate = DateTime.Today;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CmbSupplier.SelectedItem is not Supplier sup)
        { MessageBox.Show("Select a supplier.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(TxtDesc.Text))
        { MessageBox.Show("Description is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        SelectedSupplier = sup;
        ExpenseDescription = TxtDesc.Text.Trim();
        decimal.TryParse(TxtAmount.Text, out var amt); ExpenseAmount = amt;
        ExpenseDate = DpDate.SelectedDate ?? DateTime.Today;
        InvoiceNumber = TxtInvoice.Text.Trim();
        ExpenseCategory = CmbCategory.Text?.Trim() ?? "";
        IsPaid = ChkPaid.IsChecked == true;
        ExpenseNotes = TxtNotes.Text.Trim();
        DialogResult = true; Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
