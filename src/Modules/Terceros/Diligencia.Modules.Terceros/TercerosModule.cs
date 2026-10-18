using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Terceros.Contracts;
using Diligencia.Modules.Terceros.Persistence;
using Diligencia.Modules.Terceros.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Terceros;

public static class TercerosModule
{
    public static IServiceCollection AddTercerosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TercerosDbContext>(configuration, TercerosDbContext.Schema);
        services.AddScoped<ITerceroRegistry, TerceroRegistry>();
        return services;
    }

    public static ApiSurfaces MapTercerosEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
