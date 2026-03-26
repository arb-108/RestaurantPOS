using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class ExpenseManagementView : UserControl
{
    public ExpenseManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExpenseManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelSuppliers == null || MainTabControl == null) return;

        var idx = MainTabControl.SelectedIndex;
        PanelSuppliers.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelExpenses.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Show expense-specific filter only on Expenses tab
        LblSupplierFilter.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        CmbSupplierFilter.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Toggle action buttons per tab
        BtnSuppliers.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnExpenses.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;

        if (DataContext is ExpenseManagementViewModel vm)
            vm.SelectedTab = idx;
    }
}
