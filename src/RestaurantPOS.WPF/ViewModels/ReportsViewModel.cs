using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.Printing;
using RestaurantPOS.Printing.Receipt;
using AppWindow = System.Windows.Application;

namespace RestaurantPOS.WPF.ViewModels;

public class ChartDataItem
{
    public string Label { get; set; } = string.Empty;
    public long Value { get; set; }
    public string ValueDisplay => $"Rs. {Value / 100m:N0}";
    public double BarWidth { get; set; }
}

public partial class ReportsViewModel : BaseViewModel
{
    private readonly IReportService _reportService;
    private readonly PosDbContext _db;
    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;
    private string _restaurantName = "KFC Restaurant";
    private string? _configuredPrinter;

    // ══════════════════════════════════════════════
    //  SHARED
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private int _selectedTab;

    // ══════════════════════════════════════════════
    //  TAB 0: SALES OVERVIEW
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    // Summary cards
    [ObservableProperty]
    private string _totalSales = "Rs. 0";

    [ObservableProperty]
    private string _totalOrders = "0";

    [ObservableProperty]
    private string _avgOrderValue = "Rs. 0";

    [ObservableProperty]
    private string _voidedOrders = "0";

    // Payment breakdown
    [ObservableProperty]
    private string _cashSales = "Rs. 0";

    [ObservableProperty]
    private string _cardSales = "Rs. 0";

    [ObservableProperty]
    private string _digitalSales = "Rs. 0";

    [ObservableProperty]
    private string _peakHour = "-";

    // Chart data
    public ObservableCollection<ChartDataItem> SalesByCategory { get; } = [];
    public ObservableCollection<ChartDataItem> SalesByHour { get; } = [];
    public ObservableCollection<ChartDataItem> SalesByPaymentMethod { get; } = [];

    // ══════════════════════════════════════════════
    //  TAB 1: ORDER HISTORY
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _ordersFrom = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _ordersTo = DateTime.Today;

    [ObservableProperty]
    private string _orderTypeFilter = "All";

    [ObservableProperty]
    private string _orderStatusFilter = "All";

    public ObservableCollection<string> OrderTypeOptions { get; } = ["All", "DineIn", "TakeAway", "Delivery"];
    public ObservableCollection<string> OrderStatusOptions { get; } = ["All", "Closed", "Open", "Void"];
    public ObservableCollection<OrderHistoryRow> OrderHistoryItems { get; } = [];

    [ObservableProperty]
    private string _orderHistoryCount = "0 orders";

    [ObservableProperty]
    private string _orderHistoryTotal = "Rs. 0";

    // ══════════════════════════════════════════════
    //  TAB 2: MENU PERFORMANCE
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _menuFrom = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _menuTo = DateTime.Today;

    [ObservableProperty]
    private string _menuCategoryFilter = "All";

    public ObservableCollection<string> MenuCategoryOptions { get; } = ["All"];
    public ObservableCollection<MenuPerformanceRow> MenuPerformanceItems { get; } = [];

    [ObservableProperty]
    private string _menuItemsSold = "0";

    [ObservableProperty]
    private string _menuRevenueTotal = "Rs. 0";

    // ══════════════════════════════════════════════
    //  TAB 3: DELIVERY DRIVERS
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _driverFrom = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _driverTo = DateTime.Today;

    public ObservableCollection<DriverPerformanceRow> DriverPerformanceItems { get; } = [];

    [ObservableProperty]
    private string _totalDeliveries = "0";

    [ObservableProperty]
    private string _driverRevenueTotal = "Rs. 0";

    // ══════════════════════════════════════════════
    //  TAB 4: KITCHEN
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _kitchenFrom = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _kitchenTo = DateTime.Today;

    public ObservableCollection<KitchenReportRow> KitchenItems { get; } = [];

    [ObservableProperty]
    private string _kitchenCount = "0 orders";

    // ══════════════════════════════════════════════
    //  TAB 5: EXPENSES
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _expenseFrom = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _expenseTo = DateTime.Today;

    [ObservableProperty]
    private string _expenseCategoryFilter = "All";

    public ObservableCollection<string> ExpenseCategoryOptions { get; } =
        ["All", "Raw Material", "Equipment", "Packaging", "Salary", "Utility", "Rent", "Other"];

    public ObservableCollection<ExpenseReportRow> ExpenseItems { get; } = [];

    [ObservableProperty]
    private string _expenseCount = "0 expenses";

    [ObservableProperty]
    private string _expenseTotal = "Rs. 0";

    // ══════════════════════════════════════════════
    //  TAB 6: PROFIT & LOSS
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _profitFrom = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _profitTo = DateTime.Today;

    [ObservableProperty]
    private string _profitPeriod = "Day";

    public ObservableCollection<string> ProfitPeriodOptions { get; } = ["Day", "Week", "Month"];
    public ObservableCollection<ProfitLossRow> ProfitLossItems { get; } = [];

    [ObservableProperty]
    private string _summaryRevenue = "Rs. 0";

    [ObservableProperty]
    private string _summaryExpenses = "Rs. 0";

    [ObservableProperty]
    private string _summaryProfit = "Rs. 0";

    [ObservableProperty]
    private string _summaryMargin = "0%";

    // ══════════════════════════════════════════════
    //  TAB 7: SALES REPORT (Reference-style)
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _salesReportFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime _salesReportTo = DateTime.Today;

    [ObservableProperty]
    private string _salesReportMode = "All Sale";

    [ObservableProperty]
    private string _salesReportCategory = "All";

