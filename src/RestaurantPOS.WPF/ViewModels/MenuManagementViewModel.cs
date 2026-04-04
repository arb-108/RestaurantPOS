using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class MenuManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ═══ TAB 0 — Products ═══
    private List<MenuItem> _allItems = [];
    public ObservableCollection<MenuItem> MenuItems { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<KitchenStation> KitchenStations { get; } = [];
    public ObservableCollection<Category> FilterCategories { get; } = [];
    public ObservableCollection<KitchenStation> FilterStations { get; } = [];
    public ObservableCollection<string> ActiveFilterOptions { get; } = ["All", "Active", "Inactive"];

    [ObservableProperty] private Category? _filterCategory;
    [ObservableProperty] private KitchenStation? _filterStation;
    [ObservableProperty] private string _activeFilter = "All";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _pageInfoText = "0 items";

    // ═══ TAB 1 — Tables ═══
    private List<Table> _allTables = [];
    public ObservableCollection<Table> Tables { get; } = [];
    public ObservableCollection<FloorPlan> FloorPlans { get; } = [];
    public ObservableCollection<FloorPlan> FilterFloors { get; } = [];
    public ObservableCollection<string> StatusOptions { get; } = ["All", "Available", "Occupied", "Reserved", "Cleaning"];

    [ObservableProperty] private FloorPlan? _filterFloor;
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _tableSearch = string.Empty;
    [ObservableProperty] private string _tableCountText = "0 tables";

    // ═══ Deals (loaded for deal dialog) ═══
    private List<Deal> _allDeals = [];
    public ObservableCollection<Deal> Deals { get; } = [];
    [ObservableProperty] private string _dealCountText = "0 deals";

    public MenuManagementViewModel(PosDbContext db)
    {
        _db = db;
        Title = "Menu Settings";
    }

    // ═══════════════════════════════════════════
    //  DATA LOADING
    // ═══════════════════════════════════════════
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // Categories & stations
        var categories = await _db.Categories.Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ToListAsync();
        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);

        FilterCategories.Clear();
        FilterCategories.Add(new Category { Id = 0, Name = "-- None --" });
        foreach (var c in categories) FilterCategories.Add(c);
        FilterCategory = FilterCategories[0];

        var stations = await _db.KitchenStations.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        KitchenStations.Clear();
        foreach (var s in stations) KitchenStations.Add(s);

        FilterStations.Clear();
        FilterStations.Add(new KitchenStation { Id = 0, Name = "-- None --" });
        foreach (var s in stations) FilterStations.Add(s);
        FilterStation = FilterStations[0];

        // Products
        _allItems = await _db.MenuItems
            .Include(m => m.Category).Include(m => m.KitchenStation)
            .OrderBy(m => m.Category.Name).ThenBy(m => m.DisplayOrder)
            .ToListAsync();
        ApplyProductFilter();

        // Deals
        _allDeals = await _db.Deals
            .Include(d => d.Items).ThenInclude(di => di.MenuItem)
            .Include(d => d.Category)
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync();
        Deals.Clear();
        foreach (var d in _allDeals) Deals.Add(d);
        DealCountText = $"{_allDeals.Count} deal{(_allDeals.Count != 1 ? "s" : "")}";

        // Tables
        var floors = await _db.FloorPlans.Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToListAsync();
        FloorPlans.Clear();
        foreach (var f in floors) FloorPlans.Add(f);

        FilterFloors.Clear();
        FilterFloors.Add(new FloorPlan { Id = 0, Name = "-- All Floors --" });
        foreach (var f in floors) FilterFloors.Add(f);
        FilterFloor = FilterFloors[0];

        _allTables = await _db.Tables
            .Include(t => t.FloorPlan)
            .Where(t => t.IsActive)
            .OrderBy(t => t.FloorPlan.DisplayOrder).ThenBy(t => t.DisplayOrder)
            .ToListAsync();
        ApplyTableFilter();
    }

    // ═══ Product Filters ═══
    partial void OnSearchTextChanged(string value) => ApplyProductFilter();
    partial void OnFilterCategoryChanged(Category? value) => ApplyProductFilter();
    partial void OnFilterStationChanged(KitchenStation? value) => ApplyProductFilter();
    partial void OnActiveFilterChanged(string value) => ApplyProductFilter();

    private void ApplyProductFilter()
    {
        IEnumerable<MenuItem> query = _allItems;
        var search = SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(i =>
                i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (i.Category?.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Barcode?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.SKU?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        if (FilterCategory != null && FilterCategory.Id != 0)
            query = query.Where(i => i.CategoryId == FilterCategory.Id);
        if (FilterStation != null && FilterStation.Id != 0)
            query = query.Where(i => i.KitchenStationId == FilterStation.Id);
        if (ActiveFilter == "Active") query = query.Where(i => i.IsActive);
        else if (ActiveFilter == "Inactive") query = query.Where(i => !i.IsActive);

        var filtered = query.ToList();
        MenuItems.Clear();
        foreach (var item in filtered) MenuItems.Add(item);
        PageInfoText = $"{filtered.Count} item{(filtered.Count != 1 ? "s" : "")}";
    }

    // ═══ Table Filters ═══
    partial void OnTableSearchChanged(string value) => ApplyTableFilter();
    partial void OnFilterFloorChanged(FloorPlan? value) => ApplyTableFilter();
    partial void OnStatusFilterChanged(string value) => ApplyTableFilter();

    private void ApplyTableFilter()
    {
        IEnumerable<Table> query = _allTables;
        var search = TableSearch?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (FilterFloor != null && FilterFloor.Id != 0)
            query = query.Where(t => t.FloorPlanId == FilterFloor.Id);
        if (StatusFilter != "All" && Enum.TryParse<TableStatus>(StatusFilter, out var st))
            query = query.Where(t => t.Status == st);

        var filtered = query.ToList();
        Tables.Clear();
        foreach (var t in filtered) Tables.Add(t);
        TableCountText = $"{filtered.Count} table{(filtered.Count != 1 ? "s" : "")}";
    }

    // ═══ CRUD: Products ═══
    [RelayCommand]
    private async Task AddProductAsync()
    {
        var ingredients = await _db.Ingredients.OrderBy(i => i.Name).ToListAsync();
        var dlg = new AddProductWindow(Categories, KitchenStations, ingredients: ingredients) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var item = new MenuItem
            {
                Name = dlg.ItemName,
                CategoryId = dlg.SelectedCategory!.Id,
                KitchenStationId = dlg.SelectedKitchenStation?.Id,
                BasePrice = (long)(dlg.SalePrice * 100),
                CostPrice = (long)(dlg.CostPrice * 100),
                SKU = NullIfEmpty(dlg.SKU),
                Barcode = NullIfEmpty(dlg.Barcode),
                IsActive = dlg.IsActive,
                Description = NullIfEmpty(dlg.Description),
                PrepTimeMinutes = dlg.PrepTime,
                MaxDiscount = (long)(dlg.MaxDiscount * 100),
                DisplayOrder = dlg.DisplayOrder
            };
            _db.MenuItems.Add(item);
            await _db.SaveChangesAsync();

            // Save recipe items if any were defined
            if (dlg.RecipeItems.Count > 0)
            {
                foreach (var row in dlg.RecipeItems)
                    _db.Recipes.Add(new Recipe { MenuItemId = item.Id, IngredientId = row.IngredientId, Quantity = row.Quantity });
                await _db.SaveChangesAsync();
            }

            await LoadDataAsync();
            StatusMessage = $"Added: {item.Name}";
        }
    }

    [RelayCommand]
    private async Task EditProductAsync(MenuItem? item)
    {
        if (item == null) return;
        var ingredients = await _db.Ingredients.OrderBy(i => i.Name).ToListAsync();
        var existingRecipes = await _db.Recipes
            .Where(r => r.MenuItemId == item.Id)
            .Include(r => r.Ingredient)
            .ToListAsync();
        var existingRows = existingRecipes.Select(r => new RecipeItemRow
        {
            IngredientId = r.IngredientId,
            Name = r.Ingredient.Name,
            Quantity = r.Quantity,
            Unit = r.Ingredient.Unit ?? "",
            CostPerUnit = r.Ingredient.CostPerUnit
        });

        var dlg = new AddProductWindow(Categories, KitchenStations, item, ingredients, existingRows) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            item.Name = dlg.ItemName;
            item.CategoryId = dlg.SelectedCategory!.Id;
            item.KitchenStationId = dlg.SelectedKitchenStation?.Id;
            item.BasePrice = (long)(dlg.SalePrice * 100);
            item.CostPrice = (long)(dlg.CostPrice * 100);
            item.SKU = NullIfEmpty(dlg.SKU);
            item.Barcode = NullIfEmpty(dlg.Barcode);
            item.IsActive = dlg.IsActive;
            item.Description = NullIfEmpty(dlg.Description);
            item.PrepTimeMinutes = dlg.PrepTime;
            item.MaxDiscount = (long)(dlg.MaxDiscount * 100);
            item.DisplayOrder = dlg.DisplayOrder;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Save recipe items if recipe was modified
            if (dlg.RecipeModified)
            {
                var oldRecipes = await _db.Recipes.Where(r => r.MenuItemId == item.Id).ToListAsync();
                _db.Recipes.RemoveRange(oldRecipes);
                foreach (var row in dlg.RecipeItems)
                    _db.Recipes.Add(new Recipe { MenuItemId = item.Id, IngredientId = row.IngredientId, Quantity = row.Quantity });
                await _db.SaveChangesAsync();
            }

            await LoadDataAsync();
            StatusMessage = $"Updated: {item.Name}";
        }
    }

    [RelayCommand]
    private async Task DeleteProductAsync(MenuItem? item)
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
    private async Task AddCategoryAsync()
    {
        var dlg = new InputDialog("Enter new category name:", "Add Category") { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        _db.Categories.Add(new Category { Name = dlg.InputText.Trim(), DisplayOrder = Categories.Count + 1 });
        await _db.SaveChangesAsync();
        await LoadDataAsync();
        StatusMessage = $"Category added: {dlg.InputText.Trim()}";
    }

    [RelayCommand]
    private async Task SetCategoryImageAsync()
    {
        if (FilterCategory == null || FilterCategory.Id == 0)
        {
            StatusMessage = "Select a category first (not 'All')";
            return;
        }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Select image for '{FilterCategory.Name}'",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*"
        };
        if (ofd.ShowDialog() != true) return;

        // Copy to Assets\Images folder with category-friendly name
        var imagesDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images");
        System.IO.Directory.CreateDirectory(imagesDir);

        var ext = System.IO.Path.GetExtension(ofd.FileName);
        var fileName = FilterCategory.Name.ToLowerInvariant()
            .Replace(" & ", "-").Replace(" ", "-").Replace("/", "-") + ext;
        var destPath = System.IO.Path.Combine(imagesDir, fileName);

        System.IO.File.Copy(ofd.FileName, destPath, overwrite: true);

        // Also copy to source Assets\Images so it persists across builds
        var srcImagesDir = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RestaurantPOS.WPF", "Assets", "Images");
        if (System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(srcImagesDir)!))
        {
            System.IO.Directory.CreateDirectory(srcImagesDir);
            System.IO.File.Copy(ofd.FileName,
                System.IO.Path.Combine(srcImagesDir, fileName), overwrite: true);
        }

        // Update DB
        FilterCategory.ImagePath = fileName;
        _db.Categories.Update(FilterCategory);
        await _db.SaveChangesAsync();

        StatusMessage = $"Image set for {FilterCategory.Name}: {fileName}";
    }

    // ═══ CRUD: Deals ═══
    [RelayCommand]
    private async Task CreateDealAsync()
    {
        var dlg = new AddDealWindow(_allItems, Categories) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var deal = new Deal
            {
                Name = dlg.DealName,
                Description = NullIfEmpty(dlg.DealDescription),
                DealPrice = (long)(dlg.DealPrice * 100),
                OriginalPrice = (long)(dlg.OriginalPrice * 100),
                CategoryId = dlg.SelectedCategory?.Id,
                DisplayOrder = dlg.DealDisplayOrder
            };
            foreach (var di in dlg.DealItems)
                deal.Items.Add(new DealItem { MenuItemId = di.MenuItemId, Quantity = di.Quantity });
            _db.Deals.Add(deal);

            // Also create a MenuItem so the deal appears in the POS menu
            var dealsCat = await _db.Categories.FirstOrDefaultAsync(c => c.Name == "Deals");
            // Build component list for tooltip
            var componentDesc = string.Join(", ", dlg.DealItems.Select(di => $"{di.ItemName} ×{di.Quantity}"));
            var desc = string.IsNullOrWhiteSpace(deal.Description)
                ? $"Includes: {componentDesc}"
                : $"{deal.Description}\nIncludes: {componentDesc}";
            _db.MenuItems.Add(new MenuItem
            {
                Name = deal.Name,
                Description = desc,
                BasePrice = deal.DealPrice,
                CostPrice = deal.OriginalPrice,
                CategoryId = dealsCat?.Id ?? deal.CategoryId ?? 1,
                DisplayOrder = deal.DisplayOrder
            });

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deal created: {deal.Name}";
        }
    }

    [RelayCommand]
    private async Task EditDealAsync(Deal? deal)
    {
        if (deal == null) return;
        var dlg = new AddDealWindow(_allItems, Categories, deal) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var oldName = deal.Name;
            deal.Name = dlg.DealName;
            deal.Description = NullIfEmpty(dlg.DealDescription);
            deal.DealPrice = (long)(dlg.DealPrice * 100);
            deal.OriginalPrice = (long)(dlg.OriginalPrice * 100);
            deal.CategoryId = dlg.SelectedCategory?.Id;
            deal.DisplayOrder = dlg.DealDisplayOrder;
            deal.UpdatedAt = DateTime.UtcNow;
            _db.DealItems.RemoveRange(deal.Items);
            deal.Items.Clear();
            foreach (var di in dlg.DealItems)
                deal.Items.Add(new DealItem { MenuItemId = di.MenuItemId, Quantity = di.Quantity });

            // Sync corresponding MenuItem
            var linkedItem = await _db.MenuItems.FirstOrDefaultAsync(m => m.Name == oldName && m.CategoryId == (deal.CategoryId ?? 0));
            if (linkedItem == null)
                linkedItem = await _db.MenuItems.FirstOrDefaultAsync(m => m.Name == oldName);
            if (linkedItem != null)
            {
                linkedItem.Name = deal.Name;
                linkedItem.Description = deal.Description;
                linkedItem.BasePrice = deal.DealPrice;
                linkedItem.CostPrice = deal.OriginalPrice;
                linkedItem.DisplayOrder = deal.DisplayOrder;
                linkedItem.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deal updated: {deal.Name}";
        }
    }

    [RelayCommand]
    private async Task DeleteDealAsync(Deal? deal)
    {
        if (deal == null) return;
        var r = System.Windows.MessageBox.Show($"Delete deal \"{deal.Name}\"?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes)
        {
            deal.IsActive = false;
            deal.UpdatedAt = DateTime.UtcNow;

            // Also deactivate linked MenuItem
            var linkedItem = await _db.MenuItems.FirstOrDefaultAsync(m => m.Name == deal.Name);
            if (linkedItem != null)
            {
                linkedItem.IsActive = false;
                linkedItem.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deal deleted: {deal.Name}";
        }
    }

    // ═══ CRUD: Tables ═══
    [RelayCommand]
    private async Task AddTableAsync()
    {
        var dlg = new AddTableWindow(FloorPlans) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            _db.Tables.Add(new Table
            {
                Name = dlg.TableName, FloorPlanId = dlg.SelectedFloor!.Id,
                Capacity = dlg.Capacity, Shape = dlg.SelectedShape,
                DisplayOrder = dlg.TableDisplayOrder, Status = TableStatus.Available
            });
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Table added";
        }
    }

    [RelayCommand]
    private async Task EditTableAsync(Table? table)
    {
        if (table == null) return;
        var dlg = new AddTableWindow(FloorPlans, table) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            table.Name = dlg.TableName;
            table.FloorPlanId = dlg.SelectedFloor!.Id;
            table.Capacity = dlg.Capacity;
            table.Shape = dlg.SelectedShape;
            table.DisplayOrder = dlg.TableDisplayOrder;
            table.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Updated: {table.Name}";
        }
    }

    [RelayCommand]
    private async Task DeleteTableAsync(Table? table)
    {
        if (table == null) return;
        var r = System.Windows.MessageBox.Show($"Delete \"{table.Name}\"?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes)
        {
            table.IsActive = false;
            table.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deleted: {table.Name}";
        }
    }

    [RelayCommand]
    private async Task AddFloorAsync()
    {
        var dlg = new InputDialog("Enter floor/area name:", "Add Floor") { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            _db.FloorPlans.Add(new FloorPlan { Name = dlg.InputText.Trim(), DisplayOrder = FloorPlans.Count + 1 });
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Floor added: {dlg.InputText.Trim()}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
        StatusMessage = "Refreshed";
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
