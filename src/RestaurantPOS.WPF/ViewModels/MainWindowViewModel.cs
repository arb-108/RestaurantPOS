using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RestaurantPOS.WPF.ViewModels;

public partial class MainWindowViewModel : BaseViewModel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, BaseViewModel> _viewModelCache = [];

    [ObservableProperty]
    private BaseViewModel? _currentView;

    [ObservableProperty]
    private string _currentUser = "Admin";

    [ObservableProperty]
    private string _currentDate = DateTime.Now.ToString("dd MMM yyyy");

    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _restaurantName = "KFC Restaurant";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isShiftActive;

    [ObservableProperty]
    private string _shiftIndicatorText = "No Shift";

    /// <summary>The currently logged-in user object (for role-based features).</summary>
    public Domain.Entities.User? LoggedInUser { get; private set; }

    private System.Windows.Threading.DispatcherTimer? _clockTimer;

    public MainWindowViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Title = "Restaurant POS";
        StartClock();
    }

    private void StartClock()
    {
        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) =>
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            CurrentDate = DateTime.Now.ToString("dd MMM yyyy");
        };
        _clockTimer.Start();
    }

    public void NavigateTo<T>() where T : BaseViewModel
    {
        var type = typeof(T);
        if (!_viewModelCache.TryGetValue(type, out var vm))
        {
            vm = (BaseViewModel)_serviceProvider.GetService(type)!;
            _viewModelCache[type] = vm;
        }
        CurrentView = vm;
    }

    public void NavigateTo(BaseViewModel viewModel)
    {
        CurrentView = viewModel;
    }

    [RelayCommand]
    private void NavigateToPos()
    {
        NavigateTo<MainPOSViewModel>();
    }

    [RelayCommand]
    private void NavigateToOrders()
    {
        NavigateTo<OrderHistoryViewModel>();
    }

    [RelayCommand]
    private void NavigateToKitchen()
    {
        NavigateTo<KitchenDisplayViewModel>();
    }

    [RelayCommand]
    private void NavigateToMenu()
    {
        NavigateTo<MenuManagementViewModel>();
    }


    [RelayCommand]
    private void NavigateToExpenses()
    {
        NavigateTo<ExpenseManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        NavigateTo<ReportsViewModel>();
    }

    [RelayCommand]
    private void NavigateToStock()
    {
        NavigateTo<StockManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToShift()
    {
        NavigateTo<ShiftManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateTo<SettingsViewModel>();
    }

    [RelayCommand]
    private void NavigateToLogin()
    {
        IsLoggedIn = false;
        NavigateTo<LoginViewModel>();
    }

    [RelayCommand]
    private void Lock()
    {
        IsLoggedIn = false;
        NavigateTo<LoginViewModel>();
    }

    public void OnUserLoggedIn(Domain.Entities.User user)
    {
        LoggedInUser = user;
        CurrentUser = user.FullName;
        IsLoggedIn = true;

        // Set the logged-in user on MainPOSViewModel (singleton)
        var posVm = (MainPOSViewModel)_serviceProvider.GetService(typeof(MainPOSViewModel))!;
        posVm.SetCurrentUser(user);

        // Check if there's an active shift
        CheckActiveShiftAsync();

        NavigateTo<MainPOSViewModel>();
    }

    private async void CheckActiveShiftAsync()
    {
        try
        {
            var db = _serviceProvider.GetService(typeof(RestaurantPOS.Infrastructure.Data.PosDbContext))
                as RestaurantPOS.Infrastructure.Data.PosDbContext;
            if (db == null) return;

            var active = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Shifts, s => s.EndedAt == null && s.IsActive);
            UpdateShiftStatus(active != null, active?.StartedAt.ToLocalTime());
        }
        catch { /* non-critical */ }
    }

    public void UpdateShiftStatus(bool isActive, DateTime? startedAt)
    {
        IsShiftActive = isActive;
        ShiftIndicatorText = isActive && startedAt.HasValue
            ? $"Shift: {startedAt.Value:hh:mm tt}"
            : "No Shift";
    }
}
