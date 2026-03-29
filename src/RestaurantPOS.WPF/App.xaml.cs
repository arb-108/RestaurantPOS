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

            // Migrate permissions & roles to the new 3-role / 27-permission system
            try
            {
                await MigratePermissionSystemAsync(db);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Permission migration warning (non-fatal)");
            }

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

    /// <summary>
    /// Migrates existing databases to the new 3-role, 27-permission system.
    /// Idempotent — safe to run on every startup.
    /// </summary>
    private static async Task MigratePermissionSystemAsync(PosDbContext db)
    {
        // Check if migration is needed: look for one of the new permission names
        var hasNewPerms = await db.Permissions.AnyAsync(p => p.Name == "Login / authentication");
        if (hasNewPerms) return; // Already migrated

        Log.Information("Migrating to new 3-role / 27-permission system...");

        // ── 1. Clear old RolePermissions and Permissions ──
        await db.Database.ExecuteSqlRawAsync("DELETE FROM RolePermissions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Permissions");

        // ── 2. Deactivate old roles (waiter=4, kitchen=5) and ensure 3 roles exist ──
        await db.Database.ExecuteSqlRawAsync("UPDATE Roles SET IsActive = 0 WHERE Id IN (4, 5)");

        // Ensure admin/manager/cashier exist with correct names
        var roleNames = new (int Id, string Name, string Desc)[]
        {
            (1, "admin", "System Administrator"),
            (2, "manager", "Restaurant Manager"),
            (3, "cashier", "Cashier / POS Operator"),
        };
        foreach (var (id, name, desc) in roleNames)
        {
            var role = await db.Roles.FindAsync(id);
            if (role != null)
            {
                role.Name = name;
                role.Description = desc;
                role.IsActive = true;
            }
            else
            {
                db.Roles.Add(new RestaurantPOS.Domain.Entities.Role
                {
                    Id = id, Name = name, Description = desc
                });
            }
        }
        await db.SaveChangesAsync();

        // Reassign any users on old roles (waiter/kitchen) to cashier
        await db.Database.ExecuteSqlRawAsync("UPDATE Users SET RoleId = 3 WHERE RoleId IN (4, 5)");

        // ── 3. Insert the 27 new permissions ──
        var perms = new (int Id, string Name, string Module, string Desc)[]
        {
            // General (1-4)
            (1,  "Login / authentication",      "General",      "Login and authenticate"),
            (2,  "View own profile",            "General",      "View/edit own profile"),
            (3,  "Manage users & roles",        "General",      "Create/edit users and roles"),
            (4,  "View audit log",              "General",      "View system audit trail"),
            // Sales (5-14)
            (5,  "Create & process orders",     "Sales",        "Create and process orders"),
            (6,  "Apply discounts",             "Sales",        "Apply discounts to orders"),
            (7,  "Void / cancel orders",        "Sales",        "Void or cancel orders"),
            (8,  "Hold & recall orders",        "Sales",        "Hold and recall orders"),
            (9,  "Process payments",            "Sales",        "Process order payments"),
            (10, "Issue refunds",               "Sales",        "Issue refunds on orders"),
            (11, "Manage tables & sessions",    "Sales",        "Manage table assignments"),
            (12, "Manage customers & loyalty",  "Sales",        "Manage customers and loyalty"),
            (13, "Open / close shift",          "Sales",        "Open and close shifts"),
            (14, "Cash drawer operations",      "Sales",        "Cash drawer open/close"),
            // Inventory (15-18)
            (15, "View menu & categories",      "Inventory",    "View menu items and categories"),
            (16, "Manage menu items",           "Inventory",    "Add/edit/delete menu items"),
            (17, "Manage stock & recipes",      "Inventory",    "Manage stock and recipes"),
            (18, "View kitchen orders",         "Inventory",    "View kitchen display orders"),
            // HR & Finance (19-22)
            (19, "Manage employees",            "HR & Finance", "Manage employee records"),
            (20, "Manage suppliers",            "HR & Finance", "Manage supplier records"),
            (21, "Manage expenses",             "HR & Finance", "Manage expenses"),
            (22, "Generate payroll",            "HR & Finance", "Generate payroll"),
            // Config (23-27)
            (23, "View reports & analytics",    "Config",       "Access reports and analytics"),
            (24, "Manage tax & discounts",      "Config",       "Manage tax rates and discounts"),
            (25, "Manage printers & terminals", "Config",       "Manage printers and terminals"),
            (26, "System app settings",         "Config",       "System application settings"),
            (27, "Floor plan & table config",   "Config",       "Floor plan and table configuration"),
        };

        // Use raw SQL with explicit IDs to match seed data
        foreach (var (id, name, module, desc) in perms)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Permissions (Id, Name, Module, Description, IsActive, CreatedAt, UpdatedAt) VALUES ({0}, {1}, {2}, {3}, 1, {4}, {4})",
                id, name, module, desc, DateTime.UtcNow);
        }

        // ── 4. Insert RolePermission rows ──
        // Admin: all 27 at level 5
        for (int i = 1; i <= 27; i++)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RolePermissions (RoleId, PermissionId, AccessLevel) VALUES (1, {0}, 5)", i);
        }

        // Manager
        var managerPerms = new (int PermId, int Level)[]
        {
            (1, 5), (2, 5), (4, 4),                                     // General
            (5, 5), (6, 5), (7, 5), (8, 5), (9, 5), (10, 5),           // Sales
            (11, 5), (12, 5), (13, 5), (14, 5),
            (15, 5), (16, 5), (17, 5), (18, 5),                         // Inventory
            (19, 4), (20, 4), (21, 4),                                   // HR
            (23, 5), (27, 4),                                            // Config
        };
        foreach (var (permId, level) in managerPerms)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RolePermissions (RoleId, PermissionId, AccessLevel) VALUES (2, {0}, {1})", permId, level);
        }

        // Cashier
        var cashierPerms = new (int PermId, int Level)[]
        {
            (1, 5), (2, 5),                                             // General
            (5, 5), (6, 2), (8, 5), (9, 5),                             // Sales
            (11, 2), (12, 2), (13, 5), (14, 5),
            (15, 5), (18, 2),                                            // Inventory
        };
        foreach (var (permId, level) in cashierPerms)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO RolePermissions (RoleId, PermissionId, AccessLevel) VALUES (3, {0}, {1})", permId, level);
        }

        Log.Information("Permission system migration complete: 3 roles, 27 permissions");
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
