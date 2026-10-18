using Diligencia.BuildingBlocks.Configuration;
using Diligencia.BuildingBlocks.Correlation;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.BuildingBlocks.Persistence;

public static class HangfireSetup
{
    public const string Schema = "hangfire";

    /// <summary>
    /// Cliente Hangfire sobre SQL Server (schema <c>hangfire</c>, instalado por el MigrationService).
    /// El worker agrega además <c>AddHangfireServer</c>.
    /// </summary>
    public static IServiceCollection AddDiligenciaHangfire(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetRequired("ConnectionStrings:diligencia");
        services.AddSingleton<CorrelationJobFilter>();
        services.AddHangfire((sp, config) => config
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = Schema,
                PrepareSchemaIfNecessary = false,
                QueuePollInterval = TimeSpan.FromSeconds(configuration.GetValue("Hangfire:QueuePollSeconds", 5)),
            })
            .UseFilter(sp.GetRequiredService<CorrelationJobFilter>()));
        return services;
    }
}
