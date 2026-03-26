using System.Windows;

namespace RestaurantPOS.WPF.Views;

public partial class OrderNoteWindow : Window
{
    public string Note { get; private set; } = string.Empty;

    public OrderNoteWindow(string existingNote = "")
    {
        InitializeComponent();
        TxtNote.Text = existingNote;
        TxtNote.Focus();
        TxtNote.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Note = TxtNote.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
