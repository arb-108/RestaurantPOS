using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Domain.Enums;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class TableService : ITableService
{
    private readonly PosDbContext _db;

    public TableService(PosDbContext db) => _db = db;

    public async Task<IEnumerable<Table>> GetTablesAsync(int? floorPlanId = null)
    {
        var query = _db.Tables.Include(t => t.FloorPlan).Where(t => t.IsActive);
        if (floorPlanId.HasValue)
            query = query.Where(t => t.FloorPlanId == floorPlanId.Value);

        return await query.OrderBy(t => t.DisplayOrder).ToListAsync();
    }

    public async Task<Table?> GetTableByIdAsync(int tableId)
    {
        return await _db.Tables.FindAsync(tableId);
    }

    public async Task<TableSession> OpenTableSessionAsync(int tableId, int? waiterId, int guestCount = 1)
    {
        var table = await _db.Tables.FindAsync(tableId)
            ?? throw new InvalidOperationException("Table not found");

        table.Status = TableStatus.Occupied;

        var session = new TableSession
        {
            TableId = tableId,
            WaiterId = waiterId,
            GuestCount = guestCount,
            OpenedAt = DateTime.UtcNow,
            Status = TableStatus.Occupied
        };

        _db.TableSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task CloseTableSessionAsync(int sessionId)
    {
        var session = await _db.TableSessions
            .Include(ts => ts.Table)
            .FirstOrDefaultAsync(ts => ts.Id == sessionId)
            ?? throw new InvalidOperationException("Session not found");

        session.ClosedAt = DateTime.UtcNow;
        session.Status = TableStatus.Available;
        session.Table.Status = TableStatus.Available;

        await _db.SaveChangesAsync();
    }

    public async Task UpdateTableStatusAsync(int tableId, TableStatus status)
    {
        var table = await _db.Tables.FindAsync(tableId)
            ?? throw new InvalidOperationException("Table not found");

        table.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task<TableSession?> GetActiveSessionForTableAsync(int tableId)
    {
        return await _db.TableSessions
            .Include(ts => ts.Orders)
                .ThenInclude(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
            .FirstOrDefaultAsync(ts => ts.TableId == tableId && ts.ClosedAt == null);
    }
}
