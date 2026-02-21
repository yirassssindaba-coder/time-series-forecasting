using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Security;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public bool EmailVerified { get; set; }

    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }

    public bool IsLocked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<UserRole> UserRoles { get; set; } = new();
    public List<RefreshSession> Sessions { get; set; } = new();
}

public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public List<UserRole> UserRoles { get; set; } = new();
    public List<RolePermission> RolePermissions { get; set; } = new();
}

public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty; // e.g. items:read, items:write

    public List<RolePermission> RolePermissions { get; set; } = new();
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(120)]
    public string RefreshTokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    [MaxLength(200)]
    public string? UserAgent { get; set; }

    [MaxLength(80)]
    public string? Ip { get; set; }

    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
