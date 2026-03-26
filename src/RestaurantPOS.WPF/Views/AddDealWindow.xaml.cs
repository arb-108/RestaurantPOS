using System.Collections.ObjectModel;
using System.Windows;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class AddDealWindow : Window
{
    private readonly List<MenuItem> _menuItems;
    private readonly ObservableCollection<DealItemRow> _items = [];

    // ── Output properties ──
    public string DealName { get; private set; } = string.Empty;
    public string? DealDescription { get; private set; }
    public decimal DealPrice { get; private set; }
    public decimal OriginalPrice { get; private set; }
    public int DealDisplayOrder { get; private set; }
    public Category? SelectedCategory { get; private set; }
    public List<DealItemRow> DealItems => [.. _items];

    public AddDealWindow(List<MenuItem> menuItems, ObservableCollection<Category> categories, Deal? existing = null)
    {
        InitializeComponent();
        _menuItems = menuItems;

        cmbMenuItem.ItemsSource = menuItems;
        if (menuItems.Count > 0) cmbMenuItem.SelectedIndex = 0;

        cmbCategory.ItemsSource = categories;

        lstItems.ItemsSource = _items;

        if (existing != null)
        {
            Title = "Edit Deal";
            txtName.Text = existing.Name;
            txtDescription.Text = existing.Description ?? string.Empty;
            txtDealPrice.Text = (existing.DealPrice / 100m).ToString("0.##");
            txtOrder.Text = existing.DisplayOrder.ToString();
            cmbCategory.SelectedItem = categories.FirstOrDefault(c => c.Id == existing.CategoryId);

            foreach (var di in existing.Items)
            {
                _items.Add(new DealItemRow
                {
                    MenuItemId = di.MenuItemId,
                    ItemName = di.MenuItem?.Name ?? $"Item #{di.MenuItemId}",
                    Quantity = di.Quantity,
                    UnitPrice = di.MenuItem?.BasePrice ?? 0
                });
            }
            RecalcTotals();
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (cmbMenuItem.SelectedItem is not MenuItem mi) return;
        if (!int.TryParse(txtQty.Text, out var qty) || qty < 1) qty = 1;

        var existing = _items.FirstOrDefault(i => i.MenuItemId == mi.Id);
        if (existing != null)
        {
            existing.Quantity += qty;
            // Refresh list
            var idx = _items.IndexOf(existing);
            _items.RemoveAt(idx);
            _items.Insert(idx, existing);
        }
        else
        {
            _items.Add(new DealItemRow
            {
                MenuItemId = mi.Id,
                ItemName = mi.Name,
                Quantity = qty,
                UnitPrice = mi.BasePrice
            });
        }
        txtQty.Text = "1";
        RecalcTotals();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is DealItemRow row)
        {
            _items.Remove(row);
            RecalcTotals();
        }
    }

    private void RecalcTotals()
    {
        var total = _items.Sum(i => i.UnitPrice * i.Quantity) / 100m;
        OriginalPrice = total;
        runOriginal.Text = total.ToString("N0");

        if (decimal.TryParse(txtDealPrice.Text, out var dp))
            runSaving.Text = (total - dp).ToString("N0");
        else
            runSaving.Text = "0";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Please enter a deal name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!decimal.TryParse(txtDealPrice.Text, out var price) || price <= 0)
        {
            MessageBox.Show("Please enter a valid deal price.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_items.Count == 0)
        {
            MessageBox.Show("Please add at least one item to the deal.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DealName = txtName.Text.Trim();
        DealDescription = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim();
        DealPrice = price;
        OriginalPrice = _items.Sum(i => i.UnitPrice * i.Quantity) / 100m;
        DealDisplayOrder = int.TryParse(txtOrder.Text, out var o) ? o : 0;
        SelectedCategory = cmbCategory.SelectedItem as Category;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>Row in the deal items list.</summary>
public class DealItemRow
{
    public int MenuItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public long UnitPrice { get; set; }
    public string PriceText => $"Rs. {UnitPrice * Quantity / 100m:N0}";
}
