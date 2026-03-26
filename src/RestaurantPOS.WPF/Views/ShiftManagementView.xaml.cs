using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class ShiftManagementView : UserControl
{
    public ShiftManagementView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShiftManagementViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            await vm.LoadDataCommand.ExecuteAsync(null);
            SyncPanels();
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShiftManagementViewModel.HasActiveShift))
        {
            SyncPanels();
        }
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncPanels();
    }

    private void SyncPanels()
    {
        if (PanelCurrent == null || MainTabControl == null) return;

        PanelCurrent.Visibility = Visibility.Collapsed;
        PanelNoShift.Visibility = Visibility.Collapsed;
        PanelOrders.Visibility = Visibility.Collapsed;
        PanelDrawer.Visibility = Visibility.Collapsed;
        PanelHistory.Visibility = Visibility.Collapsed;

        var hasShift = DataContext is ShiftManagementViewModel vm && vm.HasActiveShift;
        var idx = MainTabControl.SelectedIndex;

        switch (idx)
        {
            case 0:
                if (hasShift)
                    PanelCurrent.Visibility = Visibility.Visible;
                else
                    PanelNoShift.Visibility = Visibility.Visible;
                break;
            case 1:
                PanelOrders.Visibility = Visibility.Visible;
                break;
            case 2:
                PanelDrawer.Visibility = Visibility.Visible;
                break;
            case 3:
                PanelHistory.Visibility = Visibility.Visible;
                break;
        }
    }
}
