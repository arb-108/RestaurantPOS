using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPOS.Application.Interfaces;

namespace RestaurantPOS.WPF.ViewModels;

public partial class MainWindowViewModel : BaseViewModel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthService _authService;
    private readonly Dictionary<Type, BaseViewModel> _viewModelCache = [];

    [ObservableProperty]
    private BaseViewModel? _currentView;

    [ObservableProperty]
    private string _currentUser = "Admin";

    [ObservableProperty]
    private string _currentDate = DateTime.Now.ToString("dd MMM yyyy");

    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("hh:mm:ss tt");

    [ObservableProperty]
    private string _restaurantName = "KFC Restaurant";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isShiftActive;

    [ObservableProperty]
    private string _shiftIndicatorText = "No Shift";

    // ═══════════════════════════════════════════════
    //  NAV BUTTON VISIBILITY (permission-based)
    // ═══════════════════════════════════════════════

    [ObservableProperty] private bool _canAccessMenu = true;
    [ObservableProperty] private bool _canAccessExpenses = true;
    [ObservableProperty] private bool _canAccessStock = true;
    [ObservableProperty] private bool _canAccessReports = true;
    [ObservableProperty] private bool _canAccessCustomers = true;
    [ObservableProperty] private bool _canAccessReturns = true;
    [ObservableProperty] private bool _canAccessEmployees = true;
    [ObservableProperty] private bool _canAccessSettings = true;
    [ObservableProperty] private bool _canAccessShift = true;
    [ObservableProperty] private bool _isCashierRole;

    /// <summary>The currently logged-in user object (for role-based features).</summary>
    public Domain.Entities.User? LoggedInUser { get; private set; }

    private System.Windows.Threading.DispatcherTimer? _clockTimer;

    public MainWindowViewModel(IServiceProvider serviceProvider, IAuthService authService)
    {
        _serviceProvider = serviceProvider;
        _authService = authService;
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
            CurrentTime = DateTime.Now.ToString("hh:mm:ss tt");
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
        if (!RequirePermission("Issue refunds", "Void / cancel orders")) return;
        NavigateTo<OrderHistoryViewModel>();
    }

    [RelayCommand]
    private void NavigateToCashierOrderSearch()
    {
        NavigateTo<CashierOrderSearchViewModel>();
    }

    [RelayCommand]
    private void NavigateToKitchen()
    {
        NavigateTo<KitchenDisplayViewModel>();
    }

    [RelayCommand]
    private void NavigateToMenu()
    {
        if (!RequirePermission("Manage menu items")) return;
        NavigateTo<MenuManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToExpenses()
    {
        if (!RequirePermission("Manage expenses", "Manage suppliers")) return;
        NavigateTo<ExpenseManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        if (!RequirePermission("View reports & analytics")) return;
        NavigateTo<ReportsViewModel>();
    }

    [RelayCommand]
    private void NavigateToStock()
    {
        if (!RequirePermission("Manage stock & recipes")) return;
        NavigateTo<StockManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToShift()
    {
        if (!RequirePermission("Manage employees", "System app settings")) return;
        NavigateTo<ShiftManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToCustomers()
    {
        if (!RequirePermission("Manage customers & loyalty")) return;
        NavigateTo<CustomerManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToEmployees()
    {
        if (!RequirePermissionStrict("Manage employees")) return;
        NavigateTo<EmployeeManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        if (!RequirePermission("System app settings", "Manage printers & terminals", "Manage tax & discounts", "Manage users & roles")) return;
        NavigateTo<SettingsViewModel>();
    }

    /// <summary>
    /// Checks if the current user has any of the required permissions (level >= 3).
    /// If not, shows ManagerAuth popup. Returns true if authorized.
    /// </summary>
    private bool RequirePermission(params string[] permissions)
    {
        foreach (var perm in permissions)
        {
            if (_authService.HasPermission(perm, minimumLevel: 3))
                return true;
        }

        // User lacks permission — show admin/manager auth popup
        var authWindow = new Views.ManagerAuthWindow(_authService);
        authWindow.Owner = System.Windows.Application.Current.MainWindow;
        return authWindow.ShowDialog() == true;
    }

    /// <summary>
    /// Strict check — requires level 5 (Admin only). Manager and Cashier get auth popup.
    /// </summary>
    private bool RequirePermissionStrict(params string[] permissions)
    {
        foreach (var perm in permissions)
        {
            if (_authService.HasPermission(perm, minimumLevel: 5))
                return true;
        }

        var authWindow = new Views.ManagerAuthWindow(_authService);
        authWindow.Owner = System.Windows.Application.Current.MainWindow;
        return authWindow.ShowDialog() == true;
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

    public async void OnUserLoggedIn(Domain.Entities.User user)
    {
        LoggedInUser = user;
        CurrentUser = user.FullName;
        IsLoggedIn = true;

        // Load permissions for the logged-in user
        await _authService.LoadPermissionsForUserAsync(user);

        // Set nav button visibility based on permissions
        ApplyNavPermissions();

        // Set the logged-in user on MainPOSViewModel (singleton)
        var posVm = (MainPOSViewModel)_serviceProvider.GetService(typeof(MainPOSViewModel))!;
        posVm.SetCurrentUser(user, _authService);

        // Check if there's an active shift
        CheckActiveShiftAsync();

        NavigateTo<MainPOSViewModel>();
    }

    private void ApplyNavPermissions()
    {
        // Menu Settings — requires Manage menu items (level >= 1)
        CanAccessMenu = _authService.HasPermission("Manage menu items");

        // Expenses — requires Manage expenses or Manage suppliers
        CanAccessExpenses = _authService.HasPermission("Manage expenses") || _authService.HasPermission("Manage suppliers");

        // Stock — requires Manage stock & recipes
        CanAccessStock = _authService.HasPermission("Manage stock & recipes");

        // Reports — requires View reports & analytics
        CanAccessReports = _authService.HasPermission("View reports & analytics");

        // Customers — requires Manage customers & loyalty
        CanAccessCustomers = _authService.HasPermission("Manage customers & loyalty");

        // Returns (Order History) — requires Issue refunds or Void / cancel orders
        CanAccessReturns = _authService.HasPermission("Issue refunds") || _authService.HasPermission("Void / cancel orders");

        // Employees — requires Manage employees
        CanAccessEmployees = _authService.HasPermission("Manage employees");

        // Shift — only Admin/Manager (Cashier has no Manage employees = proxy for management role)
        CanAccessShift = _authService.HasPermission("Manage employees")
            || _authService.HasPermission("System app settings");

        // Settings — requires at least one config/admin permission
        CanAccessSettings = _authService.HasPermission("System app settings")
            || _authService.HasPermission("Manage printers & terminals")
            || _authService.HasPermission("Manage tax & discounts")
            || _authService.HasPermission("Manage users & roles");

        // Cashier-specific: show "Orders History" button only for cashier role
        IsCashierRole = LoggedInUser?.Role?.Name?.ToLowerInvariant() == "cashier";
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
