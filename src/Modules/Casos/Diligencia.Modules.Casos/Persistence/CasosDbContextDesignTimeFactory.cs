using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Casos.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class CasosDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CasosDbContext>
{
    public CasosDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CasosDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", CasosDbContext.Schema))
            .Options);
}
