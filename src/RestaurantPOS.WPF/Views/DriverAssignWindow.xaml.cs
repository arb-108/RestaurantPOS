using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.Views;

public partial class DriverAssignWindow : Window
{
    public string SelectedDriverName { get; private set; } = string.Empty;
    public string SelectedDriverPhone { get; private set; } = string.Empty;

    public ObservableCollection<DriverInfo> Drivers { get; } = [];

    private readonly PosDbContext? _db;
    private List<DriverInfo> _allDrivers = [];
    private readonly bool _canAddDriver;

    public DriverAssignWindow(string currentDriver, string currentPhone, bool canAddDriver, PosDbContext? db = null)
    {
        InitializeComponent();
        _canAddDriver = canAddDriver;
        _db = db;

        if (canAddDriver)
            AddDriverPanel.Visibility = Visibility.Visible;

        // Load drivers from DB
        Loaded += async (_, _) =>
        {
            await LoadDriversAsync();

            // Pre-select current driver
            if (!string.IsNullOrEmpty(currentDriver))
            {
                var match = Drivers.FirstOrDefault(d => d.Name == currentDriver);
                if (match != null)
                    LstDrivers.SelectedItem = match;
            }
        };
    }

    private async Task LoadDriversAsync()
    {
        _allDrivers.Clear();

        if (_db != null)
        {
            var employees = await _db.Employees
                .Where(e => e.IsActive && e.Category == EmployeeCategory.Delivery && !e.LeavingDate.HasValue)
                .OrderBy(e => e.Name)
                .ToListAsync();

            foreach (var emp in employees)
            {
                _allDrivers.Add(new DriverInfo
                {
                    EmployeeId = emp.Id,
                    Name = emp.Name,
                    Phone = emp.Phone ?? ""
                });
            }
        }

        RefreshDriverList(string.Empty);
    }

    private void RefreshDriverList(string search)
    {
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allDrivers
            : _allDrivers.Where(d =>
                d.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                d.Phone.Contains(search, StringComparison.OrdinalIgnoreCase))
              .ToList();

        Drivers.Clear();
        foreach (var d in filtered)
            Drivers.Add(d);

        LstDrivers.ItemsSource = Drivers;
        TxtDriverCount.Text = $"{_allDrivers.Count} driver{(_allDrivers.Count != 1 ? "s" : "")} available";
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshDriverList(TxtSearch.Text);
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

    private async void AddDriver_Click(object sender, RoutedEventArgs e)
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

        // Save to Employee DB as Delivery category
        if (_db != null)
        {
            var emp = new Employee
            {
                Name = name,
                Phone = phone,
                Category = EmployeeCategory.Delivery,
                EmploymentType = EmploymentType.FullTime,
                Designation = "Driver",
                JoiningDate = DateTime.UtcNow,
                BasicSalary = 0
            };
            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();
        }

        await LoadDriversAsync();

        // Auto-select the newly added driver
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
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
