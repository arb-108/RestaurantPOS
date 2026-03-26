using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.Application.Interfaces;

public interface IMenuService
{
    Task<IEnumerable<Category>> GetCategoriesAsync();
    Task<IEnumerable<MenuItem>> GetMenuItemsByCategoryAsync(int categoryId);
    Task<IEnumerable<MenuItem>> GetAllActiveMenuItemsAsync();
    Task<IEnumerable<MenuItem>> SearchMenuItemsAsync(string query);
    Task<MenuItem?> GetMenuItemByIdAsync(int id);
    Task<MenuItem?> GetMenuItemByBarcodeAsync(string barcode);
}
