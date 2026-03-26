using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public OrderType OrderType { get; set; } = OrderType.DineIn;
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public int? TableSessionId { get; set; }
    public TableSession? TableSession { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? WaiterId { get; set; }
    public User? Waiter { get; set; }
    public int? CashierId { get; set; }
    public User? Cashier { get; set; }
    public long SubTotal { get; set; }
    public long TaxAmount { get; set; }
    public long DiscountAmount { get; set; }
    public long ServiceCharge { get; set; }
    public long GrandTotal { get; set; }
    public long Adjustment { get; set; }
    public string? Notes { get; set; }
    public string? VoidReason { get; set; }
    public int? ApprovedByUserId { get; set; }
    public User? ApprovedBy { get; set; }
    public bool IsSynced { get; set; }
    public int? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int? VariantId { get; set; }
    public MenuItemVariant? Variant { get; set; }
    public int Quantity { get; set; } = 1;
    public long UnitPrice { get; set; }
    public long LineTotal { get; set; }
    public string? Notes { get; set; }
    public string? VoidReason { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public DateTime? SentToKitchenAt { get; set; }
    public ICollection<OrderItemModifier> Modifiers { get; set; } = [];
}

public class OrderItemModifier : BaseEntity
{
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public int ModifierId { get; set; }
    public Modifier Modifier { get; set; } = null!;
    public long Price { get; set; }
}

public class PaymentMethod : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsDigital { get; set; }
    public ICollection<Payment> Payments { get; set; } = [];
}

public class Payment : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int PaymentMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public long Amount { get; set; }
    public long TenderedAmount { get; set; }
    public long ChangeAmount { get; set; }
    public string? ReferenceNo { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
}
