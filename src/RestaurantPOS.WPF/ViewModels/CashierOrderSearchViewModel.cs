using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.ViewModels;

public partial class CashierOrderSearchViewModel : BaseViewModel
{
    private readonly PosDbContext _db;
    private readonly IAuthService _authService;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Order? _foundOrder;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _noResult;
    [ObservableProperty] private string _statusMessage = "Enter an Order # or Invoice # to search";

    public CashierOrderSearchViewModel(PosDbContext db, IAuthService authService)
    {
        _db = db;
        _authService = authService;
        Title = "Orders History";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            StatusMessage = "Please enter an Order # or Invoice #";
            HasResult = false;
            NoResult = false;
            FoundOrder = null;
            return;
        }

        var query = SearchText.Trim().ToUpperInvariant();

        // Search by exact order number or invoice number
        var order = await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Customer)
            .Include(o => o.TableSession)
                .ThenInclude(ts => ts!.Table)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderNumber.ToUpper() == query); 

        if (order != null)
        {
            FoundOrder = order;
            HasResult = true;
            NoResult = false;
            StatusMessage = $"Found: {order.OrderNumber}";
        }
        else
        {
            FoundOrder = null;
            HasResult = false;
            NoResult = true;
            StatusMessage = $"No order found for \"{SearchText}\"";
        }
    }

    [RelayCommand]
    private void Clear()
    {
        SearchText = string.Empty;
        FoundOrder = null;
        HasResult = false;
        NoResult = false;
        StatusMessage = "Enter an Order # or Invoice # to search";
    }
}
