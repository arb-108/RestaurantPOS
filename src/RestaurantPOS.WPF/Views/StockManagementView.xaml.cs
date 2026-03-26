using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class StockManagementView : UserControl
{
    public StockManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is StockManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void CategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string cat && DataContext is StockManagementViewModel vm)
        {
            vm.SelectedCategory = cat;
        }
    }
}
