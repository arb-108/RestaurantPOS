using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.Application.Interfaces;

public interface ISettingsService
{
    Task<string?> GetSettingAsync(string key);
    Task SetSettingAsync(string key, string value);
    Task<IEnumerable<AppSetting>> GetAllSettingsAsync();
    Task<IEnumerable<PaymentMethod>> GetPaymentMethodsAsync();
    Task<IEnumerable<TaxRate>> GetTaxRatesAsync();
}
