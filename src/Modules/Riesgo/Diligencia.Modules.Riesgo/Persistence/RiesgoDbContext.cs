using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Riesgo.Persistence;

internal sealed class RiesgoDbContext(DbContextOptions<RiesgoDbContext> options) : DbContext(options)
{
    public const string Schema = "riesgo";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
