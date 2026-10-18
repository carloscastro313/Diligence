using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Riesgo.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class RiesgoDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RiesgoDbContext>
{
    public RiesgoDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RiesgoDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", RiesgoDbContext.Schema))
            .Options);
}
