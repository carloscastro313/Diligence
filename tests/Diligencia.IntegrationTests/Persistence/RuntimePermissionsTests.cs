using Diligencia.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Diligencia.IntegrationTests.Persistence;

public class RuntimePermissionsTests(SqlServerFixture sql)
{
    private async Task<SqlException> Denied(string statement)
    {
        await using var c = new SqlConnection(sql.RuntimeConnectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = statement;
        return await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Runtime_CannotRunDdl()
    {
        await Denied("CREATE TABLE casos.Intruso (Id int)");
        await Denied("DROP TABLE casos.__EFMigrationsHistory");
        await Denied("TRUNCATE TABLE casos.__EFMigrationsHistory");
        await Denied("ALTER TABLE casos.__EFMigrationsHistory ADD X int");
    }

    [Theory]
    [InlineData("terceros")]
    [InlineData("casos")]
    [InlineData("riesgo")]
    [InlineData("aprobaciones")]
    [InlineData("documentos")]
    [InlineData("auditoria")]
    [InlineData("identidad")]
    public async Task Runtime_CannotModifyMigrationHistory(string schema)
    {
        Assert.Equal(229, (await Denied($"UPDATE [{schema}].[__EFMigrationsHistory] SET ProductVersion = 'x'")).Number);
        Assert.Equal(229, (await Denied($"DELETE FROM [{schema}].[__EFMigrationsHistory]")).Number);
    }

    [Fact]
    public async Task Runtime_CanUseTablesInModuleSchemas()
    {
        await SqlServerFixture.ExecuteAsync(sql.MigratorConnectionString,
            "IF OBJECT_ID('casos.PermProbe') IS NULL CREATE TABLE casos.PermProbe (Id int, Name nvarchar(20))");

        await SqlServerFixture.ExecuteAsync(sql.RuntimeConnectionString, "INSERT INTO casos.PermProbe VALUES (1, 'a')");
        Assert.True(await SqlServerFixture.ScalarAsync<int>(sql.RuntimeConnectionString, "SELECT COUNT(*) FROM casos.PermProbe") >= 1);
        await SqlServerFixture.ExecuteAsync(sql.RuntimeConnectionString, "UPDATE casos.PermProbe SET Name = 'b' WHERE Id = 1");
        await SqlServerFixture.ExecuteAsync(sql.RuntimeConnectionString, "DELETE FROM casos.PermProbe WHERE Id = 1");
    }

    [Fact]
    public async Task Runtime_CanUseHangfireSchema()
    {
        await SqlServerFixture.ExecuteAsync(sql.RuntimeConnectionString,
            "INSERT INTO hangfire.[Counter]([Key],[Value]) VALUES ('probe', 1); DELETE FROM hangfire.[Counter] WHERE [Key] = 'probe';");
    }
}