    public ObservableCollection<string> SalesReportModeOptions { get; } = ["All Sale", "Bill Wise"];
    public ObservableCollection<string> SalesReportCategories { get; } = ["All"];
    public ObservableCollection<SalesReportRow> SalesReportItems { get; } = [];

    [ObservableProperty] private string _saleCategoryTotal = "";
    [ObservableProperty] private string _saleTotal = "0";
    [ObservableProperty] private string _saleDiscount = "0";
    [ObservableProperty] private string _saleGrandTotal = "0";
    [ObservableProperty] private string _saleExpenses = "0";
    [ObservableProperty] private string _saleCashInHand = "0";
    [ObservableProperty] private string _salesReportCount = "0 items";
    [ObservableProperty] private bool _isCategoryMode;

    // Auto-filter when category changes
    partial void OnSalesReportCategoryChanged(string value) => _ = LoadSalesReportAsync();
    partial void OnSalesReportModeChanged(string value)
    {
        IsCategoryMode = value != "All Sale";
        _ = LoadSalesReportAsync();
    }

    // ══════════════════════════════════════════════
    //  TAB 8: CASHIER SALES
    // ══════════════════════════════════════════════

    [ObservableProperty]
    private DateTime _cashierSalesFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime _cashierSalesTo = DateTime.Today;

    public ObservableCollection<CashierPerformanceRow> CashierSalesItems { get; } = [];

    [ObservableProperty]
    private string _cashierSalesCount = "0 cashiers";

    [ObservableProperty]
    private string _cashierSalesTotal = "Rs. 0";

    // ══════════════════════════════════════════════
    //  CONSTRUCTOR
    // ══════════════════════════════════════════════

    public ReportsViewModel(IReportService reportService, PosDbContext db, IPrintService printService, ISettingsService settingsService)
    {
        _reportService = reportService;
        _db = db;
        _printService = printService;
        _settingsService = settingsService;
        Title = "Reports";
        _ = LoadPrintSettingsAsync();
    }

    private async Task LoadPrintSettingsAsync()
    {
        try
        {
            _restaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName")
                              ?? await _settingsService.GetSettingAsync("RestaurantName")
                              ?? "KFC Restaurant";
            var printer = await _db.Printers
                .Where(p => p.IsActive && p.Type == Domain.Enums.PrinterType.Report && p.SystemPrinterName != null)
                .FirstOrDefaultAsync()
                ?? await _db.Printers
                    .Where(p => p.IsActive && p.Type == Domain.Enums.PrinterType.Receipt && p.SystemPrinterName != null)
                    .FirstOrDefaultAsync()
                ?? await _db.Printers
                    .Where(p => p.IsActive && p.SystemPrinterName != null)
                    .FirstOrDefaultAsync();
            _configuredPrinter = printer?.SystemPrinterName;
        }
        catch { }
    }

    // ══════════════════════════════════════════════
    //  MAIN DATA LOAD (called on view load)
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            // Populate category filter options from DB
            await LoadMenuCategoryOptionsAsync();

