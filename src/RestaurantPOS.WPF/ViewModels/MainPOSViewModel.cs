using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Printing;
using RestaurantPOS.Printing.Receipt;
using RestaurantPOS.WPF.Views;

namespace RestaurantPOS.WPF.ViewModels;

public partial class OrderItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public int MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private int _quantity = 1;

    public long UnitPrice { get; set; }

    [ObservableProperty]
    private long _lineTotal;

    [ObservableProperty]
    private string _remarks = string.Empty;

    public string PriceDisplay => $"Rs. {UnitPrice / 100m:N0}";
    public string LineTotalDisplay => $"Rs. {LineTotal / 100m:N0}";

    [ObservableProperty]
    private int _serialNumber;

    public bool KitchenPrinted { get; set; }
    public int KitchenPrintedQty { get; set; }

    /// <summary>Deal component items for display (e.g. "Zinger ×1, Fries ×1, Coke ×1")</summary>
    public List<DealComponentInfo> DealComponents { get; set; } = [];

    /// <summary>Display name with deal components on separate indented lines.</summary>
    public string DisplayName => DealComponents.Count > 0
        ? $"{Name}\n{string.Join("\n", DealComponents.Select(c => $"   {c.Qty} {c.ItemName}"))}"
        : Name;

    partial void OnQuantityChanged(int value)
    {
        LineTotal = UnitPrice * value;
        OnPropertyChanged(nameof(LineTotalDisplay));
        OnPropertyChanged(nameof(DisplayName));
    }
}

public class DealComponentInfo
{
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
}

