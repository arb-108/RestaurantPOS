using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly PosDbContext _db;

    public OrderService(PosDbContext db) => _db = db;

    public async Task<Order> CreateOrderAsync(OrderType orderType, int? tableId, int? customerId, int cashierId, int? shiftId = null)
    {
        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(),
            OrderType = orderType,
            Status = OrderStatus.Open,
            CustomerId = customerId,
            CashierId = cashierId,
            ShiftId = shiftId
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId)
    {
        return await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Variant)
            .Include(o => o.Payments)
                .ThenInclude(p => p.PaymentMethod)
            .Include(o => o.Customer)
            .Include(o => o.TableSession)
                .ThenInclude(ts => ts!.Table)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<OrderItem> AddItemToOrderAsync(int orderId, int menuItemId, int? variantId, int quantity, string? notes)
    {
        var menuItem = await _db.MenuItems.FindAsync(menuItemId)
            ?? throw new InvalidOperationException("Menu item not found");

        var price = variantId.HasValue
            ? (await _db.MenuItemVariants.FindAsync(variantId.Value))?.PriceOverride ?? menuItem.BasePrice
            : menuItem.BasePrice;

        // Check if this item is already in the order
        var existing = await _db.OrderItems
            .FirstOrDefaultAsync(oi => oi.OrderId == orderId && oi.MenuItemId == menuItemId
                && oi.VariantId == variantId && oi.Status != OrderStatus.Void);

        if (existing != null)
        {
            existing.Quantity += quantity;
            existing.LineTotal = existing.Quantity * existing.UnitPrice;
            await _db.SaveChangesAsync();
            return existing;
        }

        var orderItem = new OrderItem
        {
            OrderId = orderId,
            MenuItemId = menuItemId,
            VariantId = variantId,
            Quantity = quantity,
            UnitPrice = price,
            LineTotal = price * quantity,
            Notes = notes
        };

        _db.OrderItems.Add(orderItem);
        await _db.SaveChangesAsync();
        return orderItem;
    }

    public async Task UpdateItemQuantityAsync(int orderItemId, int quantity)
    {
        var item = await _db.OrderItems.FindAsync(orderItemId)
            ?? throw new InvalidOperationException("Order item not found");

        if (quantity <= 0)
        {
            _db.OrderItems.Remove(item);
        }
        else
        {
            item.Quantity = quantity;
            item.LineTotal = item.UnitPrice * quantity;
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveItemFromOrderAsync(int orderItemId, string? voidReason)
    {
        var item = await _db.OrderItems.FindAsync(orderItemId)
            ?? throw new InvalidOperationException("Order item not found");

        item.Status = OrderStatus.Void;
        item.VoidReason = voidReason;
        await _db.SaveChangesAsync();
    }

    public async Task<Order> CalculateTotalsAsync(int orderId, decimal discountPercent = 0, decimal taxPercent = 0)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems.Where(oi => oi.Status != OrderStatus.Void))
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.SubTotal = order.OrderItems.Sum(oi => oi.LineTotal);

        if (discountPercent > 0)
            order.DiscountAmount = (long)(order.SubTotal * (long)discountPercent / 100m);

        var taxable = order.SubTotal - order.DiscountAmount;

        if (taxPercent > 0)
            order.TaxAmount = (long)(taxable * (long)taxPercent / 100m);

        order.GrandTotal = taxable + order.TaxAmount + order.ServiceCharge + order.Adjustment;
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> CheckoutAsync(int orderId, int paymentMethodId, long tenderedAmount)
    {
        var order = await GetOrderByIdAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        var payment = new Payment
        {
            OrderId = orderId,
            PaymentMethodId = paymentMethodId,
            Amount = order.GrandTotal,
            TenderedAmount = tenderedAmount,
            ChangeAmount = tenderedAmount - order.GrandTotal,
            Status = PaymentStatus.Paid
        };

        _db.Payments.Add(payment);

        order.Status = OrderStatus.Closed;
        order.UpdatedAt = DateTime.UtcNow;

        // ── Deduct stock based on recipes ──
        await DeductStockForOrderAsync(order);

        // Update customer total spent
        if (order.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FindAsync(order.CustomerId.Value);
            if (customer != null)
            {
                customer.TotalSpent += order.GrandTotal;
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Close table session if dine-in
        if (order.TableSession != null)
        {
            order.TableSession.ClosedAt = DateTime.UtcNow;
            order.TableSession.Status = TableStatus.Available;
            order.TableSession.Table.Status = TableStatus.Available;
        }

        // Record sale in cash drawer log if order is linked to a shift
        if (order.ShiftId.HasValue)
        {
            var payMethod = await _db.PaymentMethods.FindAsync(paymentMethodId);
            var isCash = payMethod?.Name?.Contains("Cash", StringComparison.OrdinalIgnoreCase) == true;
            _db.CashDrawerLogs.Add(new CashDrawerLog
            {
                ShiftId = order.ShiftId.Value,
                Type = CashDrawerLogType.Sale,
                Amount = order.GrandTotal,
                Description = $"{order.OrderNumber} ({(isCash ? "Cash" : payMethod?.Name ?? "Other")})",
                OrderId = order.Id,
                UserId = order.CashierId
            });
        }

        await _db.SaveChangesAsync();
        return order;
    }

    public async Task VoidOrderAsync(int orderId, string reason, int approvedByUserId)
    {
        var order = await _db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.Status = OrderStatus.Void;
        order.VoidReason = reason;
        order.ApprovedByUserId = approvedByUserId;
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<Order>> GetOpenOrdersAsync()
    {
        return await _db.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
            .Include(o => o.TableSession).ThenInclude(ts => ts!.Table)
            .Include(o => o.Customer).ThenInclude(c => c!.Addresses)
            .Where(o => o.Status == OrderStatus.Open || o.Status == OrderStatus.Preparing)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetOrdersByDateAsync(DateTime date)
    {
        var start = date.Date.ToUniversalTime();
        var end = date.Date.AddDays(1).ToUniversalTime();
        return await _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payments)
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<string> GenerateOrderNumberAsync()
    {
        // Use LOCAL date for daily numbering (avoids UTC/local mismatch)
        var localToday = DateTime.Now.Date;
        var utcStart = localToday.ToUniversalTime();
        var utcEnd = localToday.AddDays(1).ToUniversalTime();

        var day = localToday.Day.ToString("D2"); // e.g. "24"
        var count = await _db.Orders.CountAsync(o => o.CreatedAt >= utcStart && o.CreatedAt < utcEnd);
        var counter = 10 + count;
        var candidate = $"O-{day}{counter}";

        // Safety: if candidate already exists, keep incrementing
        while (await _db.Orders.AnyAsync(o => o.OrderNumber == candidate))
        {
            counter++;
            candidate = $"O-{day}{counter}";
        }

        return candidate;
    }

    public async Task HoldOrderAsync(int orderId)
    {
        var order = await _db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.Status = OrderStatus.Open;
        order.Notes = (order.Notes ?? "") + " [HELD]";
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<Order?> RecallOrderAsync(int orderId)
    {
        return await GetOrderByIdAsync(orderId);
    }

    public async Task UpdateOrderDiscountAsync(int orderId, long discountAmount)
    {
        var order = await _db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.DiscountAmount = discountAmount;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateOrderAdjustmentAsync(int orderId, long adjustment)
    {
        var order = await _db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.Adjustment = adjustment;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateOrderNotesAsync(int orderId, string? notes, int? customerId = null)
    {
        var order = await _db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Order not found");

        order.Notes = notes;
        if (customerId.HasValue)
            order.CustomerId = customerId;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<Order>> GetBillingHistoryAsync(DateTime fromDate, DateTime toDate, int? cashierId = null)
    {
        var query = _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Payments)
                .ThenInclude(p => p.PaymentMethod)
            .Include(o => o.Cashier)
            .Include(o => o.Customer)
            .Where(o => o.CreatedAt >= fromDate.Date.ToUniversalTime() && o.CreatedAt < toDate.Date.AddDays(1).ToUniversalTime())
            // Billing shows only CLOSED (checkout) and VOID (unpaid bill) orders
            .Where(o => o.Status == OrderStatus.Closed || o.Status == OrderStatus.Void);

        // Role-based filtering: cashier sees only own orders
        if (cashierId.HasValue)
            query = query.Where(o => o.CashierId == cashierId.Value);

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    // ═══ Stock Deduction on Checkout ═══
    private async Task DeductStockForOrderAsync(Order order)
    {
        // Collect all menu-item IDs and their quantities from the order
        var activeItems = order.OrderItems
            .Where(oi => oi.Status != OrderStatus.Void)
            .ToList();

        if (activeItems.Count == 0) return;

        var menuItemIds = activeItems.Select(oi => oi.MenuItemId).Distinct().ToList();

        // Load recipes for all ordered menu items in one query
        var recipes = await _db.Recipes
            .Include(r => r.Ingredient)
            .Where(r => menuItemIds.Contains(r.MenuItemId))
            .ToListAsync();

        if (recipes.Count == 0) return;

        // Build a map: ingredientId → total quantity to deduct
        var deductions = new Dictionary<int, decimal>();

        foreach (var orderItem in activeItems)
        {
            var itemRecipes = recipes.Where(r => r.MenuItemId == orderItem.MenuItemId);
            foreach (var recipe in itemRecipes)
            {
                var deductQty = recipe.Quantity * orderItem.Quantity;
                if (deductions.ContainsKey(recipe.IngredientId))
                    deductions[recipe.IngredientId] += deductQty;
                else
                    deductions[recipe.IngredientId] = deductQty;
            }
        }

        // Apply deductions and create stock movement records
        foreach (var (ingredientId, totalQty) in deductions)
        {
            var ingredient = recipes.First(r => r.IngredientId == ingredientId).Ingredient;
            ingredient.CurrentStock -= totalQty;

            _db.StockMovements.Add(new StockMovement
            {
                IngredientId = ingredientId,
                Type = StockMovementType.Consumption,
                Quantity = -totalQty,
                CostAmount = (long)(totalQty * ingredient.CostPerUnit),
                Reference = order.OrderNumber,
                Notes = $"Auto-deducted on checkout ({order.OrderNumber})"
            });
        }
    }
}
