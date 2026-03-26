using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class MenuService : IMenuService
{
    private readonly PosDbContext _db;

    public MenuService(PosDbContext db) => _db = db;

    public async Task<IEnumerable<Category>> GetCategoriesAsync()
    {
        return await _db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<MenuItem>> GetMenuItemsByCategoryAsync(int categoryId)
    {
        return await _db.MenuItems
            .Include(m => m.TaxRate)
            .Include(m => m.Category)
            .Where(m => m.CategoryId == categoryId && m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<MenuItem>> GetAllActiveMenuItemsAsync()
    {
        return await _db.MenuItems
            .Include(m => m.TaxRate)
            .Include(m => m.Category)
            .Where(m => m.IsActive)
            .OrderBy(m => m.CategoryId)
            .ThenBy(m => m.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<MenuItem>> SearchMenuItemsAsync(string query)
    {
        var lower = query.ToLowerInvariant();
        return await _db.MenuItems
            .Include(m => m.TaxRate)
            .Include(m => m.Category)
            .Where(m => m.IsActive && m.Name.ToLower().Contains(lower))
            .OrderBy(m => m.DisplayOrder)
            .Take(20)
            .ToListAsync();
    }

    public async Task<MenuItem?> GetMenuItemByIdAsync(int id)
    {
        return await _db.MenuItems
            .Include(m => m.TaxRate)
            .Include(m => m.Variants)
            .Include(m => m.ModifierGroups)
                .ThenInclude(mg => mg.ModifierGroup)
                    .ThenInclude(g => g.Modifiers)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MenuItem?> GetMenuItemByBarcodeAsync(string barcode)
    {
        return await _db.MenuItems
            .Include(m => m.TaxRate)
            .FirstOrDefaultAsync(m => m.Barcode == barcode && m.IsActive);
    }
}
