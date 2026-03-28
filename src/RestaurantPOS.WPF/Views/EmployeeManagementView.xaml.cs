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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmployeeManagementViewModel vm)
            vm.LoadDataCommand.Execute(null);
    }

    private void TabEmployees_Click(object sender, RoutedEventArgs e)
    {
        EmployeesGrid.Visibility = Visibility.Visible;
        PayrollGrid.Visibility = Visibility.Collapsed;
        EmployeeActions.Visibility = Visibility.Visible;
        PayrollActions.Visibility = Visibility.Collapsed;
        StatusTotal.Visibility = Visibility.Collapsed;

        if (DataContext is EmployeeManagementViewModel vm)
        {
            SearchBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("EmployeeSearch")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            StatusCount.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("EmployeeCountText"));
        }
    }

    private void TabPayroll_Click(object sender, RoutedEventArgs e)
    {
        EmployeesGrid.Visibility = Visibility.Collapsed;
        PayrollGrid.Visibility = Visibility.Visible;
        EmployeeActions.Visibility = Visibility.Collapsed;
        PayrollActions.Visibility = Visibility.Visible;
        StatusTotal.Visibility = Visibility.Visible;

        if (DataContext is EmployeeManagementViewModel vm)
        {
            SearchBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("PayrollSearch")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            StatusCount.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("PayrollCountText"));
        }
    }
}
