using System.Net;
using System.Security.Claims;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class SessionTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Me = "/api/backoffice/auth/me";

    private async Task<HttpStatusCode> MeStatus(TestUser user)
    {
        using var res = await Get(Me, user);
        return res.StatusCode;
    }

    [Fact]
    public async Task Logout_RevokesTheSession_SoTheSameTokenGets401()
    {
        var user = await UserAsync(BackofficeRoles.Analista);
        Assert.Equal(HttpStatusCode.OK, await MeStatus(user));

        using (var logout = await Post("/api/backoffice/auth/logout", user))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatus(user));
        Assert.Single(await AuditAsync("Usuario", user.Id.ToString(), "LoggedOut"));
    }

    [Fact]
    public async Task IdleSession_ExpiresAfter30Minutes_ButActivityKeepsItAlive()
    {
        var user = await UserAsync(BackofficeRoles.Analista);

        Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, await MeStatus(user)); // renueva la actividad
        Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, await MeStatus(user));
        Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatus(user));
    }

    [Fact]
    public async Task Session_ExpiresAfter8Hours_EvenWithContinuousActivity()
    {
        var user = await UserAsync(BackofficeRoles.Analista);

        for (var i = 0; i < 19; i++) // 19 x 25 min = 7 h 55 min
        {
            Clock.Advance(TimeSpan.FromMinutes(25));
            Assert.Equal(HttpStatusCode.OK, await MeStatus(user));
        }
        Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatus(user));
    }

    [Fact]
    public async Task PortalToken_InBackoffice_Returns401()
    {
        var user = await UserAsync(BackofficeRoles.Administrador);
        // Mismo sub y sid, pero firmado con la clave y el emisor del portal.
        var forged = TestUsers.Sign(ActorSurface.Portal,
            [new Claim("sub", user.Id.ToString()), new Claim("sid", user.SessionId.ToString())], DateTime.UtcNow.AddHours(1));

        using var res = await SendRaw(HttpMethod.Get, Me, forged);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task NoSession_Is401_ButSessionWithoutRoles_Is403()
    {
        var orphan = TestUsers.BackofficeToken(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(1));
        using (var res = await SendRaw(HttpMethod.Get, "/api/backoffice/audit/events", orphan))
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var noRoles = await UserAsync();
        using (var res = await Get("/api/backoffice/audit/events", noRoles))
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await MeStatus(noRoles));
    }

    [Fact]
    public async Task TokenWithoutSid_IsRejected()
    {
        var user = await UserAsync(BackofficeRoles.Administrador);
        var token = TestUsers.Sign(ActorSurface.Backoffice, [new Claim("sub", user.Id.ToString())], DateTime.UtcNow.AddHours(1));

        using var res = await SendRaw(HttpMethod.Get, Me, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
