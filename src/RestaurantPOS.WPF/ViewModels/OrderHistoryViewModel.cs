using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.Printing;
using RestaurantPOS.Printing.Receipt;

namespace RestaurantPOS.WPF.ViewModels;

public partial class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderService _orderService;
    private readonly PosDbContext _db;
    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<Order> Orders { get; } = [];

    [ObservableProperty]
    private Order? _selectedOrder;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>When true, date filter is ignored and ALL orders are listed.</summary>
    [ObservableProperty]
    private bool _showAllOrders;

    /// <summary>"All", "DineIn", "TakeAway", "Delivery"</summary>
    [ObservableProperty]
    private string _selectedOrderTypeFilter = "All";

    /// <summary>"All", "Closed", "Void"</summary>
    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    public IReadOnlyList<string> OrderTypeFilters { get; } = new[] { "All", "DineIn", "TakeAway", "Delivery" };
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "All", "Closed", "Void" };

    /// <summary>Label for the toggle button — reflects current mode.</summary>
    public string ShowAllLabel => ShowAllOrders ? "Show By Date" : "Show All Orders";

    [ObservableProperty] private int _resultCount;
    [ObservableProperty] private long _totalRevenue; // paisa
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isLoading;

    public OrderHistoryViewModel(
        IOrderService orderService,
        PosDbContext db,
        IPrintService printService,
        ISettingsService settingsService)
    {
        _orderService = orderService;
        _db = db;
        _printService = printService;
        _settingsService = settingsService;
        Title = "Orders";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await SearchOrdersAsync();
    }

    [RelayCommand]
    private async Task SearchOrdersAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading orders...";
        IEnumerable<Order> fetched;

        // Use a direct DB query with full navigation includes so the grid columns
        // (Customer, Phone, Cashier, Items) are populated in both modes.
        var query = _db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Customer)
            .Include(o => o.Cashier)
            .Include(o => o.TableSession).ThenInclude(ts => ts!.Table)
            .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
            .AsQueryable();

        if (!ShowAllOrders)
        {
            var start = FromDate.Date.ToUniversalTime();
            var end = FromDate.Date.AddDays(1).ToUniversalTime();
            query = query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        fetched = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
            .ToListAsync();

        Orders.Clear();

        foreach (var order in fetched)
        {
            // Type filter
            if (!string.Equals(SelectedOrderTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(order.OrderType.ToString(), SelectedOrderTypeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Status filter
            if (!string.Equals(SelectedStatusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(order.Status.ToString(), SelectedStatusFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Text search — matches Order# or Customer phone or Customer name
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var lower = SearchText.Trim().ToLowerInvariant();
                var orderNumHit = order.OrderNumber?.ToLowerInvariant().Contains(lower) ?? false;
                var phoneHit = order.Customer?.Phone?.ToLowerInvariant().Contains(lower) ?? false;
                var nameHit = order.Customer?.Name?.ToLowerInvariant().Contains(lower) ?? false;
                if (!orderNumHit && !phoneHit && !nameHit)
                    continue;
            }

            Orders.Add(order);
        }

        ResultCount = Orders.Count;
        TotalRevenue = Orders.Where(o => o.Status != OrderStatus.Void).Sum(o => o.GrandTotal);
        IsLoading = false;
        StatusMessage = ShowAllOrders
            ? $"{ResultCount} order(s) across all dates"
            : $"{ResultCount} order(s) on {FromDate:dd MMM yyyy}";
    }

    partial void OnFromDateChanged(DateTime value)
    {
        if (!ShowAllOrders) _ = SearchOrdersAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = SearchOrdersAsync();
    partial void OnSelectedOrderTypeFilterChanged(string value) => _ = SearchOrdersAsync();
    partial void OnSelectedStatusFilterChanged(string value) => _ = SearchOrdersAsync();
    partial void OnShowAllOrdersChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAllLabel));
        _ = SearchOrdersAsync();
    }

    [RelayCommand]
    private void ToggleShowAll() => ShowAllOrders = !ShowAllOrders;

    /// <summary>Opens the receipt preview (reprint) for the selected row.</summary>
    [RelayCommand]
    private async Task ShowReceiptAsync(Order? order)
    {
        if (order == null) return;

        // Ensure the order is fully loaded with related data (in case the list fetched a lighter version).
        var full = await _db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Customer)
            .Include(o => o.Cashier)
            .Include(o => o.TableSession).ThenInclude(ts => ts!.Table)
            .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
            .FirstOrDefaultAsync(o => o.Id == order.Id) ?? order;

        var restaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName") ?? "Restaurant";
        var address = await _settingsService.GetSettingAsync("ReceiptAddress") ?? "";
        var phone = await _settingsService.GetSettingAsync("ReceiptPhone") ?? "";

        var receiptPrinter = await _db.Set<Printer>()
            .FirstOrDefaultAsync(p => p.Type == PrinterType.Receipt);
        var printerName = receiptPrinter?.SystemPrinterName;

        var receiptData = new ReceiptData
        {
            RestaurantName = restaurantName,
            RestaurantAddress = address,
            RestaurantPhone = phone,
            OrderNumber = full.OrderNumber,
            DateTime = full.CreatedAt.ToLocalTime(),
            TableName = full.TableSession?.Table?.Name,
            OrderType = full.OrderType.ToString(),
            CashierName = full.Cashier?.FullName ?? "Unknown",
            SubTotal = full.SubTotal,
            DiscountAmount = full.DiscountAmount,
            TaxAmount = full.TaxAmount,
            ServiceCharge = full.ServiceCharge,
            GrandTotal = full.GrandTotal,
            PaymentMethod = full.Payments.FirstOrDefault()?.PaymentMethod?.Name ?? "Cash",
            TenderedAmount = full.Payments.FirstOrDefault()?.TenderedAmount ?? full.GrandTotal,
            ChangeAmount = full.Payments.FirstOrDefault()?.ChangeAmount ?? 0,
            HeaderMessage = "*** REPRINT ***"
        };

        if (full.Customer != null)
        {
            receiptData.CustomerName = full.Customer.Name;
            receiptData.CustomerPhone = full.Customer.Phone;
        }

        if (!string.IsNullOrEmpty(full.Notes))
        {
            foreach (var line in full.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Driver:"))
                    receiptData.DriverName = trimmed["Driver:".Length..].Trim();
                else if (trimmed.StartsWith("Note:"))
                    receiptData.DeliveryNote = trimmed["Note:".Length..].Trim();
                else if (trimmed.StartsWith("Address:"))
                    receiptData.CustomerAddress = trimmed["Address:".Length..].Trim();
            }
        }

        foreach (var oi in full.OrderItems)
        {
            receiptData.Items.Add(new ReceiptItem
            {
                Name = oi.MenuItem?.Name ?? "Item",
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                Notes = oi.Notes
            });
        }

        var previewWindow = new Views.PrintPreviewWindow(receiptData, _printService)
        { ConfiguredPrinterName = printerName };
        previewWindow.Owner = System.Windows.Application.Current.MainWindow;
        previewWindow.ShowDialog();
    }
}
