using System.Collections.ObjectModel;
using System.IO;
using System.Printing;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly IAuthService _authService;
    private readonly PosDbContext _db;
    private readonly IDatabaseMaintenanceService _maintenance;

    [ObservableProperty] private int _selectedTab;

    // ═══════════════════════════════════════════════
    //  TAB VISIBILITY (permission-based)
    // ═══════════════════════════════════════════════
    [ObservableProperty] private bool _canAccessGeneralTab = true;
    [ObservableProperty] private bool _canAccessPrintersTab = true;
    [ObservableProperty] private bool _canAccessBackupTab = true;
    [ObservableProperty] private bool _canAccessReceiptTab = true;
    [ObservableProperty] private bool _canAccessTaxTab = true;
    [ObservableProperty] private bool _canAccessUsersTab = true;

    // ══════════════════════════════════════════════
    //  TAB 0: GENERAL SETTINGS
    // ══════════════════════════════════════════════

    [ObservableProperty] private string _restaurantName = string.Empty;
    [ObservableProperty] private string _restaurantAddress = string.Empty;
    [ObservableProperty] private string _restaurantPhone = string.Empty;
    [ObservableProperty] private string _currency = "PKR";
    [ObservableProperty] private string _currencySymbol = "Rs.";
    [ObservableProperty] private string _idleTimeout = "5";
    [ObservableProperty] private string _serviceChargePercent = "0";
    [ObservableProperty] private bool _autoPrintReceipt = true;
    [ObservableProperty] private bool _autoPrintKot = true;
    [ObservableProperty] private string _loyaltyPointsPerPkr = "1";
    [ObservableProperty] private string _loyaltyRedeemRate = "100";
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ══════════════════════════════════════════════
    //  TAB 1: PRINTER MANAGEMENT
    // ══════════════════════════════════════════════

    public ObservableCollection<Printer> Printers { get; } = [];
    public ObservableCollection<string> PrinterNames { get; } = [];
    [ObservableProperty] private Printer? _selectedPrinter;

    // Station → Printer assignment
    public ObservableCollection<StationPrinterAssignment> StationAssignments { get; } = [];

    // OnSelectedPrinterChanged — no form to populate, editing via popup window

    // ══════════════════════════════════════════════
    //  TAB 2: BACKUP & RECOVERY
    // ══════════════════════════════════════════════

    public ObservableCollection<BackupFileInfo> BackupFiles { get; } = [];
    [ObservableProperty] private BackupFileInfo? _selectedBackup;
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private string _lastBackupTime = "Never";
    [ObservableProperty] private string _databaseSize = "0 KB";
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private string _backupPath = string.Empty;

    // ══════════════════════════════════════════════
    //  TAB 3: RECEIPT SETTINGS
    // ══════════════════════════════════════════════

    [ObservableProperty] private string _receiptHeader = string.Empty;
    [ObservableProperty] private string _receiptFooter = string.Empty;
    [ObservableProperty] private string _receiptRestaurantName = string.Empty;
    [ObservableProperty] private string _receiptAddress = string.Empty;
    [ObservableProperty] private string _receiptPhone = string.Empty;
    [ObservableProperty] private bool _showCustomerOnReceipt = true;
    [ObservableProperty] private bool _showWaiterOnReceipt = true;
    [ObservableProperty] private bool _showOrderTypeOnReceipt = true;
    [ObservableProperty] private bool _showTaxBreakdown = true;

    // ══════════════════════════════════════════════
    //  TAB 4: TAX MANAGEMENT
    // ══════════════════════════════════════════════

    public ObservableCollection<TaxRate> TaxRates { get; } = [];
    [ObservableProperty] private TaxRate? _selectedTaxRate;
    [ObservableProperty] private string _taxRateName = string.Empty;
    [ObservableProperty] private string _taxRateValue = string.Empty;
    [ObservableProperty] private bool _taxRateIsInclusive;
    [ObservableProperty] private string _defaultTaxRateId = "1";
    [ObservableProperty] private TaxRate? _defaultTaxRate;
    [ObservableProperty] private string _taxCount = "0 rates";

    partial void OnSelectedTaxRateChanged(TaxRate? value)
    {
        if (value == null) return;
        TaxRateName = value.Name;
        TaxRateValue = value.Rate.ToString("G");
        TaxRateIsInclusive = value.IsInclusive;
    }

    // ══════════════════════════════════════════════
    //  TAB 5: USER & ROLE MANAGEMENT
    // ══════════════════════════════════════════════

    public ObservableCollection<User> Users { get; } = [];
    public ObservableCollection<Role> Roles { get; } = [];
    public ObservableCollection<RolePermissionRow> RolePermissions { get; } = [];
    [ObservableProperty] private User? _selectedUser;
    [ObservableProperty] private Role? _selectedRole;
    [ObservableProperty] private Role? _editingRole;

    // User form fields
    [ObservableProperty] private string _userFullName = string.Empty;
    [ObservableProperty] private string _userUsername = string.Empty;
    [ObservableProperty] private string _userPhone = string.Empty;
    [ObservableProperty] private string _userEmail = string.Empty;
    [ObservableProperty] private string _userPassword = string.Empty;
    [ObservableProperty] private string _userConfirmPassword = string.Empty;
    [ObservableProperty] private string _userPin = string.Empty;
    [ObservableProperty] private Role? _userRole;
    [ObservableProperty] private bool _isEditingUser;
    private int _editingUserId;

    // Role form
    [ObservableProperty] private string _roleName = string.Empty;
    [ObservableProperty] private string _roleDescription = string.Empty;
    [ObservableProperty] private bool _isEditingRole;
    [ObservableProperty] private string _userCount = "0 users";
    [ObservableProperty] private string _roleCount = "0 roles";

    partial void OnSelectedUserChanged(User? value)
    {
        if (value == null) return;
        UserFullName = value.FullName;
        UserUsername = value.Username;
        UserPhone = value.Phone ?? "";
        UserEmail = value.Email ?? "";
        UserRole = Roles.FirstOrDefault(r => r.Id == value.RoleId);
        UserPassword = "";
        UserConfirmPassword = "";
        UserPin = "";
        IsEditingUser = true;
        _editingUserId = value.Id;
    }

    partial void OnSelectedRoleChanged(Role? value)
    {
        if (value == null) return;
        _ = LoadRolePermissionsAsync(value.Id);
    }

    // ══════════════════════════════════════════════
    //  CONSTRUCTOR
    // ══════════════════════════════════════════════

    public SettingsViewModel(ISettingsService settingsService, IAuthService authService, PosDbContext db,
        IDatabaseMaintenanceService maintenance)
    {
        _settingsService = settingsService;
        _authService = authService;
        _db = db;
        _maintenance = maintenance;
        Title = "Settings";
        BackupPath = DatabaseConfig.GetBackupPath();
        ApplyTabPermissions();
    }

    private void ApplyTabPermissions()
    {
        // Tab 0: General — requires System app settings
        CanAccessGeneralTab = _authService.HasPermission("System app settings");

        // Tab 1: Printers — requires Manage printers & terminals
        CanAccessPrintersTab = _authService.HasPermission("Manage printers & terminals");

        // Tab 2: Backup — admin only (System app settings level 5)
        CanAccessBackupTab = _authService.HasPermission("System app settings", minimumLevel: 5);

        // Tab 3: Receipt — requires System app settings or Manage printers & terminals
        CanAccessReceiptTab = _authService.HasPermission("System app settings")
            || _authService.HasPermission("Manage printers & terminals");

        // Tab 4: Tax — requires Manage tax & discounts
        CanAccessTaxTab = _authService.HasPermission("Manage tax & discounts");

        // Tab 5: Users & Roles — requires Manage users & roles
        CanAccessUsersTab = _authService.HasPermission("Manage users & roles");
    }

    // ══════════════════════════════════════════════
    //  LOAD DATA
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // General settings
        RestaurantName = await _settingsService.GetSettingAsync("RestaurantName") ?? "KFC Restaurant";
        RestaurantAddress = await _settingsService.GetSettingAsync("RestaurantAddress") ?? "";
        RestaurantPhone = await _settingsService.GetSettingAsync("RestaurantPhone") ?? "";
        Currency = await _settingsService.GetSettingAsync("Currency") ?? "PKR";
        CurrencySymbol = await _settingsService.GetSettingAsync("CurrencySymbol") ?? "Rs.";
        IdleTimeout = await _settingsService.GetSettingAsync("IdleTimeoutMinutes") ?? "5";
        ServiceChargePercent = await _settingsService.GetSettingAsync("ServiceChargePercent") ?? "0";
        AutoPrintReceipt = (await _settingsService.GetSettingAsync("AutoPrintReceipt")) != "false";
        AutoPrintKot = (await _settingsService.GetSettingAsync("AutoPrintKOT")) != "false";
        LoyaltyPointsPerPkr = await _settingsService.GetSettingAsync("LoyaltyPointsPerPKR") ?? "1";
        LoyaltyRedeemRate = await _settingsService.GetSettingAsync("LoyaltyRedeemRate") ?? "100";
        AutoBackupEnabled = (await _settingsService.GetSettingAsync("AutoBackupEnabled")) == "true";

        // Receipt settings
        ReceiptHeader = await _settingsService.GetSettingAsync("ReceiptHeader") ?? "";
        ReceiptFooter = await _settingsService.GetSettingAsync("ReceiptFooter") ?? "";
        ReceiptRestaurantName = await _settingsService.GetSettingAsync("ReceiptRestaurantName") ?? RestaurantName;
        ReceiptAddress = await _settingsService.GetSettingAsync("ReceiptAddress") ?? RestaurantAddress;
        ReceiptPhone = await _settingsService.GetSettingAsync("ReceiptPhone") ?? RestaurantPhone;
        ShowCustomerOnReceipt = (await _settingsService.GetSettingAsync("ShowCustomerOnReceipt")) != "false";
        ShowWaiterOnReceipt = (await _settingsService.GetSettingAsync("ShowWaiterOnReceipt")) != "false";
        ShowOrderTypeOnReceipt = (await _settingsService.GetSettingAsync("ShowOrderTypeOnReceipt")) != "false";
        ShowTaxBreakdown = (await _settingsService.GetSettingAsync("ShowTaxBreakdown")) != "false";
        DefaultTaxRateId = await _settingsService.GetSettingAsync("DefaultTaxRateId") ?? "1";

        // Load collections
        await LoadPrintersAsync();
        await LoadTaxRatesAsync();
        await LoadUsersAsync();
        await LoadRolesAsync();
        await LoadBackupsAsync();
        LoadDatabaseInfo();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        switch (SelectedTab)
        {
            case 0: break; // General - already bound
            case 1: await LoadPrintersAsync(); await LoadStationAssignmentsAsync(); break;
            case 2: await LoadBackupsAsync(); LoadDatabaseInfo(); break;
            case 3: break; // Receipt - already bound
            case 4: await LoadTaxRatesAsync(); break;
            case 5: await LoadUsersAsync(); await LoadRolesAsync(); break;
        }
    }

    // ══════════════════════════════════════════════
    //  TAB 0: GENERAL SETTINGS
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task SaveGeneralSettingsAsync()
    {
        await _settingsService.SetSettingAsync("RestaurantName", RestaurantName);
        await _settingsService.SetSettingAsync("RestaurantAddress", RestaurantAddress);
        await _settingsService.SetSettingAsync("RestaurantPhone", RestaurantPhone);
        await _settingsService.SetSettingAsync("Currency", Currency);
        await _settingsService.SetSettingAsync("CurrencySymbol", CurrencySymbol);
        await _settingsService.SetSettingAsync("IdleTimeoutMinutes", IdleTimeout);
        await _settingsService.SetSettingAsync("ServiceChargePercent", ServiceChargePercent);
        await _settingsService.SetSettingAsync("AutoPrintReceipt", AutoPrintReceipt.ToString().ToLower());
        await _settingsService.SetSettingAsync("AutoPrintKOT", AutoPrintKot.ToString().ToLower());
        await _settingsService.SetSettingAsync("LoyaltyPointsPerPKR", LoyaltyPointsPerPkr);
        await _settingsService.SetSettingAsync("LoyaltyRedeemRate", LoyaltyRedeemRate);
        await _settingsService.SetSettingAsync("AutoBackupEnabled", AutoBackupEnabled.ToString().ToLower());
        StatusMessage = "General settings saved successfully!";
    }

    // ══════════════════════════════════════════════
    //  TAB 1: PRINTER MANAGEMENT
    // ══════════════════════════════════════════════

    private async Task LoadPrintersAsync()
    {
        var printers = await _db.Printers.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
        Printers.Clear();
        PrinterNames.Clear();
        PrinterNames.Add("(None)");
        foreach (var p in printers)
        {
            Printers.Add(p);
            PrinterNames.Add(p.Name);
        }
    }

    private async Task LoadStationAssignmentsAsync()
    {
        var stations = await _db.KitchenStations
            .Include(s => s.Printer)
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync();

        StationAssignments.Clear();
        foreach (var s in stations)
        {
            StationAssignments.Add(new StationPrinterAssignment
            {
                StationId = s.Id,
                StationName = s.Name,
                PrinterName = s.Printer?.Name ?? "(None)",
                PrinterId = s.PrinterId
            });
        }
    }

    [RelayCommand]
    private async Task AddPrinterAsync()
    {
        var win = new Views.PrinterFormWindow();
        win.Owner = System.Windows.Application.Current.MainWindow;
        if (win.ShowDialog() != true) return;

        if (!Enum.TryParse<PrinterType>(win.PrinterTypeName, out var pType)) return;
        if (!Enum.TryParse<ConnectionType>(win.ConnectionTypeName, out var cType)) return;

        var printer = new Printer
        {
            Name = win.PrinterDisplayName,
            Address = win.PrinterAddress,
            PaperWidth = win.PaperWidth,
            IsDefault = win.IsDefaultPrinter,
            Type = pType,
            ConnectionType = cType,
            SystemPrinterName = string.IsNullOrWhiteSpace(win.SystemPrinterName) ? null : win.SystemPrinterName
        };
        _db.Printers.Add(printer);

        if (printer.IsDefault)
        {
            var others = await _db.Printers
                .Where(p => p.IsActive && p.Type == pType && p.IsDefault)
                .ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        await _db.SaveChangesAsync();
        await LoadPrintersAsync();
        await LoadStationAssignmentsAsync();
        StatusMessage = "Printer added!";
    }

    [RelayCommand]
    private async Task EditPrinterAsync()
    {
        if (SelectedPrinter == null) { StatusMessage = "Select a printer first."; return; }

        var win = new Views.PrinterFormWindow();
        win.Owner = System.Windows.Application.Current.MainWindow;
        win.LoadPrinter(SelectedPrinter);
        if (win.ShowDialog() != true) return;

        if (!Enum.TryParse<PrinterType>(win.PrinterTypeName, out var pType)) return;
        if (!Enum.TryParse<ConnectionType>(win.ConnectionTypeName, out var cType)) return;

        SelectedPrinter.Name = win.PrinterDisplayName;
        SelectedPrinter.Address = win.PrinterAddress;
        SelectedPrinter.PaperWidth = win.PaperWidth;
        SelectedPrinter.IsDefault = win.IsDefaultPrinter;
        SelectedPrinter.Type = pType;
        SelectedPrinter.ConnectionType = cType;
        SelectedPrinter.SystemPrinterName = string.IsNullOrWhiteSpace(win.SystemPrinterName) ? null : win.SystemPrinterName;

        if (SelectedPrinter.IsDefault)
        {
            var others = await _db.Printers
                .Where(p => p.IsActive && p.Type == pType && p != SelectedPrinter && p.IsDefault)
                .ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        await _db.SaveChangesAsync();
        await LoadPrintersAsync();
        await LoadStationAssignmentsAsync();
        StatusMessage = "Printer updated!";
    }

    [RelayCommand]
    private async Task EditPrinterRowAsync(Printer? printer)
    {
        if (printer == null) return;
        SelectedPrinter = printer;
        await EditPrinterAsync();
    }

    [RelayCommand]
    private async Task DeletePrinterAsync()
    {
        if (SelectedPrinter == null) { StatusMessage = "Select a printer first."; return; }
        if (System.Windows.MessageBox.Show($"Delete printer '{SelectedPrinter.Name}'?", "Confirm",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
        SelectedPrinter.IsActive = false;
        await _db.SaveChangesAsync();
        await LoadPrintersAsync();
        await LoadStationAssignmentsAsync();
        StatusMessage = "Printer deleted.";
    }

    public async Task UpdateStationPrinterAsync(StationPrinterAssignment assignment, string printerName)
    {
        var station = await _db.KitchenStations.FindAsync(assignment.StationId);
        if (station == null) return;

        if (printerName == "(None)")
        {
            station.PrinterId = null;
        }
        else
        {
            var printer = Printers.FirstOrDefault(p => p.Name == printerName);
            if (printer != null) station.PrinterId = printer.Id;
        }

        await _db.SaveChangesAsync();
        StatusMessage = $"Station '{assignment.StationName}' → {printerName}";
    }

    // ══════════════════════════════════════════════
    //  TAB 2: BACKUP & RECOVERY
    // ══════════════════════════════════════════════

    private Task LoadBackupsAsync()
    {
        BackupFiles.Clear();
        try
        {
            if (!Directory.Exists(BackupPath)) Directory.CreateDirectory(BackupPath);

            var ext = _maintenance.BackupExtension;
            var files = Directory.GetFiles(BackupPath, $"*{ext}")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(50);

            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                BackupFiles.Add(new BackupFileInfo
                {
                    FileName = fi.Name,
                    FilePath = fi.FullName,
                    Size = FormatFileSize(fi.Length),
                    Date = fi.LastWriteTime.ToString("dd/MM/yyyy hh:mm tt"),
                    RawDate = fi.LastWriteTime
                });
            }

            LastBackupTime = BackupFiles.Count > 0
                ? BackupFiles[0].Date
                : "Never";
        }
        catch { }
        return Task.CompletedTask;
    }

    private async void LoadDatabaseInfo()
    {
        try
        {
            DatabaseSize = await _maintenance.GetDatabaseSizeAsync();
        }
        catch { }
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        try
        {
            var backupFile = await _maintenance.BackupAsync();

            await LoadBackupsAsync();
            BackupStatus = $"Backup created: {Path.GetFileName(backupFile)}";
            StatusMessage = "Database backup completed!";
        }
        catch (Exception ex)
        {
            BackupStatus = $"Backup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreDatabaseAsync()
    {
        if (SelectedBackup == null) return;

        var result = MessageBox.Show(
            $"Restore database from backup:\n{SelectedBackup.FileName}\n\nThis will REPLACE all current data. The application will need to restart.\n\nAre you sure?",
            "Restore Database", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _maintenance.RestoreAsync(SelectedBackup.FilePath);

            BackupStatus = "Database restored. Please restart the application.";
            StatusMessage = "Database restored! Please restart application.";

            MessageBox.Show("Database restored successfully!\nPlease restart the application for changes to take effect.",
                "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            BackupStatus = $"Restore failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = _maintenance.FileFilter,
                FileName = $"posdata-export-{DateTime.Now:yyyyMMdd}{_maintenance.BackupExtension}"
            };
            if (dialog.ShowDialog() != true) return;

            await _maintenance.ExportAsync(dialog.FileName);
            StatusMessage = "Database exported!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = _maintenance.FileFilter,
                Title = "Select database file to import"
            };
            if (dialog.ShowDialog() != true) return;

            await _maintenance.ImportAsync(dialog.FileName);
            await LoadBackupsAsync();
            StatusMessage = "Backup imported to backup list!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync()
    {
        if (SelectedBackup == null) return;
        try
        {
            File.Delete(SelectedBackup.FilePath);
            await LoadBackupsAsync();
        }
        catch { }
    }

    // ══════════════════════════════════════════════
    //  TAB 3: RECEIPT SETTINGS
    // ══════════════════════════════════════════════

    [RelayCommand]
    private async Task SaveReceiptSettingsAsync()
    {
        await _settingsService.SetSettingAsync("ReceiptHeader", ReceiptHeader);
        await _settingsService.SetSettingAsync("ReceiptFooter", ReceiptFooter);
        await _settingsService.SetSettingAsync("ReceiptRestaurantName", ReceiptRestaurantName);
        await _settingsService.SetSettingAsync("ReceiptAddress", ReceiptAddress);
        await _settingsService.SetSettingAsync("ReceiptPhone", ReceiptPhone);
        await _settingsService.SetSettingAsync("ShowCustomerOnReceipt", ShowCustomerOnReceipt.ToString().ToLower());
        await _settingsService.SetSettingAsync("ShowWaiterOnReceipt", ShowWaiterOnReceipt.ToString().ToLower());
        await _settingsService.SetSettingAsync("ShowOrderTypeOnReceipt", ShowOrderTypeOnReceipt.ToString().ToLower());
        await _settingsService.SetSettingAsync("ShowTaxBreakdown", ShowTaxBreakdown.ToString().ToLower());
        StatusMessage = "Receipt settings saved!";
    }

    // ══════════════════════════════════════════════
    //  TAB 4: TAX MANAGEMENT
    // ══════════════════════════════════════════════

    private async Task LoadTaxRatesAsync()
    {
        var rates = await _db.TaxRates.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        TaxRates.Clear();
        foreach (var t in rates) TaxRates.Add(t);
        TaxCount = $"{rates.Count} rates";

        if (int.TryParse(DefaultTaxRateId, out var dtId))
            DefaultTaxRate = TaxRates.FirstOrDefault(t => t.Id == dtId);
    }

    [RelayCommand]
    private async Task SaveTaxRateAsync()
    {
        if (string.IsNullOrWhiteSpace(TaxRateName)) return;
        if (!decimal.TryParse(TaxRateValue, out var rate)) return;

        if (SelectedTaxRate != null)
        {
            // Update existing
            SelectedTaxRate.Name = TaxRateName;
            SelectedTaxRate.Rate = rate;
            SelectedTaxRate.IsInclusive = TaxRateIsInclusive;
        }
        else
        {
            // Add new
            _db.TaxRates.Add(new TaxRate
            {
                Name = TaxRateName,
                Rate = rate,
                IsInclusive = TaxRateIsInclusive
            });
        }

        await _db.SaveChangesAsync();
        await LoadTaxRatesAsync();
        ClearTaxForm();
        StatusMessage = "Tax rate saved!";
    }

    [RelayCommand]
    private async Task DeleteTaxRateAsync()
    {
        if (SelectedTaxRate == null) return;

        // Check if used by menu items
        var usedCount = await _db.MenuItems.CountAsync(mi => mi.TaxRateId == SelectedTaxRate.Id && mi.IsActive);
        if (usedCount > 0)
        {
            MessageBox.Show($"Cannot delete: {usedCount} menu items use this tax rate.",
                "Tax Rate In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedTaxRate.IsActive = false;
        await _db.SaveChangesAsync();
        await LoadTaxRatesAsync();
        ClearTaxForm();
    }

    [RelayCommand]
    private async Task SetDefaultTaxRateAsync()
    {
        if (SelectedTaxRate == null) return;
        await _settingsService.SetSettingAsync("DefaultTaxRateId", SelectedTaxRate.Id.ToString());
        DefaultTaxRate = SelectedTaxRate;
        DefaultTaxRateId = SelectedTaxRate.Id.ToString();
        StatusMessage = $"Default tax rate set to: {SelectedTaxRate.Name}";
    }

    [RelayCommand]
    private void NewTaxRate()
    {
        SelectedTaxRate = null;
        ClearTaxForm();
    }

    private void ClearTaxForm()
    {
        TaxRateName = "";
        TaxRateValue = "";
        TaxRateIsInclusive = false;
    }

    // ══════════════════════════════════════════════
    //  TAB 5: USER & ROLE MANAGEMENT
    // ══════════════════════════════════════════════

    private async Task LoadUsersAsync()
    {
        var users = await _db.Users.Include(u => u.Role).Where(u => u.IsActive).OrderBy(u => u.FullName).ToListAsync();
        Users.Clear();
        foreach (var u in users) Users.Add(u);
        UserCount = $"{users.Count} users";
    }

    private async Task LoadRolesAsync()
    {
        try
        {
            var roles = await _db.Roles.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
            RoleCount = $"{roles.Count} roles";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] LoadRoles error: {ex.Message}");
        }
    }

    private async Task LoadRolePermissionsAsync(int roleId)
    {
        try
        {
            var allPermissions = await _db.Permissions.Where(p => p.IsActive).OrderBy(p => p.Module).ThenBy(p => p.Name).ToListAsync();

            // Build dict manually to avoid AccessLevel column issues on older DBs
            var rolePermsList = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
            var rolePerms = rolePermsList.ToDictionary(rp => rp.PermissionId, rp => rp.AccessLevel);

            RolePermissions.Clear();
            foreach (var p in allPermissions)
            {
                RolePermissions.Add(new RolePermissionRow
                {
                    PermissionId = p.Id,
                    PermissionName = p.Name,
                    Module = p.Module ?? "Other",
                    Description = p.Description ?? "",
                    AccessLevel = rolePerms.GetValueOrDefault(p.Id, 0),
                    IsGranted = rolePerms.ContainsKey(p.Id)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] LoadRolePermissions error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddUserWindowAsync()
    {
        var window = new Views.UserFormWindow(Roles);
        window.Owner = System.Windows.Application.Current.MainWindow;
        if (window.ShowDialog() == true)
        {
            await SaveUserFromWindow(window);
        }
    }

    [RelayCommand]
    private async Task EditUserWindowAsync(User? user)
    {
        if (user == null) return;
        var window = new Views.UserFormWindow(Roles, user);
        window.Owner = System.Windows.Application.Current.MainWindow;
        if (window.ShowDialog() == true)
        {
            await SaveUserFromWindow(window);
        }
    }

    private async Task SaveUserFromWindow(Views.UserFormWindow w)
    {
        // Check username uniqueness
        var checkId = w.IsEditing ? w.EditUserId : 0;
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Username == w.UserUsername && u.Id != checkId && u.IsActive);
        if (existing != null)
        {
            MessageBox.Show("Username already taken.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (w.IsEditing)
        {
            var user = await _db.Users.FindAsync(w.EditUserId);
            if (user == null) return;
            user.FullName = w.UserFullName;
            user.Username = w.UserUsername;
            user.Phone = string.IsNullOrWhiteSpace(w.UserPhone) ? null : w.UserPhone;
            user.Email = string.IsNullOrWhiteSpace(w.UserEmail) ? null : w.UserEmail;
            user.RoleId = w.SelectedRole!.Id;
            if (!string.IsNullOrWhiteSpace(w.UserPassword))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(w.UserPassword);
            if (!string.IsNullOrWhiteSpace(w.UserPin))
                user.Pin = HashPin(w.UserPin);
        }
        else
        {
            var user = new User
            {
                FullName = w.UserFullName,
                Username = w.UserUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(w.UserPassword),
                Pin = string.IsNullOrWhiteSpace(w.UserPin) ? null : HashPin(w.UserPin),
                RoleId = w.SelectedRole!.Id,
                Phone = string.IsNullOrWhiteSpace(w.UserPhone) ? null : w.UserPhone,
                Email = string.IsNullOrWhiteSpace(w.UserEmail) ? null : w.UserEmail
            };
            _db.Users.Add(user);
        }

        await _db.SaveChangesAsync();
        await LoadUsersAsync();
        StatusMessage = w.IsEditing ? "User updated!" : "User created!";
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null) return;
        if (SelectedUser.Id == 1)
        {
            StatusMessage = "Cannot delete the default admin user.";
            return;
        }

        var result = MessageBox.Show($"Delete user '{SelectedUser.FullName}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SelectedUser.IsActive = false;
        await _db.SaveChangesAsync();
        await LoadUsersAsync();
    }

    // Role management

    [RelayCommand]
    private async Task SaveRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(RoleName))
        {
            StatusMessage = "Role name is required.";
            return;
        }

        if (IsEditingRole && EditingRole != null)
        {
            EditingRole.Name = RoleName;
            EditingRole.Description = RoleDescription;
        }
        else
        {
            var role = new Role
            {
                Name = RoleName.ToLower(),
                Description = RoleDescription
            };
            _db.Roles.Add(role);
        }

        await _db.SaveChangesAsync();
        await LoadRolesAsync();
        ClearRoleForm();
        StatusMessage = "Role saved!";
    }

    [RelayCommand]
    private async Task DeleteRoleAsync()
    {
        if (SelectedRole == null) return;
        if (SelectedRole.Id <= 5)
        {
            StatusMessage = "Cannot delete built-in roles.";
            return;
        }

        var userCount = await _db.Users.CountAsync(u => u.RoleId == SelectedRole.Id && u.IsActive);
        if (userCount > 0)
        {
            StatusMessage = $"Cannot delete: {userCount} users have this role.";
            return;
        }

        SelectedRole.IsActive = false;
        await _db.SaveChangesAsync();
        await LoadRolesAsync();
    }

    [RelayCommand]
    private void EditRole()
    {
        if (SelectedRole == null) return;
        EditingRole = SelectedRole;
        RoleName = SelectedRole.Name;
        RoleDescription = SelectedRole.Description ?? "";
        IsEditingRole = true;
    }

    [RelayCommand]
    private void NewRole()
    {
        EditingRole = null;
        ClearRoleForm();
    }

    private void ClearRoleForm()
    {
        RoleName = "";
        RoleDescription = "";
        IsEditingRole = false;
        EditingRole = null;
    }

    [RelayCommand]
    private async Task SaveRolePermissionsAsync()
    {
        if (SelectedRole == null) return;

        // Remove existing
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == SelectedRole.Id).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        // Add new
        foreach (var rp in RolePermissions.Where(rp => rp.IsGranted))
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = SelectedRole.Id,
                PermissionId = rp.PermissionId,
                AccessLevel = rp.AccessLevel
            });
        }

        await _db.SaveChangesAsync();
        StatusMessage = $"Permissions saved for role '{SelectedRole.Name}'!";
    }

    // ══════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexStringLower(bytes);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N1} KB";
        return $"{bytes / (1024.0 * 1024):N1} MB";
    }
}

// ══════════════════════════════════════════════
//  ROW VIEW MODELS
// ══════════════════════════════════════════════

public class BackupFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public DateTime RawDate { get; set; }
}

public class StationPrinterAssignment
{
    public int StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public string PrinterName { get; set; } = "(None)";
    public int? PrinterId { get; set; }
}

public class RolePermissionRow : ObservableObject
{
    public int PermissionId { get; set; }
    public string PermissionName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    private int _accessLevel;
    public int AccessLevel
    {
        get => _accessLevel;
        set { SetProperty(ref _accessLevel, value); OnPropertyChanged(nameof(IsGranted)); }
    }

    private bool _isGranted;
    public bool IsGranted
    {
        get => _isGranted;
        set { SetProperty(ref _isGranted, value); if (!value) AccessLevel = 0; else if (AccessLevel == 0) AccessLevel = 1; }
    }
}
