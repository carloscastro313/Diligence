using Diligencia.BuildingBlocks.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Diligencia.BuildingBlocks.Persistence;

public interface IModuleDatabase
{
    string Schema { get; }
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

internal sealed class ModuleDatabase<TContext>(string schema, IServiceProvider services) : IModuleDatabase
    where TContext : DbContext
{
    public string Schema => schema;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}

public static class ModuleDbContextExtensions
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        var connectionString = configuration.GetRequired("ConnectionStrings:diligencia");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TimestampsInterceptor>();
        services.AddDbContext<TContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql
                .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                .EnableRetryOnFailure(6, TimeSpan.FromSeconds(30), null))
            .AddInterceptors(sp.GetRequiredService<TimestampsInterceptor>()));
        services.AddSingleton<IModuleDatabase>(sp => new ModuleDatabase<TContext>(schema, sp));
        return services;
    }
}
