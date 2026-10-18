using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Aprobaciones.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class AprobacionesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AprobacionesDbContext>
{
    public AprobacionesDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AprobacionesDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", AprobacionesDbContext.Schema))
            .Options);
}
