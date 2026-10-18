using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Documentos.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class DocumentosDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DocumentosDbContext>
{
    public DocumentosDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<DocumentosDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", DocumentosDbContext.Schema))
            .Options);
}
