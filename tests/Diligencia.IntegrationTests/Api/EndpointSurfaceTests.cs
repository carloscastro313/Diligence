using Diligencia.IntegrationTests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Api;

public class EndpointSurfaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] Open = ["/health", "/alive"];

    [Fact]
    public void EveryEndpoint_IsUnderASurface_AndHasAuthorizationMetadata()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>().ToList();

        var bad = endpoints
            .Where(e => !Open.Contains(e.RoutePattern.RawText))
            .Where(e =>
            {
                var path = e.RoutePattern.RawText ?? "";
                var underSurface = path.StartsWith("/api/backoffice") || path.StartsWith("/api/portal");
                var authorized = e.Metadata.GetMetadata<IAuthorizeData>() is not null;
                return !underSurface || !authorized;
            })
            .Select(e => e.RoutePattern.RawText);

        Assert.Empty(bad);
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/health");
    }
}
