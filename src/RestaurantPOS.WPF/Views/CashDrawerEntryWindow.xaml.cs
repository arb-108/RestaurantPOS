using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace RestaurantPOS.WPF.Views;

public partial class CashDrawerEntryWindow : Window
{
    public long AmountPaisa { get; private set; }
    public string EntryDescription { get; private set; } = string.Empty;

    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    public CashDrawerEntryWindow(string title)
    {
        InitializeComponent();
        TxtTitle.Text = title;
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

        AmountPaisa = (long)(amount * 100);
        EntryDescription = TxtDesc.Text.Trim();
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
