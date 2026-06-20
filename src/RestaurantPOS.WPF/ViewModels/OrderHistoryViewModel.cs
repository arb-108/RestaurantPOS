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
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderService _orderService;
    private readonly PosDbContext _db;
    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;

    // Serializes SearchOrdersAsync so multiple filter changes (or the initial
    // load + first selection trigger) cannot run two queries on the same
    // DbContext concurrently. Concurrent use of PosDbContext throws
    // "Collection was modified; enumeration operation may not execute".
    private readonly System.Threading.SemaphoreSlim _searchLock = new(1, 1);

    // True while LoadShiftFiltersAsync is populating the dropdown — used to
    // suppress the OnSelectedShiftFilterChanged auto-search that would
    // otherwise race against LoadDataAsync's own SearchOrdersAsync call.
    private bool _suppressFilterReload;

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

    // ── Shift filter dropdown ──
    public ObservableCollection<ShiftFilterOption> ShiftFilters { get; } = [];

    [ObservableProperty]
    private ShiftFilterOption? _selectedShiftFilter;

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
        await LoadShiftFiltersAsync();
        await SearchOrdersAsync();
    }

    /// <summary>
    /// Rebuild the Shift dropdown from the latest 50 shifts. Adds an "All Shifts"
    /// sentinel at position 0. Keeps the current selection if still present.
    /// Suppresses the auto-reload triggered by the SelectedShiftFilter setter
    /// — the caller (LoadDataAsync) runs SearchOrdersAsync explicitly afterwards.
    /// </summary>
    private async Task LoadShiftFiltersAsync()
    {
        // AsNoTracking is critical here: PosDbContext is long-lived (scoped/singleton),
        // so a previously-loaded Shift with EndedAt=NULL stays in the change tracker.
        // Without AsNoTracking, re-querying returns the CACHED entity — even after we
        // close that shift from another view. The dropdown then shows it as still
        // "Active" until the app is restarted. AsNoTracking bypasses the cache.
        var shifts = await _db.Shifts
            .AsNoTracking()
            .Include(s => s.User)
            .OrderByDescending(s => s.StartedAt)
            .Take(50)
            .ToListAsync();

        _suppressFilterReload = true;
        try
        {
            var previousId = SelectedShiftFilter?.ShiftId;
            ShiftFilters.Clear();
            ShiftFilters.Add(new ShiftFilterOption { ShiftId = null, Display = "All Shifts" });

            foreach (var s in shifts)
            {
                var startLocal = s.StartedAt.ToLocalTime();
                var endLabel = s.EndedAt.HasValue
                    ? s.EndedAt.Value.ToLocalTime().ToString("dd MMM hh:mm tt")
                    : "Active";
                var who = s.User?.FullName ?? s.User?.Username ?? "—";
                var label = $"#{s.Id} • {who} • {startLocal:dd MMM hh:mm tt} → {endLabel}";
                ShiftFilters.Add(new ShiftFilterOption { ShiftId = s.Id, Display = label });
            }

            SelectedShiftFilter = ShiftFilters.FirstOrDefault(o => o.ShiftId == previousId) ?? ShiftFilters[0];
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    [RelayCommand]
    private async Task SearchOrdersAsync()
    {
        // Serialize: if a previous Search is still in flight (e.g. rapid filter
        // changes, or the initial-load path), wait for it to finish before
        // touching the shared DbContext again.
        await _searchLock.WaitAsync();
        try
        {
            await SearchOrdersCoreAsync();
        }
        finally
        {
            _searchLock.Release();
        }
    }

    private async Task SearchOrdersCoreAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading orders...";
        IEnumerable<Order> fetched;

        // Use a direct DB query with full navigation includes so the grid columns
        // (Customer, Phone, Cashier, Items) are populated in both modes.
        // AsNoTracking — read-only grid; avoids stale entity reads from the
        // long-lived DbContext (same reason as LoadShiftFiltersAsync above).
        var query = _db.Orders
            .AsNoTracking()
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

        // Shift filter — if a specific shift is selected, narrow to its orders
        if (SelectedShiftFilter?.ShiftId is int shiftId)
        {
            query = query.Where(o => o.ShiftId == shiftId);
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
        if (_suppressFilterReload) return;
        if (!ShowAllOrders) _ = SearchOrdersAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressFilterReload) return;
        _ = SearchOrdersAsync();
    }

    partial void OnSelectedOrderTypeFilterChanged(string value)
    {
        if (_suppressFilterReload) return;
        _ = SearchOrdersAsync();
    }

    partial void OnSelectedStatusFilterChanged(string value)
    {
        if (_suppressFilterReload) return;
        _ = SearchOrdersAsync();
    }

    partial void OnSelectedShiftFilterChanged(ShiftFilterOption? value)
    {
        if (_suppressFilterReload) return;
        _ = SearchOrdersAsync();
    }

    partial void OnShowAllOrdersChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAllLabel));
        if (_suppressFilterReload) return;
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

        // Use the same 3-level fallback the main POS uses, otherwise a Receipt
        // printer row without IsDefault=true (or with null SystemPrinterName)
        // makes the reprint silently fall through to no-printer mode.
        var receiptPrinter = await _db.Set<Printer>()
            .Where(p => p.IsActive && p.IsDefault && p.Type == PrinterType.Receipt)
            .FirstOrDefaultAsync()
            ?? await _db.Set<Printer>()
                .Where(p => p.IsActive && p.Type == PrinterType.Receipt)
                .FirstOrDefaultAsync()
            ?? await _db.Set<Printer>()
                .Where(p => p.IsActive && p.SystemPrinterName != null)
                .FirstOrDefaultAsync();
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

    /// <summary>
    /// Builds an A4 Order-History summary report from the CURRENT filtered list
    /// and opens it in the A4 preview window (Print / Save-as-PDF).
    /// </summary>
    [RelayCommand]
    private async Task PrintOrderHistoryReportAsync()
    {
        // Make sure the list matches the filters the cashier is looking at right now.
        await SearchOrdersAsync();

        var restaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName") ?? "Restaurant";
        var address = await _settingsService.GetSettingAsync("ReceiptAddress") ?? string.Empty;
        var phone = await _settingsService.GetSettingAsync("ReceiptPhone") ?? string.Empty;

        var scopeLine = ShowAllOrders
            ? "All Dates"
            : $"Date: {FromDate:dd/MM/yyyy}";
        var typeLine = SelectedOrderTypeFilter == "All" ? "All Types" : SelectedOrderTypeFilter;
        var statusLine = SelectedStatusFilter == "All" ? "All Statuses" : SelectedStatusFilter;
        var searchLine = string.IsNullOrWhiteSpace(SearchText) ? "" : $"  |  Search: \"{SearchText}\"";

        var report = new ReportDocument
        {
            Title = "Order History Report",
            Subtitle = $"{scopeLine}  |  Type: {typeLine}  |  Status: {statusLine}{searchLine}",
            RestaurantName = restaurantName,
            RestaurantAddress = address,
            RestaurantPhone = phone,
            Columns = new List<string> { "Order #", "Date / Time", "Type", "Status", "Customer", "Items", "Total (Rs.)" },
            Rows = Orders.Select(o => new List<string>
            {
                o.OrderNumber ?? "-",
                o.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy hh:mm tt"),
                o.OrderType.ToString(),
                o.Status.ToString(),
                o.Customer?.Name ?? "-",
                o.OrderItems?.Sum(i => i.Quantity).ToString() ?? "0",
                $"{o.GrandTotal / 100m:N2}"
            }).ToList(),
            Summary = new List<(string, string)>
            {
                ("Total Orders",  ResultCount.ToString()),
                ("Total Revenue", $"Rs. {TotalRevenue / 100m:N2}")
            },
            Footer = "Generated from Restaurant POS — Order History"
        };

        var preview = new ReportPreviewWindow(report)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        preview.ShowDialog();
    }
}

/// <summary>
/// Dropdown option for the Shift filter on the OrderHistory toolbar.
/// ShiftId == null means "All Shifts" (no filter).
/// </summary>
public class ShiftFilterOption
{
    public int? ShiftId { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}
