using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RestaurantPOS.WPF.ViewModels;

namespace RestaurantPOS.WPF.Views;

/// <summary>
/// Fixed-size login window (900×580) shown at startup and when Lock/NavigateToLogin is invoked.
/// Supports:
///  — Digit 0-9 keys (PIN mode)
///  — Backspace removes last digit
///  — Escape clears PIN
///  — Enter submits login (works via IsDefault="True" on Login button)
///  — Tab / Up / Down navigates between fields in password mode
/// </summary>
public partial class LoginWindow : System.Windows.Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Close this window when login succeeds
        _vm.LoginSucceeded += OnLoginSucceeded;
        Closed += (_, _) => _vm.LoginSucceeded -= OnLoginSucceeded;
    }

    private void OnLoginSucceeded()
    {
        // Clear inputs before closing so next time it re-opens (Lock) the state is empty
        _vm.PinEntry = string.Empty;
        _vm.Username = string.Empty;
        _vm.Password = string.Empty;
        if (TxtPassword != null) TxtPassword.Password = string.Empty;

        DialogResult = true;
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Load logo from Assets/Images/mainlogo.png (Content file, copied to output)
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "mainlogo.png");
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                LogoImage.Source = bmp;
            }
        }
        catch
        {
            // Logo is cosmetic — ignore load failures
        }

        // Reset state & focus appropriately
        _vm.PinEntry = string.Empty;
        _vm.ErrorMessage = string.Empty;

        if (_vm.ShowPinPad)
        {
            // Focus the Login button so Enter submits immediately once digits are typed
            BtnLoginPin?.Focus();
        }
        else
        {
            TxtUsername?.Focus();
        }
    }

    // ───────────────────────────────────────────
    //  GLOBAL KEY HANDLER — system keys drive the keypad
    // ───────────────────────────────────────────
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // PIN mode — digit input, backspace, escape
        if (_vm.ShowPinPad)
        {
            // Digits (top row and numpad)
            if (IsDigitKey(e.Key, out var digit))
            {
                if (_vm.AppendPinCommand.CanExecute(digit))
                    _vm.AppendPinCommand.Execute(digit);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                if (_vm.BackspacePinCommand.CanExecute(null))
                    _vm.BackspacePinCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete || e.Key == Key.Escape)
            {
                if (_vm.ClearPinCommand.CanExecute(null))
                    _vm.ClearPinCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (_vm.LoginWithPinCommand.CanExecute(null))
                    _vm.LoginWithPinCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        else
        {
            // Password mode — Up/Down to move between fields; Enter triggered by IsDefault="True"
            if (e.Key == Key.Down)
            {
                if (TxtUsername.IsKeyboardFocused)
                {
                    TxtPassword.Focus();
                    e.Handled = true;
                }
                else if (TxtPassword.IsKeyboardFocused)
                {
                    BtnLoginPwd.Focus();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Up)
            {
                if (TxtPassword.IsKeyboardFocused)
                {
                    TxtUsername.Focus();
                    e.Handled = true;
                }
                else if (BtnLoginPwd.IsKeyboardFocused)
                {
                    TxtPassword.Focus();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                _vm.Username = string.Empty;
                _vm.Password = string.Empty;
                TxtPassword.Password = string.Empty;
                TxtUsername.Focus();
                e.Handled = true;
            }
        }
    }

    private static bool IsDigitKey(Key key, out string digit)
    {
        // Top-row digits D0..D9
        if (key >= Key.D0 && key <= Key.D9)
        {
            digit = ((int)(key - Key.D0)).ToString();
            return true;
        }
        // Numpad digits NumPad0..NumPad9
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            digit = ((int)(key - Key.NumPad0)).ToString();
            return true;
        }
        digit = string.Empty;
        return false;
    }

    private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.Password = TxtPassword.Password;
    }

    // ───────────────────────────────────────────
    //  CUSTOM TITLE BAR HANDLERS (WindowStyle=None)
    // ───────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
