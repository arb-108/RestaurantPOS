using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class GeneratePayrollWindow : Window
{
    public ObservableCollection<PayrollGenerateItem> Items { get; } = [];

    public List<PayrollGenerateResult> PayrollItems { get; } = [];

    public GeneratePayrollWindow(List<Employee> employees, int month, int year)
    {
        InitializeComponent();

        CmbMonth.SelectedIndex = month - 1;
        TxtYear.Text = year.ToString();

        foreach (var emp in employees.Where(e => !e.LeavingDate.HasValue))
        {
            Items.Add(new PayrollGenerateItem
            {
                EmployeeId = emp.Id,
                Name = emp.Name,
                Category = emp.Category.ToString(),
                BasicSalary = emp.BasicSalary,
                Allowances = emp.Allowances,
                Deductions = emp.Deductions,
                IsSelected = true
            });
        }

        PayrollGrid.ItemsSource = Items;
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtYear.Text, out var year) || year < 2020 || year > 2100)
        {
            MessageBox.Show("Enter a valid year.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var month = CmbMonth.SelectedIndex + 1;

        foreach (var item in Items.Where(i => i.IsSelected))
        {
            PayrollItems.Add(new PayrollGenerateResult
            {
                EmployeeId = item.EmployeeId,
                Month = month,
                Year = year,
                BasicSalary = item.BasicSalary,
                Allowances = item.Allowances,
                Deductions = item.Deductions,
                Bonus = item.BonusPaisa,
                Advance = item.AdvancePaisa,
            });
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

public class PayrollGenerateItem : INotifyPropertyChanged
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public long BasicSalary { get; set; }
    public long Allowances { get; set; }
    public long Deductions { get; set; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    private string _bonusInput = "0";
    public string BonusInput
    {
        get => _bonusInput;
        set
        {
            _bonusInput = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BonusInput)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetDisplay)));
        }
    }

    private string _advanceInput = "0";
    public string AdvanceInput
    {
        get => _advanceInput;
        set
        {
            _advanceInput = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AdvanceInput)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetDisplay)));
        }
    }

    public long BonusPaisa => decimal.TryParse(BonusInput, out var v) ? (long)(v * 100) : 0;
    public long AdvancePaisa => decimal.TryParse(AdvanceInput, out var v) ? (long)(v * 100) : 0;

    public string BasicDisplay => $"Rs. {BasicSalary / 100m:N0}";
    public string AllowDisplay => $"Rs. {Allowances / 100m:N0}";
    public string DeductDisplay => $"Rs. {Deductions / 100m:N0}";
    public string NetDisplay
    {
        get
        {
            var net = BasicSalary + Allowances - Deductions + BonusPaisa - AdvancePaisa;
            return $"Rs. {net / 100m:N0}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class PayrollGenerateResult
{
    public int EmployeeId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public long BasicSalary { get; set; }
    public long Allowances { get; set; }
    public long Deductions { get; set; }
    public long Bonus { get; set; }
    public long Advance { get; set; }
}
