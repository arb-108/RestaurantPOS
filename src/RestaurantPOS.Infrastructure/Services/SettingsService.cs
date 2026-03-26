using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly PosDbContext _db;

    public SettingsService(PosDbContext db) => _db = db;

    public async Task<string?> GetSettingAsync(string key)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync();
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
