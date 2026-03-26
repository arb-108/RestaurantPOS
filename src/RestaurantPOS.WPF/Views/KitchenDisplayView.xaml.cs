using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class KitchenDisplayView : UserControl
{
    public KitchenDisplayView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is KitchenDisplayViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }
}
