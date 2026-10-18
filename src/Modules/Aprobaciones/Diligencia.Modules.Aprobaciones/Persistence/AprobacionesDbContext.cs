using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Aprobaciones.Persistence;

internal sealed class AprobacionesDbContext(DbContextOptions<AprobacionesDbContext> options) : DbContext(options)
{
    public const string Schema = "aprobaciones";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
