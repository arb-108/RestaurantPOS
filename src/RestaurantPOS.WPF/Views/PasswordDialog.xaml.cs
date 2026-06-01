using System.Windows;
using System.Windows.Input;

namespace RestaurantPOS.WPF.Views
{
    public partial class PasswordDialog : Window
    {
        private const string CorrectPassword = "arb108";
        private int _attempts = 0;

        public PasswordDialog()
        {
            InitializeComponent();
            Loaded += (_, __) => PasswordBox.Focus();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => TryAccept();
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TryAccept();
            if (ErrorPanel.Visibility == Visibility.Visible)
                ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private void TryAccept()
        {
            if (PasswordBox.Password == CorrectPassword)
            {
                DialogResult = true;
            }
            else
            {
                _attempts++;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = _attempts >= 3
                    ? $"Access denied after {_attempts} attempts. Contact your administrator."
                    : "Incorrect Password";
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }
    }
}