using Diligencia.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Diligencia.IntegrationTests.Persistence;

// Otros entornos: el DBA crea base y logins; el MigrationService corre sin `-admin` (FR-009).
public class MigrationWithoutBootstrapTests(SqlServerFixture sql)
{
    [Fact]
    public async Task WithoutAdminConnection_MigratesAllModulesAndAppliesGrants()
    {
        const string db = "dba_managed", mig = "dba_migrator", app = "dba_app", pwd = "Dba-Pass-123!";

        // Lo que haría el DBA.
        await SqlServerFixture.ExecuteAsync(sql.AdminConnectionString, $"""
            CREATE DATABASE [{db}];
            CREATE LOGIN [{mig}] WITH PASSWORD = '{pwd}', CHECK_POLICY = OFF;
            CREATE LOGIN [{app}] WITH PASSWORD = '{pwd}', CHECK_POLICY = OFF;
            """);
        await SqlServerFixture.ExecuteAsync(sql.For("sa", sql.AdminPassword, db), $"""
            CREATE USER [{mig}] FOR LOGIN [{mig}]; ALTER ROLE db_owner ADD MEMBER [{mig}];
            CREATE ROLE diligencia_runtime;
            CREATE USER [{app}] FOR LOGIN [{app}]; ALTER ROLE diligencia_runtime ADD MEMBER [{app}];
            """);

        var migrator = sql.For(mig, pwd, db);
        await SqlServerFixture.MigrateAsync(new Dictionary<string, string?> { ["ConnectionStrings:diligencia"] = migrator });

        Assert.Equal(7, await SqlServerFixture.ScalarAsync<int>(migrator,
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'"));

        var runtime = sql.For(app, pwd, db);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(runtime, "SELECT COUNT(*) FROM terceros.__EFMigrationsHistory"));
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            SqlServerFixture.ExecuteAsync(runtime, "DELETE FROM terceros.__EFMigrationsHistory"));
        Assert.Equal(229, ex.Number);
    }
}
