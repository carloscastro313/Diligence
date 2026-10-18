using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Diligencia.Modules.Auditoria.Persistence;

// Solo para `dotnet ef`: no se conecta a ninguna base.
internal sealed class AuditDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer("Server=.;Database=design", o => o.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema))
            .Options);
}
