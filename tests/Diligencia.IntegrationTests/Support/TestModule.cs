using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria;
using Diligencia.Modules.Identidad;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Diligencia.IntegrationTests.Support;

// Módulo ficticio con su propio schema para probar la auditoría atómica desde "otro módulo".
public sealed class Thing : ITimestamped
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class TestModuleDbContext(DbContextOptions<TestModuleDbContext> options) : DbContext(options)
{
    public const string Schema = "testmodule";
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Thing>().ToTable("Things");
    }
}

public sealed class FakeActor(string actorId, ActorSurface surface) : IActorContext
{
    public string ActorId => actorId;
    public ActorSurface Surface => surface;
}

public static class TestModule
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _created;

    // Lo que haría la migración del módulo: schema, tabla y permisos del rol de runtime.
    public static async Task EnsureSchemaAsync(SqlServerFixture sql)
    {
        await Gate.WaitAsync();
        try
        {
            if (_created) return;
            await SqlServerFixture.ExecuteAsync(sql.MigratorConnectionString, $"""
                IF SCHEMA_ID('{TestModuleDbContext.Schema}') IS NULL EXEC('CREATE SCHEMA {TestModuleDbContext.Schema}');
                IF OBJECT_ID('{TestModuleDbContext.Schema}.Things') IS NULL
                    CREATE TABLE {TestModuleDbContext.Schema}.Things (
                        Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL,
                        CreatedAtUtc datetime2 NOT NULL, UpdatedAtUtc datetime2 NOT NULL);
                GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::{TestModuleDbContext.Schema} TO diligencia_runtime;
                """);
            _created = true;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Proveedor con Auditoría + Identidad reales y el módulo de prueba, usando el login de runtime.</summary>
    public static ServiceProvider Services(SqlServerFixture sql, IActorContext? actor = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:diligencia"] = sql.RuntimeConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddAuditoriaModule(config).AddIdentidadModule(config);
        services.AddModuleDbContext<TestModuleDbContext>(config, TestModuleDbContext.Schema);
        if (actor is not null) services.AddScoped<IActorContext>(_ => actor);
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Diligencia.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
