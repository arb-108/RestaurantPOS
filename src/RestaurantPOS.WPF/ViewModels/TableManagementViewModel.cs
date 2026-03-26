using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class TableManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;

    public ObservableCollection<Table> Tables { get; } = [];
    public ObservableCollection<FloorPlan> FloorPlans { get; } = [];
    public ObservableCollection<FloorPlan> FilterFloors { get; } = [];
    public ObservableCollection<string> StatusOptions { get; } = ["All", "Available", "Occupied", "Reserved", "Cleaning"];

    [ObservableProperty] private FloorPlan? _filterFloor;
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Table? _selectedTable;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _itemCountText = "0 tables";

    private List<Table> _allTables = [];

    public TableManagementViewModel(PosDbContext db)
    {
        _db = db;
        Title = "Table Management";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        var floors = await _db.FloorPlans.Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToListAsync();
        FloorPlans.Clear();
        foreach (var f in floors) FloorPlans.Add(f);

        FilterFloors.Clear();
        FilterFloors.Add(new FloorPlan { Id = 0, Name = "-- All Floors --" });
        foreach (var f in floors) FilterFloors.Add(f);
        FilterFloor = FilterFloors[0];

        _allTables = await _db.Tables
            .Include(t => t.FloorPlan)
            .OrderBy(t => t.FloorPlan.DisplayOrder)
            .ThenBy(t => t.DisplayOrder)
            .ToListAsync();

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterFloorChanged(FloorPlan? value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Table> query = _allTables;

        var search = SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        if (FilterFloor != null && FilterFloor.Id != 0)
            query = query.Where(t => t.FloorPlanId == FilterFloor.Id);

        if (StatusFilter != "All")
        {
            if (Enum.TryParse<TableStatus>(StatusFilter, out var st))
                query = query.Where(t => t.Status == st);
        }

        var filtered = query.ToList();
        Tables.Clear();
        foreach (var t in filtered) Tables.Add(t);
        ItemCountText = $"{filtered.Count} table{(filtered.Count != 1 ? "s" : "")}";
    }

    [RelayCommand]
    private async Task AddTableAsync()
    {
        var dlg = new AddTableWindow(FloorPlans) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var table = new Table
            {
                Name = dlg.TableName,
                FloorPlanId = dlg.SelectedFloor!.Id,
                Capacity = dlg.Capacity,
                Shape = dlg.SelectedShape,
                DisplayOrder = dlg.TableDisplayOrder,
                Status = TableStatus.Available
            };
            _db.Tables.Add(table);
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Added: {table.Name}";
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
}
