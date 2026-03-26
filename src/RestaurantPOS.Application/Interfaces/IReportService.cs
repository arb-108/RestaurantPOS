using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.Application.Interfaces;

public interface IReportService
{
    Task<DailySummary> GetDailySummaryAsync(DateTime date);
    Task<IEnumerable<(string Category, long Total)>> GetSalesByCategoryAsync(DateTime from, DateTime to);
    Task<IEnumerable<(int Hour, long Total)>> GetSalesByHourAsync(DateTime date);
    Task<IEnumerable<(string Method, long Total)>> GetSalesByPaymentMethodAsync(DateTime from, DateTime to);
    Task GenerateDailySummaryAsync(DateTime date);
}
