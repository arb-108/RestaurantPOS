using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class OrderHistoryView : UserControl
{
    public OrderHistoryView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is OrderHistoryViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }
}
