using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class LoginTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private static string SessionCookie(HttpResponseMessage res) =>
        Assert.Single(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith("dlg_bo="));

    [Fact]
    public async Task Login_ReturnsHardenedCookie_NormalizesEmail_AndMeListsAllRoles()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista, BackofficeRoles.Auditor);

        using var res = await Login($"  {user.Email.ToUpperInvariant()} ", user.Password!);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var cookie = SessionCookie(res);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/backoffice", cookie, StringComparison.OrdinalIgnoreCase);

        // La cookie sola autentica (sin header Authorization).
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/backoffice/auth/me");
        me.Headers.Add("Cookie", cookie.Split(';')[0]);
        using var meRes = await Client.SendAsync(me, Ct);
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        var body = await Json(meRes);
        Assert.Equal(user.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(user.Email, body.GetProperty("email").GetString());
        Assert.Equal([BackofficeRoles.Analista, BackofficeRoles.Auditor],
            body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("areaName").GetString()));
    }

    [Fact]
    public async Task Failures_AreIndistinguishable_AndAuditedWithoutTypedData()
    {
        var active = await UserWithPasswordAsync(BackofficeRoles.Analista);
        var disabled = await TestUsers.CreateAsync(Sql.RuntimeConnectionString, Clock, [BackofficeRoles.Analista],
            password: TestUsers.DefaultPassword, active: false);
        var unknownEmail = $"nadie{Guid.NewGuid():N}@test.local";
        const string typedPassword = "contrasena-tipeada-9917";

        var attempts = new[]
        {
            (unknownEmail, typedPassword),
            (disabled.Email, TestUsers.DefaultPassword),
            (active.Email, typedPassword),
        };
        var bodies = new List<string>();
        foreach (var (email, password) in attempts)
        {
            using var res = await Login(email, password);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.False(res.Headers.Contains("Set-Cookie"));
            var json = await Json(res);
            bodies.Add($"{json.GetProperty("title")}|{json.GetProperty("status")}|{json.GetProperty("type")}");
            Assert.Equal("Correo o contraseña incorrectos.", json.GetProperty("title").GetString());
        }
        Assert.Single(bodies.Distinct());

        foreach (var (id, reason) in new[] { (disabled.Id, "Desactivado"), (active.Id, "CredencialInvalida") })
        {
            var failed = Assert.Single(await AuditAsync("Usuario", id.ToString(), "LoginFailed"));
            Assert.Contains(reason, failed.GetProperty("newValues").GetString());
        }

        // Ni el correo tipeado ni la contraseña quedan en la bitácora.
        Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(Sql.MigratorConnectionString,
            $"SELECT COUNT(*) FROM auditoria.AuditEvents WHERE NewValues LIKE '%{unknownEmail}%' OR OldValues LIKE '%{unknownEmail}%' OR NewValues LIKE '%{typedPassword}%'"));
        Assert.True(await SqlServerFixture.ScalarAsync<int>(Sql.MigratorConnectionString,
            "SELECT COUNT(*) FROM auditoria.AuditEvents WHERE Action = 'LoginFailed' AND EntityId = 'desconocido'") >= 1);
    }

    [Fact]
    public async Task SuccessfulLogin_IsAudited_WithTheUserAsActor()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);

        using (await Login(user.Email, user.Password!)) { }

        var ev = Assert.Single(await AuditAsync("Usuario", user.Id.ToString(), "LoginSucceeded"));
        Assert.Equal(user.Id.ToString(), ev.GetProperty("actorId").GetString());
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("a@b.c", "")]
    public async Task EmptyFields_Return400(string email, string password)
    {
        using var res = await Login(email, password);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