            // Load all tabs
            await LoadSalesOverviewAsync();
            await LoadOrderHistoryAsync();
            await LoadMenuPerformanceAsync();
            await LoadDriverPerformanceAsync();
            await LoadKitchenAsync();
            await LoadExpensesAsync();
            await LoadProfitLossAsync();
            await LoadSalesReportCategoriesAsync();
            await LoadSalesReportAsync();
            await LoadCashierSalesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] LoadData error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            switch (SelectedTab)
            {
                case 0: await LoadSalesOverviewAsync(); break;
                case 1: await LoadOrderHistoryAsync(); break;
                case 2: await LoadMenuPerformanceAsync(); break;
                case 3: await LoadDriverPerformanceAsync(); break;
                case 4: await LoadKitchenAsync(); break;
                case 5: await LoadExpensesAsync(); break;
                case 6: await LoadProfitLossAsync(); break;
                case 7: await LoadSalesReportAsync(); break;
                case 8: await LoadCashierSalesAsync(); break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] Refresh error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMenuCategoryOptionsAsync()
    {
        try
        {
            var categories = await _db.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();

            MenuCategoryOptions.Clear();
            MenuCategoryOptions.Add("All");
            foreach (var cat in categories)
                MenuCategoryOptions.Add(cat);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] LoadCategories error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 0: SALES OVERVIEW
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadSalesOverviewAsync()
    {
        try
        {
            // Summary cards from report service
            var summary = await _reportService.GetDailySummaryAsync(SelectedDate);

            TotalSales = $"Rs. {summary.TotalRevenue / 100m:N0}";
            TotalOrders = summary.TotalOrders.ToString();
            AvgOrderValue = summary.TotalOrders > 0
                ? $"Rs. {summary.TotalRevenue / summary.TotalOrders / 100m:N0}"
                : "Rs. 0";
            VoidedOrders = summary.VoidedOrders.ToString();
            CashSales = $"Rs. {summary.CashSales / 100m:N0}";
            CardSales = $"Rs. {summary.CardSales / 100m:N0}";
            DigitalSales = $"Rs. {summary.DigitalSales / 100m:N0}";
            PeakHour = summary.PeakHour > 0 ? $"{summary.PeakHour}:00" : "-";

            // Charts — use SelectedDate as both from/to for single-day view
            await LoadChartsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] SalesOverview error: {ex.Message}");
        }
    }

    private async Task LoadChartsAsync()
    {
        // Sales by category
        var catData = await _reportService.GetSalesByCategoryAsync(SelectedDate, SelectedDate);
        SalesByCategory.Clear();
        var catList = catData.ToList();
        var catMax = catList.Count > 0 ? catList.Max(c => c.Total) : 1;
        foreach (var (category, total) in catList)
        {
            SalesByCategory.Add(new ChartDataItem
            {
                Label = category,
                Value = total,
                BarWidth = catMax > 0 ? (double)total / catMax * 200 : 0
            });
        }

        // Sales by hour
        var hourData = await _reportService.GetSalesByHourAsync(SelectedDate);
        SalesByHour.Clear();
        var hourList = hourData.ToList();
        var hourMax = hourList.Count > 0 ? hourList.Max(h => h.Total) : 1;
        foreach (var (hour, total) in hourList)
        {
            SalesByHour.Add(new ChartDataItem
            {
                Label = $"{hour}:00",
                Value = total,
                BarWidth = hourMax > 0 ? (double)total / hourMax * 200 : 0
            });
        }

        // Sales by payment method
        var payData = await _reportService.GetSalesByPaymentMethodAsync(SelectedDate, SelectedDate);
        SalesByPaymentMethod.Clear();
        var payList = payData.ToList();
        var payMax = payList.Count > 0 ? payList.Max(p => p.Total) : 1;
        foreach (var (method, total) in payList)
        {
            SalesByPaymentMethod.Add(new ChartDataItem
            {
                Label = method,
                Value = total,
                BarWidth = payMax > 0 ? (double)total / payMax * 200 : 0
            });
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 1: ORDER HISTORY
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadOrderHistoryAsync()
    {
        try
        {
            var fromUtc = OrdersFrom.Date.ToUniversalTime();
            var toUtc = OrdersTo.Date.AddDays(1).ToUniversalTime();

            var query = _db.Orders
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Customer)
                .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);

            // Type filter
            if (OrderTypeFilter != "All" && Enum.TryParse<OrderType>(OrderTypeFilter, out var ot))
                query = query.Where(o => o.OrderType == ot);

            // Status filter
            if (OrderStatusFilter != "All" && Enum.TryParse<OrderStatus>(OrderStatusFilter, out var os))
                query = query.Where(o => o.Status == os);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Take(500)
                .ToListAsync();

            OrderHistoryItems.Clear();
            long runningTotal = 0;

            foreach (var o in orders)
            {
                var items = o.OrderItems.ToList();
                var itemNames = items.Select(i => i.MenuItem?.Name ?? "Item").Take(3).ToList();
                var summary = string.Join(", ", itemNames);
                if (items.Count > 3) summary += $" +{items.Count - 3} more";

                var paymentMethod = o.Payments.FirstOrDefault()?.PaymentMethod?.Name ?? "-";

                OrderHistoryItems.Add(new OrderHistoryRow
                {
                    OrderId = o.Id,
                    OrderNumber = o.OrderNumber,
                    Date = o.CreatedAt.ToLocalTime().ToString("dd/MM/yy hh:mm tt"),
                    Type = o.OrderType.ToString(),
                    CustomerName = o.Customer?.Name ?? "-",
                    ItemCount = items.Sum(i => i.Quantity),
                    ItemsSummary = summary,
                    Status = o.Status.ToString(),
                    Total = $"Rs. {o.GrandTotal / 100m:N0}",
                    PaymentMethod = paymentMethod
                });

                if (o.Status == OrderStatus.Closed)
                    runningTotal += o.GrandTotal;
            }

            OrderHistoryCount = $"{orders.Count} orders";
            OrderHistoryTotal = $"Rs. {runningTotal / 100m:N0}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] OrderHistory error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PrintOrder(OrderHistoryRow? row)
    {
        if (row == null) return;

        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Customer)
                .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
                .Include(o => o.Cashier)
                .Include(o => o.Waiter)
                .FirstOrDefaultAsync(o => o.Id == row.OrderId);

            if (order == null) return;

            var receiptData = new ReceiptData
            {
                RestaurantName = _restaurantName,
                OrderNumber = order.OrderNumber,
                DateTime = order.CreatedAt.ToLocalTime(),
                OrderType = order.OrderType.ToString(),
                CashierName = order.Cashier?.FullName,
                WaiterName = order.Waiter?.FullName,
                CustomerName = order.Customer?.Name,
                CustomerPhone = order.Customer?.Phone,
                SubTotal = order.SubTotal,
                TaxAmount = order.TaxAmount,
                DiscountAmount = order.DiscountAmount,
                ServiceCharge = order.ServiceCharge,
                GrandTotal = order.GrandTotal,
                PaymentMethod = order.Payments.FirstOrDefault()?.PaymentMethod?.Name ?? "-",
                TenderedAmount = order.Payments.FirstOrDefault()?.TenderedAmount ?? order.GrandTotal,
                ChangeAmount = order.Payments.FirstOrDefault()?.ChangeAmount ?? 0,
                Items = order.OrderItems.Select(oi => new ReceiptItem
                {
                    Name = oi.MenuItem?.Name ?? "Item",
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    LineTotal = oi.LineTotal,
                    Notes = oi.Notes
                }).ToList()
            };

            var preview = new Views.PrintPreviewWindow(receiptData, _printService)
                { ConfiguredPrinterName = _configuredPrinter };
            preview.Owner = AppWindow.Current.MainWindow;
            preview.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintOrder error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 2: MENU PERFORMANCE
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadMenuPerformanceAsync()
    {
        try
        {
            var fromUtc = MenuFrom.Date.ToUniversalTime();
            var toUtc = MenuTo.Date.AddDays(1).ToUniversalTime();

            var query = _db.OrderItems
                .Include(oi => oi.MenuItem).ThenInclude(mi => mi.Category)
                .Include(oi => oi.Order)
                .Where(oi => oi.Order.Status == OrderStatus.Closed
                    && oi.Order.CreatedAt >= fromUtc
                    && oi.Order.CreatedAt < toUtc);

            if (MenuCategoryFilter != "All")
                query = query.Where(oi => oi.MenuItem.Category.Name == MenuCategoryFilter);

            var items = await query.ToListAsync();

            var grouped = items
                .GroupBy(oi => new { oi.MenuItem.Name, Category = oi.MenuItem.Category?.Name ?? "-" })
                .Select(g => new MenuPerformanceRow
                {
                    ItemName = g.Key.Name,
                    Category = g.Key.Category,
                    QtySold = g.Sum(x => x.Quantity),
                    Revenue = $"Rs. {g.Sum(x => x.LineTotal) / 100m:N0}",
                    AvgPrice = g.Sum(x => x.Quantity) > 0
                        ? $"Rs. {g.Sum(x => x.LineTotal) / g.Sum(x => x.Quantity) / 100m:N0}"
                        : "Rs. 0"
                })
                .OrderByDescending(r => r.QtySold)
                .ToList();

            MenuPerformanceItems.Clear();
            int totalQty = 0;
            long totalRevenue = 0;

            foreach (var row in grouped)
            {
                MenuPerformanceItems.Add(row);
                totalQty += row.QtySold;
            }

            // Parse revenue totals from raw data
            totalRevenue = items.Sum(oi => oi.LineTotal);

            MenuItemsSold = totalQty.ToString();
            MenuRevenueTotal = $"Rs. {totalRevenue / 100m:N0}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] MenuPerformance error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PrintMenuReport()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{MenuFrom:dd/MM/yy} - {MenuTo:dd/MM/yy}"),
                ("Category", MenuCategoryFilter),
                ("Total Items Sold", MenuItemsSold),
                ("Total Revenue", MenuRevenueTotal),
                ("", ""),
                ("--- Item Details ---", "")
            };

            foreach (var item in MenuPerformanceItems.Take(30))
            {
                lines.Add(($"{item.ItemName} x{item.QtySold}", item.Revenue));
            }

            if (MenuPerformanceItems.Count > 30)
                lines.Add(($"... and {MenuPerformanceItems.Count - 30} more", ""));

            OpenReportPreview("Menu Performance Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintMenuReport error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 3: DELIVERY DRIVERS
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadDriverPerformanceAsync()
    {
        try
        {
            var fromUtc = DriverFrom.Date.ToUniversalTime();
            var toUtc = DriverTo.Date.AddDays(1).ToUniversalTime();

            var deliveryOrders = await _db.Orders
                .Where(o => o.OrderType == OrderType.Delivery
                    && o.Status == OrderStatus.Closed
                    && o.CreatedAt >= fromUtc
                    && o.CreatedAt < toUtc
                    && o.Notes != null)
                .ToListAsync();

            // Parse "Driver: Name (Phone)" from Notes
            var driverRegex = new Regex(@"Driver:\s*(.+?)\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
            var driverGroups = new Dictionary<string, (string Phone, int Count, long Total)>();

            foreach (var order in deliveryOrders)
            {
                var match = driverRegex.Match(order.Notes ?? "");
                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    var phone = match.Groups[2].Value.Trim();

                    if (driverGroups.TryGetValue(name, out var existing))
                    {
                        driverGroups[name] = (phone, existing.Count + 1, existing.Total + order.GrandTotal);
                    }
                    else
                    {
                        driverGroups[name] = (phone, 1, order.GrandTotal);
                    }
                }
            }

            DriverPerformanceItems.Clear();
            int totalCount = 0;
            long totalRevenue = 0;

            foreach (var kvp in driverGroups.OrderByDescending(d => d.Value.Count))
            {
                var avg = kvp.Value.Count > 0 ? kvp.Value.Total / kvp.Value.Count : 0;
                DriverPerformanceItems.Add(new DriverPerformanceRow
                {
                    DriverName = kvp.Key,
                    Phone = kvp.Value.Phone,
                    OrderCount = kvp.Value.Count,
                    TotalRevenue = $"Rs. {kvp.Value.Total / 100m:N0}",
                    AvgPerOrder = $"Rs. {avg / 100m:N0}"
                });

                totalCount += kvp.Value.Count;
                totalRevenue += kvp.Value.Total;
            }

            TotalDeliveries = totalCount.ToString();
            DriverRevenueTotal = $"Rs. {totalRevenue / 100m:N0}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] DriverPerformance error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PrintDriverReport()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{DriverFrom:dd/MM/yy} - {DriverTo:dd/MM/yy}"),
                ("Total Deliveries", TotalDeliveries),
                ("Total Revenue", DriverRevenueTotal),
                ("", ""),
                ("--- Driver Details ---", "")
            };

            foreach (var d in DriverPerformanceItems)
            {
                lines.Add(($"{d.DriverName} ({d.Phone})", $"{d.OrderCount} orders, {d.TotalRevenue}"));
            }

            OpenReportPreview("Delivery Driver Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintDriverReport error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 4: KITCHEN
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadKitchenAsync()
    {
        try
        {
            var fromUtc = KitchenFrom.Date.ToUniversalTime();
            var toUtc = KitchenTo.Date.AddDays(1).ToUniversalTime();

            var kitchenOrders = await _db.KitchenOrders
                .Include(ko => ko.Order).ThenInclude(o => o.Cashier)
                .Include(ko => ko.Order).ThenInclude(o => o.TableSession).ThenInclude(ts => ts!.Table)
                .Include(ko => ko.Station)
                .Include(ko => ko.Items).ThenInclude(koi => koi.OrderItem).ThenInclude(oi => oi.MenuItem)
                .Where(ko => ko.Order.CreatedAt >= fromUtc && ko.Order.CreatedAt < toUtc)
                .OrderByDescending(ko => ko.Order.CreatedAt)
                .Take(500)
                .ToListAsync();

            // Group by Order
            var grouped = kitchenOrders
                .GroupBy(ko => ko.OrderId)
                .OrderByDescending(g => g.First().Order?.CreatedAt);

            KitchenItems.Clear();

            foreach (var group in grouped)
            {
                var first = group.First();
                var order = first.Order;
                var allItems = group.SelectMany(ko => ko.Items).ToList();

                var itemNames = allItems
                    .Select(koi => koi.OrderItem?.MenuItem?.Name ?? "Item")
                    .Distinct()
                    .Take(3)
                    .ToList();
                var summary = string.Join(", ", itemNames);
                var totalDistinct = allItems.Select(koi => koi.OrderItem?.MenuItem?.Name).Distinct().Count();
                if (totalDistinct > 3) summary += $" +{totalDistinct - 3} more";

                var stations = group.Select(ko => ko.Station?.Name).Distinct().ToList();

                KitchenItems.Add(new KitchenReportRow
                {
                    OrderNumber = order?.OrderNumber ?? "-",
                    OrderType = order?.OrderType.ToString() ?? "-",
                    Date = order?.CreatedAt.ToLocalTime().ToString("dd/MM/yy hh:mm tt") ?? "-",
                    Station = string.Join(", ", stations.Where(s => s != null)),
                    ItemsSummary = summary,
                    ItemCount = allItems.Count,
                    SlipCount = group.Count(),
                    Status = order?.Status == Domain.Enums.OrderStatus.Closed ? "Closed"
                           : order?.Status == Domain.Enums.OrderStatus.Void ? "Void"
                           : order?.Status == Domain.Enums.OrderStatus.Open ? "Open"
                           : order?.Status.ToString() ?? "-",
                    Cashier = order?.Cashier?.FullName ?? "-",
                    Table = order?.TableSession?.Table?.Name ?? "-",
                    KitchenOrders = group.ToList()
                });
            }

            KitchenCount = $"{KitchenItems.Count} orders ({kitchenOrders.Count} slips)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] Kitchen error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ViewKitchenDetail(KitchenReportRow? row)
    {
        if (row == null || row.KitchenOrders.Count == 0) return;
        var window = new Views.KitchenOrderDetailWindow(row);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
    }

    [RelayCommand]
    private void PrintKitchenReport()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{KitchenFrom:dd/MM/yy} - {KitchenTo:dd/MM/yy}"),
                ("Total Orders", KitchenCount),
                ("", ""),
                ("--- Kitchen Orders ---", "")
            };

            foreach (var k in KitchenItems.Take(30))
            {
                lines.Add(($"{k.OrderNumber} [{k.Station}]", $"{k.ItemCount} items - {k.Status}"));
            }

            if (KitchenItems.Count > 30)
                lines.Add(($"... and {KitchenItems.Count - 30} more", ""));

            OpenReportPreview("Kitchen Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintKitchenReport error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 5: EXPENSES
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadExpensesAsync()
    {
        try
        {
            var fromUtc = ExpenseFrom.Date.ToUniversalTime();
            var toUtc = ExpenseTo.Date.AddDays(1).ToUniversalTime();

            var query = _db.SupplierExpenses
                .Include(e => e.Supplier)
                .Where(e => e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc);

            if (ExpenseCategoryFilter != "All")
                query = query.Where(e => e.Category == ExpenseCategoryFilter);

            var expenses = await query
                .OrderByDescending(e => e.ExpenseDate)
                .ToListAsync();

            ExpenseItems.Clear();
            long totalAmount = 0;

            foreach (var e in expenses)
            {
                ExpenseItems.Add(new ExpenseReportRow
                {
                    Date = e.ExpenseDate.ToLocalTime().ToString("dd/MM/yy"),
                    Supplier = e.Supplier?.Name ?? "-",
                    Description = e.Description,
                    Category = e.Category ?? "-",
                    Amount = $"Rs. {e.Amount / 100m:N0}",
                    Status = e.IsPaid ? "Paid" : "Unpaid"
                });

                totalAmount += e.Amount;
            }

            ExpenseCount = $"{expenses.Count} expenses";
            ExpenseTotal = $"Rs. {totalAmount / 100m:N0}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] Expenses error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PrintExpenseReport()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{ExpenseFrom:dd/MM/yy} - {ExpenseTo:dd/MM/yy}"),
                ("Category", ExpenseCategoryFilter),
                ("Total Expenses", ExpenseCount),
                ("Total Amount", ExpenseTotal),
                ("", ""),
                ("--- Expense Details ---", "")
            };

            foreach (var e in ExpenseItems.Take(30))
            {
                lines.Add(($"{e.Date} {e.Supplier} - {e.Description}", $"{e.Amount} ({e.Status})"));
            }

            if (ExpenseItems.Count > 30)
                lines.Add(($"... and {ExpenseItems.Count - 30} more", ""));

            OpenReportPreview("Expense Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintExpenseReport error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 6: PROFIT & LOSS
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadProfitLossAsync()
    {
        try
        {
            var fromDate = ProfitFrom.Date;
            var toDate = ProfitTo.Date;

            ProfitLossItems.Clear();
            long grandRevenue = 0;
            long grandExpenses = 0;

            var periods = GetPeriods(fromDate, toDate, ProfitPeriod);

            foreach (var (periodStart, periodEnd, periodLabel) in periods)
            {
                var periodFromUtc = periodStart.ToUniversalTime();
                var periodToUtc = periodEnd.AddDays(1).ToUniversalTime();

                // Revenue: sum of closed orders
                var revenue = await _db.Orders
                    .Where(o => o.Status == OrderStatus.Closed
                        && o.CreatedAt >= periodFromUtc
                        && o.CreatedAt < periodToUtc)
                    .SumAsync(o => (long?)o.GrandTotal) ?? 0;

                // Expenses: sum of supplier expenses
                var expenses = await _db.SupplierExpenses
                    .Where(e => e.ExpenseDate >= periodFromUtc
                        && e.ExpenseDate < periodToUtc)
                    .SumAsync(e => (long?)e.Amount) ?? 0;

                var profit = revenue - expenses;
                var margin = revenue > 0 ? (double)profit / revenue * 100 : 0;

                ProfitLossItems.Add(new ProfitLossRow
                {
                    Period = periodLabel,
                    Revenue = $"Rs. {revenue / 100m:N0}",
                    Expenses = $"Rs. {expenses / 100m:N0}",
                    Profit = $"Rs. {profit / 100m:N0}",
                    Margin = $"{margin:N1}%",
                    RawProfit = profit
                });

                grandRevenue += revenue;
                grandExpenses += expenses;
            }

            var grandProfit = grandRevenue - grandExpenses;
            var grandMargin = grandRevenue > 0 ? (double)grandProfit / grandRevenue * 100 : 0;

            SummaryRevenue = $"Rs. {grandRevenue / 100m:N0}";
            SummaryExpenses = $"Rs. {grandExpenses / 100m:N0}";
            SummaryProfit = $"Rs. {grandProfit / 100m:N0}";
            SummaryMargin = $"{grandMargin:N1}%";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] ProfitLoss error: {ex.Message}");
        }
    }

    private static List<(DateTime Start, DateTime End, string Label)> GetPeriods(DateTime from, DateTime to, string period)
    {
        var periods = new List<(DateTime, DateTime, string)>();

        switch (period)
        {
            case "Day":
                for (var d = from; d <= to; d = d.AddDays(1))
                    periods.Add((d, d, d.ToString("dd/MM/yy")));
                break;

            case "Week":
                var weekStart = from;
                while (weekStart <= to)
                {
                    var weekEnd = weekStart.AddDays(6);
                    if (weekEnd > to) weekEnd = to;
                    periods.Add((weekStart, weekEnd, $"{weekStart:dd/MM} - {weekEnd:dd/MM}"));
                    weekStart = weekEnd.AddDays(1);
                }
                break;

            case "Month":
                var monthStart = new DateTime(from.Year, from.Month, 1);
                while (monthStart <= to)
                {
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd > to) monthEnd = to;
                    var effectiveStart = monthStart < from ? from : monthStart;
                    periods.Add((effectiveStart, monthEnd, monthStart.ToString("MMM yyyy")));
                    monthStart = monthStart.AddMonths(1);
                }
                break;
        }

        return periods;
    }

    [RelayCommand]
    private void PrintProfitLoss()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{ProfitFrom:dd/MM/yy} - {ProfitTo:dd/MM/yy}"),
                ("Grouping", ProfitPeriod),
                ("", ""),
                ("Total Revenue", SummaryRevenue),
                ("Total Expenses", SummaryExpenses),
                ("Net Profit", SummaryProfit),
                ("Margin", SummaryMargin),
                ("", ""),
                ("--- Period Breakdown ---", "")
            };

            foreach (var p in ProfitLossItems)
            {
                lines.Add((p.Period, $"Rev: {p.Revenue} | Exp: {p.Expenses} | Profit: {p.Profit}"));
            }

            OpenReportPreview("Profit & Loss Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintProfitLoss error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 7: SALES REPORT
    // ══════════════════════════════════════════════

    private async Task LoadSalesReportCategoriesAsync()
    {
        try
        {
            var categories = await _db.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();

            SalesReportCategories.Clear();
            SalesReportCategories.Add("All");
            foreach (var cat in categories)
                SalesReportCategories.Add(cat);
        }
        catch { }
    }

    [RelayCommand]
    private async Task LoadSalesReportAsync()
    {
        try
        {
            var fromUtc = SalesReportFrom.Date.ToUniversalTime();
            var toUtc = SalesReportTo.Date.AddDays(1).ToUniversalTime();

            if (SalesReportMode == "Bill Wise")
            {
                // Bill-wise: show each order as a row
                var orders = await _db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
                    .Where(o => o.Status == OrderStatus.Closed
                        && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                SalesReportItems.Clear();
                int serial = 1;
                long totalSale = 0, totalDiscount = 0, totalGrand = 0;

                foreach (var o in orders)
                {
                    SalesReportItems.Add(new SalesReportRow
                    {
                        Serial = serial++,
                        ItemName = $"{o.OrderNumber} - {o.OrderType} ({o.Customer?.Name ?? "Walk-in"})",
                        Qty = 1,
                        Total = $"Rs. {o.GrandTotal / 100m:N0}",
                        RawTotal = o.GrandTotal
                    });
                    totalSale += o.SubTotal;
                    totalDiscount += o.DiscountAmount;
                    totalGrand += o.GrandTotal;
                }

                await SetSalesSummary(totalSale, totalDiscount, totalGrand, fromUtc, toUtc);
                SalesReportCount = $"{orders.Count} bills";
                SaleCategoryTotal = "";
            }
            else
            {
                // Item-wise (All Sale or filtered by category)
                var query = _db.OrderItems
                    .Include(oi => oi.MenuItem).ThenInclude(mi => mi.Category)
                    .Include(oi => oi.Order)
                    .Where(oi => oi.Order.Status == OrderStatus.Closed
                        && oi.Order.CreatedAt >= fromUtc
                        && oi.Order.CreatedAt < toUtc
                        && oi.Status != OrderStatus.Void);

                if (SalesReportCategory != "All")
                    query = query.Where(oi => oi.MenuItem.Category.Name == SalesReportCategory);

                var items = await query.ToListAsync();

                var grouped = items
                    .GroupBy(oi => oi.MenuItem?.Name ?? "Unknown")
                    .Select(g => new
                    {
                        Name = g.Key,
                        Qty = g.Sum(x => x.Quantity),
                        Total = g.Sum(x => x.LineTotal)
                    })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                SalesReportItems.Clear();
                int serial = 1;
                long itemTotal = 0;

                foreach (var g in grouped)
                {
                    SalesReportItems.Add(new SalesReportRow
                    {
                        Serial = serial++,
                        ItemName = g.Name,
                        Qty = g.Qty,
                        Total = $"Rs. {g.Total / 100m:N0}",
                        RawTotal = g.Total
                    });
                    itemTotal += g.Total;
                }

                // Get order-level totals for the summary
                var orderIds = items.Select(oi => oi.OrderId).Distinct().ToList();
                var orderSummary = await _db.Orders
                    .Where(o => orderIds.Contains(o.Id))
                    .GroupBy(o => 1)
                    .Select(g => new
                    {
                        SubTotal = g.Sum(o => o.SubTotal),
                        Discount = g.Sum(o => o.DiscountAmount),
                        Grand = g.Sum(o => o.GrandTotal)
                    })
                    .FirstOrDefaultAsync();

                var sale = orderSummary?.SubTotal ?? itemTotal;
                var disc = orderSummary?.Discount ?? 0;
                var grand = orderSummary?.Grand ?? itemTotal;

                await SetSalesSummary(sale, disc, grand, fromUtc, toUtc);
                SalesReportCount = $"{grouped.Count} items";

                if (SalesReportCategory != "All")
                    SaleCategoryTotal = $"{SalesReportCategory}: Rs. {itemTotal / 100m:N0}";
                else
                    SaleCategoryTotal = "";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] SalesReport error: {ex.Message}");
        }
    }

    private async Task SetSalesSummary(long sale, long discount, long grand, DateTime fromUtc, DateTime toUtc)
    {
        SaleTotal = $"Rs. {sale / 100m:N0}";
        SaleDiscount = $"Rs. {discount / 100m:N0}";
        SaleGrandTotal = $"Rs. {grand / 100m:N0}";

        var expenses = await _db.SupplierExpenses
            .Where(e => e.ExpenseDate >= fromUtc && e.ExpenseDate < toUtc)
            .SumAsync(e => (long?)e.Amount) ?? 0;

        SaleExpenses = $"Rs. {expenses / 100m:N0}";
        var cashInHand = grand - expenses;
        SaleCashInHand = $"Rs. {cashInHand / 100m:N0}";
    }

    [RelayCommand]
    private void PrintSalesReport()
    {
        try
        {
            var data = new ReceiptData
            {
                RestaurantName = _restaurantName,
                OrderNumber = "Sales Report",
                OrderType = SalesReportMode == "Bill Wise" ? "Bill Wise Sales" : (SalesReportCategory != "All" ? $"Category: {SalesReportCategory}" : "All Items"),
                DateTime = DateTime.Now,
                SubTotal = 0,
                TaxAmount = 0,
                DiscountAmount = 0,
                GrandTotal = 0,
                PaymentMethod = "-",
                Items = []
            };

            // Header info
            data.Items.Add(new ReceiptItem { Name = $"Period: {SalesReportFrom:dd/MM/yy} - {SalesReportTo:dd/MM/yy}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = "─────────────────────────────", Quantity = 0, UnitPrice = 0, LineTotal = 0 });

            // Items
            foreach (var item in SalesReportItems)
            {
                data.Items.Add(new ReceiptItem
                {
                    Name = item.ItemName,
                    Quantity = item.Qty,
                    UnitPrice = item.Qty > 0 ? item.RawTotal / item.Qty : 0,
                    LineTotal = item.RawTotal
                });
            }

            // Summary
            data.Items.Add(new ReceiptItem { Name = "─────────────────────────────", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = $"Sale: {SaleTotal}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = $"Discount: {SaleDiscount}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = $"Grand Total: {SaleGrandTotal}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = $"Expenses: {SaleExpenses}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });
            data.Items.Add(new ReceiptItem { Name = $"Cash In Hand: {SaleCashInHand}", Quantity = 0, UnitPrice = 0, LineTotal = 0 });

            var preview = new Views.PrintPreviewWindow(data, _printService)
                { ConfiguredPrinterName = _configuredPrinter };
            preview.Owner = AppWindow.Current.MainWindow;
            preview.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintSalesReport error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 8: CASHIER SALES
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadCashierSalesAsync()
    {
        try
        {
            var fromUtc = CashierSalesFrom.Date.ToUniversalTime();
            var toUtc = CashierSalesTo.Date.AddDays(1).ToUniversalTime();

            var orders = await _db.Orders
                .Include(o => o.Cashier)
                .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc && o.Status == OrderStatus.Closed)
                .ToListAsync();

            var grouped = orders
                .GroupBy(o => new { CashierId = o.CashierId ?? 0, CashierName = o.Cashier?.FullName ?? "Unknown" })
                .Select(g =>
                {
                    var cashPayments = g.SelectMany(o => o.Payments)
                        .Where(p => p.PaymentMethod != null &&
                            p.PaymentMethod.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Amount);
                    var cardPayments = g.SelectMany(o => o.Payments)
                        .Where(p => p.PaymentMethod != null &&
                            !p.PaymentMethod.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Amount);
                    var voidCount = orders.Count(o => o.Status == OrderStatus.Void && (o.CashierId ?? 0) == g.Key.CashierId);
                    var totalSales = g.Sum(o => o.GrandTotal);

                    return new CashierPerformanceRow
                    {
                        CashierId = g.Key.CashierId,
                        CashierName = g.Key.CashierName,
                        OrderCount = g.Count(),
                        TotalSales = $"Rs. {totalSales / 100m:N0}",
                        RawTotalSales = totalSales,
                        CashAmount = $"Rs. {cashPayments / 100m:N0}",
                        CardAmount = $"Rs. {cardPayments / 100m:N0}",
                        VoidCount = voidCount,
                        AvgOrderValue = g.Count() > 0
                            ? $"Rs. {totalSales / g.Count() / 100m:N0}"
                            : "Rs. 0"
                    };
                })
                .OrderByDescending(r => r.RawTotalSales)
                .ToList();

            CashierSalesItems.Clear();
            long totalAll = 0;
            foreach (var row in grouped)
            {
                CashierSalesItems.Add(row);
                totalAll += row.RawTotalSales;
            }

            CashierSalesCount = $"{grouped.Count} cashier{(grouped.Count != 1 ? "s" : "")}";
            CashierSalesTotal = $"Rs. {totalAll / 100m:N0}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] CashierSales error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PrintCashierSalesReport()
    {
        try
        {
            var lines = new List<(string label, string value)>
            {
                ("Period", $"{CashierSalesFrom:dd/MM/yy} - {CashierSalesTo:dd/MM/yy}"),
                ("Total Cashiers", CashierSalesCount),
                ("Total Sales", CashierSalesTotal),
                ("", ""),
                ("--- Cashier Details ---", "")
            };

            foreach (var c in CashierSalesItems)
            {
                lines.Add(($"{c.CashierName}: {c.OrderCount} orders", $"{c.TotalSales} (Avg: {c.AvgOrderValue})"));
            }

            OpenReportPreview("Cashier Sales Report", lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reports] PrintCashierSales error: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════
    //  HELPER: Build report receipt and open preview
    // ══════════════════════════════════════════════

    private void OpenReportPreview(string title, List<(string label, string value)> lines)
    {
        var data = BuildReportReceipt(title, lines);
        var preview = new Views.PrintPreviewWindow(data, _printService)
            { ConfiguredPrinterName = _configuredPrinter };
        preview.Owner = AppWindow.Current.MainWindow;
        preview.ShowDialog();
    }

    private ReceiptData BuildReportReceipt(string title, List<(string label, string value)> lines)
    {
        var data = new ReceiptData
        {
            RestaurantName = _restaurantName,
            OrderNumber = "Report",
            OrderType = title,
            DateTime = DateTime.Now,
            SubTotal = 0,
            TaxAmount = 0,
            DiscountAmount = 0,
            GrandTotal = 0,
            PaymentMethod = "-",
            Items = []
        };

        foreach (var (label, value) in lines)
        {
            data.Items.Add(new ReceiptItem
            {
                Name = string.IsNullOrEmpty(value) ? label : $"{label}: {value}",
                Quantity = 0,
                UnitPrice = 0,
                LineTotal = 0
            });
        }

        return data;
    }
}

// ══════════════════════════════════════════════
//  ROW VIEW MODELS (POCOs)
// ══════════════════════════════════════════════

public class OrderHistoryRow
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string ItemsSummary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Total { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
}

public class MenuPerformanceRow
{
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int QtySold { get; set; }
    public string Revenue { get; set; } = string.Empty;
    public string AvgPrice { get; set; } = string.Empty;
}

public class DriverPerformanceRow
{
    public string DriverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public string TotalRevenue { get; set; } = string.Empty;
    public string AvgPerOrder { get; set; } = string.Empty;
}

public class KitchenReportRow
{
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Station { get; set; } = string.Empty;
    public string ItemsSummary { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public int SlipCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Cashier { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;

    /// <summary>All KitchenOrder records for this order (for detail view).</summary>
    public List<RestaurantPOS.Domain.Entities.KitchenOrder> KitchenOrders { get; set; } = [];
}

public class ExpenseReportRow
{
    public string Date { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ProfitLossRow
{
    public string Period { get; set; } = string.Empty;
    public string Revenue { get; set; } = string.Empty;
    public string Expenses { get; set; } = string.Empty;
    public string Profit { get; set; } = string.Empty;
    public string Margin { get; set; } = string.Empty;
    public long RawProfit { get; set; }
    public bool IsNegative => RawProfit < 0;
}

public class SalesReportRow
{
    public int Serial { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Total { get; set; } = string.Empty;
    public long RawTotal { get; set; }
}

public class CashierPerformanceRow
{
    public int CashierId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public string TotalSales { get; set; } = string.Empty;
    public long RawTotalSales { get; set; }
    public string CashAmount { get; set; } = string.Empty;
    public string CardAmount { get; set; } = string.Empty;
    public int VoidCount { get; set; }
    public string AvgOrderValue { get; set; } = string.Empty;
}
