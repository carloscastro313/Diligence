using Diligencia.BuildingBlocks.Http;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Documentos.Features.Configuracion;
using Diligencia.Modules.Documentos.Features.Store;
using Diligencia.Modules.Documentos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Documentos;

public static class DocumentosModule
{
    // No exige Documentos:EncryptionKey: la lee DocumentCipher al cifrar, así worker y MigrationService arrancan sin ella.
    public static IServiceCollection AddDocumentosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DocumentosDbContext>(configuration, DocumentosDbContext.Schema);
        services.AddSingleton<DocumentCipher>();
        services.AddScoped<IDocumentStore, DocumentStore>();
        return services.AddFeatureServices();
    }

    // Validadores (singleton) y handlers (scoped) de los slices, por convención de nombre.
    private static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        foreach (var type in typeof(DocumentosModule).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            if (type.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                services.AddSingleton(typeof(IValidator<>).MakeGenericType(b.GetGenericArguments()[0]), type);
            else if (type.Name.EndsWith("Handler") && type.Namespace!.Contains(".Features."))
                services.AddScoped(type);
        }
        return services;
    }

    public static ApiSurfaces MapDocumentosEndpoints(this ApiSurfaces surfaces)
    {
        var documentos = surfaces.Backoffice.MapGroup("/documentos").RequireAuthorization(AuthPolicies.Administrator);
        ConfiguracionDocumentosEndpoint.Map(documentos);
        return surfaces;
    }
}
