using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.WPF.Views;
using System.Collections.ObjectModel;
using System.Windows;

namespace RestaurantPOS.WPF.ViewModels;

public partial class ShiftManagementViewModel : BaseViewModel
{
    private readonly PosDbContext _db;
    private readonly MainWindowViewModel _mainVm;

    // ── Current Shift State ──
    [ObservableProperty] private bool _hasActiveShift;
    [ObservableProperty] private Shift? _activeShift;
    [ObservableProperty] private string _shiftStatusText = "No Active Shift";
    [ObservableProperty] private string _shiftStartedText = "";
    [ObservableProperty] private string _shiftUserText = "";
    [ObservableProperty] private string _shiftDurationText = "";
    [ObservableProperty] private string _openingBalanceText = "Rs 0";

    // ── Live Stats for Active Shift ──
    [ObservableProperty] private int _shiftOrderCount;
    [ObservableProperty] private string _shiftSalesText = "Rs 0";
    [ObservableProperty] private string _shiftCashText = "Rs 0";
    [ObservableProperty] private string _shiftCardText = "Rs 0";
    [ObservableProperty] private int _shiftPayInCount;
    [ObservableProperty] private string _shiftPayInText = "Rs 0";
    [ObservableProperty] private int _shiftPayOutCount;
    [ObservableProperty] private string _shiftPayOutText = "Rs 0";
    [ObservableProperty] private string _expectedDrawerText = "Rs 0";

    // ── Shift Orders list ──
    public ObservableCollection<Order> ShiftOrders { get; } = [];
    public ObservableCollection<CashDrawerLog> DrawerLogs { get; } = [];

    // ── History ──
    public ObservableCollection<Shift> ShiftHistory { get; } = [];

    // ── Selected Tab ──
    [ObservableProperty] private int _selectedTab; // 0=Current, 1=History

    // ── Role-based permissions ──
    [ObservableProperty] private bool _canOpenCloseShift;
    [ObservableProperty] private bool _canPayInOut;
    [ObservableProperty] private bool _canOpenShift;
    [ObservableProperty] private bool _canCloseShift;
    [ObservableProperty] private bool _canDoPayIn;
    [ObservableProperty] private bool _canDoPayOut;

    private System.Windows.Threading.DispatcherTimer? _refreshTimer;

    public ShiftManagementViewModel(PosDbContext db, MainWindowViewModel mainVm)
    {
        _db = db;
        _mainVm = mainVm;
        Title = "Shift Management";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        ApplyShiftPermissions();
        await RefreshActiveShiftAsync();
        await LoadHistoryAsync();
        StartAutoRefresh();
    }

    private void ApplyShiftPermissions()
    {
        var roleId = _mainVm.LoggedInUser?.RoleId;
        // Admin (1) and Manager (2) have full shift control; Cashier (3) can only view
        CanOpenCloseShift = roleId == 1 || roleId == 2;
        CanPayInOut = roleId == 1 || roleId == 2;
        UpdateShiftActionProperties();
    }

    partial void OnHasActiveShiftChanged(bool value)
    {
        UpdateShiftActionProperties();
    }

    private void UpdateShiftActionProperties()
    {
        CanOpenShift = !HasActiveShift && CanOpenCloseShift;
        CanCloseShift = HasActiveShift && CanOpenCloseShift;
        CanDoPayIn = HasActiveShift && CanPayInOut;
        CanDoPayOut = HasActiveShift && CanPayInOut;
    }

