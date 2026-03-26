using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Domain.Entities;

public class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public long LoyaltyPoints { get; set; }
    public long TotalSpent { get; set; }
    public CustomerTier Tier { get; set; } = CustomerTier.Regular;
    public string? Notes { get; set; }
    public ICollection<CustomerAddress> Addresses { get; set; } = [];
    public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}

public class CustomerAddress : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Label { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsDefault { get; set; }
}

public class LoyaltyTransaction : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public LoyaltyTransactionType Type { get; set; }
    public long Points { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public string? Description { get; set; }
}
