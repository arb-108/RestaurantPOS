using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class EmployeeManagementView : UserControl
{
    public EmployeeManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmployeeManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelEmployees == null || MainTabControl == null) return;

        var idx = MainTabControl.SelectedIndex;
        PanelEmployees.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelPayroll.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle filters per tab
        FilterEmployees.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterPayroll.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle action buttons per tab
        BtnEmployees.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnPayroll.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        if (DataContext is EmployeeManagementViewModel vm)
            vm.SelectedTab = idx;
    }
}
