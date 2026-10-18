using Diligencia.BuildingBlocks.Persistence;

namespace Diligencia.Modules.Identidad.Persistence;

internal sealed class Area : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class User : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid AreaId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? PasswordHash { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<UserRole> Roles { get; set; } = [];
}

internal sealed class UserRole
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
}

internal sealed class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

internal static class PasswordPurpose
{
    public const string Invitation = "Invitation";
    public const string Reset = "Reset";
}

internal sealed class PasswordToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "";
    public byte[] TokenHash { get; set; } = [];
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
