using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Identidad.Persistence;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Authorization;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Identidad;

public static class IdentidadModule
{
    // No exige configuración JWT: worker y MigrationService arrancan sin ella.
    public static IServiceCollection AddIdentidadModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentidadDbContext>(configuration, IdentidadDbContext.Schema);
        services.AddHttpContextAccessor();
        services.AddScoped<IActorContext, HttpActorContext>();
        return services;
    }

    // Solo la API.
    public static IServiceCollection AddIdentidadAuthentication(this IServiceCollection services, IConfiguration configuration) =>
        services.AddBearerSchemes(configuration).AddIdentityPolicies();

    public static ApiSurfaces MapIdentidadEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
