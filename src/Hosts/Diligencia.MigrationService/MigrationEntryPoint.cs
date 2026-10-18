using Diligencia.Modules.Aprobaciones;
using Diligencia.Modules.Auditoria;
using Diligencia.Modules.Casos;
using Diligencia.Modules.Documentos;
using Diligencia.Modules.Identidad;
using Diligencia.Modules.Riesgo;
using Diligencia.Modules.Terceros;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diligencia.MigrationService;

// Sin top-level statements: evita un tipo `Program` que choque con el de la API en los tests.
internal static class MigrationEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        await RunAsync(args, []);
        return 0;
    }

    // Los tests de integración lo llaman con la configuración en memoria (mismo código que el servicio, R18).
    internal static async Task RunAsync(string[] args, IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();

        var config = builder.Configuration;
        builder.Services
            .AddTercerosModule(config)
            .AddCasosModule(config)
            .AddRiesgoModule(config)
            .AddAprobacionesModule(config)
            .AddDocumentosModule(config)
            .AddAuditoriaModule(config)
            .AddIdentidadModule(config)
            .AddSingleton<DatabaseProvisioner>();

        using var host = builder.Build();
        await host.Services.GetRequiredService<DatabaseProvisioner>().RunAsync(CancellationToken.None);
    }
}
