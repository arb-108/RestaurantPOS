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

    /// <summary>Exposes the DI service provider for use by popup windows.</summary>
    public IServiceProvider Services => _host!.Services;

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

        var dbSettings = DatabaseConfig.GetSettings();
        Log.Information("Application starting — SQL Server: {Server}/{Database}",
            dbSettings.Server, dbSettings.Database);

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
                // ── Database (SQL Server) ──
                // Factory pattern: each service/VM gets its own DbContext instance
                // Prevents "second operation" threading errors with SQL Server
                services.AddDbContextFactory<PosDbContext>(options =>
                {
                    DatabaseConfig.ConfigureDbContext(options);
                });

                // Also register PosDbContext directly — each resolution gets a FRESH instance
                services.AddTransient(sp =>
                    sp.GetRequiredService<IDbContextFactory<PosDbContext>>().CreateDbContext());

                // ── Database maintenance (SQL Server backup/restore) ──
                services.AddTransient<AppInterfaces.IDatabaseMaintenanceService, SqlServerMaintenanceService>();

                // ── Services ──
                // AuthService is Singleton (holds login state: CurrentUser, permissions)
                services.AddSingleton<AppInterfaces.IAuthService, AuthService>();
                // All others are Transient — each VM gets its own service+DbContext
                services.AddTransient<AppInterfaces.IMenuService, MenuService>();
                services.AddTransient<AppInterfaces.IOrderService, OrderService>();
                services.AddTransient<AppInterfaces.ITableService, TableService>();
                services.AddTransient<AppInterfaces.ICustomerService, CustomerService>();
                services.AddTransient<AppInterfaces.ISettingsService, SettingsService>();
                services.AddTransient<AppInterfaces.IKitchenService, KitchenService>();
                services.AddTransient<AppInterfaces.IReportService, ReportService>();
                services.AddSingleton<RestaurantPOS.Printing.IPrintService, RestaurantPOS.Printing.PrintService>();

                // ── ViewModels ──
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
                services.AddTransient<CashierOrderSearchViewModel>();

                // ── Main Window ──
                services.AddSingleton<MainWindow>();
                services.AddTransient<Views.LoginWindow>();
            })
            .Build();

        // ── Initialize Database ──
        try
        {
            // Use a dedicated short-lived context for initialization
            var factory = _host.Services.GetRequiredService<IDbContextFactory<PosDbContext>>();
            using var initDb = factory.CreateDbContext();

            await initDb.Database.EnsureCreatedAsync();
            Log.Information("SQL Server database ready");

            // Run seed/migration logic (idempotent)
            await MigratePermissionSystemAsync(initDb);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize SQL Server database");
            System.Windows.MessageBox.Show(
                $"Failed to connect to SQL Server:\n{ex.Message}\n\nCheck dbconfig.json in:\n{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\RestaurantPOS",
                "Database Connection Failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = mainVm;

        // ── Show dedicated LoginWindow first (fixed-size, centered, NOT maximized) ──
        var loginVm = _host.Services.GetRequiredService<LoginViewModel>();
        var loginWin = new Views.LoginWindow(loginVm);
        var ok = loginWin.ShowDialog();

        if (ok != true)
        {
            // User closed the login window — exit
            Shutdown();
            return;
        }

        // Login succeeded — mainVm is already in logged-in state; show the MainWindow maximized
        mainWindow.Show();
        base.OnStartup(e);
    }

    // ═══════════════════════════════════════════════
    //  PERMISSION SYSTEM MIGRATION (idempotent)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Migrates existing databases to the 3-role, 27-permission system.
    /// Uses pure EF Core LINQ — safe to run on every startup.
    /// </summary>
    private static async Task MigratePermissionSystemAsync(PosDbContext db)
    {
        try
        {
            var hasNewPerms = await db.Permissions.AnyAsync(p => p.Name == "Login / authentication");
            if (hasNewPerms)
            {
                var rpCount = await db.RolePermissions.CountAsync();
                if (rpCount > 0) return; // Fully migrated
            }

            Log.Information("Migrating to new 3-role / 27-permission system...");

            // 1. Clear old data using EF Core
            var oldRps = await db.RolePermissions.ToListAsync();
            if (oldRps.Count > 0)
            {
                db.RolePermissions.RemoveRange(oldRps);
                await db.SaveChangesAsync();
            }

            if (!hasNewPerms)
            {
                var oldPerms = await db.Permissions.ToListAsync();
                if (oldPerms.Count > 0)
                {
                    db.Permissions.RemoveRange(oldPerms);
                    await db.SaveChangesAsync();
                }
            }

            // 2. Ensure roles
            var oldRoles = await db.Roles.Where(r => r.Id > 3).ToListAsync();
            foreach (var r in oldRoles) r.IsActive = false;

            var roleData = new (int Id, string Name, string Desc)[]
            {
                (1, "admin", "System Administrator"),
                (2, "manager", "Restaurant Manager"),
                (3, "cashier", "Cashier / POS Operator"),
            };
            foreach (var (id, name, desc) in roleData)
            {
                var role = await db.Roles.FindAsync(id);
                if (role != null)
                {
                    role.Name = name; role.Description = desc; role.IsActive = true;
                }
                else
                {
                    db.Roles.Add(new RestaurantPOS.Domain.Entities.Role { Id = id, Name = name, Description = desc });
                }
            }
            await db.SaveChangesAsync();

            // Reassign users on old roles to cashier
            var usersOnOldRoles = await db.Users.Where(u => u.RoleId > 3).ToListAsync();
            foreach (var u in usersOnOldRoles) u.RoleId = 3;
            await db.SaveChangesAsync();

            // 3. Insert 27 permissions
            if (!hasNewPerms)
            {
                var now = DateTime.UtcNow;
                var permDefs = new (int Id, string Name, string Module, string Desc)[]
                {
                    (1,  "Login / authentication",      "General",      "Login and authenticate"),
                    (2,  "View own profile",            "General",      "View/edit own profile"),
                    (3,  "Manage users & roles",        "General",      "Create/edit users and roles"),
                    (4,  "View audit log",              "General",      "View system audit trail"),
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
                    (15, "View menu & categories",      "Inventory",    "View menu items and categories"),
                    (16, "Manage menu items",           "Inventory",    "Add/edit/delete menu items"),
                    (17, "Manage stock & recipes",      "Inventory",    "Manage stock and recipes"),
                    (18, "View kitchen orders",         "Inventory",    "View kitchen display orders"),
                    (19, "Manage employees",            "HR & Finance", "Manage employee records"),
                    (20, "Manage suppliers",            "HR & Finance", "Manage supplier records"),
                    (21, "Manage expenses",             "HR & Finance", "Manage expenses"),
                    (22, "Generate payroll",            "HR & Finance", "Generate payroll"),
                    (23, "View reports & analytics",    "Config",       "Access reports and analytics"),
                    (24, "Manage tax & discounts",      "Config",       "Manage tax rates and discounts"),
                    (25, "Manage printers & terminals", "Config",       "Manage printers and terminals"),
                    (26, "System app settings",         "Config",       "System application settings"),
                    (27, "Floor plan & table config",   "Config",       "Floor plan and table configuration"),
                };

                foreach (var (id, name, module, desc) in permDefs)
                {
                    db.Permissions.Add(new RestaurantPOS.Domain.Entities.Permission
                    {
                        Id = id, Name = name, Module = module, Description = desc, CreatedAt = now, UpdatedAt = now
                    });
                }
                await db.SaveChangesAsync();
            }

            // 4. RolePermission rows
            for (int i = 1; i <= 27; i++)
                db.RolePermissions.Add(new RestaurantPOS.Domain.Entities.RolePermission { RoleId = 1, PermissionId = i, AccessLevel = 5 });

            foreach (var (pid, lvl) in new[] {
                (1,5),(2,5),(4,4),
                (5,5),(6,5),(7,5),(8,5),(9,5),(10,5),(11,5),(12,5),(13,5),(14,5),
                (15,5),(16,5),(17,5),(18,5),
                (19,4),(20,4),(21,4),
                (23,5),(27,4) })
                db.RolePermissions.Add(new RestaurantPOS.Domain.Entities.RolePermission { RoleId = 2, PermissionId = pid, AccessLevel = lvl });

            foreach (var (pid, lvl) in new[] {
                (1,5),(2,5),
                (5,5),(6,2),(8,5),(9,5),(11,2),(12,2),(13,5),(14,5),
                (15,5),(18,2) })
                db.RolePermissions.Add(new RestaurantPOS.Domain.Entities.RolePermission { RoleId = 3, PermissionId = pid, AccessLevel = lvl });

            await db.SaveChangesAsync();
            Log.Information("Permission system migration complete: 3 roles, 27 permissions");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Permission migration warning (non-fatal)");
        }
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
