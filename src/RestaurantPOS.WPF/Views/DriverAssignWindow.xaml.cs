using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RestaurantPOS.WPF.Views;

public partial class DriverAssignWindow : Window
{
    public string SelectedDriverName { get; private set; } = string.Empty;
    public string SelectedDriverPhone { get; private set; } = string.Empty;

    public ObservableCollection<DriverInfo> Drivers { get; } = [];

    // Static list persists across window instances during app session
    private static readonly List<DriverInfo> _savedDrivers =
    [
        new() { Name = "Ali Raza", Phone = "0301-1234567" },
        new() { Name = "Fahad Khan", Phone = "0312-9876543" },
        new() { Name = "Imran Ahmed", Phone = "0333-4567890" },
        new() { Name = "Usman Tariq", Phone = "0345-5678901" },
        new() { Name = "Bilal Shah", Phone = "0300-2345678" }
    ];

    private readonly bool _canAddDriver;
    private List<DriverInfo> _filteredDrivers = [];

    public DriverAssignWindow(string currentDriver, string currentPhone, bool canAddDriver)
    {
        InitializeComponent();
        _canAddDriver = canAddDriver;

        if (canAddDriver)
            AddDriverPanel.Visibility = Visibility.Visible;

        // Load drivers
        RefreshDriverList(string.Empty);

        // Pre-select current driver if assigned
        if (!string.IsNullOrEmpty(currentDriver))
        {
            var match = _filteredDrivers.FirstOrDefault(d => d.Name == currentDriver);
            if (match != null)
                LstDrivers.SelectedItem = match;
        }
    }

    private void RefreshDriverList(string search)
    {
        _filteredDrivers = string.IsNullOrWhiteSpace(search)
            ? _savedDrivers.ToList()
            : _savedDrivers.Where(d =>
                d.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                d.Phone.Contains(search, StringComparison.OrdinalIgnoreCase))
              .ToList();

        Drivers.Clear();
        foreach (var d in _filteredDrivers)
            Drivers.Add(d);

        LstDrivers.ItemsSource = Drivers;

        // Update driver count label
        TxtDriverCount.Text = $"{_savedDrivers.Count} drivers available";
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshDriverList(TxtSearch.Text);
        // Show/hide placeholder
        TxtSearchHint.Visibility = string.IsNullOrEmpty(TxtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DriverList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstDrivers.SelectedItem is DriverInfo driver)
        {
            SelectedDriverName = driver.Name;
            SelectedDriverPhone = driver.Phone;
        }
    }

    private void AddDriver_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewName.Text.Trim();
        var phone = TxtNewPhone.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Driver name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtNewName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            MessageBox.Show("Phone number is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtNewPhone.Focus();
            return;
        }

        var newDriver = new DriverInfo { Name = name, Phone = phone };
        _savedDrivers.Add(newDriver);

        // Clear search so newly added driver is visible in the unfiltered list
        TxtSearch.Clear();
        RefreshDriverList(string.Empty);

        // Auto-select the newly added driver and set output properties
        var match = Drivers.FirstOrDefault(d => d.Name == name && d.Phone == phone);
        if (match != null)
        {
            LstDrivers.SelectedItem = match;
            SelectedDriverName = match.Name;
            SelectedDriverPhone = match.Phone;
        }

        TxtNewName.Clear();
        TxtNewPhone.Clear();
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedDriverName))
        {
            MessageBox.Show("Please select a driver first.", "Driver Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class DriverInfo
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
