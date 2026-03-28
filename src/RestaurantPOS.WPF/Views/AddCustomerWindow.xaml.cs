using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RestaurantPOS.WPF.Views;

public partial class AddCustomerWindow : Window
{
    public string CustomerName => TxtName.Text.Trim();
    public string CustomerPhone => TxtMobile.Text.Trim();
    public string CustomerEmail => TxtEmail.Text.Trim();
    public string CustomerAddress => TxtAddress.Text.Trim();

    /// <summary>True when opened in edit/view mode for an existing matched customer.</summary>
    public bool IsEditMode { get; }

    private TextBox[] _fields = [];

    public AddCustomerWindow(string prefillPhone = "")
    {
        InitializeComponent();
        TxtMobile.Text = prefillPhone;

        _fields = [TxtName, TxtMobile, TxtEmail, TxtAddress];

        // Focus name field on load
        Loaded += (_, _) => TxtName.Focus();

        // Enter advances between fields, last field → Save button
        PreviewKeyDown += OnFormKeyDown;
    }

    /// <summary>
    /// Edit mode: pre-fill all fields and focus Save button.
    /// Used when phone is matched and user presses Enter.
    /// </summary>
    public AddCustomerWindow(string name, string phone, string email, string address)
    {
        InitializeComponent();
        IsEditMode = true;
        Title = "Customer Details";

        TxtName.Text = name;
        TxtMobile.Text = phone;
        TxtEmail.Text = email;
        TxtAddress.Text = address;

        _fields = [TxtName, TxtMobile, TxtEmail, TxtAddress];

        // Focus Save button on load so Enter immediately closes
        Loaded += (_, _) => BtnSave.Focus();

        PreviewKeyDown += OnFormKeyDown;
    }

    private void OnFormKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.FocusedElement is TextBox focused)
            {
                int idx = System.Array.IndexOf(_fields, focused);
                if (idx >= 0 && idx < _fields.Length - 1)
                {
                    // Advance to next field
                    _fields[idx + 1].Focus();
                    _fields[idx + 1].SelectAll();
                    e.Handled = true;
                }
                else if (idx == _fields.Length - 1)
                {
                    // Last field (Address) → focus Save button
                    BtnSave.Focus();
                    e.Handled = true;
                }
            }
            else if (Keyboard.FocusedElement is Button btn && btn == BtnSave)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtMobile.Text))
        {
            MessageBox.Show("Mobile number is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtMobile.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtAddress.Text))
        {
            MessageBox.Show("Address is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAddress.Focus();
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
