using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Printing;
using RestaurantPOS.Printing.Receipt;
using System.Collections.ObjectModel;
using System.Windows;

namespace RestaurantPOS.WPF.ViewModels;

public partial class CustomerManagementViewModel : BaseViewModel
{
    private readonly ICustomerService _customerService;
    private readonly PosDbContext _db;
    private readonly IPrintService _printService;
    private readonly IAuthService _authService;

    // ── All customers loaded from DB ──
    private List<Customer> _allCustomers = [];
    private Dictionary<int, int> _customerOrderCounts = new();

    // ── Displayed (filtered) customers ──
    public ObservableCollection<CustomerRowViewModel> Customers { get; } = [];

    // ── Order history for selected customer ──
    public ObservableCollection<CustomerOrderViewModel> CustomerOrders { get; } = [];

    // ── Filters ──
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedTierFilter = "ALL";

    public ObservableCollection<string> TierOptions { get; } = ["ALL", "Regular", "Silver", "Gold", "Platinum"];

    // ── Stats ──
    [ObservableProperty] private int _totalCustomers;
    [ObservableProperty] private string _totalSpentDisplay = "Rs. 0";
    [ObservableProperty] private int _totalOrders;
    [ObservableProperty] private int _goldPlatinumCount;

    // ── Role-based visibility ──
    [ObservableProperty] private bool _canSeeStats;        // admin/manager only
    [ObservableProperty] private bool _canManageCustomers;  // admin/manager only (level >= 5)

    // ── Selected customer detail ──
    [ObservableProperty] private CustomerRowViewModel? _selectedCustomer;
    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string _detailPhone = "";
    [ObservableProperty] private string _detailEmail = "";
    [ObservableProperty] private string _detailAddress = "";
    [ObservableProperty] private string _detailTier = "";
    [ObservableProperty] private string _detailTotalSpent = "";
    [ObservableProperty] private string _detailLoyaltyPoints = "";
    [ObservableProperty] private string _detailNotes = "";
    [ObservableProperty] private int _detailOrderCount;

