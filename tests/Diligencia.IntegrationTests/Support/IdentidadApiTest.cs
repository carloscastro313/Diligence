using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Diligencia.IntegrationTests.Support;

/// <summary>Base de los tests de Identidad: API real contra el SQL Server de la fixture, reloj y correo falsos.</summary>
public abstract class IdentidadApiTest(SqlServerFixture sql) : IAsyncLifetime
{
    protected SqlServerFixture Sql => sql;

    // Los tests que necesitan una base propia (p. ej. contar administradores) cambian estas antes de base.InitializeAsync().
    protected string RuntimeConnection { get; set; } = sql.RuntimeConnectionString;
    protected string MigratorConnection { get; set; } = sql.MigratorConnectionString;
    protected DbApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected FakeTimeProvider Clock => Factory.Clock;
    protected FakeEmailSender Emails => Factory.Emails;

    protected virtual DbApiFactory CreateFactory() => new(RuntimeConnection);

    public virtual ValueTask InitializeAsync()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return ValueTask.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected Task<TestUser> UserAsync(params string[] roles) =>
        TestUsers.CreateAsync(RuntimeConnection, Clock, roles);

    protected Task<TestUser> UserWithPasswordAsync(params string[] roles) =>
        TestUsers.CreateAsync(RuntimeConnection, Clock, roles, password: TestUsers.DefaultPassword);

    protected Task<HttpResponseMessage> Send(HttpMethod method, string path, TestUser? as_ = null, object? body = null) =>
        SendRaw(method, path, as_?.Token, body);

    protected Task<HttpResponseMessage> SendRaw(HttpMethod method, string path, string? token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return Client.SendAsync(req, Ct);
    }

    protected Task<HttpResponseMessage> Get(string path, TestUser? as_ = null) => Send(HttpMethod.Get, path, as_);
    protected Task<HttpResponseMessage> Post(string path, TestUser? as_ = null, object? body = null) => Send(HttpMethod.Post, path, as_, body);
    protected Task<TestUser> CustomUserAsync(string[] roles, bool active = true, string? password = null, Guid? areaId = null) =>
        TestUsers.CreateAsync(RuntimeConnection, Clock, roles, password: password, active: active, areaId: areaId);

    /// <summary>Crea un área por la API (como administrador) y devuelve su id.</summary>
    protected async Task<Guid> NewAreaAsync(TestUser admin, bool active = true)
    {
        using var created = await Post("/api/backoffice/areas", admin, new { name = "Area " + Guid.NewGuid().ToString("N") });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();
        if (!active) (await Post($"/api/backoffice/areas/{id}/deactivate", admin)).Dispose();
        return id;
    }

    protected Task<HttpResponseMessage> Put(string path, TestUser? as_, object body) => Send(HttpMethod.Put, path, as_, body);

    protected Task<HttpResponseMessage> Login(string email, string password) =>
        Post("/api/backoffice/auth/login", body: new { email, password });

    protected static async Task<JsonElement> Json(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>(Ct)).Clone();

    /// <summary>Eventos de bitácora de una entidad, en orden. Lee con el login de migraciones (sin permisos de la API).</summary>
    protected async Task<List<JsonElement>> AuditAsync(string entityType, string? entityId = null, string? action = null)
    {
        var rows = new List<JsonElement>();
        await using var c = new Microsoft.Data.SqlClient.SqlConnection(MigratorConnection);
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Action, ActorId, EntityType, EntityId, OldValues, NewValues FROM auditoria.AuditEvents
            WHERE EntityType = @t AND (@id IS NULL OR EntityId = @id) AND (@a IS NULL OR Action = @a) ORDER BY Id
            """;
        cmd.Parameters.AddWithValue("@t", entityType);
        cmd.Parameters.AddWithValue("@id", (object?)entityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)action ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        while (await r.ReadAsync(Ct))
            rows.Add(JsonSerializer.SerializeToElement(new
            {
                action = r.GetString(0), actorId = r.GetString(1), entityType = r.GetString(2), entityId = r.GetString(3),
                oldValues = r.IsDBNull(4) ? null : r.GetString(4), newValues = r.IsDBNull(5) ? null : r.GetString(5),
            }));
        return rows;
    }
}
