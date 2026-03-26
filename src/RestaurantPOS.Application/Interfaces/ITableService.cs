using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;

namespace RestaurantPOS.Application.Interfaces;

public interface ITableService
{
    Task<IEnumerable<Table>> GetTablesAsync(int? floorPlanId = null);
    Task<Table?> GetTableByIdAsync(int tableId);
    Task<TableSession> OpenTableSessionAsync(int tableId, int? waiterId, int guestCount = 1);
    Task CloseTableSessionAsync(int sessionId);
    Task UpdateTableStatusAsync(int tableId, TableStatus status);
    Task<TableSession?> GetActiveSessionForTableAsync(int tableId);
}
