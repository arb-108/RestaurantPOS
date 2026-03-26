using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;

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

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    // Summary cards
    [ObservableProperty]
    private string _totalSales = "Rs. 0";

    [ObservableProperty]
    private string _totalOrders = "0";

    [ObservableProperty]
    private string _avgOrderValue = "Rs. 0";

    [ObservableProperty]
    private string _voidedOrders = "0";

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

    public ReportsViewModel(IReportService reportService)
    {
        _reportService = reportService;
        Title = "Reports";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await LoadSummaryAsync();
        await LoadChartsAsync();
    }

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadDataAsync();

    private async Task LoadSummaryAsync()
    {
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
    }

    private async Task LoadChartsAsync()
    {
        // Sales by category
        var catData = await _reportService.GetSalesByCategoryAsync(FromDate, ToDate);
        SalesByCategory.Clear();
        var catList = catData.ToList();
        var catMax = catList.Any() ? catList.Max(c => c.Total) : 1;
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
        var hourMax = hourList.Any() ? hourList.Max(h => h.Total) : 1;
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
        var payData = await _reportService.GetSalesByPaymentMethodAsync(FromDate, ToDate);
        SalesByPaymentMethod.Clear();
        var payList = payData.ToList();
        var payMax = payList.Any() ? payList.Max(p => p.Total) : 1;
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
}
