using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Documentos.Persistence;

internal sealed class DocumentosDbContext(DbContextOptions<DocumentosDbContext> options) : DbContext(options)
{
    public const string Schema = "documentos";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
