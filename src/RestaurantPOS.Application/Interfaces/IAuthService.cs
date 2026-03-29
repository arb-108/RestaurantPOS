using RestaurantPOS.Domain.Entities;

namespace RestaurantPOS.Application.Interfaces;

public interface IAuthService
{
    Task<User?> LoginAsync(string username, string password);
    Task<User?> LoginWithPinAsync(string pin);
    Task<bool> ValidatePinAsync(int userId, string pin);
    Task LogoutAsync(int sessionId);
    Task<User?> GetUserByIdAsync(int userId);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<bool> HasPermissionAsync(int userId, string permissionName);

    /// <summary>
    /// Load all RolePermissions for the given user's role into an in-memory cache.
    /// Must be called after login before using HasPermission / GetAccessLevel.
    /// </summary>
    Task LoadPermissionsForUserAsync(User user);

    /// <summary>
    /// Synchronous permission check against the cached permissions.
    /// Returns true if the user's access level for <paramref name="permissionName"/>
    /// is >= <paramref name="minimumLevel"/>.
    /// </summary>
    bool HasPermission(string permissionName, int minimumLevel = 1);

    /// <summary>
    /// Returns the cached access level (0-5) for the given permission.
    /// 0 means no access (no row exists).
    /// </summary>
    int GetAccessLevel(string permissionName);
}
