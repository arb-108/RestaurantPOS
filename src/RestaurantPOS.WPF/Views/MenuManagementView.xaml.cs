using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class MenuManagementView : UserControl
{
    public MenuManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MenuManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelProducts == null || MainTabControl == null) return;

        var idx = MainTabControl.SelectedIndex;
        PanelProducts.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTables.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle filter and action buttons per tab
        FilterProducts.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnProducts.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnTables.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        if (DataContext is MenuManagementViewModel vm)
            vm.SelectedTab = idx;
    }
}
