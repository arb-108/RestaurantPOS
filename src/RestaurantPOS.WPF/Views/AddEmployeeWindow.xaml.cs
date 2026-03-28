using System.Windows;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.WPF.Views;

public partial class AddEmployeeWindow : Window
{
    public string EmployeeName => TxtName.Text.Trim();
    public string EmployeePhone => TxtPhone.Text.Trim();
    public string EmployeeEmail => TxtEmail.Text.Trim();
    public string EmployeeCNIC => TxtCNIC.Text.Trim();
    public string EmployeeAddress => TxtAddress.Text.Trim();
    public string EmergencyContact => "";
    public string EmployeeDesignation => TxtDesignation.Text.Trim();
    public DateTime JoiningDate => DpJoining.SelectedDate ?? DateTime.Now;
    public decimal BasicSalary => decimal.TryParse(TxtBasicSalary.Text, out var v) ? v : 0;
    public decimal AllowancesAmount => decimal.TryParse(TxtAllowances.Text, out var v) ? v : 0;
    public decimal DeductionsAmount => decimal.TryParse(TxtDeductions.Text, out var v) ? v : 0;

    public EmployeeCategory SelectedCategory =>
        CmbCategory.SelectedIndex switch
        {
            0 => EmployeeCategory.Kitchen,
            1 => EmployeeCategory.Service,
            2 => EmployeeCategory.Delivery,
            3 => EmployeeCategory.Management,
            4 => EmployeeCategory.Cleaning,
            _ => EmployeeCategory.Other,
        };

    public EmploymentType SelectedEmploymentType =>
        CmbType.SelectedIndex switch
        {
            0 => EmploymentType.FullTime,
            1 => EmploymentType.PartTime,
            2 => EmploymentType.Contract,
            _ => EmploymentType.Daily,
        };

    public AddEmployeeWindow()
    {
        InitializeComponent();
        DpJoining.SelectedDate = DateTime.Now;
        Loaded += (_, _) => TxtName.Focus();
    }

    public void SetEditData(string name, string phone, string email, string cnic,
        string address, string emergencyContact, EmployeeCategory category,
        EmploymentType empType, string designation, DateTime joiningDate,
        decimal basicSalary, decimal allowances, decimal deductions)
    {
        TxtName.Text = name;
        TxtPhone.Text = phone;
        TxtEmail.Text = email;
        TxtCNIC.Text = cnic;
        TxtAddress.Text = address;
        TxtDesignation.Text = designation;
        DpJoining.SelectedDate = joiningDate;
        TxtBasicSalary.Text = basicSalary.ToString("N0");
        TxtAllowances.Text = allowances.ToString("N0");
        TxtDeductions.Text = deductions.ToString("N0");

        CmbCategory.SelectedIndex = category switch
        {
            EmployeeCategory.Kitchen => 0,
            EmployeeCategory.Service => 1,
            EmployeeCategory.Delivery => 2,
            EmployeeCategory.Management => 3,
            EmployeeCategory.Cleaning => 4,
            _ => 5,
        };

        CmbType.SelectedIndex = empType switch
        {
            EmploymentType.FullTime => 0,
            EmploymentType.PartTime => 1,
            EmploymentType.Contract => 2,
            _ => 3,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        if (BasicSalary <= 0)
        {
            MessageBox.Show("Basic salary is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtBasicSalary.Focus();
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
