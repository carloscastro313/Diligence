using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Persistence;

internal sealed class CasosDbContext(DbContextOptions<CasosDbContext> options) : DbContext(options)
{
    public const string Schema = "casos";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
