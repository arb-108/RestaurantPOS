namespace RestaurantPOS.Domain.Entities;

/// <summary>A combo deal (e.g. 2 Burgers + Fries + Drink).</summary>
public class Deal : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long DealPrice { get; set; }          // paisa — what the customer pays
    public long OriginalPrice { get; set; }      // paisa — sum of individual items (for display)
    public int DisplayOrder { get; set; }
    public string? ImagePath { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public ICollection<DealItem> Items { get; set; } = [];
}

/// <summary>One line-item inside a deal.</summary>
public class DealItem : BaseEntity
{
    public int DealId { get; set; }
    public Deal Deal { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; } = 1;
}
