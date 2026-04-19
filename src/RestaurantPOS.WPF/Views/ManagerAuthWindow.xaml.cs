using System.Windows;
using RestaurantPOS.Application.Interfaces;

namespace RestaurantPOS.WPF.Views;

public partial class ManagerAuthWindow : Window
{
    private readonly IAuthService _authService;

    /// <summary>Full name of the admin/manager who authorized the action.</summary>
    public string AuthorizedBy { get; private set; } = string.Empty;

    public ManagerAuthWindow(IAuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        TxtUsername.Focus();
    }

    // Guards against re-entrant clicks while an auth request is in flight. Double-clicking
    // the Authorize button can launch two overlapping LoginAsync awaits which, combined with
    // the modal ShowDialog pump in the caller, was observed as the app freezing.
    private bool _isAuthorizing;

    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        if (_isAuthorizing) return;

        TxtError.Text = "";

        var username = TxtUsername.Text?.Trim();
        var password = TxtPassword.Password?.Trim();

        if (string.IsNullOrEmpty(username))
        {
            TxtError.Text = "Please enter a username.";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            TxtError.Text = "Please enter a password or PIN.";
            return;
        }

        _isAuthorizing = true;
        var senderButton = sender as System.Windows.Controls.Button;
        if (senderButton != null) senderButton.IsEnabled = false;

        try
        {
            // Try password login first
            var user = await _authService.LoginAsync(username, password);

            // If password login failed, try PIN login
            if (user == null)
                user = await _authService.LoginWithPinAsync(password);

            if (user == null)
            {
                TxtError.Text = "Invalid credentials. Please try again.";
                return;
            }

            // Check role is admin or manager
            if (user.RoleId != 1 && user.RoleId != 2)
            {
                TxtError.Text = "Only Admin or Manager can authorize this action.";
                return;
            }

            AuthorizedBy = user.FullName;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            TxtError.Text = $"Authentication error: {ex.Message}";
        }
        finally
        {
            _isAuthorizing = false;
            if (senderButton != null) senderButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
