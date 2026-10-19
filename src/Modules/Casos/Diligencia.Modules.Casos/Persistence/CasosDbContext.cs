using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Persistence;

internal sealed class CasosDbContext(DbContextOptions<CasosDbContext> options) : DbContext(options)
{
    public const string Schema = "casos";

    public DbSet<Caso> Casos => Set<Caso>();
    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();
    public DbSet<EnlacePortal> EnlacesPortal => Set<EnlacePortal>();
    public DbSet<CodigoIngreso> CodigosIngreso => Set<CodigoIngreso>();
    public DbSet<FormularioTercero> FormulariosTercero => Set<FormularioTercero>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CasosDbContext).Assembly,
            t => t.Namespace == typeof(CasosDbContext).Namespace);
    }
}
