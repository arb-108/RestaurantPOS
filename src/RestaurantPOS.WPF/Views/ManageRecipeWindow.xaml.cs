using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class ManageRecipeWindow : Window
{
    private readonly List<Ingredient> _ingredients;
    private readonly ObservableCollection<RecipeItemRow> _items = [];
    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    public List<RecipeItemRow> RecipeItems => [.. _items];

    public ManageRecipeWindow(
        string itemName,
        string categoryName,
        IEnumerable<Ingredient> ingredients,
        IEnumerable<RecipeItemRow>? existing = null)
    {
        InitializeComponent();
        _ingredients = ingredients.OrderBy(i => i.Name).ToList();

        TxtProductName.Text = itemName;
        TxtProductInfo.Text = $"Category: {categoryName}  |  Define which stock ingredients make up this item";

        CmbIngredient.ItemsSource = _ingredients;
        if (_ingredients.Count > 0) CmbIngredient.SelectedIndex = 0;

        LstRecipeItems.ItemsSource = _items;

        if (existing != null)
            foreach (var row in existing)
                _items.Add(row);

        TxtQty.PreviewTextInput += DecimalOnly;
        DataObject.AddPastingHandler(TxtQty, NumericPaste);

        RecalcTotals();
    }

    private void CmbIngredient_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbIngredient.SelectedItem is Ingredient ing)
            TxtUnit.Text = ing.Unit ?? "";
        else
            TxtUnit.Text = "";
    }

    private void AddIngredient_Click(object sender, RoutedEventArgs e)
    {
        if (CmbIngredient.SelectedItem is not Ingredient ing) return;
        if (!decimal.TryParse(TxtQty.Text.Trim(), out var qty) || qty <= 0)
        {
            MessageBox.Show("Please enter a valid quantity.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtQty.Focus();
            return;
        }

        if (_items.Any(i => i.IngredientId == ing.Id))
        {
            MessageBox.Show($"\"{ing.Name}\" is already in the recipe. Remove it first to change quantity.",
                "Duplicate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _items.Add(new RecipeItemRow
        {
            IngredientId = ing.Id,
            Name = ing.Name,
            Quantity = qty,
            Unit = ing.Unit ?? "",
            CostPerUnit = ing.CostPerUnit
        });

        TxtQty.Text = "1";
        RecalcTotals();
    }

    private void RemoveIngredient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is RecipeItemRow row)
        {
            _items.Remove(row);
            RecalcTotals();
        }
    }

    private void RecalcTotals()
    {
        RunCount.Text = _items.Count.ToString();
        var total = _items.Sum(i => i.CostPerUnit * (long)Math.Ceiling(i.Quantity)) / 100m;
        RunTotalCost.Text = total.ToString("N0");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static void DecimalOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = NumericRegex.IsMatch(e.Text);

    private static void NumericPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (NumericRegex.IsMatch(text)) e.CancelCommand();
        }
        else e.CancelCommand();
    }
}

public class RecipeItemRow
{
    public int IngredientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = string.Empty;
    public long CostPerUnit { get; set; }
    public string QuantityText => Quantity.ToString("0.##");
    public string CostText => $"Rs {CostPerUnit / 100m:N0}";
    public string LineCostText => $"Rs {CostPerUnit * (long)Math.Ceiling(Quantity) / 100m:N0}";
}
