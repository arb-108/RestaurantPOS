using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RestaurantPOS.WPF.Views;

public partial class CloseShiftWindow : Window
{
    private readonly long _expectedBalancePaisa;
    private static readonly Regex NumericRegex = new(@"[^0-9.]", RegexOptions.Compiled);

    public long CountedCashPaisa { get; private set; }
    public string ClosingNotes { get; private set; } = string.Empty;

    public CloseShiftWindow(
        DateTime shiftStartLocal,
        string userName,
        int orderCount,
        long totalSalesPaisa,
        long cashSalesPaisa,
        long cardSalesPaisa,
        long openingBalancePaisa,
        long manualPayInsPaisa,
        long payOutsPaisa,
        long expectedBalancePaisa)
    {
        InitializeComponent();
        _expectedBalancePaisa = expectedBalancePaisa;

        TxtCountedCash.PreviewTextInput += NumericOnly;
        DataObject.AddPastingHandler(TxtCountedCash, NumericPaste);

        TxtShiftInfo.Text = $"Started {shiftStartLocal:dd MMM yyyy hh:mm tt} by {userName}";
        TxtOrders.Text = orderCount.ToString();
        TxtTotalSales.Text = FormatRs(totalSalesPaisa);
        TxtCashSales.Text = FormatRs(cashSalesPaisa);
        TxtCardSales.Text = FormatRs(cardSalesPaisa);
        TxtOpening.Text = FormatRs(openingBalancePaisa);
        TxtCashIn.Text = $"+{FormatRs(cashSalesPaisa)}";
        TxtPayIns.Text = $"+{FormatRs(manualPayInsPaisa)}";
        TxtPayOuts.Text = $"-{FormatRs(payOutsPaisa)}";
        TxtExpected.Text = FormatRs(expectedBalancePaisa);

        UpdateDiscrepancy();
        TxtCountedCash.Focus();
        TxtCountedCash.SelectAll();
    }

    private void TxtCountedCash_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDiscrepancy();
    }

    private void UpdateDiscrepancy()
    {
        if (TxtDiscrepancy == null) return;

        decimal.TryParse(TxtCountedCash.Text.Replace(",", "").Trim(), out var counted);
        var countedPaisa = (long)(counted * 100);
        var diff = countedPaisa - _expectedBalancePaisa;

        if (diff == 0)
        {
            TxtDiscrepancy.Text = "Rs 0 — Perfect!";
            TxtDiscrepancy.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
            DiscrepancyBorder.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x05, 0x96, 0x69));
        }
        else if (diff > 0)
        {
            TxtDiscrepancy.Text = $"+Rs {diff / 100m:N0} (Over)";
            TxtDiscrepancy.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            DiscrepancyBorder.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x21, 0x96, 0xF3));
        }
        else
        {
            TxtDiscrepancy.Text = $"-Rs {Math.Abs(diff) / 100m:N0} (Short)";
            TxtDiscrepancy.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            DiscrepancyBorder.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xDC, 0x26, 0x26));
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Are you sure you want to close this shift?",
            "Confirm Close", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        decimal.TryParse(TxtCountedCash.Text.Replace(",", "").Trim(), out var counted);
        CountedCashPaisa = (long)(counted * 100);
        ClosingNotes = TxtNotes.Text.Trim();
        DialogResult = true;
    }

    private static string FormatRs(long paisa) => $"Rs {paisa / 100m:N0}";

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
