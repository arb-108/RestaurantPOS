using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class CustomerManagementView : UserControl
{
    public CustomerManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CustomerManagementViewModel vm)
        {
            // Hide "Total Spent" column for cashier
            TotalSpentColumn.Visibility = vm.CanSeeStats ? Visibility.Visible : Visibility.Collapsed;

            await vm.LoadDataCommand.ExecuteAsync(null);
        }
    }
}
