using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.WPF.Views;

public partial class AddTableWindow : Window
{
    public string TableName { get; private set; } = string.Empty;
    public FloorPlan? SelectedFloor { get; private set; }
    public int Capacity { get; private set; } = 4;
    public ShapeType SelectedShape { get; private set; } = ShapeType.Rectangle;
    public int TableDisplayOrder { get; private set; } = 1;

    private static readonly Regex IntegerRegex = new(@"[^0-9]", RegexOptions.Compiled);

    public AddTableWindow(IEnumerable<FloorPlan> floors, Table? existing = null)
    {
        InitializeComponent();
        CmbFloor.ItemsSource = floors;
        CmbShape.ItemsSource = Enum.GetValues<ShapeType>();
        CmbShape.SelectedItem = ShapeType.Rectangle;

        TxtCapacity.PreviewTextInput += IntOnly;
        TxtOrder.PreviewTextInput += IntOnly;

        if (existing != null)
        {
            Title = "Edit Table";
            TxtName.Text = existing.Name;
            TxtCapacity.Text = existing.Capacity.ToString();
            TxtOrder.Text = existing.DisplayOrder.ToString();
            CmbShape.SelectedItem = existing.Shape;
            foreach (FloorPlan f in CmbFloor.Items)
                if (f.Id == existing.FloorPlanId) { CmbFloor.SelectedItem = f; break; }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        { MessageBox.Show("Table name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (CmbFloor.SelectedItem is not FloorPlan floor)
        { MessageBox.Show("Select a floor.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        TableName = TxtName.Text.Trim();
        SelectedFloor = floor;
        int.TryParse(TxtCapacity.Text, out var cap); Capacity = cap > 0 ? cap : 4;
        SelectedShape = CmbShape.SelectedItem is ShapeType s ? s : ShapeType.Rectangle;
        int.TryParse(TxtOrder.Text, out var ord); TableDisplayOrder = ord;
        DialogResult = true; Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private static void IntOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = IntegerRegex.IsMatch(e.Text);
}
