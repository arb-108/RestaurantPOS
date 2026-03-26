using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class FloorPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public ICollection<Table> Tables { get; set; } = [];
}

public class Table : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int FloorPlanId { get; set; }
    public FloorPlan FloorPlan { get; set; } = null!;
    public int Capacity { get; set; } = 4;
    public TableStatus Status { get; set; } = TableStatus.Available;
    public ShapeType Shape { get; set; } = ShapeType.Rectangle;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public int DisplayOrder { get; set; }
    public ICollection<TableSession> Sessions { get; set; } = [];
    public ICollection<Reservation> Reservations { get; set; } = [];
}

public class TableSession : BaseEntity
{
    public int TableId { get; set; }
    public Table Table { get; set; } = null!;
    public int? WaiterId { get; set; }
    public User? Waiter { get; set; }
    public int GuestCount { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public TableStatus Status { get; set; } = TableStatus.Occupied;
    public ICollection<Order> Orders { get; set; } = [];
}

public class Reservation : BaseEntity
{
    public int TableId { get; set; }
    public Table Table { get; set; } = null!;
    public string GuestName { get; set; } = string.Empty;
    public string GuestPhone { get; set; } = string.Empty;
    public int GuestCount { get; set; }
    public DateTime ReservedFor { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    public string? Notes { get; set; }
}
