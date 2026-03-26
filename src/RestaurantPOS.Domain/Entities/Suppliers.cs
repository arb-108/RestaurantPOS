namespace RestaurantPOS.Domain.Entities;

public class Supplier : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public decimal Balance { get; set; }   // outstanding balance
    public ICollection<SupplierExpense> Expenses { get; set; } = [];
}

public class SupplierExpense : BaseEntity
{
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public long Amount { get; set; }          // paisa
    public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;
    public string? InvoiceNumber { get; set; }
    public string? Category { get; set; }     // e.g. "Raw Material", "Equipment", "Packaging"
    public bool IsPaid { get; set; }
    public string? Notes { get; set; }
}
