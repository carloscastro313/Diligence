using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Persistence;

public class SchemaTests(SqlServerFixture sql)
{
    private static readonly string[] Modules = ["terceros", "casos", "riesgo", "aprobaciones", "documentos", "auditoria", "identidad"];

    private static string InList(IEnumerable<string> names) => string.Join(",", names.Select(s => $"'{s}'"));

    [Fact]
    public async Task AfterMigrating_AllSchemasAndPerModuleHistoryTablesExist()
    {
        Assert.Equal(8, await SqlServerFixture.ScalarAsync<int>(sql.MigratorConnectionString,
            $"SELECT COUNT(*) FROM sys.schemas WHERE name IN ({InList(Modules.Append("hangfire"))})"));

        // Un historial por módulo, cada uno en el schema propio.
        Assert.Equal(7, await SqlServerFixture.ScalarAsync<int>(sql.MigratorConnectionString,
            $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory' AND TABLE_SCHEMA IN ({InList(Modules)})"));
    }

    [Fact]
    public async Task EachModuleHistory_RecordsOnlyItsOwnMigrations()
    {
        foreach (var schema in Modules)
            Assert.Equal(schema is "auditoria" or "identidad" ? 2 : 1, await SqlServerFixture.ScalarAsync<int>(sql.MigratorConnectionString,
                $"SELECT COUNT(*) FROM [{schema}].[__EFMigrationsHistory]"));
    }
}
