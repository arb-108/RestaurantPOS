using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.ViewModels;

public partial class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderService _orderService;

    public ObservableCollection<Order> Orders { get; } = [];

    [ObservableProperty]
    private Order? _selectedOrder;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public OrderHistoryViewModel(IOrderService orderService)
    {
        _orderService = orderService;
        Title = "Order History";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await SearchOrdersAsync();
    }

    [RelayCommand]
    private async Task SearchOrdersAsync()
    {
        var orders = await _orderService.GetOrdersByDateAsync(FromDate);
        Orders.Clear();

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var lower = SearchText.ToLowerInvariant();
                if (!order.OrderNumber.ToLower().Contains(lower)
                    && !(order.Customer?.Phone?.Contains(lower) ?? false))
                    continue;
            }
            Orders.Add(order);
        }
    }

    partial void OnFromDateChanged(DateTime value) => _ = SearchOrdersAsync();
    partial void OnSearchTextChanged(string value) => _ = SearchOrdersAsync();

    [RelayCommand]
    private async Task VoidOrderAsync()
    {
        if (SelectedOrder == null) return;
        await _orderService.VoidOrderAsync(SelectedOrder.Id, "Voided from history", 1);
        await SearchOrdersAsync();
    }
}
