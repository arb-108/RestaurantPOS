using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class SupplierManagementView : UserControl
{
    public SupplierManagementView() { InitializeComponent(); }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SupplierManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }
}
