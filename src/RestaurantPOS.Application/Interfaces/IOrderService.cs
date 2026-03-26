using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Application.Interfaces;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(OrderType orderType, int? tableId, int? customerId, int cashierId, int? shiftId = null);
    Task<Order?> GetOrderByIdAsync(int orderId);
    Task<OrderItem> AddItemToOrderAsync(int orderId, int menuItemId, int? variantId, int quantity, string? notes);
    Task UpdateItemQuantityAsync(int orderItemId, int quantity);
    Task RemoveItemFromOrderAsync(int orderItemId, string? voidReason);
    Task<Order> CalculateTotalsAsync(int orderId, decimal discountPercent = 0, decimal taxPercent = 0);
    Task<Order> CheckoutAsync(int orderId, int paymentMethodId, long tenderedAmount);
    Task VoidOrderAsync(int orderId, string reason, int approvedByUserId);
    Task<IEnumerable<Order>> GetOpenOrdersAsync();
    Task<IEnumerable<Order>> GetOrdersByDateAsync(DateTime date);
    Task<string> GenerateOrderNumberAsync();
    Task HoldOrderAsync(int orderId);
    Task<Order?> RecallOrderAsync(int orderId);
    Task UpdateOrderDiscountAsync(int orderId, long discountAmount);
    Task UpdateOrderAdjustmentAsync(int orderId, long adjustment);
    Task<IEnumerable<Order>> GetBillingHistoryAsync(DateTime fromDate, DateTime toDate, int? cashierId = null);
    Task UpdateOrderNotesAsync(int orderId, string? notes, int? customerId = null);
}
