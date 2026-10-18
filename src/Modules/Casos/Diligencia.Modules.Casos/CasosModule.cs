using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Casos.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Casos;

public static class CasosModule
{
    public static IServiceCollection AddCasosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CasosDbContext>(configuration, CasosDbContext.Schema);
        return services;
    }

    public static ApiSurfaces MapCasosEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
