using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace RestaurantPOS.WPF.Views;

public partial class CashDrawerEntryWindow : Window
{
    public long AmountPaisa { get; private set; }
    public string EntryDescription { get; private set; } = string.Empty;
    public bool AddToExpenses { get; private set; }

    private readonly bool _isPayOut;
    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    public CashDrawerEntryWindow(string title, bool isPayOut = false)
    {
        InitializeComponent();
        Title = title;
        _isPayOut = isPayOut;

        // Show expense checkbox only for PayOut
        if (isPayOut)
        {
            ChkExpense.Visibility = Visibility.Visible;
            LblDesc.Text = "DESCRIPTION *";
        }

        TxtAmount.PreviewTextInput += NumericOnly;
        DataObject.AddPastingHandler(TxtAmount, NumericPaste);
        TxtAmount.Focus();
        TxtAmount.SelectAll();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        decimal.TryParse(TxtAmount.Text.Replace(",", "").Trim(), out var amount);
        if (amount <= 0)
        {
            MessageBox.Show("Please enter a valid amount.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmount.Focus();
            return;
        }

        // Description is mandatory for PayOut
        if (_isPayOut && string.IsNullOrWhiteSpace(TxtDesc.Text))
        {
            MessageBox.Show("Description is required for Pay Out.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDesc.Focus();
            return;
        }

        AmountPaisa = (long)(amount * 100);
        EntryDescription = TxtDesc.Text.Trim();
        AddToExpenses = _isPayOut && ChkExpense.IsChecked == true;
        DialogResult = true;
    }

    private static void NumericOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = NumericRegex.IsMatch(e.Text);

    private static void NumericPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (NumericRegex.IsMatch(text)) e.CancelCommand();
        }
        else e.CancelCommand();
    }
}
