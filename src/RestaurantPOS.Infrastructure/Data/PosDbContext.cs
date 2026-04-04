using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Infrastructure.Data;

public class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    // Module A — Users & Auth
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Module B — Menu & Inventory
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemVariant> MenuItemVariants => Set<MenuItemVariant>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<MenuItemModifierGroup> MenuItemModifierGroups => Set<MenuItemModifierGroup>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Module C — Orders & Billing
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemModifier> OrderItemModifiers => Set<OrderItemModifier>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Module D — Customers & Loyalty
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();

    // Module E — Tables & Floor
    public DbSet<FloorPlan> FloorPlans => Set<FloorPlan>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    // Module F — Kitchen Display
    public DbSet<KitchenStation> KitchenStations => Set<KitchenStation>();
    public DbSet<KitchenOrder> KitchenOrders => Set<KitchenOrder>();
    public DbSet<KitchenOrderItem> KitchenOrderItems => Set<KitchenOrderItem>();

    // Module I — Deals / Combos
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealItem> DealItems => Set<DealItem>();

    // Module H — Suppliers
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierExpense> SupplierExpenses => Set<SupplierExpense>();

    // Module J — Employees & Payroll
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Payroll> Payrolls => Set<Payroll>();

    // Module G — Settings & Sync
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<CashDrawerLog> CashDrawerLogs => Set<CashDrawerLog>();
    public DbSet<SyncQueue> SyncQueues => Set<SyncQueue>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // ─── Composite Keys ───
        mb.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
        mb.Entity<MenuItemModifierGroup>().HasKey(m => new { m.MenuItemId, m.ModifierGroupId });
        mb.Entity<Recipe>().HasKey(r => new { r.MenuItemId, r.IngredientId });

        // ─── Unique Constraints ───
        mb.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        mb.Entity<Customer>().HasIndex(c => c.Phone).IsUnique();
        mb.Entity<AppSetting>().HasIndex(a => a.Key).IsUnique();
        mb.Entity<Role>().HasIndex(r => r.Name).IsUnique();
        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();

        // ─── Indexes ───
        mb.Entity<Order>().HasIndex(o => new { o.Status, o.CreatedAt });
        mb.Entity<Order>().HasIndex(o => o.CustomerId);
        mb.Entity<Order>().HasIndex(o => o.IsSynced);
        mb.Entity<OrderItem>().HasIndex(oi => oi.OrderId);
        mb.Entity<MenuItem>().HasIndex(mi => new { mi.CategoryId, mi.IsActive });
        mb.Entity<MenuItem>().HasIndex(mi => mi.SKU);
        mb.Entity<MenuItem>().HasIndex(mi => mi.Barcode);
        mb.Entity<Payment>().HasIndex(p => p.OrderId);
        mb.Entity<KitchenOrder>().HasIndex(ko => new { ko.StationId, ko.Status });
        mb.Entity<SyncQueue>().HasIndex(sq => new { sq.Status, sq.CreatedAt });
        mb.Entity<TableSession>().HasIndex(ts => new { ts.TableId, ts.Status });

        // ─── Self-referencing Category ───
        mb.Entity<Category>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ─── Order relationships ───
        mb.Entity<Order>()
            .HasOne(o => o.Waiter)
            .WithMany()
            .HasForeignKey(o => o.WaiterId)
            .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<Order>()
            .HasOne(o => o.Cashier)
            .WithMany()
            .HasForeignKey(o => o.CashierId)
            .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<Order>()
            .HasOne(o => o.ApprovedBy)
            .WithMany()
            .HasForeignKey(o => o.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ─── Enums stored as strings ───
        mb.Entity<Order>().Property(o => o.OrderType).HasConversion<string>();
        mb.Entity<Order>().Property(o => o.Status).HasConversion<string>();
        mb.Entity<OrderItem>().Property(oi => oi.Status).HasConversion<string>();
        mb.Entity<Payment>().Property(p => p.Status).HasConversion<string>();
        mb.Entity<Table>().Property(t => t.Status).HasConversion<string>();
        mb.Entity<Table>().Property(t => t.Shape).HasConversion<string>();
        mb.Entity<TableSession>().Property(ts => ts.Status).HasConversion<string>();
        mb.Entity<Reservation>().Property(r => r.Status).HasConversion<string>();
        mb.Entity<Customer>().Property(c => c.Tier).HasConversion<string>();
        mb.Entity<LoyaltyTransaction>().Property(lt => lt.Type).HasConversion<string>();
        mb.Entity<StockMovement>().Property(sm => sm.Type).HasConversion<string>();
        mb.Entity<KitchenOrder>().Property(ko => ko.Status).HasConversion<string>();
        mb.Entity<KitchenOrderItem>().Property(ki => ki.Status).HasConversion<string>();
        mb.Entity<Printer>().Property(p => p.Type).HasConversion<string>();
        mb.Entity<Printer>().Property(p => p.ConnectionType).HasConversion<string>();
        mb.Entity<Discount>().Property(d => d.Type).HasConversion<string>();
        mb.Entity<SyncQueue>().Property(sq => sq.Status).HasConversion<string>();
        mb.Entity<CashDrawerLog>().Property(c => c.Type).HasConversion<string>();
        mb.Entity<Employee>().Property(e => e.Category).HasConversion<string>();
        mb.Entity<Employee>().Property(e => e.EmploymentType).HasConversion<string>();
        mb.Entity<Payroll>().Property(p => p.Status).HasConversion<string>();
        mb.Entity<Payroll>().HasIndex(p => new { p.EmployeeId, p.Year, p.Month }).IsUnique();

        // ─── Seed Data ───
        SeedData(mb);
    }

    private static void SeedData(ModelBuilder mb)
    {
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Roles — only 3 roles
        mb.Entity<Role>().HasData(
            new Role { Id = 1, Name = "admin", Description = "System Administrator", CreatedAt = now, UpdatedAt = now },
            new Role { Id = 2, Name = "manager", Description = "Restaurant Manager", CreatedAt = now, UpdatedAt = now },
            new Role { Id = 3, Name = "cashier", Description = "Cashier / POS Operator", CreatedAt = now, UpdatedAt = now }
        );

        // Permissions — 27 permissions across 5 modules
        mb.Entity<Permission>().HasData(
            // General (1–4)
            new Permission { Id = 1,  Name = "Login / authentication",     Module = "General",      Description = "Login and authenticate", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 2,  Name = "View own profile",           Module = "General",      Description = "View/edit own profile", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 3,  Name = "Manage users & roles",       Module = "General",      Description = "Create/edit users and roles", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 4,  Name = "View audit log",             Module = "General",      Description = "View system audit trail", CreatedAt = now, UpdatedAt = now },
            // Sales (5–14)
            new Permission { Id = 5,  Name = "Create & process orders",    Module = "Sales",        Description = "Create and process orders", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 6,  Name = "Apply discounts",            Module = "Sales",        Description = "Apply discounts to orders", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 7,  Name = "Void / cancel orders",       Module = "Sales",        Description = "Void or cancel orders", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 8,  Name = "Hold & recall orders",       Module = "Sales",        Description = "Hold and recall orders", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 9,  Name = "Process payments",           Module = "Sales",        Description = "Process order payments", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 10, Name = "Issue refunds",              Module = "Sales",        Description = "Issue refunds on orders", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 11, Name = "Manage tables & sessions",   Module = "Sales",        Description = "Manage table assignments", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 12, Name = "Manage customers & loyalty", Module = "Sales",        Description = "Manage customers and loyalty", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 13, Name = "Open / close shift",         Module = "Sales",        Description = "Open and close shifts", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 14, Name = "Cash drawer operations",     Module = "Sales",        Description = "Cash drawer open/close", CreatedAt = now, UpdatedAt = now },
            // Inventory (15–18)
            new Permission { Id = 15, Name = "View menu & categories",     Module = "Inventory",    Description = "View menu items and categories", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 16, Name = "Manage menu items",          Module = "Inventory",    Description = "Add/edit/delete menu items", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 17, Name = "Manage stock & recipes",     Module = "Inventory",    Description = "Manage stock and recipes", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 18, Name = "View kitchen orders",        Module = "Inventory",    Description = "View kitchen display orders", CreatedAt = now, UpdatedAt = now },
            // HR & Finance (19–22)
            new Permission { Id = 19, Name = "Manage employees",           Module = "HR & Finance", Description = "Manage employee records", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 20, Name = "Manage suppliers",           Module = "HR & Finance", Description = "Manage supplier records", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 21, Name = "Manage expenses",            Module = "HR & Finance", Description = "Manage expenses", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 22, Name = "Generate payroll",           Module = "HR & Finance", Description = "Generate payroll", CreatedAt = now, UpdatedAt = now },
            // Config (23–27)
            new Permission { Id = 23, Name = "View reports & analytics",   Module = "Config",       Description = "Access reports and analytics", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 24, Name = "Manage tax & discounts",     Module = "Config",       Description = "Manage tax rates and discounts", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 25, Name = "Manage printers & terminals", Module = "Config",      Description = "Manage printers and terminals", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 26, Name = "System app settings",        Module = "Config",       Description = "System application settings", CreatedAt = now, UpdatedAt = now },
            new Permission { Id = 27, Name = "Floor plan & table config",  Module = "Config",       Description = "Floor plan and table configuration", CreatedAt = now, UpdatedAt = now }
        );

        // ── RolePermission seed: only rows where AccessLevel > 0 ──

        // ADMIN (RoleId=1) — all 27 permissions at level 5
        var adminPerms = new List<object>();
        for (int i = 1; i <= 27; i++)
            adminPerms.Add(new { RoleId = 1, PermissionId = i, AccessLevel = 5 });

        // MANAGER (RoleId=2) — per access matrix
        var managerPerms = new object[]
        {
            // General
            new { RoleId = 2, PermissionId = 1,  AccessLevel = 5 }, // Login / authentication
            new { RoleId = 2, PermissionId = 2,  AccessLevel = 5 }, // View own profile
            // Manage users & roles = 0 (no row)
            new { RoleId = 2, PermissionId = 4,  AccessLevel = 4 }, // View audit log
            // Sales
            new { RoleId = 2, PermissionId = 5,  AccessLevel = 5 }, // Create & process orders
            new { RoleId = 2, PermissionId = 6,  AccessLevel = 5 }, // Apply discounts
            new { RoleId = 2, PermissionId = 7,  AccessLevel = 5 }, // Void / cancel orders
            new { RoleId = 2, PermissionId = 8,  AccessLevel = 5 }, // Hold & recall orders
            new { RoleId = 2, PermissionId = 9,  AccessLevel = 5 }, // Process payments
            new { RoleId = 2, PermissionId = 10, AccessLevel = 5 }, // Issue refunds
            new { RoleId = 2, PermissionId = 11, AccessLevel = 5 }, // Manage tables & sessions
            new { RoleId = 2, PermissionId = 12, AccessLevel = 5 }, // Manage customers & loyalty
            new { RoleId = 2, PermissionId = 13, AccessLevel = 5 }, // Open / close shift
            new { RoleId = 2, PermissionId = 14, AccessLevel = 5 }, // Cash drawer operations
            // Inventory
            new { RoleId = 2, PermissionId = 15, AccessLevel = 5 }, // View menu & categories
            new { RoleId = 2, PermissionId = 16, AccessLevel = 5 }, // Manage menu items
            new { RoleId = 2, PermissionId = 17, AccessLevel = 5 }, // Manage stock & recipes
            new { RoleId = 2, PermissionId = 18, AccessLevel = 5 }, // View kitchen orders
            // HR & Finance
            new { RoleId = 2, PermissionId = 19, AccessLevel = 4 }, // Manage employees
            new { RoleId = 2, PermissionId = 20, AccessLevel = 4 }, // Manage suppliers
            new { RoleId = 2, PermissionId = 21, AccessLevel = 4 }, // Manage expenses
            // Generate payroll = 0 (no row)
            // Config
            new { RoleId = 2, PermissionId = 23, AccessLevel = 5 }, // View reports & analytics
            // Manage tax & discounts = 0
            // Manage printers & terminals = 0
            // System app settings = 0
            new { RoleId = 2, PermissionId = 27, AccessLevel = 4 }, // Floor plan & table config
        };

        // CASHIER (RoleId=3) — per access matrix
        var cashierPerms = new object[]
        {
            // General
            new { RoleId = 3, PermissionId = 1,  AccessLevel = 5 }, // Login / authentication
            new { RoleId = 3, PermissionId = 2,  AccessLevel = 5 }, // View own profile
            // Sales
            new { RoleId = 3, PermissionId = 5,  AccessLevel = 5 }, // Create & process orders
            new { RoleId = 3, PermissionId = 6,  AccessLevel = 2 }, // Apply discounts (read+create only)
            // Void / cancel orders = 0
            new { RoleId = 3, PermissionId = 8,  AccessLevel = 5 }, // Hold & recall orders
            new { RoleId = 3, PermissionId = 9,  AccessLevel = 5 }, // Process payments
            // Issue refunds = 0
            new { RoleId = 3, PermissionId = 11, AccessLevel = 2 }, // Manage tables & sessions (read+create)
            new { RoleId = 3, PermissionId = 12, AccessLevel = 2 }, // Manage customers & loyalty (read+create)
            new { RoleId = 3, PermissionId = 13, AccessLevel = 5 }, // Open / close shift
            new { RoleId = 3, PermissionId = 14, AccessLevel = 5 }, // Cash drawer operations
            // Inventory
            new { RoleId = 3, PermissionId = 15, AccessLevel = 5 }, // View menu & categories
            // Manage menu items = 0
            // Manage stock & recipes = 0
            new { RoleId = 3, PermissionId = 18, AccessLevel = 2 }, // View kitchen orders (read+create)
        };

        mb.Entity<RolePermission>().HasData(
            [.. adminPerms, .. managerPerms, .. cashierPerms]);

        // Default admin user (password: admin123, PIN: 1234)
        mb.Entity<User>().HasData(
            new User
            {
                Id = 1,
                Username = "admin",
                FullName = "System Admin",
                PasswordHash = "$2a$12$LJ3m4ys3Lk0TSwH0BN/xnOWmPGNXNJFOJKbFQnaC8fXmFz0hqmyVe",
                Pin = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4",
                RoleId = 1,
                CreatedAt = now,
                UpdatedAt = now
            }
        );

        // Payment Methods
        mb.Entity<PaymentMethod>().HasData(
            new PaymentMethod { Id = 1, Name = "Cash", Code = "CASH", IsDigital = false, CreatedAt = now, UpdatedAt = now },
            new PaymentMethod { Id = 2, Name = "Debit Card", Code = "DEBIT", IsDigital = false, CreatedAt = now, UpdatedAt = now },
            new PaymentMethod { Id = 3, Name = "Credit Card", Code = "CREDIT", IsDigital = false, CreatedAt = now, UpdatedAt = now },
            new PaymentMethod { Id = 4, Name = "JazzCash", Code = "JAZZCASH", IsDigital = true, CreatedAt = now, UpdatedAt = now },
            new PaymentMethod { Id = 5, Name = "EasyPaisa", Code = "EASYPAISA", IsDigital = true, CreatedAt = now, UpdatedAt = now },
            new PaymentMethod { Id = 6, Name = "Bank Transfer", Code = "BANK", IsDigital = true, CreatedAt = now, UpdatedAt = now }
        );

        // Tax Rates
        mb.Entity<TaxRate>().HasData(
            new TaxRate { Id = 1, Name = "GST 16% (Inclusive)", Rate = 16m, IsInclusive = true, CreatedAt = now, UpdatedAt = now },
            new TaxRate { Id = 2, Name = "GST 0%", Rate = 0m, IsInclusive = false, CreatedAt = now, UpdatedAt = now },
            new TaxRate { Id = 3, Name = "FED 16% (Exclusive)", Rate = 16m, IsInclusive = false, CreatedAt = now, UpdatedAt = now }
        );

        // Kitchen Stations
        mb.Entity<KitchenStation>().HasData(
            new KitchenStation { Id = 1, Name = "Main Kitchen", DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 2, Name = "Grill Station", DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 3, Name = "Pizza Station", DisplayOrder = 3, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 4, Name = "Fry Station", DisplayOrder = 4, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 5, Name = "Beverage Counter", DisplayOrder = 5, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 6, Name = "Dessert Counter", DisplayOrder = 6, CreatedAt = now, UpdatedAt = now },
            new KitchenStation { Id = 7, Name = "Tandoor", DisplayOrder = 7, CreatedAt = now, UpdatedAt = now }
        );

        // Default Floor Plan
        mb.Entity<FloorPlan>().HasData(
            new FloorPlan { Id = 1, Name = "Main Hall", Description = "Main dining area", DisplayOrder = 1, CreatedAt = now, UpdatedAt = now }
        );

        // Default Tables
        mb.Entity<Table>().HasData(
            new Table { Id = 1, Name = "Tbl 1", FloorPlanId = 1, Capacity = 4, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 2, Name = "Tbl 2", FloorPlanId = 1, Capacity = 4, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 3, Name = "Tbl 3", FloorPlanId = 1, Capacity = 4, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 4, Name = "Tbl 4", FloorPlanId = 1, Capacity = 4, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 5, Name = "Tbl 5", FloorPlanId = 1, Capacity = 4, DisplayOrder = 5, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 6, Name = "Tbl 6", FloorPlanId = 1, Capacity = 4, DisplayOrder = 6, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 7, Name = "Tbl 7", FloorPlanId = 1, Capacity = 6, DisplayOrder = 7, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 8, Name = "Tbl 8", FloorPlanId = 1, Capacity = 6, DisplayOrder = 8, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 9, Name = "Tbl 9", FloorPlanId = 1, Capacity = 6, DisplayOrder = 9, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 10, Name = "Family 1", FloorPlanId = 1, Capacity = 8, DisplayOrder = 10, CreatedAt = now, UpdatedAt = now },
            new Table { Id = 11, Name = "Family 2", FloorPlanId = 1, Capacity = 8, DisplayOrder = 11, CreatedAt = now, UpdatedAt = now }
        );

        // Default App Settings
        mb.Entity<AppSetting>().HasData(
            new AppSetting { Id = 1, Key = "RestaurantName", Value = "KFC Restaurant", DataType = "string", Group = "General", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 2, Key = "Currency", Value = "PKR", DataType = "string", Group = "General", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 3, Key = "CurrencySymbol", Value = "Rs.", DataType = "string", Group = "General", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 4, Key = "IdleTimeoutMinutes", Value = "5", DataType = "int", Group = "Security", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 5, Key = "DefaultTaxRateId", Value = "1", DataType = "int", Group = "Tax", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 6, Key = "ServiceChargePercent", Value = "0", DataType = "decimal", Group = "Billing", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 7, Key = "LoyaltyPointsPerPKR", Value = "1", DataType = "int", Group = "Loyalty", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 8, Key = "LoyaltyRedeemRate", Value = "100", DataType = "int", Group = "Loyalty", Description = "Points needed for 1 PKR discount", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 9, Key = "ReceiptHeader", Value = "Thank you for dining with us!", DataType = "string", Group = "Printing", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 10, Key = "ReceiptFooter", Value = "Visit us again!", DataType = "string", Group = "Printing", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 11, Key = "SyncEnabled", Value = "false", DataType = "bool", Group = "Sync", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 12, Key = "SyncServerUrl", Value = "", DataType = "string", Group = "Sync", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 13, Key = "SyncIntervalSeconds", Value = "300", DataType = "int", Group = "Sync", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 14, Key = "AutoPrintReceipt", Value = "true", DataType = "bool", Group = "Printing", CreatedAt = now, UpdatedAt = now },
            new AppSetting { Id = 15, Key = "AutoPrintKOT", Value = "true", DataType = "bool", Group = "Printing", CreatedAt = now, UpdatedAt = now }
        );

        // Sample Categories
        mb.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Burgers", DisplayOrder = 1, ImagePath = "burgers.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 2, Name = "Wraps", DisplayOrder = 2, ImagePath = "wraps.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 3, Name = "Wings", DisplayOrder = 3, ImagePath = "wings.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 4, Name = "Fish", DisplayOrder = 4, ImagePath = "fish.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 5, Name = "Sandwiches", DisplayOrder = 5, ImagePath = "sandwiches.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 6, Name = "Fries & Sides", DisplayOrder = 6, ImagePath = "fries.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 7, Name = "Naan & Bread", DisplayOrder = 7, ImagePath = "naan.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 8, Name = "Deals", DisplayOrder = 8, ImagePath = "deals.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 9, Name = "Beverages", DisplayOrder = 9, ImagePath = "beverages.png", CreatedAt = now, UpdatedAt = now },
            new Category { Id = 10, Name = "Desserts", DisplayOrder = 10, ImagePath = "desserts.png", CreatedAt = now, UpdatedAt = now }
        );

        // Suppliers (from stock sheet)
        mb.Entity<Supplier>().HasData(
            new Supplier { Id = 1, Name = "Al-Madina Traders", Phone = "0300-1234567", Notes = "Dry Goods · Oils · Spices — Lead: 1–2 days, Min Order Rs 2,000", CreatedAt = now, UpdatedAt = now },
            new Supplier { Id = 2, Name = "Punjab Fresh Co.", Phone = "0321-9876543", Notes = "Vegetables · Perishables — Lead: Same day, Min Order Rs 500", CreatedAt = now, UpdatedAt = now },
            new Supplier { Id = 3, Name = "Rafiq Beverages", Phone = "0312-5554433", Notes = "Cold Drinks · Water — Lead: 2–3 days, Min Order 1 crate", CreatedAt = now, UpdatedAt = now },
            new Supplier { Id = 4, Name = "City Wholesale", Phone = "0333-7778899", Notes = "Sauces · Condiments — Lead: 1 day, Min Order Rs 1,500", CreatedAt = now, UpdatedAt = now }
        );

        // Stock / Ingredients (CostPerUnit in paisa, quantities from Excel)
        mb.Entity<Ingredient>().HasData(
            // Dry Goods — Supplier 1 (Al-Madina Traders)
            new Ingredient { Id = 1,  Name = "Wheat Flour",      Unit = "kg",      CurrentStock = 3m,    ReorderLevel = 10m,  CostPerUnit = 8500,   StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 2,  Name = "Rice (Basmati)",    Unit = "kg",      CurrentStock = 22m,   ReorderLevel = 10m,  CostPerUnit = 12000,  StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 3,  Name = "Cooking Oil",       Unit = "L",       CurrentStock = 1m,    ReorderLevel = 5m,   CostPerUnit = 32000,  StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 4,  Name = "Salt",              Unit = "kg",      CurrentStock = 8m,    ReorderLevel = 3m,   CostPerUnit = 4000,   StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 5,  Name = "Sugar",             Unit = "kg",      CurrentStock = 6m,    ReorderLevel = 4m,   CostPerUnit = 9500,   StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 6,  Name = "Cumin",             Unit = "kg",      CurrentStock = 2m,    ReorderLevel = 1m,   CostPerUnit = 20000,  StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 7,  Name = "Turmeric",          Unit = "kg",      CurrentStock = 0.5m,  ReorderLevel = 0.5m, CostPerUnit = 35000,  StockCategory = "Dry Goods",           SupplierId = 1, CreatedAt = now, UpdatedAt = now },
            // Cold / Chilled — Supplier 2 (Punjab Fresh Co.)
            new Ingredient { Id = 8,  Name = "Potato",            Unit = "kg",      CurrentStock = 4m,    ReorderLevel = 15m,  CostPerUnit = 5900,   StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 9,  Name = "Onion",             Unit = "kg",      CurrentStock = 8m,    ReorderLevel = 8m,   CostPerUnit = 5000,   StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 10, Name = "Tomato",            Unit = "kg",      CurrentStock = 5m,    ReorderLevel = 5m,   CostPerUnit = 8000,   StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 11, Name = "Chicken (Fresh)",   Unit = "kg",      CurrentStock = 12m,   ReorderLevel = 10m,  CostPerUnit = 45000,  StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 12, Name = "Garlic",            Unit = "kg",      CurrentStock = 3m,    ReorderLevel = 2m,   CostPerUnit = 16000,  StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 13, Name = "Ginger",            Unit = "kg",      CurrentStock = 2m,    ReorderLevel = 1m,   CostPerUnit = 18000,  StockCategory = "Cold / Chilled",      SupplierId = 2, CreatedAt = now, UpdatedAt = now },
            // Sauces & Condiments — Supplier 4 (City Wholesale)
            new Ingredient { Id = 14, Name = "Ketchup (Bottle)",  Unit = "bottles", CurrentStock = 2m,    ReorderLevel = 6m,   CostPerUnit = 18000,  StockCategory = "Sauces & Condiments", SupplierId = 4, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 15, Name = "Chilli Sauce",      Unit = "bottles", CurrentStock = 4m,    ReorderLevel = 4m,   CostPerUnit = 20000,  StockCategory = "Sauces & Condiments", SupplierId = 4, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 16, Name = "Mayonnaise",        Unit = "bottles", CurrentStock = 3m,    ReorderLevel = 4m,   CostPerUnit = 25000,  StockCategory = "Sauces & Condiments", SupplierId = 4, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 17, Name = "Soy Sauce",         Unit = "bottles", CurrentStock = 2m,    ReorderLevel = 3m,   CostPerUnit = 22000,  StockCategory = "Sauces & Condiments", SupplierId = 4, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 18, Name = "Vinegar",           Unit = "bottles", CurrentStock = 5m,    ReorderLevel = 3m,   CostPerUnit = 12000,  StockCategory = "Sauces & Condiments", SupplierId = 4, CreatedAt = now, UpdatedAt = now },
            // Beverages — Supplier 3 (Rafiq Beverages)
            new Ingredient { Id = 19, Name = "Pepsi 1L",          Unit = "bottles", CurrentStock = 6m,    ReorderLevel = 24m,  CostPerUnit = 9000,   StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 20, Name = "Pepsi 250ml",       Unit = "cans",    CurrentStock = 48m,   ReorderLevel = 24m,  CostPerUnit = 4000,   StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 21, Name = "7Up 1L",            Unit = "bottles", CurrentStock = 9m,    ReorderLevel = 12m,  CostPerUnit = 9000,   StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 22, Name = "Water 600ml",       Unit = "bottles", CurrentStock = 30m,   ReorderLevel = 24m,  CostPerUnit = 2500,   StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 23, Name = "Mango Juice 1L",    Unit = "bottles", CurrentStock = 12m,   ReorderLevel = 12m,  CostPerUnit = 11000,  StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now },
            new Ingredient { Id = 24, Name = "Lassi 500ml",       Unit = "bottles", CurrentStock = 18m,   ReorderLevel = 12m,  CostPerUnit = 7500,   StockCategory = "Beverages",           SupplierId = 3, CreatedAt = now, UpdatedAt = now }
        );

        // Sample Menu Items (prices in paisa: e.g. 55000 = Rs. 550)
        mb.Entity<MenuItem>().HasData(
            new MenuItem { Id = 1, Name = "Zinger Burger", CategoryId = 1, BasePrice = 55000, CostPrice = 25000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 2, Name = "Mighty Burger", CategoryId = 1, BasePrice = 65000, CostPrice = 30000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 3, Name = "Tower Burger", CategoryId = 1, BasePrice = 75000, CostPrice = 35000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 4, Name = "Fillet Burger", CategoryId = 1, BasePrice = 50000, CostPrice = 22000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 5, Name = "Chicken Wrap", CategoryId = 2, BasePrice = 35000, CostPrice = 15000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 6, Name = "Twister Wrap", CategoryId = 2, BasePrice = 42000, CostPrice = 18000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 7, Name = "Hot Wings (6pc)", CategoryId = 3, BasePrice = 45000, CostPrice = 20000, KitchenStationId = 4, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 8, Name = "Hot Wings (12pc)", CategoryId = 3, BasePrice = 80000, CostPrice = 36000, KitchenStationId = 4, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 9, Name = "Fish Fillet", CategoryId = 4, BasePrice = 48000, CostPrice = 22000, KitchenStationId = 4, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 10, Name = "Club Sandwich", CategoryId = 5, BasePrice = 40000, CostPrice = 18000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 11, Name = "Regular Fries", CategoryId = 6, BasePrice = 20000, CostPrice = 8000, KitchenStationId = 4, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 12, Name = "Large Fries", CategoryId = 6, BasePrice = 30000, CostPrice = 12000, KitchenStationId = 4, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 13, Name = "Coleslaw", CategoryId = 6, BasePrice = 15000, CostPrice = 5000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 14, Name = "Plain Naan", CategoryId = 7, BasePrice = 5000, CostPrice = 2000, KitchenStationId = 7, TaxRateId = 2, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 15, Name = "Garlic Naan", CategoryId = 7, BasePrice = 8000, CostPrice = 3000, KitchenStationId = 7, TaxRateId = 2, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 16, Name = "Family Bucket Deal", CategoryId = 8, BasePrice = 250000, CostPrice = 110000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 17, Name = "Zinger Meal", CategoryId = 8, BasePrice = 85000, CostPrice = 38000, KitchenStationId = 1, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 18, Name = "Pepsi Regular", CategoryId = 9, BasePrice = 12000, CostPrice = 6000, KitchenStationId = 5, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 19, Name = "Pepsi 1.5L", CategoryId = 9, BasePrice = 20000, CostPrice = 10000, KitchenStationId = 5, TaxRateId = 1, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now },
            new MenuItem { Id = 20, Name = "Chocolate Brownie", CategoryId = 10, BasePrice = 25000, CostPrice = 10000, KitchenStationId = 6, TaxRateId = 1, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now }
        );

        // Recipes — link menu items → ingredients (quantity per 1 menu item sold)
        mb.Entity<Recipe>().HasData(
            // Burgers (chicken, flour bun, oil, onion, tomato, ketchup, mayo)
            new Recipe { MenuItemId = 1,  IngredientId = 11, Quantity = 0.20m },  // Zinger → Chicken 200g
            new Recipe { MenuItemId = 1,  IngredientId = 1,  Quantity = 0.08m },  // Zinger → Flour 80g (bun)
            new Recipe { MenuItemId = 1,  IngredientId = 3,  Quantity = 0.05m },  // Zinger → Oil 50ml
            new Recipe { MenuItemId = 1,  IngredientId = 9,  Quantity = 0.03m },  // Zinger → Onion 30g
            new Recipe { MenuItemId = 1,  IngredientId = 10, Quantity = 0.03m },  // Zinger → Tomato 30g
            new Recipe { MenuItemId = 1,  IngredientId = 14, Quantity = 0.02m },  // Zinger → Ketchup
            new Recipe { MenuItemId = 1,  IngredientId = 16, Quantity = 0.02m },  // Zinger → Mayo

            new Recipe { MenuItemId = 2,  IngredientId = 11, Quantity = 0.25m },  // Mighty → Chicken 250g
            new Recipe { MenuItemId = 2,  IngredientId = 1,  Quantity = 0.10m },  // Mighty → Flour
            new Recipe { MenuItemId = 2,  IngredientId = 3,  Quantity = 0.06m },  // Mighty → Oil
            new Recipe { MenuItemId = 2,  IngredientId = 14, Quantity = 0.02m },  // Mighty → Ketchup
            new Recipe { MenuItemId = 2,  IngredientId = 16, Quantity = 0.03m },  // Mighty → Mayo

            new Recipe { MenuItemId = 3,  IngredientId = 11, Quantity = 0.30m },  // Tower → Chicken 300g
            new Recipe { MenuItemId = 3,  IngredientId = 1,  Quantity = 0.10m },  // Tower → Flour
            new Recipe { MenuItemId = 3,  IngredientId = 3,  Quantity = 0.07m },  // Tower → Oil
            new Recipe { MenuItemId = 3,  IngredientId = 9,  Quantity = 0.03m },  // Tower → Onion
            new Recipe { MenuItemId = 3,  IngredientId = 10, Quantity = 0.03m },  // Tower → Tomato
            new Recipe { MenuItemId = 3,  IngredientId = 16, Quantity = 0.03m },  // Tower → Mayo

            new Recipe { MenuItemId = 4,  IngredientId = 11, Quantity = 0.18m },  // Fillet → Chicken 180g
            new Recipe { MenuItemId = 4,  IngredientId = 1,  Quantity = 0.08m },  // Fillet → Flour
            new Recipe { MenuItemId = 4,  IngredientId = 3,  Quantity = 0.05m },  // Fillet → Oil
            new Recipe { MenuItemId = 4,  IngredientId = 14, Quantity = 0.02m },  // Fillet → Ketchup

            // Wraps (chicken, flour tortilla, onion, tomato, mayo, chilli sauce)
            new Recipe { MenuItemId = 5,  IngredientId = 11, Quantity = 0.15m },  // Chicken Wrap → Chicken
            new Recipe { MenuItemId = 5,  IngredientId = 1,  Quantity = 0.06m },  // Chicken Wrap → Flour
            new Recipe { MenuItemId = 5,  IngredientId = 9,  Quantity = 0.03m },  // Chicken Wrap → Onion
            new Recipe { MenuItemId = 5,  IngredientId = 10, Quantity = 0.03m },  // Chicken Wrap → Tomato
            new Recipe { MenuItemId = 5,  IngredientId = 16, Quantity = 0.02m },  // Chicken Wrap → Mayo

            new Recipe { MenuItemId = 6,  IngredientId = 11, Quantity = 0.18m },  // Twister → Chicken
            new Recipe { MenuItemId = 6,  IngredientId = 1,  Quantity = 0.06m },  // Twister → Flour
            new Recipe { MenuItemId = 6,  IngredientId = 9,  Quantity = 0.03m },  // Twister → Onion
            new Recipe { MenuItemId = 6,  IngredientId = 15, Quantity = 0.02m },  // Twister → Chilli Sauce
            new Recipe { MenuItemId = 6,  IngredientId = 16, Quantity = 0.02m },  // Twister → Mayo

            // Wings (chicken, oil, chilli sauce, garlic, ginger, spices)
            new Recipe { MenuItemId = 7,  IngredientId = 11, Quantity = 0.35m },  // Wings 6pc → Chicken 350g
            new Recipe { MenuItemId = 7,  IngredientId = 3,  Quantity = 0.10m },  // Wings 6pc → Oil
            new Recipe { MenuItemId = 7,  IngredientId = 15, Quantity = 0.03m },  // Wings 6pc → Chilli Sauce
            new Recipe { MenuItemId = 7,  IngredientId = 12, Quantity = 0.01m },  // Wings 6pc → Garlic
            new Recipe { MenuItemId = 7,  IngredientId = 6,  Quantity = 0.005m }, // Wings 6pc → Cumin

            new Recipe { MenuItemId = 8,  IngredientId = 11, Quantity = 0.70m },  // Wings 12pc → Chicken 700g
            new Recipe { MenuItemId = 8,  IngredientId = 3,  Quantity = 0.20m },  // Wings 12pc → Oil
            new Recipe { MenuItemId = 8,  IngredientId = 15, Quantity = 0.05m },  // Wings 12pc → Chilli Sauce
            new Recipe { MenuItemId = 8,  IngredientId = 12, Quantity = 0.02m },  // Wings 12pc → Garlic
            new Recipe { MenuItemId = 8,  IngredientId = 6,  Quantity = 0.01m },  // Wings 12pc → Cumin

            // Fish Fillet (flour, oil, salt, ketchup)
            new Recipe { MenuItemId = 9,  IngredientId = 1,  Quantity = 0.10m },  // Fish → Flour (batter)
            new Recipe { MenuItemId = 9,  IngredientId = 3,  Quantity = 0.08m },  // Fish → Oil
            new Recipe { MenuItemId = 9,  IngredientId = 4,  Quantity = 0.005m }, // Fish → Salt
            new Recipe { MenuItemId = 9,  IngredientId = 14, Quantity = 0.03m },  // Fish → Ketchup

            // Club Sandwich (flour/bread, chicken, onion, tomato, mayo, ketchup)
            new Recipe { MenuItemId = 10, IngredientId = 11, Quantity = 0.12m },  // Club → Chicken
            new Recipe { MenuItemId = 10, IngredientId = 1,  Quantity = 0.06m },  // Club → Flour (bread)
            new Recipe { MenuItemId = 10, IngredientId = 9,  Quantity = 0.03m },  // Club → Onion
            new Recipe { MenuItemId = 10, IngredientId = 10, Quantity = 0.03m },  // Club → Tomato
            new Recipe { MenuItemId = 10, IngredientId = 16, Quantity = 0.02m },  // Club → Mayo
            new Recipe { MenuItemId = 10, IngredientId = 14, Quantity = 0.02m },  // Club → Ketchup

            // Fries (potato, oil, salt)
            new Recipe { MenuItemId = 11, IngredientId = 8,  Quantity = 0.15m },  // Regular Fries → Potato
            new Recipe { MenuItemId = 11, IngredientId = 3,  Quantity = 0.08m },  // Regular Fries → Oil
            new Recipe { MenuItemId = 11, IngredientId = 4,  Quantity = 0.003m }, // Regular Fries → Salt

            new Recipe { MenuItemId = 12, IngredientId = 8,  Quantity = 0.25m },  // Large Fries → Potato
            new Recipe { MenuItemId = 12, IngredientId = 3,  Quantity = 0.12m },  // Large Fries → Oil
            new Recipe { MenuItemId = 12, IngredientId = 4,  Quantity = 0.005m }, // Large Fries → Salt

            // Coleslaw (onion, mayo, vinegar, sugar, salt)
            new Recipe { MenuItemId = 13, IngredientId = 9,  Quantity = 0.05m },  // Coleslaw → Onion
            new Recipe { MenuItemId = 13, IngredientId = 16, Quantity = 0.04m },  // Coleslaw → Mayo
            new Recipe { MenuItemId = 13, IngredientId = 18, Quantity = 0.01m },  // Coleslaw → Vinegar
            new Recipe { MenuItemId = 13, IngredientId = 5,  Quantity = 0.005m }, // Coleslaw → Sugar

            // Naan (flour, salt, oil)
            new Recipe { MenuItemId = 14, IngredientId = 1,  Quantity = 0.12m },  // Plain Naan → Flour
            new Recipe { MenuItemId = 14, IngredientId = 4,  Quantity = 0.003m }, // Plain Naan → Salt
            new Recipe { MenuItemId = 14, IngredientId = 3,  Quantity = 0.01m },  // Plain Naan → Oil

            new Recipe { MenuItemId = 15, IngredientId = 1,  Quantity = 0.12m },  // Garlic Naan → Flour
            new Recipe { MenuItemId = 15, IngredientId = 12, Quantity = 0.01m },  // Garlic Naan → Garlic
            new Recipe { MenuItemId = 15, IngredientId = 4,  Quantity = 0.003m }, // Garlic Naan → Salt
            new Recipe { MenuItemId = 15, IngredientId = 3,  Quantity = 0.02m },  // Garlic Naan → Oil (butter)

            // Family Bucket Deal (chicken, oil, flour, potato, ketchup, pepsi 1L)
            new Recipe { MenuItemId = 16, IngredientId = 11, Quantity = 1.50m },  // Family Deal → Chicken 1.5kg
            new Recipe { MenuItemId = 16, IngredientId = 3,  Quantity = 0.40m },  // Family Deal → Oil
            new Recipe { MenuItemId = 16, IngredientId = 1,  Quantity = 0.30m },  // Family Deal → Flour
            new Recipe { MenuItemId = 16, IngredientId = 8,  Quantity = 0.30m },  // Family Deal → Potato (fries)
            new Recipe { MenuItemId = 16, IngredientId = 14, Quantity = 0.05m },  // Family Deal → Ketchup
            new Recipe { MenuItemId = 16, IngredientId = 19, Quantity = 1m },     // Family Deal → Pepsi 1L

            // Zinger Meal (chicken, flour, oil, potato, pepsi 250ml, ketchup)
            new Recipe { MenuItemId = 17, IngredientId = 11, Quantity = 0.20m },  // Zinger Meal → Chicken
            new Recipe { MenuItemId = 17, IngredientId = 1,  Quantity = 0.08m },  // Zinger Meal → Flour
            new Recipe { MenuItemId = 17, IngredientId = 3,  Quantity = 0.10m },  // Zinger Meal → Oil
            new Recipe { MenuItemId = 17, IngredientId = 8,  Quantity = 0.15m },  // Zinger Meal → Potato (fries)
            new Recipe { MenuItemId = 17, IngredientId = 20, Quantity = 1m },     // Zinger Meal → Pepsi 250ml
            new Recipe { MenuItemId = 17, IngredientId = 14, Quantity = 0.02m },  // Zinger Meal → Ketchup

            // Beverages — direct 1:1 link to stock
            new Recipe { MenuItemId = 18, IngredientId = 20, Quantity = 1m },     // Pepsi Regular → Pepsi 250ml (1 can)
            new Recipe { MenuItemId = 19, IngredientId = 19, Quantity = 1m },     // Pepsi 1.5L → Pepsi 1L (closest stock)

            // Chocolate Brownie (flour, sugar, oil)
            new Recipe { MenuItemId = 20, IngredientId = 1,  Quantity = 0.05m },  // Brownie → Flour
            new Recipe { MenuItemId = 20, IngredientId = 5,  Quantity = 0.04m },  // Brownie → Sugar
            new Recipe { MenuItemId = 20, IngredientId = 3,  Quantity = 0.03m }   // Brownie → Oil (butter)
        );
    }
}
