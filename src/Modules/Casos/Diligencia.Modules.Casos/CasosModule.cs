using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Casos.Features.CheckCuit;
using Diligencia.Modules.Casos.Features.CreateSolicitud;
using Diligencia.Modules.Casos.Features.ListMisSolicitudes;
using Diligencia.Modules.Casos.Features.SendPortalLink;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Casos;

public static class CasosModule
{
    public static IServiceCollection AddCasosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CasosDbContext>(configuration, CasosDbContext.Schema);
        services.AddOptions<CasosOptions>().Bind(configuration.GetSection(CasosOptions.Section));
        services.AddScoped<IPortalLinkDelivery, PortalLinkDelivery>();
        return services.AddFeatureServices();
    }

    // Validadores (singleton) y handlers (scoped) de los slices, por convención de nombre.
    private static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        foreach (var type in typeof(CasosModule).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            if (type.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                services.AddSingleton(typeof(IValidator<>).MakeGenericType(b.GetGenericArguments()[0]), type);
            else if (type.Name.EndsWith("Handler") && type.Namespace!.Contains(".Features."))
                services.AddScoped(type);
        }
        return services;
    }

    public static ApiSurfaces MapCasosEndpoints(this ApiSurfaces surfaces)
    {
        var solicitudes = surfaces.Backoffice.MapGroup("/solicitudes")
            .RequireAuthorization(p => p.RequireRole(BackofficeRoles.Solicitante));
        CreateSolicitudEndpoint.Map(solicitudes);
        CheckCuitEndpoint.Map(solicitudes);
        ListMisSolicitudesEndpoint.Map(solicitudes);
        return surfaces;
    }
}
