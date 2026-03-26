using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Application.Interfaces;

public interface IKitchenService
{
    Task<IEnumerable<KitchenOrder>> GetActiveKitchenOrdersAsync(int? stationId = null);
    Task SendToKitchenAsync(int orderId);
    Task UpdateKitchenOrderStatusAsync(int kitchenOrderId, KitchenOrderStatus status);
    Task UpdateKitchenItemStatusAsync(int kitchenOrderItemId, KitchenItemStatus status);
    Task BumpOrderAsync(int kitchenOrderId);
    Task<IEnumerable<KitchenStation>> GetStationsAsync();
}
