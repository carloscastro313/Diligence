using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Identidad;

// SC-003: cada endpoint del backoffice contra cada uno de los cinco roles, según la tabla de FR-016.
public class PermissionMatrixTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Prefix = "/api/backoffice";
    private static readonly string[] AllRoles =
    [
        BackofficeRoles.Solicitante, BackofficeRoles.Analista, BackofficeRoles.Aprobador,
        BackofficeRoles.Administrador, BackofficeRoles.Auditor,
    ];
    private static readonly string[] Everyone = AllRoles;
    private static readonly string[] AdminOnly = [BackofficeRoles.Administrador];
    private static readonly string[] AdminOrAuditor = [BackofficeRoles.Administrador, BackofficeRoles.Auditor];

    // Rutas anónimas: no entran en la matriz (las cubre la lista cerrada de EndpointSurfaceTests).
    private static readonly string[] Anonymous = ["POST /auth/login", "POST /auth/password/forgot", "POST /auth/password/reset"];

    // Roles con acceso por endpoint. Un endpoint nuevo sin fila aquí hace fallar EveryEndpoint_HasARowInTheMatrix.
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["GET /auth/me"] = Everyone,
        ["POST /auth/logout"] = Everyone,
        ["GET /audit/events"] = AdminOrAuditor,
        ["GET /users"] = AdminOrAuditor,
        ["GET /users/{id:guid}"] = AdminOrAuditor,
        ["POST /users"] = AdminOnly,
        ["PUT /users/{id:guid}"] = AdminOnly,
        ["POST /users/{id:guid}/deactivate"] = AdminOnly,
        ["POST /users/{id:guid}/reactivate"] = AdminOnly,
        ["GET /areas"] = AdminOrAuditor,
        ["POST /areas"] = AdminOnly,
        ["PUT /areas/{id:guid}"] = AdminOnly,
        ["POST /areas/{id:guid}/deactivate"] = AdminOnly,
        ["POST /areas/{id:guid}/reactivate"] = AdminOnly,
        ["POST /diagnostics/jobs"] = AdminOnly,
    };

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var endpoint in Allowed.Keys)
            foreach (var role in AllRoles)
                data.Add(endpoint, role);
        return data;
    }

    private static string Key(RouteEndpoint e)
    {
        var method = e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
        return $"{method} {e.RoutePattern.RawText![Prefix.Length..]}";
    }

    [Fact]
    public void EveryEndpoint_HasARowInTheMatrix()
    {
        var keys = Factory.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Prefix + "/") && !e.RoutePattern.RawText.Contains("/_test/"))
            .Select(Key)
            .Where(k => !Anonymous.Contains(k))
            .ToList();

        Assert.Empty(keys.Except(Allowed.Keys));
        Assert.Empty(Allowed.Keys.Except(keys)); // y no sobran filas
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Endpoint_IsAllowedExactlyForItsRoles(string endpoint, string role)
    {
        var user = await UserAsync(role);
        var (method, pattern) = (endpoint.Split(' ')[0], endpoint.Split(' ')[1]);
        var path = Prefix + System.Text.RegularExpressions.Regex.Replace(pattern, @"\{id:guid\}", Guid.NewGuid().ToString());

        using var res = await Send(new HttpMethod(method), path, user, method is "POST" or "PUT" ? new { } : null);

        if (Allowed[endpoint].Contains(role))
            Assert.True(res.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                $"{role} debería poder usar {endpoint} pero recibió {(int)res.StatusCode}.");
        else
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task MultiRoleUser_GetsTheUnionOfPermissions()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante, BackofficeRoles.Auditor);

        using (var r = await Get(Prefix + "/audit/events", user)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Get(Prefix + "/users", user)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Post(Prefix + "/users", user, new { })) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
