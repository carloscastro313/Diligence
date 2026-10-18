using System.Net;
using System.Net.Http.Headers;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Api;

public class AuthenticationSurfaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpStatusCode> Get(string path, string? token)
    {
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        return res.StatusCode;
    }

    [Fact]
    public async Task ValidToken_EachSurface_Returns200() 
    {
        Assert.Equal(HttpStatusCode.OK, await Get("/api/backoffice/_test/ping", TestTokens.Create(ActorSurface.Backoffice, "ana", BackofficeRoles.Analista)));
        Assert.Equal(HttpStatusCode.OK, await Get("/api/portal/_test/ping", TestTokens.Create(ActorSurface.Portal, "tercero-1")));
    }

    [Fact]
    public async Task NoToken_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await Get("/api/backoffice/_test/ping", null));

    [Fact]
    public async Task PortalToken_InBackoffice_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await Get("/api/backoffice/_test/ping", TestTokens.Create(ActorSurface.Portal, "t")));

    [Fact]
    public async Task BackofficeToken_InPortal_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await Get("/api/portal/_test/ping", TestTokens.Create(ActorSurface.Backoffice, "ana", BackofficeRoles.Administrador)));

    [Fact]
    public async Task InsufficientRole_Returns403() =>
        Assert.Equal(HttpStatusCode.Forbidden, await Get("/api/backoffice/_test/admin", TestTokens.Create(ActorSurface.Backoffice, "ana", BackofficeRoles.Analista)));

    [Fact]
    public async Task Administrator_Returns200() =>
        Assert.Equal(HttpStatusCode.OK, await Get("/api/backoffice/_test/admin", TestTokens.Create(ActorSurface.Backoffice, "root", BackofficeRoles.Administrador)));
}
