using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Documentos.Persistence;

internal sealed class DocumentosDbContext(DbContextOptions<DocumentosDbContext> options) : DbContext(options)
{
    public const string Schema = "documentos";

    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<DocumentoContenido> Contenidos => Set<DocumentoContenido>();
    public DbSet<ParametrosDocumentos> Parametros => Set<ParametrosDocumentos>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentosDbContext).Assembly,
            t => t.Namespace == typeof(DocumentosDbContext).Namespace);
    }
}
