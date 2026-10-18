using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Persistence;

internal sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> options) : DbContext(options)
{
    public const string Schema = "identidad";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
