using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class MainPOSView : UserControl
{
    public MainPOSView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainPOSViewModel vm)
        {
            await vm.LoadDataCommand.ExecuteAsync(null);
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Handle Enter key on mobile text box:
    /// - If green (matched) → confirm selection
    /// - If red (not matched) → open Add Customer form
    /// </summary>
    private async void MobileTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainPOSViewModel vm)
        {
            e.Handled = true;
            await vm.PhoneEnterPressedCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Click a customer in the search dropdown to select them.
    /// </summary>
    private void CustomerSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Customer customer
            && DataContext is MainPOSViewModel vm)
        {
            vm.SelectCustomerFromSearchCommand.Execute(customer);
        }
    }

    
}
