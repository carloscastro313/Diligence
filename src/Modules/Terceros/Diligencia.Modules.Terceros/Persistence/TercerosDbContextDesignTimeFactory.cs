using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Terceros.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class TercerosDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TercerosDbContext>
{
    public TercerosDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<TercerosDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", TercerosDbContext.Schema))
            .Options);
}
