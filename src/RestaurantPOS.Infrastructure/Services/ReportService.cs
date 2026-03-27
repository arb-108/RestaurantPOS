using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly PosDbContext _db;

    public ReportService(PosDbContext db) => _db = db;

    public async Task<DailySummary> GetDailySummaryAsync(DateTime date)
    {
        // Always regenerate for today (data changes throughout the day)
        if (date.Date == DateTime.Today)
        {
            await GenerateDailySummaryAsync(date);
            var todaySummary = await _db.DailySummaries.FirstOrDefaultAsync(ds => ds.Date == date.Date.ToUniversalTime());
            return todaySummary ?? new DailySummary { Date = date.Date };
        }

        var existing = await _db.DailySummaries
            .FirstOrDefaultAsync(ds => ds.Date == date.Date.ToUniversalTime());

        if (existing != null) return existing;

        // Generate on the fly
        await GenerateDailySummaryAsync(date);
        return await _db.DailySummaries.FirstAsync(ds => ds.Date == date.Date.ToUniversalTime());
    }

    public async Task<IEnumerable<(string Category, long Total)>> GetSalesByCategoryAsync(DateTime from, DateTime to)
    {
        var results = await _db.OrderItems
            .Include(oi => oi.MenuItem).ThenInclude(mi => mi.Category)
            .Include(oi => oi.Order)
            .Where(oi => oi.Order.Status == OrderStatus.Closed
                && oi.Order.CreatedAt >= from.Date.ToUniversalTime()
                && oi.Order.CreatedAt < to.Date.AddDays(1).ToUniversalTime()
                && oi.Status != OrderStatus.Void)
            .GroupBy(oi => oi.MenuItem.Category.Name)
            .Select(g => new { Category = g.Key, Total = g.Sum(oi => oi.LineTotal) })
            .ToListAsync();

        return results.Select(r => (r.Category, r.Total));
    }

    public async Task<IEnumerable<(int Hour, long Total)>> GetSalesByHourAsync(DateTime date)
    {
        var start = date.Date.ToUniversalTime();
        var end = start.AddDays(1);

        var orders = await _db.Orders
            .Where(o => o.Status == OrderStatus.Closed && o.CreatedAt >= start && o.CreatedAt < end)
            .ToListAsync();

        var results = orders
            .GroupBy(o => o.CreatedAt.Hour)
            .Select(g => (Hour: g.Key, Total: g.Sum(o => o.GrandTotal)))
            .OrderBy(r => r.Hour);

        return results;
    }

    public async Task<IEnumerable<(string Method, long Total)>> GetSalesByPaymentMethodAsync(DateTime from, DateTime to)
    {
        var results = await _db.Payments
            .Include(p => p.PaymentMethod)
            .Include(p => p.Order)
            .Where(p => p.Order.Status == OrderStatus.Closed
                && p.Order.CreatedAt >= from.Date.ToUniversalTime()
                && p.Order.CreatedAt < to.Date.AddDays(1).ToUniversalTime())
            .GroupBy(p => p.PaymentMethod.Name)
            .Select(g => new { Method = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync();

        return results.Select(r => (r.Method, r.Total));
    }

    public async Task GenerateDailySummaryAsync(DateTime date)
    {
        var start = date.Date.ToUniversalTime();
        var end = start.AddDays(1);

        var orders = await _db.Orders
            .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
            .ToListAsync();

        var closed = orders.Where(o => o.Status == OrderStatus.Closed).ToList();

        var summary = new DailySummary
        {
            Date = start,
            TotalOrders = closed.Count,
            TotalRevenue = closed.Sum(o => o.GrandTotal),
            TotalTax = closed.Sum(o => o.TaxAmount),
            TotalDiscount = closed.Sum(o => o.DiscountAmount),
            CashSales = closed.SelectMany(o => o.Payments)
                .Where(p => p.PaymentMethod.Code == "CASH").Sum(p => p.Amount),
            CardSales = closed.SelectMany(o => o.Payments)
                .Where(p => p.PaymentMethod.Code == "DEBIT" || p.PaymentMethod.Code == "CREDIT").Sum(p => p.Amount),
            DigitalSales = closed.SelectMany(o => o.Payments)
                .Where(p => p.PaymentMethod.IsDigital).Sum(p => p.Amount),
            VoidedOrders = orders.Count(o => o.Status == OrderStatus.Void),
            PeakHour = closed.Any()
                ? closed.GroupBy(o => o.CreatedAt.Hour).OrderByDescending(g => g.Count()).First().Key
                : 0
        };

        var existing = await _db.DailySummaries.FirstOrDefaultAsync(ds => ds.Date == start);
        if (existing != null)
        {
            existing.TotalOrders = summary.TotalOrders;
            existing.TotalRevenue = summary.TotalRevenue;
            existing.TotalTax = summary.TotalTax;
            existing.TotalDiscount = summary.TotalDiscount;
            existing.CashSales = summary.CashSales;
            existing.CardSales = summary.CardSales;
            existing.DigitalSales = summary.DigitalSales;
            existing.VoidedOrders = summary.VoidedOrders;
            existing.PeakHour = summary.PeakHour;
        }
        else
        {
            _db.DailySummaries.Add(summary);
        }

        await _db.SaveChangesAsync();
    }
}
