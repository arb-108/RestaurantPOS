using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;

namespace RestaurantPOS.WPF.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly MainWindowViewModel _mainWindow;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinMask))]
    private string _pinEntry = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _showPinPad = true;

    [ObservableProperty]
    private bool _showPasswordLogin;

    /// <summary>Raised after a successful login so the host window can close itself.</summary>
    public event Action? LoginSucceeded;

    /// <summary>Masked PIN display (one '*' per entered digit) used by the PIN display TextBlock.</summary>
    public string PinMask => new string('*', PinEntry?.Length ?? 0);

    public LoginViewModel(IAuthService authService, MainWindowViewModel mainWindow)
    {
        _authService = authService;
        _mainWindow = mainWindow;
        Title = "Login";
    }

    [RelayCommand]
    private void AppendPin(string digit)
    {
        if (PinEntry.Length < 6)
            PinEntry += digit;
    }

    [RelayCommand]
    private void ClearPin()
    {
        PinEntry = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void BackspacePin()
    {
        if (PinEntry.Length > 0)
            PinEntry = PinEntry[..^1];
    }

    [RelayCommand]
    private async Task LoginWithPinAsync()
    {
        if (string.IsNullOrWhiteSpace(PinEntry))
        {
            ErrorMessage = "Please enter your PIN";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var user = await _authService.LoginWithPinAsync(PinEntry);
            if (user != null)
            {
                // Clear fields on success so the window is clean next time
                PinEntry = string.Empty;
                _mainWindow.OnUserLoggedIn(user);
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = "Invalid PIN. Please try again.";
                PinEntry = string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login failed: {ex.Message}";
            PinEntry = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginWithPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var user = await _authService.LoginAsync(Username, Password);
            if (user != null)
            {
                // Clear fields on success
                Username = string.Empty;
                Password = string.Empty;
                _mainWindow.OnUserLoggedIn(user);
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = "Invalid username or password.";
                Password = string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login failed: {ex.Message}";
            Password = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleLoginMode()
    {
        ShowPinPad = !ShowPinPad;
        ShowPasswordLogin = !ShowPasswordLogin;
        ErrorMessage = string.Empty;
        PinEntry = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
    }
}
