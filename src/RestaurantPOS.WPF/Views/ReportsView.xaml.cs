using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReportsViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl == null || FilterSales == null) return;
        var idx = MainTabControl.SelectedIndex;

        // Toggle filter panels
        FilterSales.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterOrders.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        FilterMenu.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        FilterDrivers.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        FilterKitchen.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        FilterExpenses.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        FilterProfit.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle action buttons
        BtnSales.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnOrders.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnMenu.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        BtnDrivers.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnKitchen.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        BtnExpenses.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        BtnProfit.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle content panels
        PanelSales.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelOrders.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelMenu.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelDrivers.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelKitchen.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelExpenses.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        PanelProfit.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle status bar items
        StatusSales.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusOrders.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        StatusMenu.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        StatusDrivers.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        StatusKitchen.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        StatusExpenses.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        StatusProfit.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;

        if (DataContext is ReportsViewModel vm)
            vm.SelectedTab = idx;
    }
}
