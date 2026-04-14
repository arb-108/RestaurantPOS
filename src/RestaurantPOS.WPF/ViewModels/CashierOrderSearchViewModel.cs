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

public partial class CashierOrderSearchViewModel : BaseViewModel
{
    private readonly PosDbContext _db;
    private readonly IAuthService _authService;
    private readonly IPrintService _printService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = "Enter Order # to search";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasResults;

    public ObservableCollection<OrderRowViewModel> Orders { get; } = [];

    public CashierOrderSearchViewModel(
        PosDbContext db,
        IAuthService authService,
        IPrintService printService,
        ISettingsService settingsService)
    {
        _db = db;
        _authService = authService;
        _printService = printService;
        _settingsService = settingsService;
        Title = "Orders History";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            StatusMessage = "Please enter an Order # to search";
            return;
        }

        IsLoading = true;
        StatusMessage = "Searching...";

        try
        {
            var query = SearchText.Trim().ToUpperInvariant();

            // Search by order number (partial match)
            var orders = await _db.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Customer)
                .Include(o => o.Cashier)
                .Include(o => o.TableSession)
                    .ThenInclude(ts => ts!.Table)
                .Include(o => o.Payments)
                    .ThenInclude(p => p.PaymentMethod)
                .Where(o => o.OrderNumber.ToUpper().Contains(query))
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .ToListAsync();

            Orders.Clear();
            foreach (var o in orders)
                Orders.Add(new OrderRowViewModel(o));

            HasResults = Orders.Count > 0;
            StatusMessage = Orders.Count > 0
                ? $"{Orders.Count} order(s) found for \"{SearchText}\""
                : $"No orders found for \"{SearchText}\"";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        SearchText = string.Empty;
        Orders.Clear();
        HasResults = false;
        StatusMessage = "Enter Order # to search";
    }

    [RelayCommand]
    private async Task ReprintAsync(OrderRowViewModel? row)
    {
        if (row?.Order == null) return;

        var order = row.Order;

        // Load receipt settings
        var restaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName") ?? "Restaurant";
        var address = await _settingsService.GetSettingAsync("ReceiptAddress") ?? "";
        var phone = await _settingsService.GetSettingAsync("ReceiptPhone") ?? "";

        // Find configured receipt printer
        var receiptPrinter = await _db.Set<Printer>()
            .FirstOrDefaultAsync(p => p.Type == PrinterType.Receipt);
        var printerName = receiptPrinter?.SystemPrinterName;

        var receiptData = new ReceiptData
        {
            RestaurantName = restaurantName,
            RestaurantAddress = address,
            RestaurantPhone = phone,
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

        // Customer info
        if (order.Customer != null)
        {
            receiptData.CustomerName = order.Customer.Name;
            receiptData.CustomerPhone = order.Customer.Phone;
        }

        // Parse delivery info from notes
        if (!string.IsNullOrEmpty(order.Notes))
        {
            foreach (var line in order.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
            { ConfiguredPrinterName = printerName };
        previewWindow.Owner = System.Windows.Application.Current.MainWindow;
        previewWindow.ShowDialog();
    }
}

/// <summary>Flat row model for the Orders DataGrid.</summary>
public class OrderRowViewModel
{
    public Order Order { get; }

    public string OrderNumber { get; }
    public string OrderType { get; }
    public string Status { get; }
    public int ItemCount { get; }
    public long GrandTotal { get; }
    public string CashierName { get; }
    public string? CustomerName { get; }
    public string? CustomerPhone { get; }
    public DateTime Timestamp { get; }
    public string TimestampDisplay { get; }

    public OrderRowViewModel(Order order)
    {
        Order = order;
        OrderNumber = order.OrderNumber;
        OrderType = order.OrderType.ToString();
        Status = order.Status.ToString();
        ItemCount = order.OrderItems.Count;
        GrandTotal = order.GrandTotal;
        CashierName = order.Cashier?.FullName ?? "Unknown";
        CustomerName = order.Customer?.Name;
        CustomerPhone = order.Customer?.Phone;
        Timestamp = order.CreatedAt.ToLocalTime();
        TimestampDisplay = Timestamp.ToString("dd/MM/yyyy hh:mm tt");
    }
}
