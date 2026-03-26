using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class TableManagementView : UserControl
{
    public TableManagementView() { InitializeComponent(); }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TableManagementViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }
}
