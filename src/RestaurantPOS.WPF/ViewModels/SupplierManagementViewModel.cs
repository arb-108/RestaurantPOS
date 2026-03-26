using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class SupplierManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;

    // ── Suppliers tab ──
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    private List<Supplier> _allSuppliers = [];

    // ── Expenses tab ──
    public ObservableCollection<SupplierExpense> Expenses { get; } = [];
    private List<SupplierExpense> _allExpenses = [];
    public ObservableCollection<Supplier> ExpenseFilterSuppliers { get; } = [];

    // ── Filters ──
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _expenseSearch = string.Empty;
    [ObservableProperty] private Supplier? _expenseFilterSupplier;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private SupplierExpense? _selectedExpense;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _supplierCountText = "0 suppliers";
    [ObservableProperty] private string _expenseCountText = "0 expenses";
    [ObservableProperty] private string _totalExpenseText = "Rs. 0";
    [ObservableProperty] private int _selectedTab;

    public SupplierManagementViewModel(PosDbContext db)
    {
        _db = db;
        Title = "Supplier Management";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _allSuppliers = await _db.Suppliers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

        _allExpenses = await _db.SupplierExpenses
            .Include(e => e.Supplier)
            .Where(e => e.IsActive)
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync();

        ExpenseFilterSuppliers.Clear();
        ExpenseFilterSuppliers.Add(new Supplier { Id = 0, Name = "-- All --" });
        foreach (var s in _allSuppliers) ExpenseFilterSuppliers.Add(s);
        ExpenseFilterSupplier = ExpenseFilterSuppliers[0];

        ApplySupplierFilter();
        ApplyExpenseFilter();
    }

    // ── Supplier filter ──
    partial void OnSearchTextChanged(string value) => ApplySupplierFilter();

    private void ApplySupplierFilter()
    {
        IEnumerable<Supplier> query = _allSuppliers;
        var search = SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(s =>
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Phone?.Contains(search) ?? false) ||
                (s.City?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        var list = query.ToList();
        Suppliers.Clear();
        foreach (var s in list) Suppliers.Add(s);
        SupplierCountText = $"{list.Count} supplier{(list.Count != 1 ? "s" : "")}";
    }

    // ── Expense filter ──
    partial void OnExpenseSearchChanged(string value) => ApplyExpenseFilter();
    partial void OnExpenseFilterSupplierChanged(Supplier? value) => ApplyExpenseFilter();

    private void ApplyExpenseFilter()
    {
        IEnumerable<SupplierExpense> query = _allExpenses;

        if (ExpenseFilterSupplier != null && ExpenseFilterSupplier.Id != 0)
            query = query.Where(e => e.SupplierId == ExpenseFilterSupplier.Id);

        var search = ExpenseSearch?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(e =>
                e.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.InvoiceNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

        var list = query.ToList();
        Expenses.Clear();
        foreach (var e in list) Expenses.Add(e);
        ExpenseCountText = $"{list.Count} expense{(list.Count != 1 ? "s" : "")}";
        TotalExpenseText = $"Rs. {list.Sum(e => e.Amount) / 100m:N0}";
    }

    // ── CRUD: Suppliers ──
    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        var dlg = new AddSupplierWindow() { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var s = new Supplier
            {
                Name = dlg.SupplierName,
                ContactPerson = NullIfEmpty(dlg.ContactPerson),
                Phone = NullIfEmpty(dlg.SupplierPhone),
                Email = NullIfEmpty(dlg.SupplierEmail),
                Address = NullIfEmpty(dlg.SupplierAddress),
                City = NullIfEmpty(dlg.SupplierCity),
                Notes = NullIfEmpty(dlg.SupplierNotes)
            };
            _db.Suppliers.Add(s);
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Added: {s.Name}";
        }
    }

    [RelayCommand]
    private async Task EditSupplierAsync(Supplier? supplier)
    {
        if (supplier == null) return;
        var dlg = new AddSupplierWindow(supplier) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            supplier.Name = dlg.SupplierName;
            supplier.ContactPerson = NullIfEmpty(dlg.ContactPerson);
            supplier.Phone = NullIfEmpty(dlg.SupplierPhone);
            supplier.Email = NullIfEmpty(dlg.SupplierEmail);
            supplier.Address = NullIfEmpty(dlg.SupplierAddress);
            supplier.City = NullIfEmpty(dlg.SupplierCity);
            supplier.Notes = NullIfEmpty(dlg.SupplierNotes);
            supplier.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Updated: {supplier.Name}";
        }
    }

    [RelayCommand]
    private async Task DeleteSupplierAsync(Supplier? supplier)
    {
        if (supplier == null) return;
        var r = System.Windows.MessageBox.Show($"Delete \"{supplier.Name}\"?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes)
        {
            supplier.IsActive = false;
            supplier.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Deleted: {supplier.Name}";
        }
    }

    // ── CRUD: Expenses ──
    [RelayCommand]
    private async Task AddExpenseAsync()
    {
        var dlg = new AddExpenseWindow(_allSuppliers) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var exp = new SupplierExpense
            {
                SupplierId = dlg.SelectedSupplier!.Id,
                Description = dlg.ExpenseDescription,
                Amount = (long)(dlg.ExpenseAmount * 100),
                ExpenseDate = dlg.ExpenseDate.ToUniversalTime(),
                InvoiceNumber = NullIfEmpty(dlg.InvoiceNumber),
                Category = NullIfEmpty(dlg.ExpenseCategory),
                IsPaid = dlg.IsPaid,
                Notes = NullIfEmpty(dlg.ExpenseNotes)
            };
            _db.SupplierExpenses.Add(exp);
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = $"Expense added: {exp.Description}";
        }
    }

    [RelayCommand]
    private async Task DeleteExpenseAsync(SupplierExpense? expense)
    {
        if (expense == null) return;
        var r = System.Windows.MessageBox.Show($"Delete expense \"{expense.Description}\"?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes)
        {
            expense.IsActive = false;
            expense.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await LoadDataAsync();
            StatusMessage = "Expense deleted";
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
