using System.Windows;
using System.Windows.Controls;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl == null || PanelGeneral == null) return;
        var idx = MainTabControl.SelectedIndex;

        if (DataContext is SettingsViewModel vm)
        {
            vm.SelectedTab = idx;
            vm.StatusMessage = "";
        }

        // Filter/action buttons
        BtnSaveGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnPrinters.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnBackup.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        BtnSaveReceipt.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnTax.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        BtnUsers.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Content panels
        PanelGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelPrinters.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelBackup.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelReceipt.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelTax.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelUsers.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Status bar items
        StatusGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusPrinters.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        StatusBackup.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        StatusReceipt.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        StatusTax.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        StatusUsers.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Trigger refresh for the selected tab
        if (DataContext is SettingsViewModel vm2)
            _ = vm2.RefreshCommand.ExecuteAsync(null);
    }

    // User form is now a separate window (UserFormWindow)

    private async void StationPrinterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is StationPrinterAssignment assignment
            && DataContext is SettingsViewModel vm && cb.SelectedItem is string printerName)
        {
            await vm.UpdateStationPrinterAsync(assignment, printerName);
        }
    }

    // Access level +/- buttons for role permissions
    private void IncrementLevel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RolePermissionRow row)
        {
            if (row.AccessLevel < 5)
            {
                row.AccessLevel++;
                row.IsGranted = true;
            }
        }
    }

    private void DecrementLevel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RolePermissionRow row)
        {
            if (row.AccessLevel > 0)
            {
                row.AccessLevel--;
                if (row.AccessLevel == 0) row.IsGranted = false;
            }
        }
    }
}
