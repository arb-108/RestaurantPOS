using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class KitchenStation : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int? PrinterId { get; set; }
    public Printer? Printer { get; set; }
    public string? DisplayTerminalId { get; set; }
    public int DisplayOrder { get; set; }
    public ICollection<KitchenOrder> KitchenOrders { get; set; } = [];
    public ICollection<MenuItem> MenuItems { get; set; } = [];
}

public class KitchenOrder : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int StationId { get; set; }
    public KitchenStation Station { get; set; } = null!;
    public KitchenOrderStatus Status { get; set; } = KitchenOrderStatus.New;
    public int Priority { get; set; }
    public ICollection<KitchenOrderItem> Items { get; set; } = [];
}

public class KitchenOrderItem : BaseEntity
{
    public int KitchenOrderId { get; set; }
    public KitchenOrder KitchenOrder { get; set; } = null!;
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public KitchenItemStatus Status { get; set; } = KitchenItemStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