// ══════════════════════════════════════════════════════════
//  Saved order state — allows switching between orders
// ══════════════════════════════════════════════════════════
public class SavedOrderState
{
    public Order? Order { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderType OrderType { get; set; }
    public List<OrderItemViewModel> Items { get; set; } = [];
    public int? TableId { get; set; }
    public Table? Table { get; set; }

    // Billing fields
    public decimal DiscountPercent { get; set; }
    public decimal DiscountRs { get; set; }
    public decimal TaxPercent { get; set; } = 16;
    public decimal GstRs { get; set; }
    public long CashTendered { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public bool IsCash { get; set; } = true;
    public bool IsCardCredit { get; set; }
    public bool IsOnlinePayment { get; set; }
    public bool IsCOD { get; set; }

    // ── Delivery management fields ──
    public string DeliveryStatus { get; set; } = "Preparing";   // Preparing | Dispatched | Completed
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    public string OrderNote { get; set; } = string.Empty;
    public bool IsSettled { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // ── Takeaway management fields ──
    public string TakeawayStatus { get; set; } = "Preparing";  // Preparing | Ready | Completed

    // ── K-Slip tracking ──
    public bool KSlipPrinted { get; set; }

    // ── Matched customer reference (for restoring _matchedCustomer) ──
    public Customer? MatchedCustomer { get; set; }

    /// <summary>Unique key for this order in the state dictionary.</summary>
    public string Key => OrderType == OrderType.DineIn
        ? $"table-{TableId}"
        : $"{OrderType}-{OrderNumber}";
}

public partial class MainPOSViewModel : BaseViewModel
{
    private readonly IMenuService _menuService;
    private readonly IOrderService _orderService;
    private readonly ITableService _tableService;
    private readonly ICustomerService _customerService;
    private readonly ISettingsService _settingsService;
    private readonly IPrintService _printService;
    private readonly Infrastructure.Data.PosDbContext _db;

    // ── Current logged-in user (for role-based billing history) ──
    private User? _loggedInUser;
    private IAuthService? _authService;

    // ── Cached receipt/print settings (loaded once at startup) ──
    private string _receiptRestaurantName = "KFC RESTAURANT";
    private string _receiptAddress = "";
    private string _receiptPhone = "";
    private string _receiptHeader = "";
    private string _receiptFooter = "";
    private string? _configuredReceiptPrinter;
    private string? _configuredKotPrinter;

    public void SetCurrentUser(User user, IAuthService authService)
    {
        _loggedInUser = user;
        _authService = authService;
        ApplyPermissions();
    }

    // ═══════════════════════════════════════════════
    //  PERMISSION-BASED UI GUARDS
    // ═══════════════════════════════════════════════

    [ObservableProperty] private bool _canVoidOrder;
    [ObservableProperty] private bool _canApplyDiscount;
    [ObservableProperty] private bool _canCreateDiscount;
    [ObservableProperty] private bool _canIssueRefund;
    [ObservableProperty] private bool _canHoldRecallOrders;
    [ObservableProperty] private bool _canProcessPayments;
    [ObservableProperty] private bool _canManageTables;
    [ObservableProperty] private bool _canOpenCashDrawer;

    private void ApplyPermissions()
    {
        if (_authService == null) return;

        CanVoidOrder = _authService.HasPermission("Void / cancel orders", minimumLevel: 5);
        CanIssueRefund = _authService.HasPermission("Issue refunds", minimumLevel: 5);
        CanHoldRecallOrders = _authService.HasPermission("Hold & recall orders");
        CanProcessPayments = _authService.HasPermission("Process payments");
        CanManageTables = _authService.HasPermission("Manage tables & sessions");
        CanOpenCashDrawer = _authService.HasPermission("Cash drawer operations");

        // Discount: level >= 2 can apply existing, level >= 5 can create new
        int discountLevel = _authService.GetAccessLevel("Apply discounts");
        CanApplyDiscount = discountLevel >= 2;
        CanCreateDiscount = discountLevel >= 5;
    }

    // ── Multi-order state store ──
    private readonly Dictionary<string, SavedOrderState> _orderStates = new();
    private bool _isRestoringState;

    // ══════════════════════════════════════════════════════════
    //  BILLING HISTORY
    // ══════════════════════════════════════════════════════════

    public ObservableCollection<Order> BillingHistory { get; } = [];

    [ObservableProperty]
    private Order? _selectedBillingOrder;

    [ObservableProperty]
    private DateTime _billingFromDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _billingToDate = DateTime.Today;

    [ObservableProperty]
    private string _billingSearchText = string.Empty;

    [ObservableProperty]
    private string _billingSummary = string.Empty;

    /// <summary>True if logged-in user is Admin (sees all orders). False = Cashier (sees own only).</summary>
    public bool IsAdmin => _loggedInUser?.Role?.Name?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true
                        || _loggedInUser?.RoleId == 1;

    /// <summary>True if logged-in user is Admin or Manager (both see all billing history).</summary>
    public bool IsAdminOrManager => _loggedInUser?.RoleId == 1 || _loggedInUser?.RoleId == 2;

    // Categories
    public ObservableCollection<Category> Categories { get; } = [];

    [ObservableProperty]
    private Category? _selectedCategory;

    // Menu Items
    public ObservableCollection<MenuItem> MenuItems { get; } = [];

    [ObservableProperty]
    private MenuItem? _selectedMenuItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Order Items (current active order)
    public ObservableCollection<OrderItemViewModel> OrderItems { get; } = [];

    [ObservableProperty]
    private OrderItemViewModel? _selectedOrderItem;

    // Order info
    private Order? _currentOrder;

    [ObservableProperty]
    private OrderType _selectedOrderType = OrderType.DineIn;

    [ObservableProperty]
    private string _orderNumber = string.Empty;

    // Tables
    public ObservableCollection<Table> Tables { get; } = [];

    [ObservableProperty]
    private Table? _selectedTable;

    // Totals
    [ObservableProperty]
    private long _subTotal;

    [ObservableProperty]
    private long _discountAmount;

    [ObservableProperty]
    private long _taxAmount;

    [ObservableProperty]
    private long _serviceCharge;

    [ObservableProperty]
    private long _adjustment;

    [ObservableProperty]
    private long _grandTotal;

    [ObservableProperty]
    private decimal _discountPercent;

    [ObservableProperty]
    private decimal _discountRs;

    [ObservableProperty]
    private decimal _taxPercent = 16;

    [ObservableProperty]
    private decimal _gstRs;

    // Payment
    [ObservableProperty]
    private long _cashTendered;

    [ObservableProperty]
    private long _changeAmount;

    // Customer
    [ObservableProperty]
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    // Customer search/match state
    [ObservableProperty]
    private bool _isPhoneMatched;

    [ObservableProperty]
    private bool _isPhoneSearchActive;

    /// <summary>True when user is typing but no results found at all (red text).</summary>
    [ObservableProperty]
    private bool _isPhoneNoResults;

    private Customer? _matchedCustomer;

    public ObservableCollection<Customer> PhoneSearchResults { get; } = [];

    // Comment
    [ObservableProperty]
    private string _commentText = string.Empty;

    // Payment method
    [ObservableProperty]
    private bool _isCash = true;

    [ObservableProperty]
    private bool _isCardCredit;

    [ObservableProperty]
    private bool _isOnlinePayment;

    [ObservableProperty]
    private bool _isCOD;

    // Print options
    [ObservableProperty]
    private bool _printA;

    [ObservableProperty]
    private bool _printC = true;

    [ObservableProperty]
    private bool _printKitchen;

    [ObservableProperty]
    private bool _isKFull;

    [ObservableProperty]
    private bool _isKBill;

    // Active orders by type (for tabs)
    public ObservableCollection<DeliveryOrderViewModel> DeliveryOrders { get; } = [];
    public ObservableCollection<TakeawayOrderViewModel> TakeawayOrders { get; } = [];

    // ── Delivery tab filter ──
    [ObservableProperty]
    private string _deliveryFilter = "All";

    // ── Takeaway tab filter ──
    [ObservableProperty]
    private string _takeawayFilter = "All";

    // ── Delivery/Takeaway full-screen toggle ──
    [ObservableProperty]
    private bool _isDeliveryMaximized;

    [ObservableProperty]
    private bool _isTakeawayMaximized;

    [RelayCommand]
    private void ToggleDeliveryMaximize() => IsDeliveryMaximized = !IsDeliveryMaximized;

    [RelayCommand]
    private void ToggleTakeawayMaximize() => IsTakeawayMaximized = !IsTakeawayMaximized;

    // ── HOLD tab: all open (non-checkout, non-unpaid) orders ──
    public ObservableCollection<HoldOrderViewModel> HoldOrders { get; } = [];

    [ObservableProperty]
    private string _holdFilter = "ALL";

    [ObservableProperty]
    private HoldOrderViewModel? _selectedHoldOrder;

    partial void OnSelectedHoldOrderChanged(HoldOrderViewModel? value)
    {
        if (value != null)
            SelectHoldOrder(value);
    }

    // Display helpers
    public string SubTotalDisplay => $"{SubTotal / 100m:N2}";
    public string DiscountDisplay => $"{DiscountAmount / 100m:N2}";
    public string TaxDisplay => $"{TaxAmount / 100m:N2}";
    public string ServiceChargeDisplay => $"{ServiceCharge / 100m:N2}";
    public string GrandTotalDisplay => $"{GrandTotal / 100m:N2}";
    public string ChangeDisplay => $"{ChangeAmount / 100m:N2}";

    public string AfterDiscountDisplay => $"{(SubTotal - DiscountAmount) / 100m:N1}";
    public string AfterGSTDisplay => $"{(SubTotal - DiscountAmount + TaxAmount) / 100m:N1}";
    public string RemainingDisplay => $"{(CashTendered > GrandTotal ? CashTendered - GrandTotal : 0) / 100m:N2}";
    public string PayDisplay => CashTendered == 0 ? "" : $"{CashTendered / 100m:N2}";
    public string OrderTypeDisplay => SelectedOrderType switch
    {
        OrderType.DineIn => "Din-in",
        OrderType.TakeAway => "Takeaway",
        OrderType.Delivery => "Delivery",
        _ => "Din-in"
    };
    public string TableDisplay => SelectedTable?.Name ?? "";

    public MainPOSViewModel(
        IMenuService menuService,
        IOrderService orderService,
        ITableService tableService,
        ICustomerService customerService,
        ISettingsService settingsService,
        IPrintService printService,
        Infrastructure.Data.PosDbContext db)
    {
        _menuService = menuService;
        _orderService = orderService;
        _tableService = tableService;
        _customerService = customerService;
        _settingsService = settingsService;
        _printService = printService;
        _db = db;
        Title = "POS";
    }

    [RelayCommand]
    private void SelectCategory(Category category)
    {
        SelectedCategory = category;
    }

    private bool _dataLoaded;

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // Only load categories/tables on first load — preserve state across navigation
        if (_dataLoaded) return;

        IsBusy = true;
        try
        {
            var categories = await _menuService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var cat in categories)
                Categories.Add(cat);

            var tables = (await _tableService.GetTablesAsync())
                .OrderBy(t => t.Name.Contains("Family", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(t => t.DisplayOrder);
            Tables.Clear();
            foreach (var table in tables)
                Tables.Add(table);

            if (Categories.Count > 0)
                SelectedCategory = Categories[0];

            // ── Reload open orders from DB (persists across app restarts) ──
            await ReloadOpenOrdersFromDbAsync();

            // ── Load receipt & printer settings from DB ──
            await LoadPrintSettingsAsync();

            _dataLoaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPrintSettingsAsync()
    {
        try
        {
            _receiptRestaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName")
                                     ?? await _settingsService.GetSettingAsync("RestaurantName")
                                     ?? "KFC RESTAURANT";
            _receiptAddress = await _settingsService.GetSettingAsync("ReceiptAddress")
                              ?? await _settingsService.GetSettingAsync("RestaurantAddress")
                              ?? "";
            _receiptPhone = await _settingsService.GetSettingAsync("ReceiptPhone")
                            ?? await _settingsService.GetSettingAsync("RestaurantPhone")
                            ?? "";
            _receiptHeader = await _settingsService.GetSettingAsync("ReceiptHeader") ?? "";
            _receiptFooter = await _settingsService.GetSettingAsync("ReceiptFooter") ?? "";

            // Load default receipt printer (or the only one if just one exists)
            var receiptPrinter = await _db.Printers
                .Where(p => p.IsActive && p.IsDefault && p.Type == Domain.Enums.PrinterType.Receipt)
                .FirstOrDefaultAsync()
                ?? await _db.Printers
                    .Where(p => p.IsActive && p.Type == Domain.Enums.PrinterType.Receipt)
                    .FirstOrDefaultAsync()
                ?? await _db.Printers
                    .Where(p => p.IsActive && p.SystemPrinterName != null)
                    .FirstOrDefaultAsync();
            _configuredReceiptPrinter = receiptPrinter?.SystemPrinterName;

            // Load default KOT printer (fall back to receipt printer)
            var kotPrinter = await _db.Printers
                .Where(p => p.IsActive && p.IsDefault && p.Type == Domain.Enums.PrinterType.KOT)
                .FirstOrDefaultAsync()
                ?? await _db.Printers
                    .Where(p => p.IsActive && p.Type == Domain.Enums.PrinterType.KOT)
                    .FirstOrDefaultAsync();
            _configuredKotPrinter = kotPrinter?.SystemPrinterName;
        }
        catch { /* Non-fatal — use defaults */ }
    }

    /// <summary>
    /// Reload all open/preparing orders from the database into _orderStates.
    /// This ensures orders survive app restart — table colors stay correct
    /// and order details are preserved.
    /// </summary>
    private async Task ReloadOpenOrdersFromDbAsync()
    {
        try
        {
            // ── Step 1: Reset ALL tables to Available first ──
            // Tables may come from DB with stale Occupied status from
            // a previous session where the app was closed without checkout.
            foreach (var tbl in Tables)
            {
                if (tbl.Status != TableStatus.Available)
                {
                    tbl.Status = TableStatus.Available;
                    RefreshTableInList(tbl);
                }
            }

            // ── Step 2: Load open orders and rebuild state ──
            var openOrders = await _orderService.GetOpenOrdersAsync();

            foreach (var order in openOrders)
            {
                // Build order items from DB
                var items = order.OrderItems
                    .Where(oi => oi.Status != OrderStatus.Void)
                    .Select((oi, idx) => new OrderItemViewModel
                    {
                        OrderItemId = oi.Id,
                        MenuItemId = oi.MenuItemId,
                        Name = oi.MenuItem?.Name ?? "Unknown",
                        Quantity = oi.Quantity,
                        UnitPrice = oi.UnitPrice,
                        LineTotal = oi.LineTotal,
                        Remarks = oi.Notes ?? string.Empty,
                        SerialNumber = idx + 1,
                        KitchenPrinted = true, // Assume already printed if order exists in DB
                        KitchenPrintedQty = oi.Quantity
                    })
                    .ToList();

                if (items.Count == 0) continue;

                // Determine table from session
                Table? table = null;
                int? tableId = null;
                if (order.TableSession?.Table != null)
                {
                    tableId = order.TableSession.Table.Id;
                    // Find matching table in our loaded Tables collection
                    table = Tables.FirstOrDefault(t => t.Id == tableId);
                    if (table != null)
                    {
                        // Only mark occupied if the table session is still open (not closed)
                        if (order.TableSession.ClosedAt == null)
                        {
                            table.Status = TableStatus.Occupied;
                            RefreshTableInList(table);
                        }
                    }
                }

                // Build saved order state
                var state = new SavedOrderState
                {
                    Order = order,
                    OrderNumber = order.OrderNumber,
                    OrderType = order.OrderType,
                    Items = items,
                    TableId = tableId,
                    Table = table,
                    CustomerPhone = order.Customer?.Phone ?? string.Empty,
                    CustomerName = order.Customer?.Name ?? string.Empty,
                    CustomerAddress = order.Customer?.Addresses?.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                                     ?? order.Customer?.Addresses?.FirstOrDefault()?.AddressLine1
                                     ?? string.Empty,
                    TaxPercent = 16,
                    DeliveryStatus = order.OrderType == OrderType.Delivery ? "Preparing" : "Preparing",
                    KSlipPrinted = true  // Orders in DB already had K-slip printed
                };

                // Don't overwrite if already in memory (shouldn't happen on fresh start, but be safe)
                if (!_orderStates.ContainsKey(state.Key))
                    _orderStates[state.Key] = state;
            }

            // Refresh tab lists
            RefreshHoldOrders();
            RefreshDeliveryOrders();
            RefreshTakeawayOrders();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReloadOpenOrders] Error: {ex.Message}");
        }
    }

    partial void OnSelectedCategoryChanged(Category? value)
    {
        if (value != null)
            _ = LoadMenuItemsAsync(value.Id);
    }

    private async Task LoadMenuItemsAsync(int categoryId)
    {
        var items = await _menuService.GetMenuItemsByCategoryAsync(categoryId);
        MenuItems.Clear();
        foreach (var item in items)
            MenuItems.Add(item);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (SelectedCategory != null)
                _ = LoadMenuItemsAsync(SelectedCategory.Id);
        }
        else
        {
            _ = SearchMenuItemsAsync(value);
        }
    }

    private async Task SearchMenuItemsAsync(string query)
    {
        var items = await _menuService.SearchMenuItemsAsync(query);
        MenuItems.Clear();
        foreach (var item in items)
            MenuItems.Add(item);
    }

    // ══════════════════════════════════════════════════════════
    //  ORDER STATE MANAGEMENT — Save / Restore / Switch
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Save current billing state to the order states dictionary.
    /// </summary>
    private void SaveCurrentOrderState()
    {
        if (_currentOrder == null) return;

        var state = new SavedOrderState
        {
            Order = _currentOrder,
            OrderNumber = OrderNumber,
            OrderType = SelectedOrderType,
            TableId = SelectedTable?.Id,
            Table = SelectedTable,
            DiscountPercent = DiscountPercent,
            DiscountRs = DiscountRs,
            TaxPercent = TaxPercent,
            GstRs = GstRs,
            CashTendered = CashTendered,
            CustomerPhone = CustomerPhone,
            CustomerName = CustomerName,
            CustomerAddress = _matchedCustomer?.Addresses?.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                              ?? _matchedCustomer?.Addresses?.FirstOrDefault()?.AddressLine1
                              ?? string.Empty,
            CommentText = CommentText,
            IsCash = IsCash,
            IsCardCredit = IsCardCredit,
            IsOnlinePayment = IsOnlinePayment,
            IsCOD = IsCOD,
            // Preserve delivery fields from existing state
            DeliveryStatus = _orderStates.TryGetValue(
                SelectedOrderType == OrderType.DineIn ? $"table-{SelectedTable?.Id}" : $"{SelectedOrderType}-{OrderNumber}",
                out var prev) ? prev.DeliveryStatus : "Preparing",
            DriverName = prev?.DriverName ?? string.Empty,
            DriverPhone = prev?.DriverPhone ?? string.Empty,
            OrderNote = prev?.OrderNote ?? string.Empty,
            IsSettled = prev?.IsSettled ?? false,
            DispatchedAt = prev?.DispatchedAt,
            CompletedAt = prev?.CompletedAt,
            KSlipPrinted = prev?.KSlipPrinted ?? false,
            MatchedCustomer = _matchedCustomer
        };

        // Deep copy order items
        state.Items.Clear();
        foreach (var oi in OrderItems)
        {
            state.Items.Add(new OrderItemViewModel
            {
                Id = oi.Id,
                OrderItemId = oi.OrderItemId,
                MenuItemId = oi.MenuItemId,
                Name = oi.Name,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                Remarks = oi.Remarks,
                SerialNumber = oi.SerialNumber,
                KitchenPrinted = oi.KitchenPrinted,
                KitchenPrintedQty = oi.KitchenPrintedQty
            });
        }

        _orderStates[state.Key] = state;
    }

    /// <summary>
    /// Restore a saved order state into the billing section.
    /// </summary>
    private void RestoreOrderState(SavedOrderState state)
    {
        _isRestoringState = true;
        try
        {
            _currentOrder = state.Order;
            OrderNumber = state.OrderNumber;
            SelectedOrderType = state.OrderType;
            SelectedTable = state.Table;
            DiscountPercent = state.DiscountPercent;
            DiscountRs = state.DiscountRs;
            TaxPercent = state.TaxPercent;
            GstRs = state.GstRs;
            CashTendered = state.CashTendered;
            _matchedCustomer = state.MatchedCustomer;
            CustomerPhone = state.CustomerPhone;
            CustomerName = state.CustomerName;
            IsPhoneMatched = _matchedCustomer != null;
            IsPhoneSearchActive = false;
            IsPhoneNoResults = false;
            CommentText = state.CommentText;
            IsCash = state.IsCash;
            IsCardCredit = state.IsCardCredit;
            IsOnlinePayment = state.IsOnlinePayment;
            IsCOD = state.IsCOD;

            OrderItems.Clear();
            foreach (var oi in state.Items)
                OrderItems.Add(oi);

            RecalculateTotals();
            OnPropertyChanged(nameof(OrderTypeDisplay));
            OnPropertyChanged(nameof(TableDisplay));
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    /// <summary>
    /// Clear the billing section for a brand new order (no save).
    /// </summary>
    private void ClearBillingSection()
    {
        _isRestoringState = true;
        try
        {
        _currentOrder = null;
        _matchedCustomer = null;
        OrderItems.Clear();
        OrderNumber = string.Empty;
        SubTotal = 0;
        DiscountAmount = 0;
        TaxAmount = 0;
        ServiceCharge = 0;
        Adjustment = 0;
        GrandTotal = 0;
        CashTendered = 0;
        ChangeAmount = 0;
        DiscountPercent = 0;
        DiscountRs = 0;
        TaxPercent = 16;
        GstRs = 0;
        CustomerPhone = string.Empty;
        CustomerName = string.Empty;
        IsPhoneMatched = false;
        IsPhoneSearchActive = false;
        IsPhoneNoResults = false;
        CommentText = string.Empty;
        IsCash = true;
        IsCardCredit = false;
        IsOnlinePayment = false;
        IsCOD = false;

        NotifyAllDisplayProperties();
        OnPropertyChanged(nameof(ChangeDisplay));
        OnPropertyChanged(nameof(OrderTypeDisplay));
        OnPropertyChanged(nameof(TableDisplay));
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  TABLE SELECTION — Click a table to load/create its order
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SelectTable(Table table)
    {
        // Save current order if one exists
        SaveCurrentOrderState();

        // Deselect previous (triggers re-evaluation of all table backgrounds)
        var previousTable = SelectedTable;

        // Check if this table already has a saved order
        var key = $"table-{table.Id}";
        if (_orderStates.TryGetValue(key, out var existingState))
        {
            // Load existing order for this table
            RestoreOrderState(existingState);
            SelectedTable = table;
        }
        else
        {
            // New table — clear billing, set table, ready for items
            ClearBillingSection();
            SelectedTable = table;
            SelectedOrderType = OrderType.DineIn;
        }

        // Refresh previous and new table to update background colors
        if (previousTable != null) RefreshTableInList(previousTable);
        RefreshTableInList(table);

        OnPropertyChanged(nameof(TableDisplay));
        OnPropertyChanged(nameof(OrderTypeDisplay));
    }

    // ══════════════════════════════════════════════════════════
    //  DELIVERY / TAKEAWAY ORDER SELECTION (from tab lists)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SelectDeliveryOrder(DeliveryOrderViewModel summary)
    {
        SwitchToSavedOrder(OrderType.Delivery, summary.OrderNumber);
    }

    private void SwitchToSavedOrder(OrderType type, string orderNumber)
    {
        SaveCurrentOrderState();

        var key = $"{type}-{orderNumber}";
        if (_orderStates.TryGetValue(key, out var state))
        {
            RestoreOrderState(state);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ADD MENU ITEM
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task AddMenuItemAsync(MenuItem menuItem)
    {
        // ── Shift check: POS requires an active shift ──
        var activeShiftId = await ShiftManagementViewModel.GetActiveShiftIdAsync(_db);
        if (activeShiftId == null)
        {
            System.Windows.MessageBox.Show(
                "No active shift. Please open a shift before creating orders.\n\nGo to Shift Management to start a shift.",
                "Shift Required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Din-in requires table selection BEFORE adding items
        if (SelectedOrderType == OrderType.DineIn && SelectedTable == null)
        {
            System.Windows.MessageBox.Show("Please select a table first for Dine-In orders.",
                "Table Required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Ensure we have a current order
        if (_currentOrder == null)
        {
            var cashierId = _loggedInUser?.Id ?? 1;
            _currentOrder = await _orderService.CreateOrderAsync(
                SelectedOrderType, SelectedTable?.Id, _matchedCustomer?.Id, cashierId, activeShiftId);
            OrderNumber = _currentOrder.OrderNumber;

            // For Din-in: open table session (blue highlight handled by MultiBinding converter)
            if (SelectedOrderType == OrderType.DineIn && SelectedTable != null)
            {
                await _tableService.OpenTableSessionAsync(SelectedTable.Id, null);
            }
        }

        var orderItem = await _orderService.AddItemToOrderAsync(
            _currentOrder.Id, menuItem.Id, null, 1, null);

        // Check if already in list
        var existing = OrderItems.FirstOrDefault(oi => oi.MenuItemId == menuItem.Id);
        if (existing != null)
        {
            existing.Quantity = orderItem.Quantity;
            existing.LineTotal = orderItem.LineTotal;
        }
        else
        {
            var vm = new OrderItemViewModel
            {
                OrderItemId = orderItem.Id,
                MenuItemId = menuItem.Id,
                Name = menuItem.Name,
                Quantity = orderItem.Quantity,
                UnitPrice = orderItem.UnitPrice,
                LineTotal = orderItem.LineTotal,
                SerialNumber = OrderItems.Count + 1
            };

            // If this item is a deal, load its component items for display
            var deal = await _db.Deals
                .Include(d => d.Items).ThenInclude(di => di.MenuItem)
                .FirstOrDefaultAsync(d => d.IsActive && d.Name == menuItem.Name);
            if (deal != null && deal.Items.Count > 0)
            {
                vm.DealComponents = deal.Items.Select(di => new DealComponentInfo
                {
                    ItemName = di.MenuItem.Name,
                    Qty = di.Quantity
                }).ToList();
            }

            OrderItems.Add(vm);
        }

        RecalculateTotals();

        // Auto-save state + update tab lists
        SaveCurrentOrderState();
        if (SelectedOrderType == OrderType.Delivery || SelectedOrderType == OrderType.TakeAway)
            UpdateOrderTypeTab();
        RefreshHoldOrders();
    }

    /// <summary>
    /// Force UI refresh of a table's color by replacing it in the collection.
    /// </summary>
    private void RefreshTableInList(Table table)
    {
        var idx = -1;
        for (int i = 0; i < Tables.Count; i++)
        {
            if (Tables[i].Id == table.Id)
            {
                idx = i;
                break;
            }
        }
        if (idx >= 0)
        {
            Tables.RemoveAt(idx);
            Tables.Insert(idx, table);
        }
    }

    /// <summary>
    /// Add/update the current order summary in the Delivery or Takeaway orders list.
    /// </summary>
    private void UpdateOrderTypeTab()
    {
        if (_currentOrder == null) return;

        // Save current order state first so latest customer info is persisted
        SaveCurrentOrderState();

        if (SelectedOrderType == OrderType.Delivery)
        {
            RefreshDeliveryOrders();
            return;
        }

        if (SelectedOrderType == OrderType.TakeAway)
        {
            RefreshTakeawayOrders();
            return;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  HOLD TAB — All open orders (not checked-out / not un-paid)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetHoldFilter(string filter)
    {
        HoldFilter = filter;
        RefreshHoldOrders();
    }

    /// <summary>
    /// Rebuild the HoldOrders list from the in-memory _orderStates dictionary.
    /// Filters by current HoldFilter (ALL, DineIn, TakeAway, Delivery).
    /// </summary>
    public void RefreshHoldOrders()
    {
        HoldOrders.Clear();

        var openStates = _orderStates.Values
            .Where(s => s.Items.Count > 0)
            .OrderByDescending(s => s.Order?.CreatedAt ?? DateTime.MinValue)
            .ToList();

        foreach (var state in openStates)
        {
            // Apply filter
            if (HoldFilter != "ALL")
            {
                var filterType = HoldFilter switch
                {
                    "DineIn" => OrderType.DineIn,
                    "TakeAway" => OrderType.TakeAway,
                    "Delivery" => OrderType.Delivery,
                    _ => (OrderType?)null
                };
                if (filterType.HasValue && state.OrderType != filterType.Value)
                    continue;
            }

            var tableName = state.OrderType == OrderType.DineIn ? state.Table?.Name ?? "" : "";
            var typeLabel = state.OrderType switch
            {
                OrderType.DineIn => "Din-in",
                OrderType.TakeAway => "Takeaway",
                OrderType.Delivery => "Delivery",
                _ => ""
            };

            var total = state.Items.Sum(i => i.LineTotal);
            var itemCount = state.Items.Count;
            var hasKSlip = state.Items.Any(i => i.KitchenPrinted);

            HoldOrders.Add(new HoldOrderViewModel
            {
                StateKey = state.Key,
                OrderNumber = state.OrderNumber,
                OrderTypeLabel = typeLabel,
                TableName = tableName,
                ItemCount = itemCount,
                TotalDisplay = $"Rs. {total / 100m:N0}",
                CustomerName = state.CustomerName,
                CustomerPhone = state.CustomerPhone,
                KSlipStatus = hasKSlip ? "Sent" : "Pending",
                OrderTime = state.Order?.CreatedAt ?? DateTime.Now
            });
        }
    }

    [RelayCommand]
    private void SelectHoldOrder(HoldOrderViewModel holdOrder)
    {
        if (holdOrder == null) return;

        // Save current first
        SaveCurrentOrderState();

        if (_orderStates.TryGetValue(holdOrder.StateKey, out var state))
        {
            RestoreOrderState(state);

            // If DineIn, set the selected table for blue highlight
            if (state.OrderType == OrderType.DineIn && state.Table != null)
            {
                var prevTable = SelectedTable;
                SelectedTable = state.Table;
                if (prevTable != null) RefreshTableInList(prevTable);
                RefreshTableInList(state.Table);
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  DELIVERY TAB — Full lifecycle management
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetDeliveryFilter(string filter)
    {
        DeliveryFilter = filter;
        RefreshDeliveryOrders();
    }

    /// <summary>
    /// Rebuild the DeliveryOrders list from _orderStates (delivery orders only).
    /// Filters by DeliveryFilter: All, Preparing, Dispatched, Completed.
    /// </summary>
    public void RefreshDeliveryOrders()
    {
        DeliveryOrders.Clear();

        var deliveryStates = _orderStates.Values
            .Where(s => s.OrderType == OrderType.Delivery && s.Items.Count > 0 && s.KSlipPrinted)
            .OrderByDescending(s => s.Order?.CreatedAt ?? DateTime.MinValue)
            .ToList();

        foreach (var state in deliveryStates)
        {
            // Apply filter
            if (DeliveryFilter != "All" &&
                !string.Equals(state.DeliveryStatus, DeliveryFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var subTot = state.Items.Sum(i => i.LineTotal);
            var disc = state.DiscountPercent > 0 ? (long)(subTot * (long)state.DiscountPercent / 100m) : 0L;
            var taxable = subTot - disc;
            var tax = state.TaxPercent > 0 ? (long)(taxable * (long)state.TaxPercent / 100m) : 0L;
            var total = taxable + tax;
            var orderTime = state.Order?.CreatedAt ?? DateTime.Now;

            DeliveryOrders.Add(new DeliveryOrderViewModel
            {
                StateKey = state.Key,
                OrderId = state.Order?.Id ?? 0,
                OrderNumber = state.OrderNumber,
                ItemCount = state.Items.Count,
                TotalDisplay = $"Rs. {total / 100m:N0}",
                TotalAmount = total,
                CustomerName = state.CustomerName,
                CustomerPhone = state.CustomerPhone,
                CustomerAddress = state.CustomerAddress,
                DeliveryStatus = state.DeliveryStatus,
                DriverName = state.DriverName,
                DriverPhone = state.DriverPhone,
                OrderNote = state.OrderNote,
                IsSettled = state.IsSettled,
                OrderTime = orderTime,
                DispatchedAt = state.DispatchedAt,
                CompletedAt = state.CompletedAt
            });
        }
    }

    // ══════════════════════════════════════════════════════════
    //  TAKEAWAY TAB — management
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetTakeawayFilter(string filter)
    {
        TakeawayFilter = filter;
        RefreshTakeawayOrders();
    }

    public void RefreshTakeawayOrders()
    {
        TakeawayOrders.Clear();

        var takeawayStates = _orderStates.Values
            .Where(s => s.OrderType == OrderType.TakeAway && s.Items.Count > 0 && s.KSlipPrinted)
            .OrderByDescending(s => s.Order?.CreatedAt ?? DateTime.MinValue)
            .ToList();

        foreach (var state in takeawayStates)
        {
            if (TakeawayFilter != "All" &&
                !string.Equals(state.TakeawayStatus, TakeawayFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var subTot = state.Items.Sum(i => i.LineTotal);
            var disc = state.DiscountPercent > 0 ? (long)(subTot * (long)state.DiscountPercent / 100m) : 0L;
            var taxable = subTot - disc;
            var tax = state.TaxPercent > 0 ? (long)(taxable * (long)state.TaxPercent / 100m) : 0L;
            var total = taxable + tax;
            var orderTime = state.Order?.CreatedAt ?? DateTime.Now;

            TakeawayOrders.Add(new TakeawayOrderViewModel
            {
                StateKey = state.Key,
                OrderId = state.Order?.Id ?? 0,
                OrderNumber = state.OrderNumber,
                ItemCount = state.Items.Count,
                TotalDisplay = $"Rs. {total / 100m:N0}",
                TotalAmount = total,
                CustomerName = state.CustomerName,
                CustomerPhone = state.CustomerPhone,
                TakeawayStatus = state.TakeawayStatus,
                OrderNote = state.OrderNote,
                IsSettled = state.IsSettled,
                OrderTime = orderTime
            });
        }
    }

    [RelayCommand]
    private void SelectTakeawayOrderCard(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        SaveCurrentOrderState();
        if (_orderStates.TryGetValue(takeaway.StateKey, out var state))
        {
            RestoreOrderState(state);
        }
    }

    [RelayCommand]
    private void EditTakeawayOrder(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        SwitchToSavedOrder(OrderType.TakeAway, takeaway.OrderNumber);
    }

    [RelayCommand]
    private void TakeawayOrderNote(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        if (!_orderStates.TryGetValue(takeaway.StateKey, out var state)) return;

        var noteWindow = new Views.OrderNoteWindow(state.OrderNote);
        noteWindow.Owner = System.Windows.Application.Current.MainWindow;
        if (noteWindow.ShowDialog() == true)
        {
            state.OrderNote = noteWindow.Note;
            RefreshTakeawayOrders();
        }
    }

    [RelayCommand]
    private void MarkTakeawayReady(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        if (!_orderStates.TryGetValue(takeaway.StateKey, out var state)) return;

        state.TakeawayStatus = "Ready";
        RefreshTakeawayOrders();
        RefreshHoldOrders();
    }

    [RelayCommand]
    private void CompleteTakeaway(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        if (!_orderStates.TryGetValue(takeaway.StateKey, out var state)) return;

        state.TakeawayStatus = "Completed";
        RefreshTakeawayOrders();
        RefreshHoldOrders();
    }

    [RelayCommand]
    private async Task QuickSettleTakeawayAsync(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        if (!_orderStates.TryGetValue(takeaway.StateKey, out var state)) return;

        var settleWindow = new Views.QuickSettleWindow(takeaway.OrderNumber, takeaway.TotalAmount);
        settleWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (settleWindow.ShowDialog() == true)
        {
            try
            {
                state.IsSettled = true;

                if (state.Order != null)
                {
                    // Link customer to order (so it shows in Customer Management)
                    int? custId = state.MatchedCustomer?.Id;

                    // Save notes if any
                    var noteInfo = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(state.OrderNote))
                        noteInfo.AppendLine($"Note: {state.OrderNote}");
                    if (!string.IsNullOrEmpty(state.CustomerName))
                        noteInfo.AppendLine($"Customer: {state.CustomerName}");
                    if (!string.IsNullOrEmpty(state.CustomerPhone))
                        noteInfo.AppendLine($"Mobile: {state.CustomerPhone}");

                    var notes = noteInfo.ToString().TrimEnd();
                    if (!string.IsNullOrEmpty(notes) || custId.HasValue)
                        await _orderService.UpdateOrderNotesAsync(state.Order.Id, string.IsNullOrEmpty(notes) ? null : notes, custId);

                    await _orderService.CalculateTotalsAsync(state.Order.Id, state.DiscountPercent, state.TaxPercent);
                    await _orderService.CheckoutAsync(state.Order.Id, settleWindow.SelectedPaymentMethodId, takeaway.TotalAmount);
                }

                _orderStates.Remove(takeaway.StateKey);

                // Clear cart if this was the current order
                if (OrderNumber == takeaway.OrderNumber && SelectedOrderType == OrderType.TakeAway)
                {
                    ClearBillingSection();
                }

                RefreshTakeawayOrders();
                RefreshHoldOrders();
                await LoadBillingHistoryAsync();
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                System.Windows.MessageBox.Show(
                    $"Failed to settle takeaway order:\n{innerMsg}",
                    "Settlement Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void PrintTakeawayOrder(TakeawayOrderViewModel takeaway)
    {
        if (takeaway == null) return;
        if (!_orderStates.TryGetValue(takeaway.StateKey, out var state)) return;

        var subTotal = state.Items.Sum(i => i.LineTotal);
        var discAmt = state.DiscountPercent > 0 ? (long)(subTotal * (long)state.DiscountPercent / 100m) : 0L;
        var taxableAmt = subTotal - discAmt;
        var taxAmt = state.TaxPercent > 0 ? (long)(taxableAmt * (long)state.TaxPercent / 100m) : 0L;
        var grandTotalAmt = taxableAmt + taxAmt;

        var receiptData = new ReceiptData
        {
            RestaurantName = "KFC Restaurant",
            OrderNumber = state.OrderNumber,
            OrderType = state.OrderType.ToString(),
            CashierName = _loggedInUser?.FullName ?? "Admin",
            SubTotal = subTotal,
            TaxAmount = taxAmt,
            DiscountAmount = discAmt,
            GrandTotal = grandTotalAmt,
            CustomerName = state.CustomerName,
            CustomerPhone = state.CustomerPhone,
        };
        foreach (var oi in state.Items)
        {
            receiptData.Items.Add(new ReceiptItem
            {
                Name = oi.Name,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                Notes = oi.Remarks
            });
        }
        var previewWindow = new Views.PrintPreviewWindow(receiptData, _printService)
            { ConfiguredPrinterName = _configuredReceiptPrinter };
        previewWindow.Owner = System.Windows.Application.Current.MainWindow;
        previewWindow.ShowDialog();
    }

    [RelayCommand]
    private void SelectDeliveryOrderCard(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;
        SaveCurrentOrderState();
        if (_orderStates.TryGetValue(delivery.StateKey, out var state))
        {
            RestoreOrderState(state);
        }
    }

    [RelayCommand]
    private void EditDeliveryOrder(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;
        // Load order into billing panel for editing
        SelectDeliveryOrderCard(delivery);
    }

    [RelayCommand]
    private void DeliveryOrderNote(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;

        var noteWindow = new Views.OrderNoteWindow(delivery.OrderNote);
        noteWindow.Owner = System.Windows.Application.Current.MainWindow;
        if (noteWindow.ShowDialog() == true)
        {
            // Save note to state
            if (_orderStates.TryGetValue(delivery.StateKey, out var state))
            {
                state.OrderNote = noteWindow.Note;
            }
            RefreshDeliveryOrders();
        }
    }

    [RelayCommand]
    private void AssignDriver(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;

        var driverWindow = new Views.DriverAssignWindow(
            delivery.DriverName, delivery.DriverPhone,
            IsAdmin || _loggedInUser?.Role?.Name?.Equals("Manager", StringComparison.OrdinalIgnoreCase) == true,
            _db);
        driverWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (driverWindow.ShowDialog() == true)
        {
            if (_orderStates.TryGetValue(delivery.StateKey, out var state))
            {
                state.DriverName = driverWindow.SelectedDriverName;
                state.DriverPhone = driverWindow.SelectedDriverPhone;
                // Assign driver → auto transition to Dispatched
                state.DeliveryStatus = "Dispatched";
                state.DispatchedAt = DateTime.Now;
            }
            RefreshDeliveryOrders();
            RefreshHoldOrders();
        }
    }

    [RelayCommand]
    private void CompleteDelivery(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;

        if (_orderStates.TryGetValue(delivery.StateKey, out var state))
        {
            state.DeliveryStatus = "Completed";
            state.CompletedAt = DateTime.Now;
        }
        RefreshDeliveryOrders();
        RefreshHoldOrders();
    }

    [RelayCommand]
    private async Task QuickSettleDeliveryAsync(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;

        // Calculate total with GST (like RecalculateTotals does for main checkout)
        if (!_orderStates.TryGetValue(delivery.StateKey, out var state)) return;

        var subTotal = state.Items.Sum(i => i.LineTotal);
        var discountAmt = state.DiscountPercent > 0
            ? (long)(subTotal * (long)state.DiscountPercent / 100m)
            : 0L;
        var taxable = subTotal - discountAmt;
        var taxAmt = state.TaxPercent > 0
            ? (long)(taxable * (long)state.TaxPercent / 100m)
            : 0L;
        var grandTotal = taxable + taxAmt;

        var settleWindow = new Views.QuickSettleWindow(delivery.OrderNumber, grandTotal);
        settleWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (settleWindow.ShowDialog() == true)
        {
            try
            {
                state.IsSettled = true;

                // Process payment in DB
                if (state.Order != null)
                {
                    // Save delivery details to Order.Notes so they persist in billing history
                    var deliveryInfo = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(state.DriverName))
                        deliveryInfo.AppendLine($"Driver: {state.DriverName} ({state.DriverPhone})");
                    if (!string.IsNullOrEmpty(state.OrderNote))
                        deliveryInfo.AppendLine($"Note: {state.OrderNote}");
                    if (!string.IsNullOrEmpty(state.CustomerName))
                        deliveryInfo.AppendLine($"Customer: {state.CustomerName}");
                    if (!string.IsNullOrEmpty(state.CustomerPhone))
                        deliveryInfo.AppendLine($"Mobile: {state.CustomerPhone}");
                    if (!string.IsNullOrEmpty(state.CustomerAddress))
                        deliveryInfo.AppendLine($"Address: {state.CustomerAddress}");

                    // Link customer to order (so it shows in Customer Management)
                    int? custId = state.MatchedCustomer?.Id;
                    await _orderService.UpdateOrderNotesAsync(state.Order.Id, deliveryInfo.ToString().TrimEnd(), custId);

                    await _orderService.CalculateTotalsAsync(state.Order.Id, state.DiscountPercent, state.TaxPercent);
                    await _orderService.CheckoutAsync(state.Order.Id, settleWindow.SelectedPaymentMethodId, grandTotal);
                }

                // Remove from state store (fully closed)
                _orderStates.Remove(delivery.StateKey);

                // Clear the cart if this was the currently displayed order
                if (OrderNumber == delivery.OrderNumber && SelectedOrderType == OrderType.Delivery)
                {
                    ClearBillingSection();
                }

                RefreshDeliveryOrders();
                RefreshHoldOrders();

                // Refresh billing history so the settled order appears immediately
                await LoadBillingHistoryAsync();
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                System.Windows.MessageBox.Show(
                    $"Failed to settle delivery order:\n{innerMsg}",
                    "Settlement Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void PrintDeliveryOrder(DeliveryOrderViewModel delivery)
    {
        if (delivery == null) return;

        // Load order into billing panel and trigger print
        if (_orderStates.TryGetValue(delivery.StateKey, out var state))
        {
            var subTotal = state.Items.Sum(i => i.LineTotal);
            var discAmt = state.DiscountPercent > 0 ? (long)(subTotal * (long)state.DiscountPercent / 100m) : 0L;
            var taxableAmt = subTotal - discAmt;
            var taxAmt = state.TaxPercent > 0 ? (long)(taxableAmt * (long)state.TaxPercent / 100m) : 0L;
            var grandTotalAmt = taxableAmt + taxAmt;

            var receiptData = new ReceiptData
            {
                RestaurantName = "KFC Restaurant",
                OrderNumber = state.OrderNumber,
                OrderType = state.OrderType.ToString(),
                CashierName = _loggedInUser?.FullName ?? "Admin",
                SubTotal = subTotal,
                TaxAmount = taxAmt,
                DiscountAmount = discAmt,
                GrandTotal = grandTotalAmt,
                // Delivery-specific fields
                CustomerName = state.CustomerName,
                CustomerPhone = state.CustomerPhone,
                CustomerAddress = state.CustomerAddress,
                DeliveryNote = state.OrderNote,
                DriverName = state.DriverName,
                DriverPhone = state.DriverPhone,
            };
            foreach (var oi in state.Items)
            {
                receiptData.Items.Add(new ReceiptItem
                {
                    Name = oi.Name,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    LineTotal = oi.LineTotal,
                    Notes = oi.Remarks
                });
            }
            var previewWindow = new Views.PrintPreviewWindow(receiptData, _printService)
                { ConfiguredPrinterName = _configuredReceiptPrinter };
            previewWindow.Owner = System.Windows.Application.Current.MainWindow;
            previewWindow.ShowDialog();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  QUANTITY & REMOVE
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task IncrementQuantityAsync()
    {
        if (SelectedOrderItem == null) return;

        // Block quantity change on kitchen-printed items
        if (SelectedOrderItem.KitchenPrinted)
        {
            System.Windows.MessageBox.Show(
                "Cannot change quantity after Kitchen Slip has been printed.\nAdd a new item instead, or use 'K-Bill' to send additions.",
                "Locked", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        SelectedOrderItem.Quantity++;
        await _orderService.UpdateItemQuantityAsync(SelectedOrderItem.OrderItemId, SelectedOrderItem.Quantity);
        RecalculateTotals();
        SaveCurrentOrderState();
    }

    [RelayCommand]
    private async Task DecrementQuantityAsync()
    {
        if (SelectedOrderItem == null) return;

        // Block quantity change / removal on kitchen-printed items
        if (SelectedOrderItem.KitchenPrinted)
        {
            System.Windows.MessageBox.Show(
                "Cannot modify items after Kitchen Slip has been printed.\nUse 'Un-Paid Bill' to void the order.",
                "Locked", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (SelectedOrderItem.Quantity <= 1)
        {
            await _orderService.RemoveItemFromOrderAsync(SelectedOrderItem.OrderItemId, null);
            OrderItems.Remove(SelectedOrderItem);
            RenumberItems();
        }
        else
        {
            SelectedOrderItem.Quantity--;
            await _orderService.UpdateItemQuantityAsync(SelectedOrderItem.OrderItemId, SelectedOrderItem.Quantity);
        }
        RecalculateTotals();

        if (OrderItems.Count == 0)
            CleanupEmptyOrder();
        else
            SaveCurrentOrderState();

        RefreshHoldOrders();
    }

    [RelayCommand]
    private async Task RemoveItemAsync()
    {
        if (SelectedOrderItem == null) return;

        // Block removal of kitchen-printed items
        if (SelectedOrderItem.KitchenPrinted)
        {
            System.Windows.MessageBox.Show(
                "Cannot delete items after Kitchen Slip has been printed.\nUse 'Un-Paid Bill' to void the order.",
                "Locked", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        await _orderService.RemoveItemFromOrderAsync(SelectedOrderItem.OrderItemId, "Removed by user");
        OrderItems.Remove(SelectedOrderItem);
        RenumberItems();
        RecalculateTotals();

        if (OrderItems.Count == 0)
            CleanupEmptyOrder();
        else
            SaveCurrentOrderState();

        RefreshHoldOrders();
    }

    [RelayCommand]
    private async Task DeleteOrderAsync()
    {
        if (SelectedOrderItem != null)
        {
            await RemoveItemAsync();
        }
    }

    /// <summary>
    /// Remove order from state store and reset table when cart becomes empty.
    /// </summary>
    private void CleanupEmptyOrder()
    {
        if (SelectedTable != null)
        {
            var key = $"table-{SelectedTable.Id}";
            _orderStates.Remove(key);
            SelectedTable.Status = TableStatus.Available;
            var tbl = SelectedTable;
            SelectedTable = null;
            RefreshTableInList(tbl);
        }
        else if (!string.IsNullOrEmpty(OrderNumber))
        {
            var key = $"{SelectedOrderType}-{OrderNumber}";
            _orderStates.Remove(key);
        }

        ClearBillingSection();
        SelectedOrderType = OrderType.DineIn;
        OnPropertyChanged(nameof(OrderTypeDisplay));
        OnPropertyChanged(nameof(TableDisplay));
    }

    private void RenumberItems()
    {
        for (int i = 0; i < OrderItems.Count; i++)
            OrderItems[i].SerialNumber = i + 1;
    }

    // ══════════════════════════════════════════════════════════
    //  TOTALS CALCULATION
    // ══════════════════════════════════════════════════════════

    private void RecalculateTotals()
    {
        SubTotal = OrderItems.Sum(oi => oi.LineTotal);

        if (DiscountPercent > 0)
        {
            DiscountAmount = (long)(SubTotal * (long)DiscountPercent / 100m);
            DiscountRs = DiscountAmount / 100m;
        }
        else if (DiscountRs > 0)
        {
            DiscountAmount = (long)(DiscountRs * 100);
        }
        else
        {
            DiscountAmount = 0;
        }

        var taxable = SubTotal - DiscountAmount;

        if (TaxPercent > 0)
        {
            TaxAmount = (long)(taxable * (long)TaxPercent / 100m);
            GstRs = TaxAmount / 100m;
        }
        else if (GstRs > 0)
        {
            TaxAmount = (long)(GstRs * 100);
        }
        else
        {
            TaxAmount = 0;
        }

        GrandTotal = taxable + TaxAmount + ServiceCharge + Adjustment;

        NotifyAllDisplayProperties();
        CalculateChange();
    }

    private void NotifyAllDisplayProperties()
    {
        OnPropertyChanged(nameof(SubTotalDisplay));
        OnPropertyChanged(nameof(DiscountDisplay));
        OnPropertyChanged(nameof(TaxDisplay));
        OnPropertyChanged(nameof(ServiceChargeDisplay));
        OnPropertyChanged(nameof(GrandTotalDisplay));
        OnPropertyChanged(nameof(AfterDiscountDisplay));
        OnPropertyChanged(nameof(AfterGSTDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(PayDisplay));
    }

    partial void OnDiscountPercentChanged(decimal value) => RecalculateTotals();
    partial void OnDiscountRsChanged(decimal value)
    {
        if (DiscountPercent == 0) RecalculateTotals();
    }
    partial void OnTaxPercentChanged(decimal value) => RecalculateTotals();
    partial void OnGstRsChanged(decimal value)
    {
        if (TaxPercent == 0) RecalculateTotals();
    }
    partial void OnAdjustmentChanged(long value) => RecalculateTotals();

    partial void OnCashTenderedChanged(long value) => CalculateChange();

    partial void OnSelectedOrderTypeChanged(OrderType value)
    {
        OnPropertyChanged(nameof(OrderTypeDisplay));
    }

    partial void OnSelectedTableChanged(Table? value)
    {
        OnPropertyChanged(nameof(TableDisplay));
    }

    private void CalculateChange()
    {
        ChangeAmount = CashTendered > GrandTotal ? CashTendered - GrandTotal : 0;
        OnPropertyChanged(nameof(ChangeDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(PayDisplay));
    }

    // ══════════════════════════════════════════════════════════
    //  ORDER TYPE BUTTONS (Delivery / Takeaway)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void SetOrderType(string type)
    {
        // Save current order before switching type
        SaveCurrentOrderState();

        var newType = type switch
        {
            "DineIn" => OrderType.DineIn,
            "TakeAway" => OrderType.TakeAway,
            "Delivery" => OrderType.Delivery,
            _ => OrderType.DineIn
        };

        // Clear billing for fresh order of new type
        ClearBillingSection();
        SelectedOrderType = newType;

        // If switching to DineIn, table must be selected first (don't auto-clear)
        if (newType != OrderType.DineIn)
            SelectedTable = null;
    }

    // ══════════════════════════════════════════════════════════
    //  CHECKOUT
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (_currentOrder == null || OrderItems.Count == 0) return;

        // Delivery/Takeaway orders must be closed from their own sections
        if (SelectedOrderType == OrderType.Delivery)
        {
            IsDeliveryMaximized = true;
            return;
        }
        if (SelectedOrderType == OrderType.TakeAway)
        {
            IsTakeawayMaximized = true;
            return;
        }

        IsBusy = true;
        try
        {
            // Link customer to order for ALL order types (so it shows in Customer Management)
            if (_matchedCustomer != null)
            {
                await _orderService.UpdateOrderNotesAsync(_currentOrder.Id, null, _matchedCustomer.Id);
            }

            // For delivery orders, save delivery info to Order.Notes before checkout
            if (SelectedOrderType == OrderType.Delivery)
            {
                var stKey = $"{SelectedOrderType}-{OrderNumber}";
                if (_orderStates.TryGetValue(stKey, out var dState))
                {
                    var dInfo = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(dState.DriverName))
                        dInfo.AppendLine($"Driver: {dState.DriverName} ({dState.DriverPhone})");
                    if (!string.IsNullOrEmpty(dState.OrderNote))
                        dInfo.AppendLine($"Note: {dState.OrderNote}");
                    if (!string.IsNullOrEmpty(CustomerName))
                        dInfo.AppendLine($"Customer: {CustomerName}");
                    if (!string.IsNullOrEmpty(CustomerPhone))
                        dInfo.AppendLine($"Mobile: {CustomerPhone}");

                    int? custId = _matchedCustomer?.Id;
                    await _orderService.UpdateOrderNotesAsync(_currentOrder.Id, dInfo.ToString().TrimEnd(), custId);
                }
            }

            await _orderService.CalculateTotalsAsync(_currentOrder.Id, DiscountPercent, TaxPercent);
            var tendered = CashTendered > 0 ? CashTendered : GrandTotal;
            await _orderService.CheckoutAsync(_currentOrder.Id, 1, tendered);

            // Remove from state store
            var stateKey = SelectedOrderType == OrderType.DineIn
                ? $"table-{SelectedTable?.Id}"
                : $"{SelectedOrderType}-{OrderNumber}";
            _orderStates.Remove(stateKey);

            // Reset table to Available (YELLOW) on checkout
            if (SelectedTable != null)
            {
                SelectedTable.Status = TableStatus.Available;
                var checkoutTable = SelectedTable;
                // Clear selection first so blue highlight goes away
                SelectedTable = null;
                RefreshTableInList(checkoutTable);
            }

            // Remove from Delivery/Takeaway tab lists
            RefreshDeliveryOrders();
            RefreshTakeawayOrders();

            // Clear billing for next order
            ClearBillingSection();
            RefreshHoldOrders();

            // Refresh billing history so the closed order appears immediately
            await LoadBillingHistoryAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  NEW ORDER (F1)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void NewOrder()
    {
        // Save current order state if exists
        SaveCurrentOrderState();

        // Deselect table (refresh so blue goes away)
        var prevTable = SelectedTable;
        ClearBillingSection();
        SelectedTable = null;
        SelectedOrderType = OrderType.DineIn;
        if (prevTable != null) RefreshTableInList(prevTable);
    }

    /// <summary>
    /// Clear cart: only works if K-slip has NOT been printed.
    /// Once K-slip is printed, cart can only be cleared via Checkout or Un-Paid Bill.
    /// </summary>
    [RelayCommand]
    private void ClearCart()
    {
        if (OrderItems.Count == 0) return;

        // Block if any item has been kitchen-printed
        if (OrderItems.Any(oi => oi.KitchenPrinted))
        {
            System.Windows.MessageBox.Show(
                "Cannot clear cart after Kitchen Slip has been printed.\nUse 'Checkout' or 'Un-Paid Bill' instead.",
                "Clear Cart Blocked",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Remove saved order state for this table/order
        if (SelectedTable != null)
        {
            var key = $"table-{SelectedTable.Id}";
            _orderStates.Remove(key);

            // Reset table back to Available (yellow)
            SelectedTable.Status = TableStatus.Available;
            var tbl = SelectedTable;
            SelectedTable = null;
            RefreshTableInList(tbl);
        }
        else if (!string.IsNullOrEmpty(OrderNumber))
        {
            var key = $"{SelectedOrderType}-{OrderNumber}";
            _orderStates.Remove(key);
        }

        ClearBillingSection();
        SelectedOrderType = OrderType.DineIn;

        OnPropertyChanged(nameof(OrderTypeDisplay));
        OnPropertyChanged(nameof(TableDisplay));
        RefreshHoldOrders();
    }

    /// <summary>
    /// Un-Paid Bill: requires admin/manager authorization, opens a reason window, then clears the order.
    /// Used when K-slip is already printed but customer doesn't pay.
    /// </summary>
    [RelayCommand]
    private async Task UnPaidBillAsync()
    {
        if (OrderItems.Count == 0) return;

        // Only require admin/manager authorization if current user is Cashier
        if (_authService == null) return;
        bool isCashier = !_authService.HasPermission("Void / cancel orders", minimumLevel: 5);
        string authorizedBy;
        if (isCashier)
        {
            var authWindow = new Views.ManagerAuthWindow(_authService);
            authWindow.Owner = System.Windows.Application.Current.MainWindow;
            if (authWindow.ShowDialog() != true)
                return;
            authorizedBy = authWindow.AuthorizedBy;
        }
        else
        {
            authorizedBy = _loggedInUser?.FullName ?? _loggedInUser?.Username ?? "Admin";
        }

        // Open the UnPaid Bill reason window
        var reasonWindow = new Views.UnPaidBillWindow();
        reasonWindow.Owner = System.Windows.Application.Current.MainWindow;
        var result = reasonWindow.ShowDialog();

        if (result != true || string.IsNullOrWhiteSpace(reasonWindow.Reason))
            return;

        var reason = $"{reasonWindow.Reason} [Authorized by: {authorizedBy}]";

        // Mark as Void in database with reason
        if (_currentOrder != null)
        {
            await _orderService.VoidOrderAsync(_currentOrder.Id, $"Un-Paid: {reason}", _loggedInUser?.Id ?? 1);
        }

        // Remove saved order state
        if (SelectedTable != null)
        {
            var key = $"table-{SelectedTable.Id}";
            _orderStates.Remove(key);

            SelectedTable.Status = TableStatus.Available;
            var tbl = SelectedTable;
            SelectedTable = null;
            RefreshTableInList(tbl);
        }
        else if (!string.IsNullOrEmpty(OrderNumber))
        {
            var key = $"{SelectedOrderType}-{OrderNumber}";
            _orderStates.Remove(key);
        }

        ClearBillingSection();
        SelectedOrderType = OrderType.DineIn;

        OnPropertyChanged(nameof(OrderTypeDisplay));
        OnPropertyChanged(nameof(TableDisplay));
        RefreshHoldOrders();
        RefreshDeliveryOrders();
        RefreshTakeawayOrders();

        System.Windows.MessageBox.Show(
            $"Order marked as Un-Paid.\nReason: {reason}",
            "Un-Paid Bill",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private async Task LoadTablesAsync()
    {
        var tables = await _tableService.GetTablesAsync();
        // Preserve table statuses from state store
        foreach (var table in tables)
        {
            var key = $"table-{table.Id}";
            if (_orderStates.ContainsKey(key))
            {
                // Table has an active order — keep its non-Available status
                var state = _orderStates[key];
                if (state.Items.Count > 0)
                {
                    // Check if any items were kitchen-printed
                    bool anyKitchenPrinted = state.Items.Any(i => i.KitchenPrinted);
                    table.Status = anyKitchenPrinted ? TableStatus.Reserved : TableStatus.Occupied;
                }
            }
        }
        // Sort: simple tables first, family tables last
        var sorted = tables
            .OrderBy(t => t.Name.Contains("Family", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(t => t.DisplayOrder)
            .ToList();

        Tables.Clear();
        foreach (var table in sorted)
            Tables.Add(table);
    }

    [RelayCommand]
    private async Task AddQuickTableAsync()
    {
        // Cashier needs admin/manager authorization to add tables
        if (_authService != null && !_authService.HasPermission("Manage tables & sessions", minimumLevel: 5))
        {
            var authWindow = new Views.ManagerAuthWindow(_authService);
            authWindow.Owner = System.Windows.Application.Current.MainWindow;
            if (authWindow.ShowDialog() != true)
                return;
        }

        var floors = await _db.FloorPlans.Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToListAsync();
        var dlg = new AddTableWindow(floors) { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            _db.Tables.Add(new Table
            {
                Name = dlg.TableName,
                FloorPlanId = dlg.SelectedFloor!.Id,
                Capacity = dlg.Capacity,
                Shape = dlg.SelectedShape,
                DisplayOrder = dlg.TableDisplayOrder,
                Status = TableStatus.Available
            });
            await _db.SaveChangesAsync();
            await LoadTablesAsync();
        }
    }

    /// <summary>
    /// Called by source generator whenever CustomerPhone changes.
    /// Searches customers as digits are typed.
    /// </summary>
    partial void OnCustomerNameChanged(string value)
    {
        if (_isRestoringState) return;

        // When customer name changes (e.g. from phone lookup), save state and refresh
        if (_currentOrder != null)
        {
            SaveCurrentOrderState();
            RefreshHoldOrders();
            if (SelectedOrderType == OrderType.Delivery)
                RefreshDeliveryOrders();
            else if (SelectedOrderType == OrderType.TakeAway)
                RefreshTakeawayOrders();
        }
    }

    partial void OnCustomerPhoneChanged(string value)
    {
        if (_isRestoringState) return;
        _ = SearchCustomerByPhoneAsync(value);
    }

    private async Task SearchCustomerByPhoneAsync(string phone)
    {
        PhoneSearchResults.Clear();

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 2)
        {
            IsPhoneMatched = false;
            IsPhoneSearchActive = false;
            IsPhoneNoResults = false;
            _matchedCustomer = null;
            CustomerName = string.Empty;
            return;
        }

        var results = await _customerService.SearchCustomersAsync(phone);
        var list = results.ToList();

        PhoneSearchResults.Clear();
        foreach (var c in list)
            PhoneSearchResults.Add(c);

        // Check for exact match
        var exact = list.FirstOrDefault(c => c.Phone == phone);
        if (exact != null)
        {
            // Exact match → green, no dropdown
            _matchedCustomer = exact;
            IsPhoneMatched = true;
            IsPhoneSearchActive = false;
            IsPhoneNoResults = false;
            CustomerName = exact.Name;

            // Immediately link customer to current order in DB
            if (_currentOrder != null && _currentOrder.CustomerId != exact.Id)
            {
                _currentOrder.CustomerId = exact.Id;
                _ = _orderService.UpdateOrderNotesAsync(_currentOrder.Id, _currentOrder.Notes, exact.Id);
            }
        }
        else if (list.Count > 0)
        {
            // Partial matches found → green text, show dropdown
            _matchedCustomer = null;
            IsPhoneMatched = false;
            IsPhoneSearchActive = true;
            IsPhoneNoResults = false;
            CustomerName = string.Empty;
        }
        else
        {
            // No matches at all → red text, no dropdown
            _matchedCustomer = null;
            IsPhoneMatched = false;
            IsPhoneSearchActive = false;
            IsPhoneNoResults = true;
            CustomerName = string.Empty;
        }
    }

    /// <summary>
    /// Select a customer from the dropdown list.
    /// </summary>
    [RelayCommand]
    private void SelectCustomerFromSearch(Customer customer)
    {
        _matchedCustomer = customer;
        CustomerPhone = customer.Phone;
        CustomerName = customer.Name;
        IsPhoneMatched = true;
        IsPhoneSearchActive = false;
        IsPhoneNoResults = false;
        PhoneSearchResults.Clear();

        // Immediately link customer to current order in DB
        if (_currentOrder != null && _currentOrder.CustomerId != customer.Id)
        {
            _currentOrder.CustomerId = customer.Id;
            _ = _orderService.UpdateOrderNotesAsync(_currentOrder.Id, _currentOrder.Notes, customer.Id);
        }
    }

    /// <summary>
    /// Called when Enter is pressed in the mobile field.
    /// Green = select customer. Red = open AddCustomer form.
    /// </summary>
    [RelayCommand]
    private async Task PhoneEnterPressedAsync()
    {
        if (IsPhoneMatched && _matchedCustomer != null)
        {
            // Matched — for delivery/takeaway, show customer details with edit ability
            if (SelectedOrderType == OrderType.Delivery || SelectedOrderType == OrderType.TakeAway)
            {
                var addr = _matchedCustomer.Addresses?.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                           ?? _matchedCustomer.Addresses?.FirstOrDefault()?.AddressLine1 ?? "";
                var detailWindow = new Views.AddCustomerWindow(
                    _matchedCustomer.Name, _matchedCustomer.Phone,
                    _matchedCustomer.Email ?? "", addr);
                detailWindow.Owner = System.Windows.Application.Current.MainWindow;

                if (detailWindow.ShowDialog() == true)
                {
                    // Update customer in DB if changed
                    _matchedCustomer.Name = detailWindow.CustomerName;
                    _matchedCustomer.Phone = detailWindow.CustomerPhone;
                    _matchedCustomer.Email = string.IsNullOrWhiteSpace(detailWindow.CustomerEmail) ? null : detailWindow.CustomerEmail;
                    var existingAddr = _matchedCustomer.Addresses?.FirstOrDefault(a => a.IsDefault)
                                       ?? _matchedCustomer.Addresses?.FirstOrDefault();
                    if (existingAddr != null)
                        existingAddr.AddressLine1 = detailWindow.CustomerAddress;
                    else if (!string.IsNullOrWhiteSpace(detailWindow.CustomerAddress))
                    {
                        _matchedCustomer.Addresses ??= [];
                        _matchedCustomer.Addresses.Add(new CustomerAddress { Label = "Primary", AddressLine1 = detailWindow.CustomerAddress, IsDefault = true });
                    }

                    await _customerService.UpdateCustomerAsync(_matchedCustomer);
                    CustomerName = _matchedCustomer.Name;
                    CustomerPhone = _matchedCustomer.Phone;

                    // Link customer to current order
                    if (_currentOrder != null)
                    {
                        _currentOrder.CustomerId = _matchedCustomer.Id;
                        await _orderService.UpdateOrderNotesAsync(_currentOrder.Id, _currentOrder.Notes, _matchedCustomer.Id);
                    }
                }
            }

            IsPhoneSearchActive = false;
            PhoneSearchResults.Clear();
            return;
        }

        // Not matched — open Add Customer window
        var addWindow = new Views.AddCustomerWindow(CustomerPhone);
        addWindow.Owner = System.Windows.Application.Current.MainWindow;

        if (addWindow.ShowDialog() == true)
        {
            // Create customer in DB
            var newCustomer = await _customerService.CreateCustomerAsync(
                addWindow.CustomerName,
                addWindow.CustomerPhone,
                string.IsNullOrWhiteSpace(addWindow.CustomerEmail) ? null : addWindow.CustomerEmail,
                string.IsNullOrWhiteSpace(addWindow.CustomerAddress) ? null : addWindow.CustomerAddress);

            // Set matched state
            _matchedCustomer = newCustomer;
            CustomerPhone = newCustomer.Phone;
            CustomerName = newCustomer.Name;
            IsPhoneMatched = true;
            IsPhoneSearchActive = false;
            PhoneSearchResults.Clear();

            // Immediately link to current order
            if (_currentOrder != null && newCustomer != null)
            {
                _currentOrder.CustomerId = newCustomer.Id;
                await _orderService.UpdateOrderNotesAsync(_currentOrder.Id, _currentOrder.Notes, newCustomer.Id);
            }
        }
    }

    [RelayCommand]
    private async Task LookupCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerPhone)) return;
        var customer = await _customerService.GetByPhoneAsync(CustomerPhone);
        if (customer != null)
        {
            CustomerName = customer.Name;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PRINTING — Bill Print
    // ══════════════════════════════════════════════════════════

    public ReceiptData BuildReceiptData()
    {
        var paymentMethod = IsCash ? "Cash" :
                            IsCardCredit ? "Card/Credit" :
                            IsOnlinePayment ? "Online Payment" :
                            IsCOD ? "COD" : "Cash";

        var data = new ReceiptData
        {
            RestaurantName = _receiptRestaurantName,
            RestaurantAddress = _receiptAddress,
            RestaurantPhone = _receiptPhone,
            OrderNumber = OrderNumber,
            DateTime = DateTime.Now,
            TableName = SelectedTable?.Name,
            OrderType = OrderTypeDisplay,
            CashierName = _loggedInUser?.FullName ?? "Admin",
            SubTotal = SubTotal,
            TaxAmount = TaxAmount,
            DiscountAmount = DiscountAmount,
            ServiceCharge = ServiceCharge,
            GrandTotal = GrandTotal,
            PaymentMethod = paymentMethod,
            TenderedAmount = CashTendered > 0 ? CashTendered : GrandTotal,
            ChangeAmount = CashTendered > 0 ? ChangeAmount : 0,
            HeaderMessage = !string.IsNullOrWhiteSpace(_receiptHeader) ? _receiptHeader : null,
            FooterMessage = !string.IsNullOrWhiteSpace(CommentText) ? CommentText
                          : !string.IsNullOrWhiteSpace(_receiptFooter) ? _receiptFooter : null
        };

        // Add delivery-specific info if this is a delivery order
        if (SelectedOrderType == OrderType.Delivery)
        {
            data.CustomerName = CustomerName;
            data.CustomerPhone = CustomerPhone;
            data.CustomerAddress = _matchedCustomer?.Addresses?.FirstOrDefault(a => a.IsDefault)?.AddressLine1
                                   ?? _matchedCustomer?.Addresses?.FirstOrDefault()?.AddressLine1;

            var stKey = $"{SelectedOrderType}-{OrderNumber}";
            if (_orderStates.TryGetValue(stKey, out var dState))
            {
                data.DriverName = dState.DriverName;
                data.DriverPhone = dState.DriverPhone;
                data.DeliveryNote = dState.OrderNote;
                if (string.IsNullOrEmpty(data.CustomerAddress))
                    data.CustomerAddress = dState.CustomerAddress;
            }
        }

        foreach (var oi in OrderItems)
        {
            data.Items.Add(new ReceiptItem
            {
                Name = oi.Name,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                LineTotal = oi.LineTotal,
                Notes = string.IsNullOrWhiteSpace(oi.Remarks) ? null : oi.Remarks
            });

            // Expand deal components on customer receipt
            if (oi.DealComponents.Count > 0)
            {
                foreach (var dc in oi.DealComponents)
                {
                    data.Items.Add(new ReceiptItem
                    {
                        Name = $"   {dc.ItemName} ×{dc.Qty}",
                        Quantity = 0,      // 0 = sub-item, no separate qty column
                        UnitPrice = 0,
                        LineTotal = 0,
                        Notes = null
                    });
                }
            }
        }

        return data;
    }

    /// <summary>
    /// Validate that mobile number is provided and matched for Delivery/Takeaway orders.
    /// </summary>
    private bool ValidateMobileForOrderType()
    {
        if (SelectedOrderType == OrderType.Delivery || SelectedOrderType == OrderType.TakeAway)
        {
            if (string.IsNullOrWhiteSpace(CustomerPhone) || !IsPhoneMatched)
            {
                System.Windows.MessageBox.Show(
                    "Mobile number is required for Delivery/Takeaway orders.\nPlease enter a valid customer mobile number.",
                    "Customer Required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }
        }
        return true;
    }

    [RelayCommand]
    private async Task BillPrintAsync()
    {
        if (OrderItems.Count == 0)
        {
            System.Windows.MessageBox.Show("No items in current order to print.",
                "Print", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!ValidateMobileForOrderType()) return;

        var receiptData = BuildReceiptData();

        // If K-Bill checkbox is checked, show COMBINED preview (customer + kitchen in one window)
        if (IsKBill)
        {
            var kotItems = await BuildKitchenItemsListAsync(forceAll: true);
            if (kotItems.Count > 0)
            {
                var kitchenData = BuildKitchenReceiptData(kotItems, isFullReprint: false);
                var combinedPreview = new Views.PrintPreviewWindow(receiptData, kitchenData, _printService)
                    { ConfiguredPrinterName = _configuredReceiptPrinter };
                combinedPreview.ShowDialog();
                MarkItemsAsKitchenPrinted();
                // Table → Green (kitchen printed)
                if (SelectedTable != null && SelectedOrderType == OrderType.DineIn)
                {
                    SelectedTable.Status = TableStatus.Reserved;
                    RefreshTableInList(SelectedTable);
                }
                SaveCurrentOrderState();
                RefreshHoldOrders();
                return;
            }
        }

        var previewWindow = new Views.PrintPreviewWindow(receiptData, _printService)
            { ConfiguredPrinterName = _configuredReceiptPrinter };
        previewWindow.ShowDialog();
    }

    // ══════════════════════════════════════════════════════════
    //  PRINTING — K-Bill (Kitchen Order Ticket)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task KBillAsync()
    {
        if (OrderItems.Count == 0)
        {
            System.Windows.MessageBox.Show("No items in current order.",
                "K-Bill", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!ValidateMobileForOrderType()) return;

        bool fullReprint = IsKFull;
        var kotItems = await BuildKitchenItemsListAsync(forceAll: fullReprint);

        if (kotItems.Count == 0)
        {
            System.Windows.MessageBox.Show("No new items to send to kitchen.\nCheck 'K-Full' to reprint entire order.",
                "K-Bill", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var kitchenData = BuildKitchenReceiptData(kotItems, isFullReprint: fullReprint);
        var kitchenPreview = new Views.PrintPreviewWindow(kitchenData, _printService, isKitchenSlip: true)
            { ConfiguredPrinterName = _configuredKotPrinter ?? _configuredReceiptPrinter };
        kitchenPreview.ShowDialog();

        // After print, mark items as kitchen-printed
        MarkItemsAsKitchenPrinted();

        // Table → Green (Reserved = K-Bill printed, order in kitchen)
        if (SelectedTable != null && SelectedOrderType == OrderType.DineIn)
        {
            SelectedTable.Status = TableStatus.Reserved;
            RefreshTableInList(SelectedTable);
        }

        // Save updated state (mark K-slip as printed)
        SaveCurrentOrderState();

        // Mark KSlipPrinted on the saved state
        var stateKey = SelectedOrderType == OrderType.DineIn
            ? $"table-{SelectedTable?.Id}"
            : $"{SelectedOrderType}-{OrderNumber}";
        if (_orderStates.TryGetValue(stateKey, out var savedState))
            savedState.KSlipPrinted = true;

        RefreshHoldOrders();

        // For Delivery/Takeaway: push order into management tab and clear billing for next order
        if (SelectedOrderType == OrderType.Delivery)
        {
            RefreshDeliveryOrders();
            ClearBillingSection();
        }
        else if (SelectedOrderType == OrderType.TakeAway)
        {
            RefreshTakeawayOrders();
            ClearBillingSection();
        }

        if (IsKFull) IsKFull = false;
    }

    private async Task<List<KitchenPrintItem>> BuildKitchenItemsListAsync(bool forceAll)
    {
        // Pre-load all active deals with their component items for expansion
        var deals = await _db.Deals
            .Include(d => d.Items).ThenInclude(di => di.MenuItem)
            .Where(d => d.IsActive)
            .ToListAsync();

        var result = new List<KitchenPrintItem>();

        foreach (var oi in OrderItems)
        {
            int printQty;
            bool isAdditional = false;

            if (forceAll)
            {
                printQty = oi.Quantity;
            }
            else if (!oi.KitchenPrinted)
            {
                printQty = oi.Quantity;
            }
            else if (oi.Quantity > oi.KitchenPrintedQty)
            {
                printQty = oi.Quantity - oi.KitchenPrintedQty;
                isAdditional = true;
            }
            else continue;

            // Check if this item is a deal — expand into component items
            var deal = deals.FirstOrDefault(d =>
                d.Name.Equals(oi.Name, StringComparison.OrdinalIgnoreCase));

            if (deal != null && deal.Items.Count > 0)
            {
                // Add deal header with indent
                result.Add(new KitchenPrintItem
                {
                    Name = $"   ── {oi.Name} ──",
                    Quantity = printQty,
                    Remarks = oi.Remarks,
                    IsAdditional = isAdditional
                });

                // Expand: each deal component × order quantity, indented
                foreach (var di in deal.Items)
                {
                    result.Add(new KitchenPrintItem
                    {
                        Name = $"      {di.MenuItem.Name}",
                        Quantity = di.Quantity * printQty,
                        Remarks = string.Empty,
                        IsAdditional = isAdditional
                    });
                }
            }
            else
            {
                // Regular item — print as-is
                result.Add(new KitchenPrintItem
                {
                    Name = oi.Name,
                    Quantity = printQty,
                    Remarks = oi.Remarks,
                    IsAdditional = isAdditional
                });
            }
        }

        return result;
    }

    private void MarkItemsAsKitchenPrinted()
    {
        foreach (var oi in OrderItems)
        {
            oi.KitchenPrinted = true;
            oi.KitchenPrintedQty = oi.Quantity;
        }
    }

    private ReceiptData BuildKitchenReceiptData(List<KitchenPrintItem> items, bool isFullReprint)
    {
        var data = new ReceiptData
        {
            RestaurantName = _receiptRestaurantName,
            RestaurantAddress = _receiptAddress,
            RestaurantPhone = _receiptPhone,
            OrderNumber = OrderNumber,
            DateTime = DateTime.Now,
            TableName = SelectedTable?.Name,
            OrderType = OrderTypeDisplay,
            CashierName = _loggedInUser?.FullName ?? "Admin",
            SubTotal = 0,
            TaxAmount = 0,
            DiscountAmount = 0,
            GrandTotal = 0,
            PaymentMethod = "",
            TenderedAmount = 0,
            ChangeAmount = 0,
            HeaderMessage = isFullReprint ? "*** FULL REPRINT ***" : null,
            FooterMessage = null
        };

        foreach (var ki in items)
        {
            data.Items.Add(new ReceiptItem
            {
                Name = ki.Name,
                Quantity = ki.Quantity,
                UnitPrice = 0,
                LineTotal = 0,
                Notes = ki.IsAdditional
                    ? $"(+{ki.Quantity} EXTRA)" + (string.IsNullOrWhiteSpace(ki.Remarks) ? "" : $" {ki.Remarks}")
                    : (string.IsNullOrWhiteSpace(ki.Remarks) ? null : ki.Remarks)
            });
        }

        return data;
    }

    // ══════════════════════════════════════════════════════════
    //  BILLING HISTORY — Load / Search / Reprint
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadBillingHistoryAsync()
    {
        try
        {
            // Role-based: Admin/Manager see all, Cashier sees only own orders
            int? cashierFilter = IsAdminOrManager ? null : _loggedInUser?.Id;

            var orders = await _orderService.GetBillingHistoryAsync(
                BillingFromDate, BillingToDate, cashierFilter);

            var list = orders.ToList();

            // Apply text search filter if provided
            if (!string.IsNullOrWhiteSpace(BillingSearchText))
            {
                var search = BillingSearchText.ToLowerInvariant();
                list = list.Where(o =>
                    o.OrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (o.Customer?.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                    (o.Customer?.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                    (o.Cashier?.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                ).ToList();
            }

            // Retroactively link orders that have customer phone in notes but no CustomerId
            var unlinked = list.Where(o => o.CustomerId == null && !string.IsNullOrEmpty(o.Notes) && o.Notes.Contains("Mobile:")).ToList();
            if (unlinked.Count > 0)
            {
                foreach (var order in unlinked)
                {
                    // Extract phone from "Mobile: xxx" line in notes
                    var mobileLine = order.Notes!.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Mobile:"));
                    if (mobileLine != null)
                    {
                        var phone = mobileLine.Substring(mobileLine.IndexOf(':') + 1).Trim();
                        if (!string.IsNullOrEmpty(phone))
                        {
                            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == phone);
                            if (customer != null)
                            {
                                order.CustomerId = customer.Id;
                                order.Customer = customer;
                            }
                        }
                    }
                }
                await _db.SaveChangesAsync();
            }

            BillingHistory.Clear();
            foreach (var order in list)
                BillingHistory.Add(order);

            // Summary
            var totalOrders = list.Count;
            var totalRevenue = list.Where(o => o.Status == OrderStatus.Closed).Sum(o => o.GrandTotal);
            var paidCount = list.Count(o => o.Status == OrderStatus.Closed);
            var voidCount = list.Count(o => o.Status == OrderStatus.Void);
            BillingSummary = $"Orders: {totalOrders}  |  Paid: {paidCount}  |  Void: {voidCount}  |  Revenue: Rs. {totalRevenue / 100m:N2}";
        }
        catch (Exception ex)
        {
            BillingSummary = $"Error loading history: {ex.Message}";
        }
    }

    partial void OnBillingFromDateChanged(DateTime value) => _ = LoadBillingHistoryAsync();
    partial void OnBillingToDateChanged(DateTime value) => _ = LoadBillingHistoryAsync();

    [RelayCommand]
    private async Task SearchBillingHistoryAsync()
    {
        await LoadBillingHistoryAsync();
    }

    [RelayCommand]
    private void ReprintBill(Order order)
    {
        if (order == null) return;

        var receiptData = new ReceiptData
        {
            RestaurantName = _receiptRestaurantName,
            RestaurantAddress = _receiptAddress,
            RestaurantPhone = _receiptPhone,
            OrderNumber = order.OrderNumber,
            DateTime = order.CreatedAt.ToLocalTime(),
            TableName = order.TableSession?.Table?.Name,
            OrderType = order.OrderType.ToString(),
            CashierName = order.Cashier?.FullName ?? "Unknown",
            SubTotal = order.SubTotal,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            ServiceCharge = order.ServiceCharge,
            GrandTotal = order.GrandTotal,
            PaymentMethod = order.Payments.FirstOrDefault()?.PaymentMethod?.Name ?? "Cash",
            TenderedAmount = order.Payments.FirstOrDefault()?.TenderedAmount ?? order.GrandTotal,
            ChangeAmount = order.Payments.FirstOrDefault()?.ChangeAmount ?? 0,
            HeaderMessage = "*** REPRINT ***"
        };

        // Delivery info from Customer and Order.Notes
        if (order.OrderType == OrderType.Delivery)
        {
            receiptData.CustomerName = order.Customer?.Name;
            receiptData.CustomerPhone = order.Customer?.Phone;

            // Parse driver/note info from Order.Notes (saved at checkout)
            if (!string.IsNullOrEmpty(order.Notes))
            {
                foreach (var line in order.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Driver:"))
                        receiptData.DriverName = trimmed["Driver:".Length..].Trim();
                    else if (trimmed.StartsWith("Note:"))
                        receiptData.DeliveryNote = trimmed["Note:".Length..].Trim();
                }
            }
        }

        foreach (var oi in order.OrderItems)
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
            { ConfiguredPrinterName = _configuredReceiptPrinter };
        previewWindow.Owner = System.Windows.Application.Current.MainWindow;
        previewWindow.ShowDialog();
    }
}

// ══════════════════════════════════════════════════════════
//  HELPER CLASSES
// ══════════════════════════════════════════════════════════

public class KitchenPrintItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public bool IsAdditional { get; set; }
}

public class OrderSummaryViewModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string TotalDisplay { get; set; } = "Rs. 0.00";
    public string Status { get; set; } = "Open";
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
}

public class DeliveryOrderViewModel
{
    public string StateKey { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string TotalDisplay { get; set; } = "Rs. 0";
    public long TotalAmount { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "Preparing";
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    public string OrderNote { get; set; } = string.Empty;
    public bool IsSettled { get; set; }
    public DateTime OrderTime { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Display helpers
    public string StatusBadgeColor => DeliveryStatus switch
    {
        "Preparing" => "#FF9800",   // Orange
        "Dispatched" => "#1976D2",  // Blue
        "Completed" => "#4CAF50",   // Green
        _ => "#9E9E9E"
    };

    public string TimeAgo
    {
        get
        {
            var elapsed = DateTime.Now - OrderTime;
            if (elapsed.TotalMinutes < 1) return "Just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} hr ago";
            return OrderTime.ToString("dd MMM HH:mm");
        }
    }

    public string DriverDisplay => string.IsNullOrEmpty(DriverName) ? "No driver" : DriverName;
    public string PaymentStatusDisplay => IsSettled ? "Paid" : "Unpaid";
    public string PaymentStatusColor => IsSettled ? "#4CAF50" : "#E53935";
    public bool HasDriver => !string.IsNullOrEmpty(DriverName);
    public bool HasNote => !string.IsNullOrEmpty(OrderNote);
    public bool HasAddress => !string.IsNullOrEmpty(CustomerAddress);
    public string AddressDisplay => string.IsNullOrEmpty(CustomerAddress) ? "No address" : CustomerAddress;

    // ── Step-wise button visibility per phase ──
    // Preparing: Edit, Note, Assign Driver, Print → enabled. Complete, Pay → disabled.
    public bool CanEdit => DeliveryStatus == "Preparing";
    public bool CanAddNote => DeliveryStatus == "Preparing";
    public bool CanAssignDriver => DeliveryStatus == "Preparing";
    public bool CanComplete => DeliveryStatus == "Dispatched";
    public bool CanPay => DeliveryStatus == "Dispatched" || DeliveryStatus == "Completed";
    public bool CanPrint => true;  // Always available
}

public class TakeawayOrderViewModel
{
    public string StateKey { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string TotalDisplay { get; set; } = "Rs. 0";
    public long TotalAmount { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string TakeawayStatus { get; set; } = "Preparing";
    public string OrderNote { get; set; } = string.Empty;
    public bool IsSettled { get; set; }
    public DateTime OrderTime { get; set; }

    // Display helpers
    public string StatusBadgeColor => TakeawayStatus switch
    {
        "Preparing" => "#FF9800",   // Orange
        "Ready" => "#1976D2",       // Blue
        "Completed" => "#4CAF50",   // Green
        _ => "#9E9E9E"
    };

    public string TimeAgo
    {
        get
        {
            var elapsed = DateTime.Now - OrderTime;
            if (elapsed.TotalMinutes < 1) return "Just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} hr ago";
            return OrderTime.ToString("dd MMM HH:mm");
        }
    }

    public string PaymentStatusDisplay => IsSettled ? "Paid" : "Unpaid";
    public string PaymentStatusColor => IsSettled ? "#4CAF50" : "#E53935";
    public bool HasNote => !string.IsNullOrEmpty(OrderNote);

    // Step-wise button visibility
    public bool CanEdit => TakeawayStatus == "Preparing";
    public bool CanAddNote => TakeawayStatus == "Preparing";
    public bool CanMarkReady => TakeawayStatus == "Preparing";
    public bool CanComplete => TakeawayStatus == "Ready";
    public bool CanPay => TakeawayStatus == "Ready" || TakeawayStatus == "Completed";
}

public class HoldOrderViewModel
{
    public string StateKey { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderTypeLabel { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string TotalDisplay { get; set; } = "Rs. 0";
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string KSlipStatus { get; set; } = "Pending";
    public DateTime OrderTime { get; set; }
    public string OrderTimeDisplay => OrderTime.ToString("HH:mm");
}
