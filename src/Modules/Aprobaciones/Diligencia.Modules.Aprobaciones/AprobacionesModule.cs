using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Aprobaciones.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Aprobaciones;

public static class AprobacionesModule
{
    public static IServiceCollection AddAprobacionesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AprobacionesDbContext>(configuration, AprobacionesDbContext.Schema);
        return services;
    }

    public static ApiSurfaces MapAprobacionesEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
