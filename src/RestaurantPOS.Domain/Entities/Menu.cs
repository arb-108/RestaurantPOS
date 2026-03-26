namespace RestaurantPOS.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public int DisplayOrder { get; set; }
    public string? ImagePath { get; set; }
    public ICollection<Category> Children { get; set; } = [];
    public ICollection<MenuItem> MenuItems { get; set; } = [];
}

public class MenuItem : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public long BasePrice { get; set; }
    public long CostPrice { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? TaxRateId { get; set; }
    public TaxRate? TaxRate { get; set; }
    public int? KitchenStationId { get; set; }
    public KitchenStation? KitchenStation { get; set; }
    public int PrepTimeMinutes { get; set; }
    public bool IsVeg { get; set; }
    public bool IsSpicy { get; set; }
    public string? Allergens { get; set; }
    public string? ImagePath { get; set; }
    public long MaxDiscount { get; set; }
    public int DisplayOrder { get; set; }
    public ICollection<MenuItemVariant> Variants { get; set; } = [];
    public ICollection<MenuItemModifierGroup> ModifierGroups { get; set; } = [];
    public ICollection<Recipe> Recipes { get; set; } = [];
}

public class MenuItemVariant : BaseEntity
{
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public long PriceOverride { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
}

public class ModifierGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; }
    public ICollection<Modifier> Modifiers { get; set; } = [];
    public ICollection<MenuItemModifierGroup> MenuItemModifierGroups { get; set; } = [];
}

public class Modifier : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public long ExtraPrice { get; set; }
    public int ModifierGroupId { get; set; }
    public ModifierGroup ModifierGroup { get; set; } = null!;
}

public class MenuItemModifierGroup
{
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int ModifierGroupId { get; set; }
    public ModifierGroup ModifierGroup { get; set; } = null!;
}

public class Ingredient : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public long CostPerUnit { get; set; }          // paisa per unit
    public string? StockCategory { get; set; }      // e.g. "Dry Goods", "Cold / Chilled", "Beverages"
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public ICollection<Recipe> Recipes { get; set; } = [];
    public ICollection<StockMovement> StockMovements { get; set; } = [];
}

public class Recipe
{
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
    public decimal Quantity { get; set; }
}

public class StockMovement : BaseEntity
{
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
    public Enums.StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public long? CostAmount { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
}
