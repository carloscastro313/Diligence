using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Auditoria.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Auditoria;

// SC-006: la bitácora no se puede modificar ni borrar, ni por la base ni por la aplicación.
public class ImmutabilityTests(SqlServerFixture sql)
{
    private async Task<string> SeedEvent()
    {
        var entityId = Guid.NewGuid().ToString("N");
        await using var sp = TestModule.Services(sql);
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>()
            .RecordStandaloneAsync(new AuditEntry(AuditAction.Created, "Probe", entityId), TestContext.Current.CancellationToken);
        return entityId;
    }

    private async Task<SqlException> RuntimeFails(string statement)
    {
        await using var c = new SqlConnection(sql.RuntimeConnectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = statement;
        return await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Runtime_CanInsertAndSelect_ButNotUpdateDeleteOrTruncate()
    {
        var entityId = await SeedEvent();
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(sql.RuntimeConnectionString,
            $"SELECT COUNT(*) FROM auditoria.AuditEvents WHERE EntityId = '{entityId}'"));

        Assert.Equal(229, (await RuntimeFails("UPDATE auditoria.AuditEvents SET ActorId = 'x'")).Number);
        Assert.Equal(229, (await RuntimeFails("DELETE FROM auditoria.AuditEvents")).Number);
        await RuntimeFails("TRUNCATE TABLE auditoria.AuditEvents");

        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(sql.MigratorConnectionString,
            $"SELECT COUNT(*) FROM auditoria.AuditEvents WHERE EntityId = '{entityId}' AND ActorId <> 'x'"));
    }

    [Fact]
    public async Task SaveChanges_RejectsModifiedOrDeletedEvents()
    {
        var entityId = await SeedEvent();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(sql.RuntimeConnectionString).Options;

        await using (var db = new AuditDbContext(options))
        {
            var e = await db.Events.FirstAsync(x => x.EntityId == entityId, TestContext.Current.CancellationToken);
            e.ActorId = "otro";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var db = new AuditDbContext(options))
        {
            var e = await db.Events.FirstAsync(x => x.EntityId == entityId, TestContext.Current.CancellationToken);
            db.Events.Remove(e);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }
    }
}
