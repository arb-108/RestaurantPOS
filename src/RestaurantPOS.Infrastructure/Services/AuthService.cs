using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Domain.Entities;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly PosDbContext _db;

    /// <summary>In-memory cache: permission name → access level (0-5).</summary>
    private Dictionary<string, int> _permissionCache = new(StringComparer.OrdinalIgnoreCase);

    public AuthService(PosDbContext db) => _db = db;

    public async Task<User?> LoginAsync(string username, string password)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null) return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<User?> LoginWithPinAsync(string pin)
    {
        var pinHash = HashPin(pin);
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Pin == pinHash && u.IsActive);

        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return user;
    }

    public async Task<bool> ValidatePinAsync(int userId, string pin)
    {
        var pinHash = HashPin(pin);
        return await _db.Users.AnyAsync(u => u.Id == userId && u.Pin == pinHash);
    }

    public async Task LogoutAsync(int sessionId)
    {
        var session = await _db.UserSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.LogoutAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await _db.Users
            .Include(u => u.Role)
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    public async Task<bool> HasPermissionAsync(int userId, string permissionName)
    {
        return await _db.Users
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Role.RolePermissions)
            .AnyAsync(rp => rp.Permission.Name == permissionName);
    }

    // ═══════════════════════════════════════════════════════
    //  SYNCHRONOUS PERMISSION API (cached after login)
    // ═══════════════════════════════════════════════════════

    public async Task LoadPermissionsForUserAsync(User user)
    {
        var perms = await _db.RolePermissions
            .Where(rp => rp.RoleId == user.RoleId)
            .Include(rp => rp.Permission)
            .ToListAsync();

        _permissionCache = perms.ToDictionary(
            rp => rp.Permission.Name,
            rp => rp.AccessLevel,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool HasPermission(string permissionName, int minimumLevel = 1)
    {
        return _permissionCache.TryGetValue(permissionName, out var level) && level >= minimumLevel;
    }

    public int GetAccessLevel(string permissionName)
    {
        return _permissionCache.GetValueOrDefault(permissionName, 0);
    }

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexStringLower(bytes);
    }
}
