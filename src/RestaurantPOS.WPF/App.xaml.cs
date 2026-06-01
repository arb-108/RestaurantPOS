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

        // ── Pre-flight: make sure SQL Server is reachable BEFORE building the host.
        //    AddDbContextFactory captures the connection string once; if we let
        //    the host build with bad settings we'd have to tear it down to retry.
        //    Looping with a SqlConnection here keeps it simple.
        if (!await EnsureDatabaseReachableAsync())
        {
            Shutdown(1);
            return;
        }

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
            await SeedRecipesAsync(initDb);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize SQL Server database after pre-flight succeeded");
            System.Windows.MessageBox.Show(
                $"Failed to initialize database:\n{ex.Message}",
                "Database Initialization Failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

    // Verifies the configured SQL Server is reachable. On failure, opens the
    // DatabaseSettingsDialog so the user can fix the server / instance / auth
    // without editing dbconfig.json by hand. Returns true on success, false if
    // the user closed the dialog without resolving the connection.
    private static async Task<bool> EnsureDatabaseReachableAsync()
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var connStr = DatabaseConfig.GetConnectionString();
                // Connect to master so a missing target DB doesn't mask a real
                // server/instance/auth problem — we only need to prove the
                // instance accepts logins.
                var masterConn = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr) { InitialCatalog = "master" }.ToString();
                await using var conn = new Microsoft.Data.SqlClient.SqlConnection(masterConn);
                await conn.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SQL Server pre-flight failed (attempt {Attempt}/{Max})", attempt, maxAttempts);

                var dlg = new Views.DatabaseSettingsDialog
                {
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
                };

                // Show the failure reason on the dialog title so the user has context.
                dlg.Title = $"Database Connection Failed — {(ex.InnerException?.Message ?? ex.Message)}";

                var result = dlg.ShowDialog();
                if (result != true || !dlg.SettingsSaved)
                {
                    // User cancelled / closed without saving — give up.
                    System.Windows.MessageBox.Show(
                        "Database connection not configured. The application will exit.",
                        "Setup Required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return false;
                }

                // Saved settings — flush cache and retry the loop.
                DatabaseConfig.ResetCache();
            }
        }

        Log.Fatal("Exceeded SQL Server pre-flight retry budget");
        return false;
    }

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

    // Seeds recipe (MenuItem → Ingredient mapping) rows for deployments that
    // were created via custom SQL scripts and never received the EF HasData
    // recipe seed. Without recipes, OrderService.CheckoutFastAsync runs the
    // deduction loop but exits early because Recipes is empty, so stock
    // never moves. Idempotent: only inserts if Recipes table is empty AND
    // both the source MenuItem and target Ingredient exist.
    private static async Task SeedRecipesAsync(PosDbContext db)
    {
        try
        {
            if (await db.Recipes.AnyAsync()) return;

            var menuItemIds = (await db.MenuItems.Select(m => m.Id).ToListAsync()).ToHashSet();
            var ingredientIds = (await db.Ingredients.Select(i => i.Id).ToListAsync()).ToHashSet();

            if (menuItemIds.Count == 0 || ingredientIds.Count == 0)
            {
                Log.Information("Recipe seeder skipped — no menu items or ingredients to link");
                return;
            }

            var seed = new (int MenuItemId, int IngredientId, decimal Quantity)[]
            {
                (1,11,0.20m),(1,1,0.08m),(1,3,0.05m),(1,9,0.03m),(1,10,0.03m),(1,14,0.02m),(1,16,0.02m),
                (2,11,0.25m),(2,1,0.10m),(2,3,0.06m),(2,14,0.02m),(2,16,0.03m),
                (3,11,0.30m),(3,1,0.10m),(3,3,0.07m),(3,9,0.03m),(3,10,0.03m),(3,16,0.03m),
                (4,11,0.18m),(4,1,0.08m),(4,3,0.05m),(4,14,0.02m),
                (5,11,0.15m),(5,1,0.06m),(5,9,0.03m),(5,10,0.03m),(5,16,0.02m),
                (6,11,0.18m),(6,1,0.06m),(6,9,0.03m),(6,15,0.02m),(6,16,0.02m),
                (7,11,0.35m),(7,3,0.10m),(7,15,0.03m),(7,12,0.01m),(7,6,0.005m),
                (8,11,0.70m),(8,3,0.20m),(8,15,0.05m),(8,12,0.02m),(8,6,0.01m),
                (9,1,0.10m),(9,3,0.08m),(9,4,0.005m),(9,14,0.03m),
                (10,11,0.12m),(10,1,0.06m),(10,9,0.03m),(10,10,0.03m),(10,16,0.02m),(10,14,0.02m),
                (11,8,0.15m),(11,3,0.08m),(11,4,0.003m),
                (12,8,0.25m),(12,3,0.12m),(12,4,0.005m),
                (13,9,0.05m),(13,16,0.04m),(13,18,0.01m),(13,5,0.005m),
                (14,1,0.12m),(14,4,0.003m),(14,3,0.01m),
                (15,1,0.12m),(15,12,0.01m),(15,4,0.003m),(15,3,0.02m),
                (16,11,1.50m),(16,3,0.40m),(16,1,0.30m),(16,8,0.30m),(16,14,0.05m),(16,19,1m),
                (17,11,0.20m),(17,1,0.08m),(17,3,0.10m),(17,8,0.15m),(17,20,1m),(17,14,0.02m),
                (18,20,1m),
                (19,19,1m),
                (20,1,0.05m),(20,5,0.04m),(20,3,0.03m),
            };

            var inserted = 0;
            foreach (var (mid, iid, qty) in seed)
            {
                if (!menuItemIds.Contains(mid) || !ingredientIds.Contains(iid)) continue;
                db.Recipes.Add(new RestaurantPOS.Domain.Entities.Recipe { MenuItemId = mid, IngredientId = iid, Quantity = qty });
                inserted++;
            }

            if (inserted > 0)
            {
                await db.SaveChangesAsync();
                Log.Information("Recipe seeder inserted {Count} recipe rows", inserted);
            }
            else
            {
                Log.Information("Recipe seeder: no matching MenuItem/Ingredient IDs to link");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Recipe seeder failed (non-fatal)");
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
