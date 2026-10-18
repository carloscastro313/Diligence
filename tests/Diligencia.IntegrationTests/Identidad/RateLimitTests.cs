using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class RateLimitTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private sealed class Limited(string connectionString) : DbApiFactory(connectionString)
    {
        protected override int AuthRateLimitPerMinute => 3;
    }

    protected override DbApiFactory CreateFactory() => new Limited(RuntimeConnection);

    [Fact]
    public async Task FourthAnonymousRequestInAMinute_Gets429_ButAuthenticatedEndpointsAreNotLimited()
    {
        var user = await UserAsync(BackofficeRoles.Analista);

        for (var i = 0; i < 3; i++)
        {
            using var res = await Login("alguien@test.local", "incorrecta");
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        using (var limited = await Login("alguien@test.local", "incorrecta")) Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        // La política es compartida por las tres rutas anónimas.
        using (var forgot = await Post("/api/backoffice/auth/password/forgot", body: new { email = "a@test.local" }))
            Assert.Equal(HttpStatusCode.TooManyRequests, forgot.StatusCode);

        for (var i = 0; i < 10; i++)
        {
            using var me = await Get("/api/backoffice/auth/me", user);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
    }
}
