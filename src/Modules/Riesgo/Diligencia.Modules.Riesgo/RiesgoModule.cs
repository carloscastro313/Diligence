using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Riesgo.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Riesgo;

public static class RiesgoModule
{
    public static IServiceCollection AddRiesgoModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<RiesgoDbContext>(configuration, RiesgoDbContext.Schema);
        return services;
    }

    public static ApiSurfaces MapRiesgoEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
