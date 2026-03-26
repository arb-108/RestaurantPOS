using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class KitchenService : IKitchenService
{
    private readonly PosDbContext _db;

    public KitchenService(PosDbContext db) => _db = db;

    public async Task<IEnumerable<KitchenOrder>> GetActiveKitchenOrdersAsync(int? stationId = null)
    {
        var query = _db.KitchenOrders
            .Include(ko => ko.Order)
                .ThenInclude(o => o.TableSession)
                    .ThenInclude(ts => ts!.Table)
            .Include(ko => ko.Items)
                .ThenInclude(ki => ki.OrderItem)
                    .ThenInclude(oi => oi.MenuItem)
            .Include(ko => ko.Station)
            .Where(ko => ko.Status != KitchenOrderStatus.PickedUp);

        if (stationId.HasValue)
            query = query.Where(ko => ko.StationId == stationId.Value);

        return await query
            .OrderBy(ko => ko.Priority)
            .ThenBy(ko => ko.CreatedAt)
            .ToListAsync();
    }

    public async Task SendToKitchenAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems.Where(oi => oi.Status != OrderStatus.Void && oi.SentToKitchenAt == null))
                .ThenInclude(oi => oi.MenuItem)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found");

        // Group items by kitchen station
        var groups = order.OrderItems
            .Where(oi => oi.MenuItem.KitchenStationId.HasValue)
            .GroupBy(oi => oi.MenuItem.KitchenStationId!.Value);

        foreach (var group in groups)
        {
            var kitchenOrder = new KitchenOrder
            {
                OrderId = orderId,
                StationId = group.Key,
                Status = KitchenOrderStatus.New,
                Priority = 0
            };

            foreach (var orderItem in group)
            {
                kitchenOrder.Items.Add(new KitchenOrderItem
                {
                    OrderItemId = orderItem.Id,
                    Status = KitchenItemStatus.Pending
                });
                orderItem.SentToKitchenAt = DateTime.UtcNow;
            }

            _db.KitchenOrders.Add(kitchenOrder);
        }

        order.Status = OrderStatus.Preparing;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateKitchenOrderStatusAsync(int kitchenOrderId, KitchenOrderStatus status)
    {
        var ko = await _db.KitchenOrders.FindAsync(kitchenOrderId)
            ?? throw new InvalidOperationException("Kitchen order not found");

        ko.Status = status;
        ko.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateKitchenItemStatusAsync(int kitchenOrderItemId, KitchenItemStatus status)
    {
        var item = await _db.KitchenOrderItems.FindAsync(kitchenOrderItemId)
            ?? throw new InvalidOperationException("Kitchen order item not found");

        item.Status = status;
        if (status == KitchenItemStatus.Cooking)
            item.StartedAt = DateTime.UtcNow;
        else if (status == KitchenItemStatus.Done)
            item.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task BumpOrderAsync(int kitchenOrderId)
    {
        var ko = await _db.KitchenOrders
            .Include(k => k.Items)
            .FirstOrDefaultAsync(k => k.Id == kitchenOrderId)
            ?? throw new InvalidOperationException("Kitchen order not found");

        ko.Status = KitchenOrderStatus.Ready;
        foreach (var item in ko.Items)
        {
            item.Status = KitchenItemStatus.Done;
            item.CompletedAt ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<KitchenStation>> GetStationsAsync()
    {
        return await _db.KitchenStations
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync();
    }
}
