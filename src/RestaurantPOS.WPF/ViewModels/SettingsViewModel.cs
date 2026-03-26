using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly PosDbContext _db;

    // General Settings
    [ObservableProperty]
    private string _restaurantName = string.Empty;

    [ObservableProperty]
    private string _currency = "PKR";

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    [ObservableProperty]
    private string _receiptHeader = string.Empty;

    [ObservableProperty]
    private string _receiptFooter = string.Empty;

    // Security
    [ObservableProperty]
    private string _idleTimeout = "5";

    // Tax
    public ObservableCollection<TaxRate> TaxRates { get; } = [];

    [ObservableProperty]
    private TaxRate? _selectedTaxRate;

    [ObservableProperty]
    private string _taxRateName = string.Empty;

    [ObservableProperty]
    private string _taxRateValue = string.Empty;

    [ObservableProperty]
    private bool _taxRateIsInclusive;

    // Users
    public ObservableCollection<User> Users { get; } = [];

    [ObservableProperty]
    private User? _selectedUser;

    // Printers
    public ObservableCollection<Printer> Printers { get; } = [];

    // Sync
    [ObservableProperty]
    private bool _syncEnabled;

    [ObservableProperty]
    private string _syncServerUrl = string.Empty;

    [ObservableProperty]
    private string _syncInterval = "300";

    public SettingsViewModel(ISettingsService settingsService, PosDbContext db)
    {
        _settingsService = settingsService;
        _db = db;
        Title = "Settings";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        RestaurantName = await _settingsService.GetSettingAsync("RestaurantName") ?? "KFC Restaurant";
        Currency = await _settingsService.GetSettingAsync("Currency") ?? "PKR";
        CurrencySymbol = await _settingsService.GetSettingAsync("CurrencySymbol") ?? "Rs.";
        ReceiptHeader = await _settingsService.GetSettingAsync("ReceiptHeader") ?? "";
        ReceiptFooter = await _settingsService.GetSettingAsync("ReceiptFooter") ?? "";
        IdleTimeout = await _settingsService.GetSettingAsync("IdleTimeoutMinutes") ?? "5";
        SyncEnabled = (await _settingsService.GetSettingAsync("SyncEnabled")) == "true";
        SyncServerUrl = await _settingsService.GetSettingAsync("SyncServerUrl") ?? "";
        SyncInterval = await _settingsService.GetSettingAsync("SyncIntervalSeconds") ?? "300";

        var taxRates = await _settingsService.GetTaxRatesAsync();
        TaxRates.Clear();
        foreach (var tr in taxRates) TaxRates.Add(tr);

        var users = await _db.Users.Include(u => u.Role).Where(u => u.IsActive).ToListAsync();
        Users.Clear();
        foreach (var u in users) Users.Add(u);

        var printers = await _db.Printers.Where(p => p.IsActive).ToListAsync();
        Printers.Clear();
        foreach (var p in printers) Printers.Add(p);
    }

    [RelayCommand]
    private async Task SaveGeneralSettingsAsync()
    {
        await _settingsService.SetSettingAsync("RestaurantName", RestaurantName);
        await _settingsService.SetSettingAsync("Currency", Currency);
        await _settingsService.SetSettingAsync("CurrencySymbol", CurrencySymbol);
        await _settingsService.SetSettingAsync("ReceiptHeader", ReceiptHeader);
        await _settingsService.SetSettingAsync("ReceiptFooter", ReceiptFooter);
        await _settingsService.SetSettingAsync("IdleTimeoutMinutes", IdleTimeout);
    }

    [RelayCommand]
    private async Task SaveSyncSettingsAsync()
    {
        await _settingsService.SetSettingAsync("SyncEnabled", SyncEnabled.ToString().ToLower());
        await _settingsService.SetSettingAsync("SyncServerUrl", SyncServerUrl);
        await _settingsService.SetSettingAsync("SyncIntervalSeconds", SyncInterval);
    }

    [RelayCommand]
    private async Task AddTaxRateAsync()
    {
        if (string.IsNullOrWhiteSpace(TaxRateName)) return;
        if (!decimal.TryParse(TaxRateValue, out var rate)) return;

        var taxRate = new TaxRate
        {
            Name = TaxRateName,
            Rate = rate,
            IsInclusive = TaxRateIsInclusive
        };

        _db.TaxRates.Add(taxRate);
        await _db.SaveChangesAsync();
        await LoadDataAsync();
        TaxRateName = string.Empty;
        TaxRateValue = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteTaxRateAsync()
    {
        if (SelectedTaxRate == null) return;
        SelectedTaxRate.IsActive = false;
        await _db.SaveChangesAsync();
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        var dbPath = DatabaseConfig.GetDatabasePath();
        var backupDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RestaurantPOS", "backups");

        //Directory.CreateDirectory(backupDir);
        //var backupFile = System.IO.Path.Combine(backupDir, $"posdata-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        //File.Copy(dbPath, backupFile, true);
    }
}
