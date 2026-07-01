using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly PosDbContext _db;

    // The PosDbContext is long-lived and shared. WPF dispatches these calls on
    // one thread, but they interleave at await points — two SetSettingAsync calls
    // for the same key can both read "not found" and both INSERT, hitting the
    // unique Key index. Serialize all writes through this gate to prevent that.
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsService(PosDbContext db) => _db = db;

    public async Task<string?> GetSettingAsync(string key)
    {
        var setting = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await _writeLock.WaitAsync();
        try
        {
            await UpsertSettingAsync(key, value);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task UpsertSettingAsync(string key, string value)
    {
        // Check already-tracked entities first (a row inserted earlier in this
        // same context), then the database.
        var setting = _db.AppSettings.Local.FirstOrDefault(s => s.Key == key)
                      ?? await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);

        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost a race: the row now exists in the DB. Drop our failed INSERT,
            // reload the real row, and apply the update.
            foreach (var entry in _db.ChangeTracker.Entries<AppSetting>()
                         .Where(e => e.State == EntityState.Added && e.Entity.Key == key)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }

    public async Task<IEnumerable<AppSetting>> GetAllSettingsAsync()
    {
        return await _db.AppSettings.ToListAsync();
    }

    public async Task<IEnumerable<PaymentMethod>> GetPaymentMethodsAsync()
    {
        return await _db.PaymentMethods.Where(pm => pm.IsActive).ToListAsync();
    }

    public async Task<IEnumerable<TaxRate>> GetTaxRatesAsync()
    {
        return await _db.TaxRates.Where(t => t.IsActive).ToListAsync();
    }
}
