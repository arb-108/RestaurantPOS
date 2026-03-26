using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.WPF.ViewModels;

public partial class KitchenOrderCardViewModel : ObservableObject
{
    public int KitchenOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string TableLabel { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public KitchenOrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ElapsedTime => $"{(DateTime.UtcNow - CreatedAt).TotalMinutes:F0} min";
    public ObservableCollection<KitchenItemLine> Items { get; } = [];

    public string StatusColor => Status switch
    {
        KitchenOrderStatus.New => "#FFFFFF",
        KitchenOrderStatus.InProgress => "#FFF9C4",
        KitchenOrderStatus.Ready => "#C8E6C9",
        _ => "#E0E0E0"
    };
}

public class KitchenItemLine
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public KitchenItemStatus Status { get; set; }
}

public partial class KitchenDisplayViewModel : BaseViewModel
{
    private readonly IKitchenService _kitchenService;
    private DispatcherTimer? _refreshTimer;

    public ObservableCollection<KitchenOrderCardViewModel> KitchenOrders { get; } = [];
    public ObservableCollection<KitchenStation> Stations { get; } = [];

    [ObservableProperty]
    private KitchenStation? _selectedStation;

    [ObservableProperty]
    private bool _showAllStations = true;

    public KitchenDisplayViewModel(IKitchenService kitchenService)
    {
        _kitchenService = kitchenService;
        Title = "Kitchen Display";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        var stations = await _kitchenService.GetStationsAsync();
        Stations.Clear();
        foreach (var s in stations) Stations.Add(s);

        await RefreshOrdersAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (s, e) => await RefreshOrdersAsync();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task RefreshOrdersAsync()
    {
        int? stationFilter = ShowAllStations ? null : SelectedStation?.Id;
        var orders = await _kitchenService.GetActiveKitchenOrdersAsync(stationFilter);

        KitchenOrders.Clear();
        foreach (var ko in orders)
        {
            var card = new KitchenOrderCardViewModel
            {
                KitchenOrderId = ko.Id,
                OrderNumber = ko.Order.OrderNumber,
                TableLabel = ko.Order.TableSession?.Table?.Name ?? ko.Order.OrderType.ToString(),
                StationName = ko.Station.Name,
                Status = ko.Status,
                CreatedAt = ko.CreatedAt
            };

            foreach (var item in ko.Items)
            {
                card.Items.Add(new KitchenItemLine
                {
                    ItemName = item.OrderItem.MenuItem.Name,
                    Quantity = item.OrderItem.Quantity,
                    Notes = item.OrderItem.Notes,
                    Status = item.Status
                });
            }

            KitchenOrders.Add(card);
        }
    }

    [RelayCommand]
    private async Task BumpOrderAsync(KitchenOrderCardViewModel card)
    {
        await _kitchenService.BumpOrderAsync(card.KitchenOrderId);
        await RefreshOrdersAsync();
    }

    [RelayCommand]
    private async Task StartCookingAsync(KitchenOrderCardViewModel card)
    {
        await _kitchenService.UpdateKitchenOrderStatusAsync(card.KitchenOrderId, KitchenOrderStatus.InProgress);
        await RefreshOrdersAsync();
    }

    partial void OnSelectedStationChanged(KitchenStation? value)
    {
        ShowAllStations = value == null;
        _ = RefreshOrdersAsync();
    }

    [RelayCommand]
    private async Task ShowAllAsync()
    {
        SelectedStation = null;
        ShowAllStations = true;
        await RefreshOrdersAsync();
    }

    public void StopRefresh()
    {
        _refreshTimer?.Stop();
    }
}
