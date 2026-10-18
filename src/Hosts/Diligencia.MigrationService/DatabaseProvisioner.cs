using Diligencia.BuildingBlocks.Configuration;
using Diligencia.BuildingBlocks.Persistence;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Diligencia.MigrationService;

/// <summary>
/// Bootstrap (solo con <c>ConnectionStrings:diligencia-admin</c>), migraciones de los módulos y permisos (R3, FR-009, FR-019).
/// Lo usan el servicio y la fixture de integración, para probar el mismo código.
/// </summary>
internal sealed class DatabaseProvisioner(
    IConfiguration configuration,
    IEnumerable<IModuleDatabase> modules,
    ILogger<DatabaseProvisioner> logger)
{
    public const string RuntimeRole = "diligencia_runtime";
    public const string HangfireSchema = "hangfire";

    public async Task RunAsync(CancellationToken ct)
    {
        var migrator = new SqlConnectionStringBuilder(configuration.GetRequired("ConnectionStrings:diligencia"));

        if (configuration["ConnectionStrings:diligencia-admin"] is { Length: > 0 } admin)
        {
            var runtime = new SqlConnectionStringBuilder(configuration.GetRequired("ConnectionStrings:diligencia-runtime"));
            await BootstrapAsync(new SqlConnectionStringBuilder(admin), migrator, runtime, ct);
        }

        await using var connection = new SqlConnection(migrator.ConnectionString);
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, $"IF DATABASE_PRINCIPAL_ID({Str(RuntimeRole)}) IS NULL CREATE ROLE {Id(RuntimeRole)};", ct);

        foreach (var module in modules)
        {
            logger.LogInformation("Migrando el módulo {Schema}", module.Schema);
            await module.MigrateAsync(ct);
        }

        SqlServerObjectsInstaller.Install(connection, HangfireSchema);

        foreach (var schema in modules.Select(m => m.Schema).Append(HangfireSchema))
            await ExecuteAsync(connection,
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::{Id(schema)} TO {Id(RuntimeRole)};", ct);

        foreach (var schema in modules.Select(m => m.Schema))
            await ExecuteAsync(connection,
                $"DENY UPDATE, DELETE ON {Id(schema)}.[__EFMigrationsHistory] TO {Id(RuntimeRole)};", ct);

        logger.LogInformation("Base de datos lista");
    }

    private async Task BootstrapAsync(
        SqlConnectionStringBuilder admin, SqlConnectionStringBuilder migrator, SqlConnectionStringBuilder runtime, CancellationToken ct)
    {
        var database = migrator.InitialCatalog;
        logger.LogInformation("Bootstrap de la base {Database}", database);

        admin.InitialCatalog = "master";
        await using (var master = new SqlConnection(admin.ConnectionString))
        {
            await master.OpenAsync(ct);
            await ExecuteAsync(master, $"IF DB_ID({Str(database)}) IS NULL CREATE DATABASE {Id(database)};", ct);
            foreach (var login in new[] { migrator, runtime })
                await ExecuteAsync(master, $"""
                    IF SUSER_ID({Str(login.UserID)}) IS NULL
                        CREATE LOGIN {Id(login.UserID)} WITH PASSWORD = {Str(login.Password)}, CHECK_POLICY = OFF;
                    ELSE
                        ALTER LOGIN {Id(login.UserID)} WITH PASSWORD = {Str(login.Password)};
                    """, ct);
        }

        admin.InitialCatalog = database;
        await using var db = new SqlConnection(admin.ConnectionString);
        await db.OpenAsync(ct);
        await ExecuteAsync(db, $"""
            IF DATABASE_PRINCIPAL_ID({Str(migrator.UserID)}) IS NULL CREATE USER {Id(migrator.UserID)} FOR LOGIN {Id(migrator.UserID)};
            ALTER ROLE db_owner ADD MEMBER {Id(migrator.UserID)};
            IF DATABASE_PRINCIPAL_ID({Str(RuntimeRole)}) IS NULL CREATE ROLE {Id(RuntimeRole)};
            IF DATABASE_PRINCIPAL_ID({Str(runtime.UserID)}) IS NULL CREATE USER {Id(runtime.UserID)} FOR LOGIN {Id(runtime.UserID)};
            ALTER ROLE {Id(RuntimeRole)} ADD MEMBER {Id(runtime.UserID)};
            """, ct);
    }

    // DDL no admite parámetros: se escapa a mano y los nombres vienen de configuración, no de usuarios.
    private static string Id(string name) => $"[{name.Replace("]", "]]")}]";
    private static string Str(string value) => $"N'{value.Replace("'", "''")}'";

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
