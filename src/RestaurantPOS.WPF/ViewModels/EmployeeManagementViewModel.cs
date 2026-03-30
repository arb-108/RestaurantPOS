using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using System.Collections.ObjectModel;
using System.Windows;

namespace RestaurantPOS.WPF.ViewModels;

public partial class EmployeeManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;
    private readonly IAuthService _authService;

    // ── Tab control ──
    [ObservableProperty] private int _selectedTab;

    // ── Role-based visibility ──
    /// <summary>True if user can see payroll tab and salary info (admin only, not manager).</summary>
    [ObservableProperty] private bool _canSeePayroll;
    /// <summary>True if user can add/edit/delete employees (admin=level5, manager=level4 can view only).</summary>
    [ObservableProperty] private bool _canManageEmployees;

    // ── Employee list ──
    private List<Employee> _allEmployees = [];
    public ObservableCollection<EmployeeRowViewModel> Employees { get; } = [];
    [ObservableProperty] private EmployeeRowViewModel? _selectedEmployee;
    [ObservableProperty] private string _employeeSearch = "";
    [ObservableProperty] private string _employeeCategoryFilter = "All";
    [ObservableProperty] private string _employeeCountText = "0 employees";

    public ObservableCollection<string> CategoryOptions { get; } =
        ["All", "Kitchen", "Service", "Delivery", "Management", "Cleaning", "Other"];

    // ── Payroll list ──
    public ObservableCollection<PayrollRowViewModel> Payrolls { get; } = [];
    [ObservableProperty] private string _payrollSearch = "";
    [ObservableProperty] private string _payrollMonthFilter = "";
    [ObservableProperty] private string _payrollCountText = "0 records";
    [ObservableProperty] private string _totalPayrollText = "Rs. 0";

    public ObservableCollection<string> MonthOptions { get; } = [];

    // ── Detail panel ──
    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string _detailPhone = "";
    [ObservableProperty] private string _detailCategory = "";
    [ObservableProperty] private string _detailDesignation = "";
    [ObservableProperty] private string _detailSalary = "";
    [ObservableProperty] private string _detailJoiningDate = "";
    [ObservableProperty] private string _detailEmploymentType = "";

    public EmployeeManagementViewModel(PosDbContext db, IAuthService authService)
    {
        _db = db;
        _authService = authService;
        Title = "Employee Management";
        BuildMonthOptions();

        // Admin (level 5) sees payroll & can manage; Manager (level 4) can only view employees
        int empLevel = _authService.GetAccessLevel("Manage employees");
        CanSeePayroll = _authService.HasPermission("Generate payroll"); // admin only
        CanManageEmployees = empLevel >= 5; // admin only
    }

    private void BuildMonthOptions()
    {
        MonthOptions.Clear();
        MonthOptions.Add("All");
        var now = DateTime.Now;
        for (int i = 0; i < 12; i++)
        {
            var d = now.AddMonths(-i);
            MonthOptions.Add(d.ToString("MMM yyyy"));
        }
        PayrollMonthFilter = "All";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _allEmployees = await _db.Employees
            .Include(e => e.User)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync();

        ApplyEmployeeFilter();
        await LoadPayrollsAsync();
    }

    partial void OnEmployeeSearchChanged(string value) => ApplyEmployeeFilter();
    partial void OnEmployeeCategoryFilterChanged(string value) => ApplyEmployeeFilter();
    partial void OnPayrollSearchChanged(string value) => _ = LoadPayrollsAsync();
    partial void OnPayrollMonthFilterChanged(string value) => _ = LoadPayrollsAsync();

    private void ApplyEmployeeFilter()
    {
        Employees.Clear();
        var filtered = _allEmployees.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(EmployeeSearch))
        {
            var q = EmployeeSearch.ToLowerInvariant();
            filtered = filtered.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (e.Phone != null && e.Phone.Contains(q)) ||
                (e.CNIC != null && e.CNIC.Contains(q)) ||
                (e.Designation != null && e.Designation.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (EmployeeCategoryFilter != "All")
        {
            var cat = Enum.Parse<EmployeeCategory>(EmployeeCategoryFilter);
            filtered = filtered.Where(e => e.Category == cat);
        }

        int serial = 1;
        foreach (var e in filtered)
        {
            Employees.Add(new EmployeeRowViewModel
            {
                Id = e.Id,
                Serial = serial++,
                Name = e.Name,
                Phone = e.Phone ?? "",
                CNIC = e.CNIC ?? "",
                Category = e.Category.ToString(),
                Designation = e.Designation ?? "",
                EmploymentType = e.EmploymentType.ToString(),
                BasicSalary = $"Rs. {e.BasicSalary / 100m:N0}",
                JoiningDate = e.JoiningDate.ToLocalTime().ToString("dd/MM/yyyy"),
                Status = e.LeavingDate.HasValue ? "Left" : "Active"
            });
        }

        EmployeeCountText = $"{Employees.Count} employee{(Employees.Count != 1 ? "s" : "")}";
    }

    private async Task LoadPayrollsAsync()
    {
        Payrolls.Clear();

        var query = _db.Payrolls
            .Include(p => p.Employee)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(PayrollSearch))
        {
            var q = PayrollSearch.ToLowerInvariant();
            query = query.Where(p => p.Employee.Name.ToLower().Contains(q));
        }

        if (PayrollMonthFilter != "All" && !string.IsNullOrEmpty(PayrollMonthFilter))
        {
            if (DateTime.TryParseExact(PayrollMonthFilter, "MMM yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                query = query.Where(p => p.Month == dt.Month && p.Year == dt.Year);
            }
        }

        var payrolls = await query
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .ThenBy(p => p.Employee.Name)
            .Take(200)
            .ToListAsync();

        long totalNet = 0;
        foreach (var p in payrolls)
        {
            totalNet += p.NetSalary;
            Payrolls.Add(new PayrollRowViewModel
            {
                Id = p.Id,
                EmployeeId = p.EmployeeId,
                EmployeeName = p.Employee.Name,
                Period = $"{new DateTime(p.Year, p.Month, 1):MMM yyyy}",
                BasicSalary = $"Rs. {p.BasicSalary / 100m:N0}",
                Allowances = $"Rs. {p.Allowances / 100m:N0}",
                Deductions = $"Rs. {p.Deductions / 100m:N0}",
                Bonus = $"Rs. {p.Bonus / 100m:N0}",
                Advance = $"Rs. {p.Advance / 100m:N0}",
                NetSalary = $"Rs. {p.NetSalary / 100m:N0}",
                Status = p.Status.ToString(),
                PaidAt = p.PaidAt?.ToLocalTime().ToString("dd/MM/yy") ?? ""
            });
        }

        PayrollCountText = $"{Payrolls.Count} record{(Payrolls.Count != 1 ? "s" : "")}";
        TotalPayrollText = $"Rs. {totalNet / 100m:N0}";
    }

    [RelayCommand]
    private async Task SelectEmployeeAsync(EmployeeRowViewModel? row)
    {
        if (row == null) return;
        SelectedEmployee = row;

        var emp = _allEmployees.FirstOrDefault(e => e.Id == row.Id);
        if (emp == null) return;

        DetailName = emp.Name;
        DetailPhone = emp.Phone ?? "";
        DetailCategory = emp.Category.ToString();
        DetailDesignation = emp.Designation ?? "";
        DetailSalary = $"Rs. {(emp.BasicSalary + emp.Allowances - emp.Deductions) / 100m:N0}";
        DetailJoiningDate = emp.JoiningDate.ToLocalTime().ToString("dd/MM/yyyy");
        DetailEmploymentType = emp.EmploymentType.ToString();
        IsDetailVisible = true;
    }

    [RelayCommand]
    private async Task AddEmployeeAsync()
    {
        var window = new Views.AddEmployeeWindow();
        window.Owner = System.Windows.Application.Current.MainWindow;

        if (window.ShowDialog() == true)
        {
            var emp = new Employee
            {
                Name = window.EmployeeName,
                Phone = string.IsNullOrWhiteSpace(window.EmployeePhone) ? null : window.EmployeePhone,
                Email = string.IsNullOrWhiteSpace(window.EmployeeEmail) ? null : window.EmployeeEmail,
                CNIC = string.IsNullOrWhiteSpace(window.EmployeeCNIC) ? null : window.EmployeeCNIC,
                Address = string.IsNullOrWhiteSpace(window.EmployeeAddress) ? null : window.EmployeeAddress,
                EmergencyContact = string.IsNullOrWhiteSpace(window.EmergencyContact) ? null : window.EmergencyContact,
                Category = window.SelectedCategory,
                EmploymentType = window.SelectedEmploymentType,
                Designation = string.IsNullOrWhiteSpace(window.EmployeeDesignation) ? null : window.EmployeeDesignation,
                JoiningDate = window.JoiningDate.ToUniversalTime(),
                BasicSalary = (long)(window.BasicSalary * 100),
                Allowances = (long)(window.AllowancesAmount * 100),
                Deductions = (long)(window.DeductionsAmount * 100),
            };

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task EditEmployeeAsync()
    {
        if (SelectedEmployee == null) return;
        var emp = await _db.Employees.FindAsync(SelectedEmployee.Id);
        if (emp == null) return;

        var window = new Views.AddEmployeeWindow();
        window.Title = "Edit Employee";
        window.Owner = System.Windows.Application.Current.MainWindow;

        window.Loaded += (_, _) =>
        {
            window.SetEditData(emp.Name, emp.Phone ?? "", emp.Email ?? "",
                emp.CNIC ?? "", emp.Address ?? "", emp.EmergencyContact ?? "",
                emp.Category, emp.EmploymentType, emp.Designation ?? "",
                emp.JoiningDate.ToLocalTime(), emp.BasicSalary / 100m,
                emp.Allowances / 100m, emp.Deductions / 100m);
        };

        if (window.ShowDialog() == true)
        {
            emp.Name = window.EmployeeName;
            emp.Phone = string.IsNullOrWhiteSpace(window.EmployeePhone) ? null : window.EmployeePhone;
            emp.Email = string.IsNullOrWhiteSpace(window.EmployeeEmail) ? null : window.EmployeeEmail;
            emp.CNIC = string.IsNullOrWhiteSpace(window.EmployeeCNIC) ? null : window.EmployeeCNIC;
            emp.Address = string.IsNullOrWhiteSpace(window.EmployeeAddress) ? null : window.EmployeeAddress;
            emp.EmergencyContact = string.IsNullOrWhiteSpace(window.EmergencyContact) ? null : window.EmergencyContact;
            emp.Category = window.SelectedCategory;
            emp.EmploymentType = window.SelectedEmploymentType;
            emp.Designation = string.IsNullOrWhiteSpace(window.EmployeeDesignation) ? null : window.EmployeeDesignation;
            emp.JoiningDate = window.JoiningDate.ToUniversalTime();
            emp.BasicSalary = (long)(window.BasicSalary * 100);
            emp.Allowances = (long)(window.AllowancesAmount * 100);
            emp.Deductions = (long)(window.DeductionsAmount * 100);
            emp.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await LoadDataAsync();
            await SelectEmployeeAsync(Employees.FirstOrDefault(e => e.Id == emp.Id));
        }
    }

    [RelayCommand]
    private async Task DeleteEmployeeAsync()
    {
        if (SelectedEmployee == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{SelectedEmployee.Name}'?",
            "Delete Employee", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var payrollCount = await _db.Payrolls.CountAsync(p => p.EmployeeId == SelectedEmployee.Id && p.IsActive);
            if (payrollCount > 0)
            {
                MessageBox.Show($"Cannot delete '{SelectedEmployee.Name}' — has {payrollCount} active payroll record(s).\nDelete payroll records first.",
                    "Delete Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var emp = await _db.Employees.FindAsync(SelectedEmployee.Id);
            if (emp != null)
            {
                emp.IsActive = false;
                emp.LeavingDate = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            IsDetailVisible = false;
            SelectedEmployee = null;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task GeneratePayrollAsync()
    {
        var now = DateTime.Now;
        var window = new Views.GeneratePayrollWindow(_allEmployees, now.Month, now.Year);
        window.Owner = System.Windows.Application.Current.MainWindow;

        if (window.ShowDialog() == true)
        {
            foreach (var item in window.PayrollItems)
            {
                // Check if already exists
                var existing = await _db.Payrolls.FirstOrDefaultAsync(p =>
                    p.EmployeeId == item.EmployeeId && p.Month == item.Month && p.Year == item.Year);
                if (existing != null) continue;

                _db.Payrolls.Add(new Payroll
                {
                    EmployeeId = item.EmployeeId,
                    Month = item.Month,
                    Year = item.Year,
                    BasicSalary = item.BasicSalary,
                    Allowances = item.Allowances,
                    Deductions = item.Deductions,
                    Bonus = item.Bonus,
                    Advance = item.Advance,
                    NetSalary = item.BasicSalary + item.Allowances - item.Deductions + item.Bonus - item.Advance,
                    Status = PayrollStatus.Pending,
                });
            }
            await _db.SaveChangesAsync();
            await LoadPayrollsAsync();
        }
    }

    [RelayCommand]
    private async Task MarkPayrollPaidAsync(PayrollRowViewModel? row)
    {
        if (row == null) return;
        var payroll = await _db.Payrolls.Include(p => p.Employee).FirstOrDefaultAsync(p => p.Id == row.Id);
        if (payroll == null || payroll.Status == PayrollStatus.Paid) return;

        payroll.Status = PayrollStatus.Paid;
        payroll.PaidAt = DateTime.UtcNow;

        // Create expense record for this salary payment
        var expense = new SupplierExpense
        {
            Description = $"Salary - {payroll.Employee.Name} ({new DateTime(payroll.Year, payroll.Month, 1):MMM yyyy})",
            Amount = payroll.NetSalary,
            ExpenseDate = DateTime.UtcNow,
            Category = "Salary",
            IsPaid = true,
            Notes = $"Payroll #{payroll.Id}"
        };
        _db.SupplierExpenses.Add(expense);
        payroll.ExpenseId = expense.Id;

        await _db.SaveChangesAsync();

        // Update ExpenseId after save (EF generates ID)
        payroll.ExpenseId = expense.Id;
        await _db.SaveChangesAsync();

        await LoadPayrollsAsync();
    }

    [RelayCommand]
    private async Task DeletePayrollAsync(PayrollRowViewModel? row)
    {
        if (row == null) return;
        var result = MessageBox.Show("Delete this payroll record?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var payroll = await _db.Payrolls.FindAsync(row.Id);
        if (payroll != null)
        {
            payroll.IsActive = false;
            await _db.SaveChangesAsync();
        }
        await LoadPayrollsAsync();
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailVisible = false;
        SelectedEmployee = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }
}

// ── Row view models ──

public class EmployeeRowViewModel
{
    public int Id { get; set; }
    public int Serial { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string CNIC { get; set; } = "";
    public string Category { get; set; } = "";
    public string Designation { get; set; } = "";
    public string EmploymentType { get; set; } = "";
    public string BasicSalary { get; set; } = "";
    public string JoiningDate { get; set; } = "";
    public string Status { get; set; } = "";
}

public class PayrollRowViewModel
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string Period { get; set; } = "";
    public string BasicSalary { get; set; } = "";
    public string Allowances { get; set; } = "";
    public string Deductions { get; set; } = "";
    public string Bonus { get; set; } = "";
    public string Advance { get; set; } = "";
    public string NetSalary { get; set; } = "";
    public string Status { get; set; } = "";
    public string PaidAt { get; set; } = "";
}
