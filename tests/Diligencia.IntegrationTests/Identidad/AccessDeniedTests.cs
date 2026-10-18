using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class AccessDeniedTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    [Fact]
    public async Task Forbidden_IsAudited_WithEndpointAndActor()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get("/api/backoffice/audit/events", user);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var events = await AuditAsync("Endpoint", "/api/backoffice/audit/events", "AccessDenied");
        Assert.Contains(events, e => e.GetProperty("actorId").GetString() == user.Id.ToString());
    }
}
