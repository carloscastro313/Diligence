using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Terceros.Persistence;

internal sealed class TercerosDbContext(DbContextOptions<TercerosDbContext> options) : DbContext(options)
{
    public const string Schema = "terceros";

    public DbSet<Tercero> Terceros => Set<Tercero>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TercerosDbContext).Assembly,
            t => t.Namespace == typeof(TercerosDbContext).Namespace);
    }
}
