using System.Printing;
using System.Windows;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.WPF.Views;

public partial class PrinterFormWindow : Window
{
    public string PrinterDisplayName => TxtName.Text.Trim();
    public string PrinterTypeName => (CbType.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Receipt";
    public string ConnectionTypeName => (CbConnection.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "USB";
    public string SystemPrinterName => CbSystemPrinter.SelectedItem?.ToString() ?? "";
    public string PrinterAddress => TxtAddress.Text.Trim();
    public int PaperWidth => int.TryParse(TxtPaperWidth.Text, out var w) ? w : 80;
    public bool IsDefaultPrinter => ChkDefault.IsChecked == true;

    public PrinterFormWindow()
    {
        InitializeComponent();
        LoadSystemPrinters();
    }

    /// <summary>Pre-fill the form for editing an existing printer.</summary>
    public void LoadPrinter(Printer printer)
    {
        HeaderText.Text = "Edit Printer";
        Title = "Edit Printer";
        TxtName.Text = printer.Name;
        TxtAddress.Text = printer.Address ?? "";
        TxtPaperWidth.Text = printer.PaperWidth.ToString();
        ChkDefault.IsChecked = printer.IsDefault;

        // Select printer type
        foreach (System.Windows.Controls.ComboBoxItem item in CbType.Items)
        {
            if (item.Content?.ToString() == printer.Type.ToString())
            { CbType.SelectedItem = item; break; }
        }

        // Select connection type
        foreach (System.Windows.Controls.ComboBoxItem item in CbConnection.Items)
        {
            if (item.Content?.ToString() == printer.ConnectionType.ToString())
            { CbConnection.SelectedItem = item; break; }
        }

        // Select system printer
        if (!string.IsNullOrEmpty(printer.SystemPrinterName))
        {
            for (int i = 0; i < CbSystemPrinter.Items.Count; i++)
            {
                if (CbSystemPrinter.Items[i]?.ToString() == printer.SystemPrinterName)
                { CbSystemPrinter.SelectedIndex = i; break; }
            }
        }
    }

    private void LoadSystemPrinters()
    {
        try
        {
            var server = new LocalPrintServer();
            foreach (var q in server.GetPrintQueues())
                CbSystemPrinter.Items.Add(q.Name);
        }
        catch { /* No printers available */ }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Printer name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        if (CbSystemPrinter.SelectedItem == null)
        {
            MessageBox.Show("Please select a System Printer.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
