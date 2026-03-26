using System.Windows;

namespace RestaurantPOS.WPF.Views;

public partial class QuickSettleWindow : Window
{
    /// <summary>Payment method ID: 1=Cash, 2=Card, 3=Online, 4=COD</summary>
    public int SelectedPaymentMethodId { get; private set; } = 1;

    private readonly long _amount;

    public QuickSettleWindow(string orderNumber, long amountPaisa)
    {
        InitializeComponent();
        _amount = amountPaisa;

        TxtOrderNumber.Text = orderNumber;
        TxtAmount.Text = $"Rs. {amountPaisa / 100m:N0}";
    }

    private void Settle_Click(object sender, RoutedEventArgs e)
    {
        SelectedPaymentMethodId = RbCash.IsChecked == true ? 1
            : RbCard.IsChecked == true ? 2
            : RbOnline.IsChecked == true ? 3
            : RbCOD.IsChecked == true ? 4
            : 1;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
