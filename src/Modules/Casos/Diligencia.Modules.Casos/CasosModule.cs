using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Casos.Features.CheckCuit;
using Diligencia.Modules.Casos.Features.CreateSolicitud;
using Diligencia.Modules.Casos.Features.EliminarDocumento;
using Diligencia.Modules.Casos.Features.EnviarFormulario;
using Diligencia.Modules.Casos.Features.GetCasoPortal;
using Diligencia.Modules.Casos.Features.GetFormulario;
using Diligencia.Modules.Casos.Features.GuardarFormulario;
using Diligencia.Modules.Casos.Features.Ingresar;
using Diligencia.Modules.Casos.Features.ListDocumentosPortal;
using Diligencia.Modules.Casos.Features.ListMisSolicitudes;
using Diligencia.Modules.Casos.Features.SendPortalLink;
using Diligencia.Modules.Casos.Features.SolicitarCodigo;
using Diligencia.Modules.Casos.Features.SubirDocumento;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Casos;

internal static class RateLimitPolicies
{
    public const string PortalAcceso = "portal-acceso";
}

public static class CasosModule
{
    public static IServiceCollection AddCasosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CasosDbContext>(configuration, CasosDbContext.Schema);
        services.AddOptions<CasosOptions>().Bind(configuration.GetSection(CasosOptions.Section));
        services.AddScoped<IPortalLinkDelivery, PortalLinkDelivery>();
        // Ventana fija de 1 minuto por IP, igual que auth-anonymous de Identidad. En TestServer la IP remota es nula.
        services.Configure<RateLimiterOptions>(o => o.AddPolicy(RateLimitPolicies.PortalAcceso, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ctx.RequestServices.GetRequiredService<IOptions<CasosOptions>>().Value.AccesoRateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })));
        return services.AddFeatureServices();
    }

    // Validadores (singleton) y handlers (scoped) de los slices, por convención de nombre.
    private static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        foreach (var type in typeof(CasosModule).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            // Los validadores con parámetros (p. ej. FormularioEnvioValidator) los instancia el handler.
            if (type.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            {
                if (type.GetConstructor(Type.EmptyTypes) is not null)
                    services.AddSingleton(typeof(IValidator<>).MakeGenericType(b.GetGenericArguments()[0]), type);
            }
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

        // Portal: el caso sale siempre de la sesión (IPortalActor), ninguna ruta recibe un id de caso (FR-006).
        var portal = surfaces.Portal.MapGroup("");
        SolicitarCodigoEndpoint.Map(portal);
        IngresarEndpoint.Map(portal);
        GetCasoPortalEndpoint.Map(portal);
        GetFormularioEndpoint.Map(portal);
        GuardarFormularioEndpoint.Map(portal);
        EnviarFormularioEndpoint.Map(portal);
        ListDocumentosPortalEndpoint.Map(portal);
        SubirDocumentoEndpoint.Map(portal);
        EliminarDocumentoEndpoint.Map(portal);
        return surfaces;
    }
}
