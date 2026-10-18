using Diligencia.MigrationService;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Diligencia.IntegrationTests.Support.SqlServerFixture))]

namespace Diligencia.IntegrationTests.Support;

/// <summary>Un SQL Server por ejecución, migrado con el mismo código del MigrationService (R18).</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Database = "diligencia";
    public const string MigratorPassword = "Migrator-Pass-123!";
    public const string RuntimePassword = "Runtime-Pass-123!";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string AdminConnectionString { get; private set; } = "";
    public string AdminPassword => new SqlConnectionStringBuilder(AdminConnectionString).Password;
    public string MigratorConnectionString { get; private set; } = "";
    public string RuntimeConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        AdminConnectionString = _container.GetConnectionString();
        MigratorConnectionString = For("diligencia_migrator", MigratorPassword, Database);
        RuntimeConnectionString = For("diligencia_app", RuntimePassword, Database);

        await MigrateAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings:diligencia"] = MigratorConnectionString,
            ["ConnectionStrings:diligencia-runtime"] = RuntimeConnectionString,
            ["ConnectionStrings:diligencia-admin"] = AdminConnectionString,
        });
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public string For(string user, string password, string database) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            UserID = user, Password = password, InitialCatalog = database, TrustServerCertificate = true,
        }.ConnectionString;

    public sealed record IsolatedDatabase(string Runtime, string Migrator, Dictionary<string, string?> Settings);

    /// <summary>Una base nueva y migrada, para los tests que no pueden compartir datos con el resto.</summary>
    public async Task<IsolatedDatabase> CreateDatabaseAsync(Dictionary<string, string?>? extraSettings = null)
    {
        var name = "iso_" + Guid.NewGuid().ToString("N")[..12];
        var migrator = For("diligencia_migrator", MigratorPassword, name);
        var runtime = For("diligencia_app", RuntimePassword, name);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:diligencia"] = migrator,
            ["ConnectionStrings:diligencia-runtime"] = runtime,
            ["ConnectionStrings:diligencia-admin"] = AdminConnectionString,
        };
        foreach (var (k, v) in extraSettings ?? []) settings[k] = v;
        await MigrateAsync(settings);
        return new IsolatedDatabase(runtime, migrator, settings);
    }

    public static Task MigrateAsync(Dictionary<string, string?> settings) =>
        MigrationEntryPoint.RunAsync([], settings);

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var c = new SqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var c = new SqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
