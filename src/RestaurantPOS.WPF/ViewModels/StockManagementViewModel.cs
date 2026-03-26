using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class StockManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;

    // ═══ Stats ═══
    [ObservableProperty] private int _totalItems;
    [ObservableProperty] private int _lowStockCount;
    [ObservableProperty] private string _stockValueText = "Rs 0";
    [ObservableProperty] private int _activeSupplierCount;

    // ═══ Stock Items ═══
    private List<Ingredient> _allItems = [];
    public ObservableCollection<Ingredient> StockItems { get; } = [];

    // ═══ Low Stock / Reorder ═══
    public ObservableCollection<Ingredient> LowStockItems { get; } = [];

    // ═══ Suppliers ═══
    public ObservableCollection<Supplier> Suppliers { get; } = [];

    // ═══ Recent Movements ═══
    public ObservableCollection<StockMovement> RecentMovements { get; } = [];

    // ═══ Filters ═══
    public ObservableCollection<string> StockCategoryOptions { get; } = ["All", "Dry Goods", "Cold / Chilled", "Sauces & Condiments", "Beverages", "Packaging"];
    public ObservableCollection<string> StockStatusOptions { get; } = ["All", "Low Stock", "OK", "Critical"];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _itemCountText = "0 items";
    [ObservableProperty] private int _selectedTab;

    public StockManagementViewModel(PosDbContext db)
    {
        _db = db;
        Title = "Stock Management";
    }

    // ═══════════════════════════════════════════
    //  DATA LOADING
    // ═══════════════════════════════════════════
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // Stock items
        _allItems = await _db.Ingredients
            .Include(i => i.Supplier)
            .Where(i => i.IsActive)
            .OrderBy(i => i.Name)
            .ToListAsync();

        ApplyFilter();

        // Stats
        TotalItems = _allItems.Count;
        LowStockCount = _allItems.Count(i => i.CurrentStock <= i.ReorderLevel && i.CurrentStock > 0);
        var criticalCount = _allItems.Count(i => i.CurrentStock <= 0);
        LowStockCount += criticalCount;

        var totalValue = _allItems.Sum(i => (long)(i.CurrentStock * i.CostPerUnit));
        StockValueText = $"Rs {totalValue / 100m:N0}";

        // Low stock items for reorder section
        LowStockItems.Clear();
        foreach (var item in _allItems.Where(i => i.CurrentStock <= i.ReorderLevel).OrderBy(i => i.CurrentStock / Math.Max(i.ReorderLevel, 1)))
            LowStockItems.Add(item);

        // Suppliers
        var suppliers = await _db.Suppliers.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        Suppliers.Clear();
        foreach (var s in suppliers) Suppliers.Add(s);
        ActiveSupplierCount = suppliers.Count;

        // Recent movements
        var movements = await _db.StockMovements
            .Include(m => m.Ingredient)
            .Include(m => m.User)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .ToListAsync();
        RecentMovements.Clear();
        foreach (var m in movements) RecentMovements.Add(m);
    }

    // ═══ Filters ═══
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedStatusChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Ingredient> query = _allItems;
        var search = SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(i =>
                i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (i.StockCategory?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Supplier?.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (SelectedCategory != "All")
            query = query.Where(i => i.StockCategory == SelectedCategory);

        if (SelectedStatus == "Low Stock")
            query = query.Where(i => i.CurrentStock <= i.ReorderLevel && i.CurrentStock > 0);
        else if (SelectedStatus == "Critical")
            query = query.Where(i => i.CurrentStock <= 0);
        else if (SelectedStatus == "OK")
            query = query.Where(i => i.CurrentStock > i.ReorderLevel);

        var filtered = query.ToList();
        StockItems.Clear();
        foreach (var item in filtered) StockItems.Add(item);
        ItemCountText = $"Showing {filtered.Count} / {_allItems.Count}";
    }

    // ═══ CRUD: Stock Items ═══
    [RelayCommand]
    private async Task AddStockItemAsync()
    {
        var suppliers = await _db.Suppliers.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        var dlg = new AddStockItemWindow(suppliers) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var item = new Ingredient
            {
                Name = dlg.ItemName,
                StockCategory = dlg.StockCategory,
                CurrentStock = dlg.CurrentQty,
                ReorderLevel = dlg.MinLevel,
                Unit = dlg.StockUnit,
                CostPerUnit = (long)(dlg.UnitCost * 100),
                SupplierId = dlg.SelectedSupplier?.Id
            };
            _db.Ingredients.Add(item);

            // Record initial stock movement
            if (dlg.CurrentQty > 0)
            {
                _db.StockMovements.Add(new StockMovement
                {
                    Ingredient = item,
                    Type = StockMovementType.Purchase,
                    Quantity = dlg.CurrentQty,
                    CostAmount = (long)(dlg.CurrentQty * dlg.UnitCost * 100),
                    Reference = "Initial Stock",
                    Notes = "Added via Stock Management"
                });
            }

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Added: {item.Name}";
        }
    }

    [RelayCommand]
    private async Task EditStockItemAsync(Ingredient? item)
    {
        if (item == null) return;
        var suppliers = await _db.Suppliers.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        var dlg = new AddStockItemWindow(suppliers, item) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var oldQty = item.CurrentStock;
            item.Name = dlg.ItemName;
            item.StockCategory = dlg.StockCategory;
            item.CurrentStock = dlg.CurrentQty;
            item.ReorderLevel = dlg.MinLevel;
            item.Unit = dlg.StockUnit;
            item.CostPerUnit = (long)(dlg.UnitCost * 100);
            item.SupplierId = dlg.SelectedSupplier?.Id;
            item.UpdatedAt = DateTime.UtcNow;

            // Record adjustment if qty changed
            if (dlg.CurrentQty != oldQty)
            {
                _db.StockMovements.Add(new StockMovement
                {
                    IngredientId = item.Id,
                    Type = StockMovementType.Adjustment,
                    Quantity = dlg.CurrentQty - oldQty,
                    Notes = $"Adjusted from {oldQty} to {dlg.CurrentQty}"
                });
            }

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Updated: {item.Name}";
        }
    }

    [RelayCommand]
    private async Task DeleteStockItemAsync(Ingredient? item)
    {
        if (item == null) return;
        var r = System.Windows.MessageBox.Show($"Delete \"{item.Name}\"?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes)
        {
            item.IsActive = false;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deleted: {item.Name}";
        }
    }

    [RelayCommand]
    private async Task RestockAsync(Ingredient? item)
    {
        if (item == null) return;
        var dlg = new InputDialog($"Enter restock quantity for \"{item.Name}\" ({item.Unit}):", "Restock Item")
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && decimal.TryParse(dlg.InputText, out var qty) && qty > 0)
        {
            item.CurrentStock += qty;
            item.UpdatedAt = DateTime.UtcNow;
            _db.StockMovements.Add(new StockMovement
            {
                IngredientId = item.Id,
                Type = StockMovementType.Purchase,
                Quantity = qty,
                CostAmount = (long)(qty * item.CostPerUnit),
                Notes = "Manual restock"
            });
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Restocked {qty} {item.Unit} of {item.Name}";
        }
    }

    [RelayCommand]
    private async Task RecordWastageAsync(Ingredient? item)
    {
        if (item == null) return;
        var dlg = new InputDialog($"Enter wastage quantity for \"{item.Name}\" ({item.Unit}):", "Record Wastage")
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && decimal.TryParse(dlg.InputText, out var qty) && qty > 0)
        {
            item.CurrentStock = Math.Max(0, item.CurrentStock - qty);
            item.UpdatedAt = DateTime.UtcNow;
            _db.StockMovements.Add(new StockMovement
            {
                IngredientId = item.Id,
                Type = StockMovementType.Waste,
                Quantity = -qty,
                Notes = "Manual wastage entry"
            });
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Recorded wastage: {qty} {item.Unit} of {item.Name}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
        StatusMessage = "Refreshed";
    }
}
