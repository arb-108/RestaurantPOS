using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class AddStockItemWindow : Window
{
    // Output properties
    public string ItemName { get; private set; } = string.Empty;
    public string StockCategory { get; private set; } = "Dry Goods";
    public decimal CurrentQty { get; private set; }
    public decimal MinLevel { get; private set; }
    public string StockUnit { get; private set; } = "kg";
    public decimal UnitCost { get; private set; }
    public Supplier? SelectedSupplier { get; private set; }

    private readonly List<Supplier> _suppliers;

    public AddStockItemWindow(List<Supplier> suppliers, Ingredient? existing = null)
    {
        InitializeComponent();
        _suppliers = suppliers;

        // Populate supplier combo
        CmbSupplier.Items.Clear();
        CmbSupplier.Items.Add(new Supplier { Id = 0, Name = "-- None --" });
        foreach (var s in suppliers) CmbSupplier.Items.Add(s);
        CmbSupplier.SelectedIndex = 0;

        // Wire up cost preview
        TxtQty.TextChanged += (_, _) => UpdatePreview();
        TxtCost.TextChanged += (_, _) => UpdatePreview();

        if (existing != null)
        {
            Title = "Edit Stock Item";
            TitleText.Text = "Edit Stock Item";
            TxtName.Text = existing.Name;
            TxtQty.Text = existing.CurrentStock.ToString("G");
            TxtMinLevel.Text = existing.ReorderLevel.ToString("G");
            TxtCost.Text = (existing.CostPerUnit / 100m).ToString("G");

            // Select category
            if (!string.IsNullOrEmpty(existing.StockCategory))
            {
                foreach (ComboBoxItem item in CmbCategory.Items)
                {
                    if (item.Content?.ToString() == existing.StockCategory)
                    { CmbCategory.SelectedItem = item; break; }
                }
            }

            // Select unit
            if (!string.IsNullOrEmpty(existing.Unit))
            {
                foreach (ComboBoxItem item in CmbUnit.Items)
                {
                    if (item.Content?.ToString() == existing.Unit)
                    { CmbUnit.SelectedItem = item; break; }
                }
            }

            // Select supplier
            if (existing.SupplierId != null)
            {
                for (int i = 0; i < CmbSupplier.Items.Count; i++)
                {
                    if (CmbSupplier.Items[i] is Supplier s && s.Id == existing.SupplierId)
                    { CmbSupplier.SelectedIndex = i; break; }
                }
            }
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (TxtPreview == null) return;
        decimal.TryParse(TxtQty.Text, out var qty);
        decimal.TryParse(TxtCost.Text, out var cost);
        TxtPreview.Text = $"Total stock value: Rs {qty * cost:N0}";
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Item name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        ItemName = TxtName.Text.Trim();
        StockCategory = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dry Goods";
        decimal.TryParse(TxtQty.Text, out var qty); CurrentQty = qty;
        decimal.TryParse(TxtMinLevel.Text, out var min); MinLevel = min;
        StockUnit = (CmbUnit.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "kg";
        decimal.TryParse(TxtCost.Text, out var cost); UnitCost = cost;

        var sup = CmbSupplier.SelectedItem as Supplier;
        SelectedSupplier = sup?.Id > 0 ? sup : null;

        DialogResult = true;
    }
}
