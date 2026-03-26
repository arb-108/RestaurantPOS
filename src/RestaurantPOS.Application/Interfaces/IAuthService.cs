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
}