    public CustomerManagementViewModel(ICustomerService customerService, PosDbContext db, IPrintService printService, IAuthService authService)
    {
        _customerService = customerService;
        _db = db;
        _printService = printService;
        _authService = authService;
        Title = "Customer Management";

        // Role-based: level >= 5 = admin/manager full control, level 2 = cashier read-only
        var level = _authService.GetAccessLevel("Manage customers & loyalty");
        CanSeeStats = level >= 5;
        CanManageCustomers = level >= 5;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        _allCustomers = (await _customerService.GetAllCustomersAsync()).ToList();

        // Load order counts and actual totals for all customers in one query
        var customerIds = _allCustomers.Select(c => c.Id).ToList();
        var orderStats = await _db.Orders
            .Where(o => o.CustomerId != null && customerIds.Contains(o.CustomerId!.Value) && o.Status == OrderStatus.Closed)
            .GroupBy(o => o.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), TotalSpent = g.Sum(o => o.GrandTotal) })
            .ToDictionaryAsync(x => x.CustomerId);

        _customerOrderCounts = orderStats.ToDictionary(x => x.Key, x => x.Value.Count);

        // Sync TotalSpent from actual order data (fixes stale/zero values)
        bool needsSave = false;
        foreach (var c in _allCustomers)
        {
            if (orderStats.TryGetValue(c.Id, out var stats) && c.TotalSpent != stats.TotalSpent)
            {
                c.TotalSpent = stats.TotalSpent;
                needsSave = true;
            }
        }
        if (needsSave) await _db.SaveChangesAsync();

        ApplyFilter();
        UpdateStats();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTierFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Customers.Clear();
        var filtered = _allCustomers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Phone.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (c.Email != null && c.Email.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (SelectedTierFilter != "ALL")
        {
            var tier = Enum.Parse<CustomerTier>(SelectedTierFilter);
            filtered = filtered.Where(c => c.Tier == tier);
        }

        int serial = 1;
        foreach (var c in filtered)
        {
            Customers.Add(new CustomerRowViewModel
            {
                Id = c.Id,
                Serial = serial++,
                Name = c.Name,
                Phone = c.Phone,
                Email = c.Email ?? "",
                Address = c.Addresses.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                          ?? c.Addresses.FirstOrDefault()?.AddressLine1 ?? "",
                Tier = c.Tier.ToString(),
                TotalSpent = $"Rs. {c.TotalSpent / 100m:N0}",
                LoyaltyPoints = c.LoyaltyPoints,
                OrderCount = _customerOrderCounts.GetValueOrDefault(c.Id, 0),
                Notes = c.Notes ?? "",
                CreatedAt = c.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy")
            });
        }
    }

    private void UpdateStats()
    {
        TotalCustomers = _allCustomers.Count;
        var totalSpent = _allCustomers.Sum(c => c.TotalSpent);
        TotalSpentDisplay = $"Rs. {totalSpent / 100m:N0}";
        GoldPlatinumCount = _allCustomers.Count(c => c.Tier == CustomerTier.Gold || c.Tier == CustomerTier.Platinum);

        // Count total orders
        _ = Task.Run(async () =>
        {
            var count = await _db.Orders.CountAsync(o => o.CustomerId != null);
            System.Windows.Application.Current.Dispatcher.Invoke(() => TotalOrders = count);
        });
    }

    [RelayCommand]
    private async Task SelectCustomerAsync(CustomerRowViewModel? row)
    {
        if (row == null) return;
        SelectedCustomer = row;

        // Load customer with orders
        var customer = await _customerService.GetByIdWithOrdersAsync(row.Id);
        if (customer == null) return;

        DetailName = customer.Name;
        DetailPhone = customer.Phone;
        DetailEmail = customer.Email ?? "";
        DetailAddress = customer.Addresses.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                        ?? customer.Addresses.FirstOrDefault()?.AddressLine1 ?? "";
        DetailTier = customer.Tier.ToString();
        DetailTotalSpent = $"Rs. {customer.TotalSpent / 100m:N0}";
        DetailLoyaltyPoints = $"{customer.LoyaltyPoints}";
        DetailNotes = customer.Notes ?? "";

        // Load orders with items — match by CustomerId OR by phone in notes (for older orders)
        CustomerOrders.Clear();
        var phoneSearch = $"Mobile: {customer.Phone}";
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
            .Where(o => o.CustomerId == customer.Id
                     || (o.Notes != null && o.Notes.Contains(phoneSearch)))
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .ToListAsync();

        // Retroactively link unlinked orders to this customer
        var unlinked = orders.Where(o => o.CustomerId == null).ToList();
        if (unlinked.Count > 0)
        {
            foreach (var o in unlinked)
                o.CustomerId = customer.Id;
            await _db.SaveChangesAsync();
        }

        DetailOrderCount = orders.Count;
        foreach (var o in orders)
        {
            var itemsSummary = string.Join(", ",
                o.OrderItems.Select(oi => $"{oi.MenuItem?.Name ?? "Item"} x{oi.Quantity}"));

            CustomerOrders.Add(new CustomerOrderViewModel
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                Date = o.CreatedAt.ToLocalTime().ToString("dd/MM/yy HH:mm"),
                Type = o.OrderType.ToString(),
                Status = o.Status.ToString(),
                Total = $"Rs. {o.GrandTotal / 100m:N0}",
                Items = o.OrderItems.Count,
                ItemsSummary = itemsSummary
            });
        }

        IsDetailVisible = true;
    }

    [RelayCommand]
    private async Task AddCustomerAsync()
    {
        var addWindow = new Views.AddCustomerWindow();
        addWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (addWindow.ShowDialog() == true)
        {
            await _customerService.CreateCustomerAsync(
                addWindow.CustomerName,
                addWindow.CustomerPhone,
                string.IsNullOrWhiteSpace(addWindow.CustomerEmail) ? null : addWindow.CustomerEmail,
                string.IsNullOrWhiteSpace(addWindow.CustomerAddress) ? null : addWindow.CustomerAddress);

            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task EditCustomerAsync()
    {
        if (SelectedCustomer == null) return;

        var customer = await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == SelectedCustomer.Id);
        if (customer == null) return;

        var editWindow = new Views.AddCustomerWindow(customer.Phone);
        editWindow.Title = "Edit Customer";
        editWindow.Owner = System.Windows.Application.Current.MainWindow;

        // Pre-fill fields via reflection on named controls
        editWindow.Loaded += (_, _) =>
        {
            var nameBox = editWindow.FindName("TxtName") as System.Windows.Controls.TextBox;
            var emailBox = editWindow.FindName("TxtEmail") as System.Windows.Controls.TextBox;
            var addressBox = editWindow.FindName("TxtAddress") as System.Windows.Controls.TextBox;
            if (nameBox != null) nameBox.Text = customer.Name;
            if (emailBox != null) emailBox.Text = customer.Email ?? "";
            if (addressBox != null)
                addressBox.Text = customer.Addresses.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                                  ?? customer.Addresses.FirstOrDefault()?.AddressLine1 ?? "";
        };

        if (editWindow.ShowDialog() == true)
        {
            customer.Name = editWindow.CustomerName;
            customer.Phone = editWindow.CustomerPhone;
            customer.Email = string.IsNullOrWhiteSpace(editWindow.CustomerEmail) ? null : editWindow.CustomerEmail;

            var addr = customer.Addresses.FirstOrDefault(a => a.IsDefault)
                       ?? customer.Addresses.FirstOrDefault();
            if (addr != null && !string.IsNullOrWhiteSpace(editWindow.CustomerAddress))
                addr.AddressLine1 = editWindow.CustomerAddress;
            else if (addr == null && !string.IsNullOrWhiteSpace(editWindow.CustomerAddress))
                customer.Addresses.Add(new CustomerAddress { Label = "Primary", AddressLine1 = editWindow.CustomerAddress, IsDefault = true });

            await _customerService.UpdateCustomerAsync(customer);
            await LoadDataAsync();
            await SelectCustomerAsync(Customers.FirstOrDefault(c => c.Id == customer.Id));
        }
    }

    [RelayCommand]
    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer == null) return;

        var orderCount = await _db.Orders.CountAsync(o => o.CustomerId == SelectedCustomer.Id);
        var msg = orderCount > 0
            ? $"Customer '{SelectedCustomer.Name}' has {orderCount} order(s) linked.\nThe customer will be deactivated but billing history will be preserved."
            : $"Are you sure you want to delete customer '{SelectedCustomer.Name}'?";

        var result = MessageBox.Show(msg, "Delete Customer", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _customerService.DeleteCustomerAsync(SelectedCustomer.Id);
            IsDetailVisible = false;
            SelectedCustomer = null;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task PrintOrderReceiptAsync(CustomerOrderViewModel? orderRow)
    {
        if (orderRow == null) return;

        var order = await _db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Customer).ThenInclude(c => c!.Addresses)
            .FirstOrDefaultAsync(o => o.Id == orderRow.OrderId);
        if (order == null) return;

        var receiptData = new ReceiptData
        {
            RestaurantName = "KFC Restaurant",
            OrderNumber = order.OrderNumber,
            OrderType = order.OrderType.ToString(),
            CashierName = "Admin",
            SubTotal = order.SubTotal,
            TaxAmount = order.TaxAmount,
            DiscountAmount = order.DiscountAmount,
            GrandTotal = order.GrandTotal,
            CustomerName = order.Customer?.Name ?? "",
            CustomerPhone = order.Customer?.Phone ?? "",
            CustomerAddress = order.Customer?.Addresses?.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                              ?? order.Customer?.Addresses?.FirstOrDefault()?.AddressLine1 ?? "",
        };

        foreach (var oi in order.OrderItems)
        {
            receiptData.Items.Add(new ReceiptItem
            {
                Name = oi.MenuItem?.Name ?? "Item",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                Notes = oi.Notes ?? ""
            });
        }

        var previewWindow = new Views.PrintPreviewWindow(receiptData, _printService);
        previewWindow.Owner = System.Windows.Application.Current.MainWindow;
        previewWindow.ShowDialog();
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailVisible = false;
        SelectedCustomer = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }
}

// ── Row view model for the customer grid ──
public partial class CustomerRowViewModel : ObservableObject
{
    public int Id { get; set; }
    public int Serial { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string Tier { get; set; } = "";
    public string TotalSpent { get; set; } = "";
    public long LoyaltyPoints { get; set; }
    public int OrderCount { get; set; }
    public string Notes { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

// ── Row view model for customer's order history ──
public partial class CustomerOrderViewModel : ObservableObject
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string Date { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Total { get; set; } = "";
    public int Items { get; set; }
    public string ItemsSummary { get; set; } = "";
}
