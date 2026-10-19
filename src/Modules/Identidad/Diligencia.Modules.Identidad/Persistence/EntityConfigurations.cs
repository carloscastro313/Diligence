using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Diligencia.Modules.Identidad.Persistence;

internal sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> b)
    {
        b.ToTable("Areas");
        b.HasKey(a => a.Id);
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(a => a.Name).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Name).HasMaxLength(200).IsRequired();
        b.Property(u => u.Email).HasMaxLength(254).IsRequired();
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.PasswordHash).HasColumnType("varchar(60)");
        b.Property(u => u.LockoutEndUtc).HasColumnType("datetime2(7)");
        b.Property(u => u.LastLoginAtUtc).HasColumnType("datetime2(7)");
        b.HasOne<Area>().WithMany().HasForeignKey(u => u.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    private static readonly string[] Valid =
    [
        BackofficeRoles.Solicitante, BackofficeRoles.Analista, BackofficeRoles.Aprobador,
        BackofficeRoles.Administrador, BackofficeRoles.Auditor,
    ];

    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles", t => t.HasCheckConstraint(
            "CK_UserRoles_Role", $"[Role] IN ({string.Join(", ", Valid.Select(r => $"'{r}'"))})"));
        b.HasKey(r => new { r.UserId, r.Role });
        b.Property(r => r.Role).HasColumnType("varchar(32)");
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    // shortcut: no hay limpieza de Sessions ni PasswordTokens vencidos; crecen con cada ingreso.
    // Agregar un job recurrente en el worker cuando el volumen lo justifique.
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("Sessions");
        b.HasKey(s => s.Id);
        b.HasIndex(s => s.UserId);
        b.Property(s => s.CreatedAtUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.LastActivityUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.RevokedAtUtc).HasColumnType("datetime2(7)");
        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PortalSessionConfiguration : IEntityTypeConfiguration<PortalSession>
{
    // shortcut: no hay limpieza de PortalSessions vencidas (igual que Sessions); agregar un job en el worker al crecer.
    public void Configure(EntityTypeBuilder<PortalSession> b)
    {
        b.ToTable("PortalSessions");
        b.HasKey(s => s.Id);
        b.HasIndex(s => s.CasoId);
        b.Property(s => s.CreatedAtUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.LastActivityUtc).HasColumnType("datetime2(7)");
        b.Property(s => s.RevokedAtUtc).HasColumnType("datetime2(7)");
    }
}

internal sealed class PasswordTokenConfiguration : IEntityTypeConfiguration<PasswordToken>
{
    public void Configure(EntityTypeBuilder<PasswordToken> b)
    {
        b.ToTable("PasswordTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Purpose).HasColumnType("varchar(16)").IsRequired();
        b.Property(t => t.TokenHash).HasColumnType("binary(32)").IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.UserId);
        b.Property(t => t.ExpiresAtUtc).HasColumnType("datetime2(7)");
        b.Property(t => t.UsedAtUtc).HasColumnType("datetime2(7)");
        b.Property(t => t.CreatedAtUtc).HasColumnType("datetime2(7)");
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
