namespace RestaurantPOS.Domain.Enums;

public enum OrderStatus
{
    Open,
    Preparing,
    Ready,
    Served,
    Closed,
    Void
}

public enum OrderType
{
    DineIn,
    TakeAway,
    Delivery
}

public enum PaymentStatus
{
    Pending,
    Partial,
    Paid,
    Refunded
}

public enum TableStatus
{
    Available,
    Occupied,
    Reserved,
    Cleaning
}

public enum ReservationStatus
{
    Pending,
    Confirmed,
    Seated,
    Completed,
    Cancelled,
    NoShow
}

public enum StockMovementType
{
    Purchase,
    Consumption,
    Waste,
    Adjustment
}

public enum DiscountType
{
    Percentage,
    FixedAmount,
    BuyXGetY
}

public enum SyncStatus
{
    Pending,
    Syncing,
    Synced,
    Failed
}

public enum CashDrawerLogType
{
    Sale,
    Refund,
    PayIn,
    PayOut,
    Tip
}

public enum LoyaltyTransactionType
{
    Earn,
    Redeem,
    Adjust,
    Expire
}

public enum PrinterType
{
    Receipt,
    KOT,
    Report
}

public enum ConnectionType
{
    USB,
    Network,
    Bluetooth,
    Serial
}

public enum KitchenOrderStatus
{
    New,
    InProgress,
    Ready,
    PickedUp
}

public enum KitchenItemStatus
{
    Pending,
    Cooking,
    Done
}

public enum ShapeType
{
    Rectangle,
    Circle,
    Square
}

public enum CustomerTier
{
    Regular,
    Silver,
    Gold,
    Platinum
}
