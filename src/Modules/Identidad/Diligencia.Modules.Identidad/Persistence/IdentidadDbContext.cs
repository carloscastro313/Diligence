using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Diligencia.Modules.Identidad.Persistence;

internal sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> options) : DbContext(options)
{
    public const string Schema = "identidad";

    public DbSet<Area> Areas => Set<Area>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<PortalSession> PortalSessions => Set<PortalSession>();
    public DbSet<PasswordToken> PasswordTokens => Set<PasswordToken>();

    // Las fechas se guardan en UTC: devolverlas con Kind=Utc para que se serialicen con "Z".
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentidadDbContext).Assembly,
            t => t.Namespace == typeof(IdentidadDbContext).Namespace);
    }
}
