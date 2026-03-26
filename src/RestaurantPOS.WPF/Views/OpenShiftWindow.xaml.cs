using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace RestaurantPOS.WPF.Views;

public partial class OpenShiftWindow : Window
{
    public long OpeningBalancePaisa { get; private set; }
    public string ShiftNotes { get; private set; } = string.Empty;

    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    public OpenShiftWindow()
    {
        InitializeComponent();
        TxtOpeningBalance.PreviewTextInput += NumericOnly;
        DataObject.AddPastingHandler(TxtOpeningBalance, NumericPaste);
        TxtOpeningBalance.Focus();
        TxtOpeningBalance.SelectAll();
    }

    private static void NumericOnly(object sender, TextCompositionEventArgs e)
    {
        e.Handled = NumericRegex.IsMatch(e.Text);
    }

    private static void NumericPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (NumericRegex.IsMatch(text)) e.CancelCommand();
        }
        else e.CancelCommand();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void StartClick(object sender, RoutedEventArgs e)
    {
        decimal.TryParse(TxtOpeningBalance.Text.Replace(",", "").Trim(), out var amount);
        OpeningBalancePaisa = (long)(amount * 100);
        ShiftNotes = TxtNotes.Text.Trim();
        DialogResult = true;
    }
}
