using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class AddProductWindow : Window
{
    // ── Public results ──
    public string ItemName { get; private set; } = string.Empty;
    public Category? SelectedCategory { get; private set; }
    public KitchenStation? SelectedKitchenStation { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal SalePrice { get; private set; }
    public string SKU { get; private set; } = string.Empty;
    public string Barcode { get; private set; } = string.Empty;
    public decimal MaxDiscount { get; private set; }
    public int PrepTime { get; private set; }
    public int DisplayOrder { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public new bool IsActive { get; private set; } = true;

    // ── Recipe results ──
    public List<RecipeItemRow> RecipeItems { get; private set; } = [];
    public bool RecipeModified { get; private set; }

    private readonly List<Ingredient> _ingredients;
    private List<RecipeItemRow> _currentRecipeRows = [];
    private readonly bool _isEditMode;

    private static readonly Brush WatermarkBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5));
    private static readonly Brush TextBrush = Brushes.Black;
    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);
    private static readonly Regex IntegerRegex = new(@"[^0-9]", RegexOptions.Compiled);

    /// <summary>
    /// Create dialog for Add or Edit product.
    /// </summary>
    public AddProductWindow(
        IEnumerable<Category> categories,
        IEnumerable<KitchenStation> stations,
        MenuItem? existing = null,
        IEnumerable<Ingredient>? ingredients = null,
        IEnumerable<RecipeItemRow>? existingRecipes = null)
    {
        InitializeComponent();

        _ingredients = ingredients?.ToList() ?? [];
        _isEditMode = existing != null;

        CmbCategory.ItemsSource = categories;
        CmbKitchen.ItemsSource = stations;

        // Watermarks
        SetupWatermark(TxtItemName, "Item Name");
        SetupWatermark(TxtCostPrice, "Cost Price");
        SetupWatermark(TxtSalePrice, "Sale Price");
        SetupWatermark(TxtSKU, "SKU");
        SetupWatermark(TxtBarcode, "Barcode");
        SetupWatermark(TxtMaxDisc, "Max Discount");
        SetupWatermark(TxtPrepTime, "Prep Time (min)");
        SetupWatermark(TxtDisplayOrder, "Display Order");
        SetupWatermark(TxtDescription, "Description");

        if (existing != null)
        {
            Title = "Edit Product";
            TxtRecipeBtn.Text = "Edit Recipe";

            SetValue(TxtItemName, existing.Name);
            SetValue(TxtCostPrice, (existing.CostPrice / 100m).ToString("F0"));
            SetValue(TxtSalePrice, (existing.BasePrice / 100m).ToString("F0"));
            SetValue(TxtSKU, existing.SKU ?? "");
            SetValue(TxtBarcode, existing.Barcode ?? "");
            SetValue(TxtMaxDisc, (existing.MaxDiscount / 100m).ToString("F0"));
            SetValue(TxtPrepTime, existing.PrepTimeMinutes.ToString());
            SetValue(TxtDisplayOrder, existing.DisplayOrder.ToString());
            SetValue(TxtDescription, existing.Description ?? "");
            ChkActive.IsChecked = existing.IsActive;

            foreach (Category c in CmbCategory.Items)
                if (c.Id == existing.CategoryId) { CmbCategory.SelectedItem = c; break; }

            if (existing.KitchenStationId.HasValue)
                foreach (KitchenStation s in CmbKitchen.Items)
                    if (s.Id == existing.KitchenStationId) { CmbKitchen.SelectedItem = s; break; }
        }
        else
        {
            TxtRecipeBtn.Text = "Add Recipe";
        }

        // Load existing recipe rows
        if (existingRecipes != null)
            _currentRecipeRows = existingRecipes.ToList();

        // Hide recipe button if no ingredients available
        if (_ingredients.Count == 0)
            BtnManageRecipe.Visibility = Visibility.Collapsed;

        // Numeric-only validation for price/number fields
        TxtCostPrice.PreviewTextInput += DecimalOnly;
        TxtSalePrice.PreviewTextInput += DecimalOnly;
        TxtMaxDisc.PreviewTextInput += DecimalOnly;
        TxtPrepTime.PreviewTextInput += IntOnly;
        TxtDisplayOrder.PreviewTextInput += IntOnly;

        TxtItemName.Focus();
    }

    // ── Watermark helpers ──

    private static void SetupWatermark(System.Windows.Controls.TextBox tb, string placeholder)
    {
        tb.Tag = placeholder;
        tb.Text = placeholder;
        tb.Foreground = WatermarkBrush;

        tb.GotFocus += (_, _) =>
        {
            if (Equals(tb.Foreground, WatermarkBrush))
            {
                tb.Text = "";
                tb.Foreground = TextBrush;
            }
        };
        tb.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(tb.Text))
            {
                tb.Text = tb.Tag?.ToString() ?? "";
                tb.Foreground = WatermarkBrush;
            }
        };
    }

    private static void SetValue(System.Windows.Controls.TextBox tb, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            tb.Text = value;
            tb.Foreground = TextBrush;
        }
    }

    private string GetText(System.Windows.Controls.TextBox tb)
    {
        if (Equals(tb.Foreground, WatermarkBrush)) return string.Empty;
        return tb.Text.Trim();
    }

    // ── Manage Recipe ──
    private void ManageRecipe_Click(object sender, RoutedEventArgs e)
    {
        var itemName = GetText(TxtItemName);
        if (string.IsNullOrWhiteSpace(itemName))
            itemName = "(New Product)";

        var catName = CmbCategory.SelectedItem is Category cat ? cat.Name : "Uncategorized";

        var recipeDlg = new ManageRecipeWindow(itemName, catName, _ingredients, _currentRecipeRows)
        {
            Owner = this
        };

        if (recipeDlg.ShowDialog() == true)
        {
            _currentRecipeRows = recipeDlg.RecipeItems;
            RecipeModified = true;

            // Update button text to show count
            var count = _currentRecipeRows.Count;
            TxtRecipeBtn.Text = count > 0
                ? $"{(_isEditMode ? "Edit" : "Manage")} Recipe ({count})"
                : (_isEditMode ? "Edit Recipe" : "Add Recipe");
        }
    }

    // ── Save / Cancel ──
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = GetText(TxtItemName);
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Item Name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtItemName.Focus();
            return;
        }

        if (CmbCategory.SelectedItem is not Category cat)
        {
            MessageBox.Show("Please select a Category.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ItemName = name;
        SelectedCategory = cat;
        SelectedKitchenStation = CmbKitchen.SelectedItem as KitchenStation;

        decimal.TryParse(GetText(TxtCostPrice), out var cost);
        decimal.TryParse(GetText(TxtSalePrice), out var sale);
        decimal.TryParse(GetText(TxtMaxDisc), out var maxDisc);
        int.TryParse(GetText(TxtPrepTime), out var prep);
        int.TryParse(GetText(TxtDisplayOrder), out var displayOrd);

        CostPrice = cost;
        SalePrice = sale;
        MaxDiscount = maxDisc;
        PrepTime = prep;
        DisplayOrder = displayOrd;
        SKU = GetText(TxtSKU);
        Barcode = GetText(TxtBarcode);
        Description = GetText(TxtDescription);
        IsActive = ChkActive.IsChecked == true;

        // Expose recipe items
        RecipeItems = _currentRecipeRows;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void DecimalOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = NumericRegex.IsMatch(e.Text);

    private static void IntOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = IntegerRegex.IsMatch(e.Text);
}
