using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestaurantPOS.Infrastructure.Data;
using RestaurantPOS.Infrastructure.Services;
using RestaurantPOS.WPF.ViewModels;
using Serilog;
using AppInterfaces = RestaurantPOS.Application.Interfaces;

namespace RestaurantPOS.WPF;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Configure Serilog
        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RestaurantPOS", "logs", "pos-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        Log.Information("Application starting...");

        // Global exception handler
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled exception");
            var msg = args.Exception.InnerException?.Message ?? args.Exception.Message;
            System.Windows.MessageBox.Show($"An unexpected error occurred:\n{msg}",
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Database
                var connectionString = DatabaseConfig.GetConnectionString();
                services.AddDbContext<PosDbContext>(options =>
                {
                    options.UseSqlite(connectionString);
                    options.AddInterceptors(new SqlitePragmaInterceptor());
                }, ServiceLifetime.Singleton);

                // Services (Singleton for desktop app — no per-request scope needed)
                services.AddSingleton<AppInterfaces.IAuthService, AuthService>();
                services.AddSingleton<AppInterfaces.IMenuService, MenuService>();
                services.AddSingleton<AppInterfaces.IOrderService, OrderService>();
                services.AddSingleton<AppInterfaces.ITableService, TableService>();
                services.AddSingleton<AppInterfaces.ICustomerService, CustomerService>();
                services.AddSingleton<AppInterfaces.ISettingsService, SettingsService>();
                services.AddSingleton<AppInterfaces.IKitchenService, KitchenService>();
                services.AddSingleton<AppInterfaces.IReportService, ReportService>();
                services.AddSingleton<RestaurantPOS.Printing.IPrintService, RestaurantPOS.Printing.PrintService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<LoginViewModel>();
                services.AddSingleton<MainPOSViewModel>();        // Singleton: preserves orders/cart across navigation
                services.AddTransient<KitchenDisplayViewModel>();
                services.AddTransient<OrderHistoryViewModel>();
                services.AddTransient<MenuManagementViewModel>();
                services.AddTransient<ReportsViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ExpenseManagementViewModel>();
                services.AddTransient<StockManagementViewModel>();
                services.AddTransient<ShiftManagementViewModel>();
                services.AddTransient<CustomerManagementViewModel>();
                services.AddTransient<EmployeeManagementViewModel>();

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Run migrations / ensure DB schema
        try
        {
            var db = _host.Services.GetRequiredService<PosDbContext>();

            // Try migrations first
            try { await db.Database.MigrateAsync(); Log.Information("Database migration completed"); }
            catch
            {
                // No migrations — try EnsureCreated
                await db.Database.EnsureCreatedAsync();
                Log.Information("Database created with EnsureCreated");
            }

            // Add AccessLevel column to RolePermissions if missing
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT AccessLevel FROM RolePermissions LIMIT 0");
            }
            catch
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE RolePermissions ADD COLUMN AccessLevel INTEGER NOT NULL DEFAULT 5");
                    Log.Information("Added AccessLevel column to RolePermissions");
                }
                catch { /* column may already exist */ }
            }

            // Seed Permissions table if empty
            try
            {
                var permCount = await db.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM Permissions");
                var hasPerms = await db.Permissions.AnyAsync();
                if (!hasPerms)
                {
                    await SeedPermissionsAsync(db);
                    Log.Information("Seeded permissions data");
                }
            }
            catch { }

            // Validate schema: check a column from a recent change
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT StockCategory FROM Ingredients LIMIT 0");
            }
            catch
            {
                // Schema mismatch — close everything, delete DB, recreate
                Log.Warning("Schema mismatch detected, recreating database");
                var dbPath = DatabaseConfig.GetDatabasePath();

                // Close connection and clear SQLite connection pool to release file lock
                await db.Database.CloseConnectionAsync();
                db.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                // Small delay to ensure file handles are released
                await Task.Delay(200);

                foreach (var f in new[] { dbPath, dbPath + "-shm", dbPath + "-wal" })
                {
                    if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
                }
                Log.Information("Old database deleted");

                // Rebuild host with fresh DbContext
                _host.Dispose();
                _host = Host.CreateDefaultBuilder()
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        var cs = DatabaseConfig.GetConnectionString();
                        services.AddDbContext<PosDbContext>(options =>
                        {
                            options.UseSqlite(cs);
                            options.AddInterceptors(new SqlitePragmaInterceptor());
                        }, ServiceLifetime.Singleton);

                        services.AddSingleton<AppInterfaces.IAuthService, AuthService>();
                        services.AddSingleton<AppInterfaces.IMenuService, MenuService>();
                        services.AddSingleton<AppInterfaces.IOrderService, OrderService>();
                        services.AddSingleton<AppInterfaces.ITableService, TableService>();
                        services.AddSingleton<AppInterfaces.ICustomerService, CustomerService>();
                        services.AddSingleton<AppInterfaces.ISettingsService, SettingsService>();
                        services.AddSingleton<AppInterfaces.IKitchenService, KitchenService>();
                        services.AddSingleton<AppInterfaces.IReportService, ReportService>();
                        services.AddSingleton<RestaurantPOS.Printing.IPrintService, RestaurantPOS.Printing.PrintService>();

                        services.AddSingleton<MainWindowViewModel>();
                        services.AddTransient<LoginViewModel>();
                        services.AddSingleton<MainPOSViewModel>();
                        services.AddTransient<KitchenDisplayViewModel>();
                        services.AddTransient<OrderHistoryViewModel>();
                        services.AddTransient<MenuManagementViewModel>();
                        services.AddTransient<ReportsViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        services.AddTransient<ExpenseManagementViewModel>();
                        services.AddTransient<StockManagementViewModel>();
                        services.AddTransient<ShiftManagementViewModel>();
                        services.AddTransient<CustomerManagementViewModel>();
                        services.AddTransient<EmployeeManagementViewModel>();
                        services.AddSingleton<MainWindow>();
                    })
                    .Build();

                var db2 = _host.Services.GetRequiredService<PosDbContext>();
                await db2.Database.EnsureCreatedAsync();
                Log.Information("Database recreated after schema reset");
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to create database");
            System.Windows.MessageBox.Show($"Failed to initialize database:\n{ex.Message}",
                "Fatal Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = mainVm;

        // Start with login
        mainVm.NavigateTo<LoginViewModel>();

        mainWindow.Show();
        base.OnStartup(e);
    }

    private static async Task SeedPermissionsAsync(PosDbContext db)
    {
        var perms = new (string Name, string Module, string Desc)[]
        {
            ("Management", "General", "Access management features"),
            ("Settings", "General", "Modify system settings"),
            ("End of Day", "General", "Run end-of-day process"),
            ("User Profile", "General", "View/edit own profile"),
            ("Design Floor Plans", "General", "Edit floor plan layout"),
            ("View Reports", "General", "Access reports module"),
            ("View All Open Orders", "Sales", "See all open orders"),
            ("Void Order", "Sales", "Void an entire order"),
            ("Void Item", "Sales", "Void individual items"),
            ("Lock Sale", "Sales", "Lock a sale/hold order"),
            ("Unlock Sale", "Sales", "Unlock a held sale"),
            ("Split Order", "Sales", "Split order between bills"),
            ("Reprint Receipt", "Sales", "Reprint any receipt"),
            ("Starting Cash", "Sales", "Set starting cash amount"),
            ("Open Cash Drawer", "Sales", "Open the cash drawer"),
            ("Apply Discount", "Sales", "Apply discounts to orders"),
            ("Zero Stock Quantity Sale", "Sales", "Sell items with zero stock"),
            ("Manage Stock", "Inventory", "Adjust stock levels"),
            ("Manage Menu", "Inventory", "Edit menu items/categories"),
            ("Manage Suppliers", "Inventory", "Manage suppliers/expenses"),
            ("Manage Employees", "HR", "Manage employee records"),
            ("Manage Payroll", "HR", "Process payroll"),
            ("Manage Customers", "HR", "Manage customer records"),
        };

        foreach (var (name, module, desc) in perms)
        {
            db.Permissions.Add(new RestaurantPOS.Domain.Entities.Permission
            {
                Name = name, Module = module, Description = desc
            });
        }
        await db.SaveChangesAsync();

        // Give admin role (Id=1) all permissions
        var allPerms = await db.Permissions.ToListAsync();
        foreach (var p in allPerms)
        {
            db.RolePermissions.Add(new RestaurantPOS.Domain.Entities.RolePermission
            {
                RoleId = 1, PermissionId = p.Id, AccessLevel = 5
            });
        }
        await db.SaveChangesAsync();
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
