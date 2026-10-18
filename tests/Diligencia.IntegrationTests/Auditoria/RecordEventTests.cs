using System.Text.Json;
using Diligencia.BuildingBlocks.Correlation;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Auditoria;

public class RecordEventTests(SqlServerFixture sql) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await TestModule.EnsureSchemaAsync(sql);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Row(string ActorId, string Surface, string Action, string EntityType, string EntityId,
        string? OldValues, string? NewValues, string CorrelationId, DateTime OccurredAtUtc);

    private async Task<List<Row>> EventsFor(string entityId)
    {
        await using var c = new SqlConnection(sql.MigratorConnectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT ActorId, Surface, Action, EntityType, EntityId, OldValues, NewValues, CorrelationId, OccurredAtUtc
            FROM auditoria.AuditEvents WHERE EntityId = @id ORDER BY Id
            """;
        cmd.Parameters.AddWithValue("@id", entityId);
        var rows = new List<Row>();
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
            rows.Add(new Row(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.GetDateTime(8)));
        return rows;
    }

    private async Task<int> ThingsNamed(string name) =>
        await SqlServerFixture.ScalarAsync<int>(sql.MigratorConnectionString,
            $"SELECT COUNT(*) FROM testmodule.Things WHERE Name = '{name}'");

    [Fact]
    public async Task RecordAsync_InCallersTransaction_StoresCompleteEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = TestModule.Services(sql);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var entityId = Guid.NewGuid().ToString("N");
        var name = "n" + entityId;
        CorrelationContext.Set("corr-" + entityId[..8]);

        await db.ExecuteInTransactionAsync(async (tx, token) =>
        {
            db.Things.Add(new Thing { Name = name });
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Thing", entityId,
            [
                new AuditChange("Name", null, name),
                new AuditChange("ApiToken", "old", "new"),
                new AuditChange("Same", "x", "x"),
            ]), tx, token);
        }, ct);

        Assert.Equal(1, await ThingsNamed(name));
        var e = Assert.Single(await EventsFor(entityId));
        Assert.Equal("system:Diligencia.Tests", e.ActorId);
        Assert.Equal("System", e.Surface);
        Assert.Equal("Created", e.Action);
        Assert.Equal("Thing", e.EntityType);
        Assert.Equal("corr-" + entityId[..8], e.CorrelationId);
        Assert.InRange(e.OccurredAtUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));

        var oldValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(e.OldValues!)!;
        var newValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(e.NewValues!)!;
        Assert.Equal(["ApiToken", "Name"], newValues.Keys.Order());
        Assert.Null(oldValues["Name"]);
        Assert.Equal(name, newValues["Name"]);
        Assert.Equal("***", oldValues["ApiToken"]);
        Assert.Equal("***", newValues["ApiToken"]);
    }

    [Fact]
    public async Task ActionRolledBack_LeavesNoEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = TestModule.Services(sql);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var entityId = Guid.NewGuid().ToString("N");
        var name = "n" + entityId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteInTransactionAsync(async (tx, token) =>
        {
            db.Things.Add(new Thing { Name = name });
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Thing", entityId), tx, token);
            throw new InvalidOperationException("falla de negocio posterior");
        }, ct));

        Assert.Equal(0, await ThingsNamed(name));
        Assert.Empty(await EventsFor(entityId));
    }

    [Fact]
    public async Task InvalidEntry_FailsAndTheActionIsNotCommitted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = TestModule.Services(sql);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var name = "n" + Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<ArgumentException>(() => db.ExecuteInTransactionAsync(async (tx, token) =>
        {
            db.Things.Add(new Thing { Name = name });
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Thing", ""), tx, token);
        }, ct));

        Assert.Equal(0, await ThingsNamed(name));
    }

    [Fact]
    public async Task DatabaseFailureWhileRecording_RollsBackTheAction()
    {
        var ct = TestContext.Current.CancellationToken;
        // ActorId de 300 caracteres: la base rechaza el INSERT (columna de 200).
        await using var sp = TestModule.Services(sql, new FakeActor(new string('a', 300), ActorSurface.Backoffice));
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var name = "n" + Guid.NewGuid().ToString("N");

        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteInTransactionAsync(async (tx, token) =>
        {
            db.Things.Add(new Thing { Name = name });
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Thing", name), tx, token);
        }, ct));

        Assert.Equal(0, await ThingsNamed(name));
    }

    [Fact]
    public async Task NullTransaction_Throws()
    {
        await using var sp = TestModule.Services(sql);
        await using var scope = sp.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            audit.RecordAsync(new AuditEntry(AuditAction.Created, "Thing", "1"), null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UserActor_IsRecordedWithItsSurface()
    {
        await using var sp = TestModule.Services(sql, new FakeActor("ana", ActorSurface.Backoffice));
        await using var scope = sp.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var entityId = Guid.NewGuid().ToString("N");

        await audit.RecordStandaloneAsync(new AuditEntry(AuditAction.Downloaded, "Documento", entityId), TestContext.Current.CancellationToken);

        var e = Assert.Single(await EventsFor(entityId));
        Assert.Equal(("ana", "Backoffice", "Downloaded"), (e.ActorId, e.Surface, e.Action));
    }
}
