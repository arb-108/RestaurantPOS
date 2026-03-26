using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class TaxRate : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public bool IsInclusive { get; set; }
    public ICollection<MenuItem> MenuItems { get; set; } = [];
}

public class Discount : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public long MinOrderAmount { get; set; }
    public long MaxDiscountAmount { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool RequiresApproval { get; set; }
    public string? Code { get; set; }
}

public class Terminal : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? MachineId { get; set; }
    public DateTime? LastActiveAt { get; set; }
}

public class Printer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public PrinterType Type { get; set; }
    public ConnectionType ConnectionType { get; set; }
    public string? Address { get; set; }
    public int PaperWidth { get; set; } = 80;
    public bool IsDefault { get; set; }
}

public class AppSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? DataType { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
}

public class Shift : BaseEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public long OpeningBalance { get; set; }
    public long ClosingBalance { get; set; }
    public long ExpectedBalance { get; set; }
    public long Discrepancy { get; set; }
    public string? Notes { get; set; }
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<CashDrawerLog> CashDrawerLogs { get; set; } = [];
}

public class CashDrawerLog : BaseEntity
{
    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;
    public CashDrawerLogType Type { get; set; }
    public long Amount { get; set; }
    public string? Description { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
}

public class SyncQueue : BaseEntity
{
    public string TableName { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public int Retries { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DailySummary : BaseEntity
{
    public DateTime Date { get; set; }
    public int TotalOrders { get; set; }
    public long TotalRevenue { get; set; }
    public long TotalTax { get; set; }
    public long TotalDiscount { get; set; }
    public long CashSales { get; set; }
    public long CardSales { get; set; }
    public long DigitalSales { get; set; }
    public int VoidedOrders { get; set; }
    public int PeakHour { get; set; }
    public int? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
}
