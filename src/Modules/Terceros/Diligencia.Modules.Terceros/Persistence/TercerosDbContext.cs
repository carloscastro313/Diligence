using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Terceros.Persistence;

internal sealed class TercerosDbContext(DbContextOptions<TercerosDbContext> options) : DbContext(options)
{
    public const string Schema = "terceros";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