    private void StartAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (HasActiveShift) await RefreshActiveShiftAsync();
        };
        _refreshTimer.Start();
    }

    private async Task RefreshActiveShiftAsync()
    {
        try
        {
            var shift = await _db.Shifts
                .Include(s => s.User)
                .Include(s => s.CashDrawerLogs).ThenInclude(l => l.User)
                .Where(s => s.EndedAt == null && s.IsActive)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            ActiveShift = shift;
            HasActiveShift = shift != null;

            if (shift != null)
            {
                ShiftStatusText = "Shift Active";
                ShiftStartedText = shift.StartedAt.ToLocalTime().ToString("dd MMM yyyy  hh:mm tt");
                ShiftUserText = shift.User?.FullName ?? "Unknown";
                OpeningBalanceText = FormatRs(shift.OpeningBalance);

                var elapsed = DateTime.UtcNow - shift.StartedAt;
                ShiftDurationText = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                    : $"{elapsed.Minutes}m";

                // ── Load orders linked to this shift ──
                var shiftOrders = await _db.Orders
                    .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
                    .Include(o => o.OrderItems)
                    .Where(o => o.ShiftId == shift.Id && o.Status == OrderStatus.Closed)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                ShiftOrderCount = shiftOrders.Count;
                var totalSales = shiftOrders.Sum(o => o.GrandTotal);
                ShiftSalesText = FormatRs(totalSales);

                // Cash sales = payments via Cash payment method
                var cashSales = shiftOrders
                    .SelectMany(o => o.Payments)
                    .Where(p => p.PaymentMethod != null &&
                        p.PaymentMethod.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.Amount);
                ShiftCashText = FormatRs(cashSales);

                // Card/Digital = non-cash
                var cardSales = totalSales - cashSales;
                ShiftCardText = FormatRs(cardSales);

                // Populate shift orders list for the view
                ShiftOrders.Clear();
                foreach (var o in shiftOrders) ShiftOrders.Add(o);

                // ── Pay-ins and pay-outs (exclude "Opening balance" from pay-in count/sum for display) ──
                var manualPayIns = shift.CashDrawerLogs
                    .Where(l => l.Type == CashDrawerLogType.PayIn && l.Description != "Opening balance")
                    .ToList();
                ShiftPayInCount = manualPayIns.Count;
                ShiftPayInText = FormatRs(manualPayIns.Sum(l => l.Amount));

                var payOuts = shift.CashDrawerLogs.Where(l => l.Type == CashDrawerLogType.PayOut).ToList();
                ShiftPayOutCount = payOuts.Count;
                ShiftPayOutText = FormatRs(payOuts.Sum(l => l.Amount));

                // ── Expected drawer = opening + cash sales + manual pay-ins - pay-outs ──
                var expected = shift.OpeningBalance
                    + cashSales
                    + manualPayIns.Sum(l => l.Amount)
                    - payOuts.Sum(l => l.Amount);
                ExpectedDrawerText = FormatRs(expected);

                // ── Drawer logs (all entries) ──
                DrawerLogs.Clear();
                foreach (var log in shift.CashDrawerLogs.OrderByDescending(l => l.CreatedAt))
                    DrawerLogs.Add(log);
            }
            else
            {
                ShiftStatusText = "No Active Shift";
                ShiftStartedText = "";
                ShiftUserText = "";
                ShiftDurationText = "";
                OpeningBalanceText = "Rs 0";
                ShiftOrderCount = 0;
                ShiftSalesText = "Rs 0";
                ShiftCashText = "Rs 0";
                ShiftCardText = "Rs 0";
                ShiftPayInCount = 0;
                ShiftPayInText = "Rs 0";
                ShiftPayOutCount = 0;
                ShiftPayOutText = "Rs 0";
                ExpectedDrawerText = "Rs 0";
                ShiftOrders.Clear();
                DrawerLogs.Clear();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to refresh active shift");
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _db.Shifts
                .Include(s => s.User)
                .Where(s => s.EndedAt != null)
                .OrderByDescending(s => s.EndedAt)
                .Take(50)
                .ToListAsync();

            ShiftHistory.Clear();
            foreach (var s in history) ShiftHistory.Add(s);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load shift history");
        }
    }

    [RelayCommand]
    private async Task OpenShiftAsync()
    {
        if (HasActiveShift)
        {
            MessageBox.Show("A shift is already active. Please close the current shift first.",
                "Shift Active", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var user = _mainVm.LoggedInUser;
        if (user == null)
        {
            MessageBox.Show("No user is logged in.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new OpenShiftWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true)
        {
            var shift = new Shift
            {
                UserId = user.Id,
                StartedAt = DateTime.UtcNow,
                OpeningBalance = dlg.OpeningBalancePaisa,
                Notes = dlg.ShiftNotes,
                IsActive = true
            };
            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync();

            // Record opening balance as a drawer log entry (informational)
            _db.CashDrawerLogs.Add(new CashDrawerLog
            {
                ShiftId = shift.Id,
                Type = CashDrawerLogType.PayIn,
                Amount = shift.OpeningBalance,
                Description = "Opening balance",
                UserId = user.Id
            });
            await _db.SaveChangesAsync();

            await RefreshActiveShiftAsync();
            _mainVm.UpdateShiftStatus(true, shift.StartedAt.ToLocalTime());
        }
    }

    [RelayCommand]
    private async Task CloseShiftAsync()
    {
        if (!HasActiveShift || ActiveShift == null)
        {
            MessageBox.Show("No active shift to close.", "No Shift", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Check for open orders (any status that isn't Closed or Void)
        var openOrdersList = await _db.Orders
            .Where(o => o.ShiftId == ActiveShift.Id &&
                o.Status != OrderStatus.Closed && o.Status != OrderStatus.Void)
            .ToListAsync();

        // Auto-void ghost orders (created but no items were added)
        var ghostOrders = new List<Order>();
        foreach (var order in openOrdersList.ToList())
        {
            var itemCount = await _db.OrderItems.CountAsync(oi => oi.OrderId == order.Id && oi.Status != OrderStatus.Void);
            if (itemCount == 0)
            {
                order.Status = OrderStatus.Void;
                order.VoidReason = "Auto-voided: empty order on shift close";
                ghostOrders.Add(order);
                openOrdersList.Remove(order);
            }
        }
        if (ghostOrders.Count > 0)
            await _db.SaveChangesAsync();

        if (openOrdersList.Count > 0)
        {
            // Build a summary of open orders for the dialog
            var orderSummary = string.Join("\n", openOrdersList
                .Take(15)
                .Select(o => $"  • {o.OrderNumber}  ({o.OrderType})  {o.CreatedAt:HH:mm}"));
            if (openOrdersList.Count > 15)
                orderSummary += $"\n  ... and {openOrdersList.Count - 15} more";

            var result = MessageBox.Show(
                $"There are {openOrdersList.Count} open order(s) in this shift:\n\n" +
                $"{orderSummary}\n\n" +
                "Click YES to void all open orders and close shift.\n" +
                "Click NO to go back and handle them manually.",
                "Open Orders", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var order in openOrdersList)
                {
                    order.Status = OrderStatus.Void;
                    order.VoidReason = "Voided on shift close";
                }
                await _db.SaveChangesAsync();
            }
            else
            {
                return;
            }
        }

        // Reload drawer logs to be safe
        await _db.Entry(ActiveShift).Collection(s => s.CashDrawerLogs).LoadAsync();

        // Calculate expected balance
        var shiftOrders = await _db.Orders
            .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
            .Where(o => o.ShiftId == ActiveShift.Id && o.Status == OrderStatus.Closed)
            .ToListAsync();

        var totalSales = shiftOrders.Sum(o => o.GrandTotal);
        var cashSales = shiftOrders
            .SelectMany(o => o.Payments)
            .Where(p => p.PaymentMethod != null &&
                p.PaymentMethod.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Amount);
        var cardSales = totalSales - cashSales;

        var manualPayIns = ActiveShift.CashDrawerLogs
            .Where(l => l.Type == CashDrawerLogType.PayIn && l.Description != "Opening balance")
            .Sum(l => l.Amount);
        var payOuts = ActiveShift.CashDrawerLogs
            .Where(l => l.Type == CashDrawerLogType.PayOut)
            .Sum(l => l.Amount);

        var expectedBalance = ActiveShift.OpeningBalance + cashSales + manualPayIns - payOuts;

        var dlg = new CloseShiftWindow(
            ActiveShift.StartedAt.ToLocalTime(),
            _mainVm.LoggedInUser?.FullName ?? "Unknown",
            shiftOrders.Count,
            totalSales,
            cashSales,
            cardSales,
            ActiveShift.OpeningBalance,
            manualPayIns,
            payOuts,
            expectedBalance)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true)
        {
            ActiveShift.EndedAt = DateTime.UtcNow;
            ActiveShift.ClosingBalance = dlg.CountedCashPaisa;
            ActiveShift.ExpectedBalance = expectedBalance;
            ActiveShift.Discrepancy = dlg.CountedCashPaisa - expectedBalance;
            ActiveShift.Notes = (ActiveShift.Notes ?? "") +
                (string.IsNullOrEmpty(dlg.ClosingNotes) ? "" : $"\nClose: {dlg.ClosingNotes}");

            await _db.SaveChangesAsync();
            await RefreshActiveShiftAsync();
            await LoadHistoryAsync();
            _mainVm.UpdateShiftStatus(false, null);
        }
    }

    [RelayCommand]
    private async Task PayInAsync()
    {
        if (!HasActiveShift || ActiveShift == null) return;

        var dlg = new CashDrawerEntryWindow("Pay In — Cash Added to Drawer")
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true)
        {
            _db.CashDrawerLogs.Add(new CashDrawerLog
            {
                ShiftId = ActiveShift.Id,
                Type = CashDrawerLogType.PayIn,
                Amount = dlg.AmountPaisa,
                Description = dlg.EntryDescription,
                UserId = _mainVm.LoggedInUser?.Id
            });
            await _db.SaveChangesAsync();
            await RefreshActiveShiftAsync();
        }
    }

    [RelayCommand]
    private async Task PayOutAsync()
    {
        if (!HasActiveShift || ActiveShift == null) return;

        var dlg = new CashDrawerEntryWindow("Pay Out — Cash Removed from Drawer")
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true)
        {
            _db.CashDrawerLogs.Add(new CashDrawerLog
            {
                ShiftId = ActiveShift.Id,
                Type = CashDrawerLogType.PayOut,
                Amount = dlg.AmountPaisa,
                Description = dlg.EntryDescription,
                UserId = _mainVm.LoggedInUser?.Id
            });
            await _db.SaveChangesAsync();
            await RefreshActiveShiftAsync();
        }
    }

    /// <summary>Gets the active shift ID for linking orders. Returns null if no shift.</summary>
    public static async Task<int?> GetActiveShiftIdAsync(PosDbContext db)
    {
        var shift = await db.Shifts
            .Where(s => s.EndedAt == null && s.IsActive)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync();
        return shift;
    }

    private static string FormatRs(long paisa) => $"Rs {paisa / 100m:N0}";
}
