using System.Windows;

namespace RestaurantPOS.WPF;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    private void ShiftPill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
            vm.NavigateToShiftCommand.Execute(null); // Permission check is inside the command
    }
}
