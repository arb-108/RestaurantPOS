using System.Windows;
using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.WPF.Views;

public partial class UserFormWindow : Window
{
    public string UserFullName { get; private set; } = string.Empty;
    public string UserUsername { get; private set; } = string.Empty;
    public string UserPhone { get; private set; } = string.Empty;
    public string UserEmail { get; private set; } = string.Empty;
    public string UserPassword { get; private set; } = string.Empty;
    public string UserPin { get; private set; } = string.Empty;
    public Role? SelectedRole { get; private set; }

    private readonly bool _isEditing;
    private readonly int _editUserId;

    /// <summary>Create mode.</summary>
    public UserFormWindow(IEnumerable<Role> roles)
    {
        InitializeComponent();
        RoleCombo.ItemsSource = roles;
        Title = "Create New User";
        SaveBtn.Content = "Create User";
    }

    /// <summary>Edit mode — prefill fields.</summary>
    public UserFormWindow(IEnumerable<Role> roles, User user) : this(roles)
    {
        _isEditing = true;
        _editUserId = user.Id;
        Title = "Edit User";
        SaveBtn.Content = "Update User";

        FullNameBox.Text = user.FullName;
        UsernameBox.Text = user.Username;
        PhoneBox.Text = user.Phone ?? "";
        EmailBox.Text = user.Email ?? "";
        RoleCombo.SelectedItem = ((IEnumerable<Role>)RoleCombo.ItemsSource)
            .FirstOrDefault(r => r.Id == user.RoleId);
    }

    public bool IsEditing => _isEditing;
    public int EditUserId => _editUserId;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(FullNameBox.Text))
        {
            ShowError("Full Name is required."); return;
        }
        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            ShowError("Username is required."); return;
        }
        if (RoleCombo.SelectedItem is not Role)
        {
            ShowError("Please select a role."); return;
        }

        if (!_isEditing && string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            ShowError("Password is required for new users."); return;
        }
        if (!string.IsNullOrWhiteSpace(PasswordBox.Password) && PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError("Passwords do not match."); return;
        }
        if (!string.IsNullOrWhiteSpace(PinBox.Password) && (PinBox.Password.Length != 4 || !PinBox.Password.All(char.IsDigit)))
        {
            ShowError("PIN must be exactly 4 digits."); return;
        }

        UserFullName = FullNameBox.Text.Trim();
        UserUsername = UsernameBox.Text.Trim();
        UserPhone = PhoneBox.Text.Trim();
        UserEmail = EmailBox.Text.Trim();
        UserPassword = PasswordBox.Password;
        UserPin = PinBox.Password;
        SelectedRole = RoleCombo.SelectedItem as Role;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
