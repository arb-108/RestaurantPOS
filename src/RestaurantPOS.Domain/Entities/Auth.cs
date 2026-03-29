namespace RestaurantPOS.Domain.Entities;

public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}

public class Permission : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? Description { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

public class RolePermission
{
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
    /// <summary>
    /// Access level 0-5. 0 = no access, 5 = full access.
    /// Higher levels can override lower-level operations.
    /// </summary>
    public int AccessLevel { get; set; } = 5;
}

public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Pin { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public ICollection<UserSession> Sessions { get; set; } = [];
}

public class UserSession : BaseEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public DateTime LoginAt { get; set; } = DateTime.UtcNow;
    public DateTime? LogoutAt { get; set; }
    public string? IpAddress { get; set; }
}

public class AuditLog : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public int? RecordId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
}
