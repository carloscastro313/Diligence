using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Auditoria.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.IntegrationTests.Auditoria;

public class QueryEventsTests(SqlServerFixture sql) : IAsyncLifetime
{
    private DbApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new DbApiFactory(sql.RuntimeConnectionString);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string Token(string role, string sub = "ana") =>
        TestTokens.Create(ActorSurface.Backoffice, sub, role);

    private async Task<HttpResponseMessage> Query(string query, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/backoffice/audit/events" + query);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> QueryOk(string query, string? token = null)
    {
        using var res = await Query(query, token ?? Token(BackofficeRoles.Auditor));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).Clone();
    }

    private async Task Seed(string entityType, string entityId, string actor, DateTime at)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(sql.RuntimeConnectionString).Options;
        await using var db = new AuditDbContext(options);
        db.Events.Add(new AuditEvent
        {
            OccurredAtUtc = at, ActorId = actor, Surface = "Backoffice", Action = "Updated",
            EntityType = entityType, EntityId = entityId, CorrelationId = "seed",
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("entityId").GetString()!).ToArray();

    [Fact]
    public async Task Filters_ByEntityActorAndRange_InChronologicalOrder()
    {
        var type = "T" + Guid.NewGuid().ToString("N");
        var actor = "u" + Guid.NewGuid().ToString("N");
        var t0 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Seed(type, "b", "other", t0.AddHours(2));
        await Seed(type, "a", actor, t0.AddHours(1));
        await Seed(type, "a", actor, t0.AddHours(3));
        await Seed("Otro", "a", actor, t0.AddHours(1));

        Assert.Equal(["a", "b", "a"], Ids(await QueryOk($"?entityType={type}")));
        Assert.Equal(["a", "a"], Ids(await QueryOk($"?entityType={type}&entityId=a")));
        Assert.Equal(["a", "a", "a"], Ids(await QueryOk($"?actorId={actor}")));
        // fromUtc inclusivo, toUtc exclusivo.
        Assert.Equal(["b"], Ids(await QueryOk($"?entityType={type}&fromUtc=2020-01-01T02:00:00Z&toUtc=2020-01-01T03:00:00Z")));
    }

    [Fact]
    public async Task Pagination_ReportsHasNextPage()
    {
        var type = "T" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < 5; i++) await Seed(type, i.ToString(), "u", new DateTime(2021, 1, 1, i, 0, 0, DateTimeKind.Utc));

        var p1 = await QueryOk($"?entityType={type}&pageSize=2&page=1");
        Assert.Equal(["0", "1"], Ids(p1));
        Assert.True(p1.GetProperty("hasNextPage").GetBoolean());

        var p3 = await QueryOk($"?entityType={type}&pageSize=2&page=3");
        Assert.Equal(["4"], Ids(p3));
        Assert.False(p3.GetProperty("hasNextPage").GetBoolean());

        // Última página exacta: no hay página siguiente.
        var exact = await QueryOk($"?entityType={type}&pageSize=5");
        Assert.False(exact.GetProperty("hasNextPage").GetBoolean());
    }

    [Theory]
    [InlineData("?entityId=1")]
    [InlineData("?fromUtc=2020-01-02T00:00:00Z&toUtc=2020-01-01T00:00:00Z")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task InvalidParameters_Return400(string query)
    {
        using var res = await Query(query, Token(BackofficeRoles.Auditor));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Authorization_RolesAndSchemes()
    {
        using (var r = await Query("", Token(BackofficeRoles.Administrador))) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Query("", Token(BackofficeRoles.Analista))) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var r = await Query("", TestTokens.Create(ActorSurface.Portal, "t"))) Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        using (var r = await Query("")) Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task EveryQuery_IsItselfAudited_AsSensitiveDataViewed()
    {
        var actor = "auditor-" + Guid.NewGuid().ToString("N");
        var type = "T" + Guid.NewGuid().ToString("N");

        await QueryOk($"?entityType={type}", Token(BackofficeRoles.Auditor, actor));
        await QueryOk($"?entityType={type}&pageSize=10", Token(BackofficeRoles.Auditor, actor));

        var audit = await QueryOk($"?entityType=AuditLog&actorId={actor}", Token(BackofficeRoles.Auditor, actor));
        var items = audit.GetProperty("items").EnumerateArray().ToList();
        // Las dos consultas anteriores más la que acaba de hacerse.
        Assert.Equal(3, items.Count);
        Assert.All(items, e =>
        {
            Assert.Equal("SensitiveDataViewed", e.GetProperty("action").GetString());
            Assert.Equal("query", e.GetProperty("entityId").GetString());
            Assert.Equal("Backoffice", e.GetProperty("surface").GetString());
            Assert.False(string.IsNullOrEmpty(e.GetProperty("correlationId").GetString()));
        });
        Assert.Equal(type, items[0].GetProperty("newValues").GetProperty("entityType").GetString());
        Assert.Equal("10", items[1].GetProperty("newValues").GetProperty("pageSize").GetString());
    }
}
