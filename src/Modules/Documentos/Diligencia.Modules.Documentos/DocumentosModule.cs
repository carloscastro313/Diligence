using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Documentos.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Documentos;

public static class DocumentosModule
{
    public static IServiceCollection AddDocumentosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DocumentosDbContext>(configuration, DocumentosDbContext.Schema);
        return services;
    }

    public static ApiSurfaces MapDocumentosEndpoints(this ApiSurfaces surfaces)
    {
        return surfaces;
    }
}
