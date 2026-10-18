using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Identidad.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class IdentidadDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentidadDbContext>
{
    public IdentidadDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IdentidadDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", IdentidadDbContext.Schema))
            .Options);
}
