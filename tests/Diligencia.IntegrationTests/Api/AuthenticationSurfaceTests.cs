using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Api;

public class AuthenticationSurfaceTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private async Task<HttpStatusCode> Status(string path, TestUser? user = null, string? token = null)
    {
        using var res = await SendRaw(HttpMethod.Get, path, token ?? user?.Token);
        return res.StatusCode;
    }

    [Fact]
    public async Task ValidToken_EachSurface_Returns200()
    {
        Assert.Equal(HttpStatusCode.OK, await Status("/api/backoffice/_test/ping", await UserAsync(BackofficeRoles.Analista)));
        Assert.Equal(HttpStatusCode.OK, await Status("/api/portal/_test/ping", token: await TestUsers.CreatePortalTokenAsync(RuntimeConnection, Clock)));
    }

    [Fact]
    public async Task NoToken_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await Status("/api/backoffice/_test/ping"));

    [Fact]
    public async Task PortalToken_InBackoffice_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            await Status("/api/backoffice/_test/ping", token: await TestUsers.CreatePortalTokenAsync(RuntimeConnection, Clock)));

    [Fact]
    public async Task BackofficeToken_InPortal_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await Status("/api/portal/_test/ping", await UserAsync(BackofficeRoles.Administrador)));

    [Fact]
    public async Task InsufficientRole_Returns403() =>
        Assert.Equal(HttpStatusCode.Forbidden, await Status("/api/backoffice/_test/admin", await UserAsync(BackofficeRoles.Analista)));

    [Fact]
    public async Task Administrator_Returns200() =>
        Assert.Equal(HttpStatusCode.OK, await Status("/api/backoffice/_test/admin", await UserAsync(BackofficeRoles.Administrador)));
}
