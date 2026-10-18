using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class AreasTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Areas = "/api/backoffice/areas";

    [Fact]
    public async Task Create_Rename_Deactivate_Reactivate()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var name = "Compras " + Guid.NewGuid().ToString("N");

        using var created = await Post(Areas, admin, new { name = $"  {name} " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        using (var dup = await Post(Areas, admin, new { name = name.ToUpperInvariant() })) Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        using (var empty = await Post(Areas, admin, new { name = "  " })) Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var other = await NewAreaAsync(admin);
        using (var clash = await Put($"{Areas}/{other}", admin, new { name })) Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        using (var rename = await Put($"{Areas}/{id}", admin, new { name = name + " 2" }))
            Assert.Equal(name + " 2", (await Json(rename)).GetProperty("name").GetString());
        using (var missing = await Put($"{Areas}/{Guid.NewGuid()}", admin, new { name = "x" })) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        (await Post($"{Areas}/{id}/deactivate", admin)).Dispose();
        (await Post($"{Areas}/{id}/deactivate", admin)).Dispose(); // idempotente
        using (var inactive = await Get($"{Areas}?status=inactive", admin))
            Assert.Contains(id, (await Json(inactive)).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));
        using (var active = await Get($"{Areas}?status=active", admin))
            Assert.DoesNotContain(id, (await Json(active)).EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));

        using (var react = await Post($"{Areas}/{id}/reactivate", admin)) Assert.Equal(HttpStatusCode.NoContent, react.StatusCode);
        using (var gone = await Post($"{Areas}/{Guid.NewGuid()}/deactivate", admin)) Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task AnInactiveArea_CannotBeAssigned_ButItsUsersKeepIt()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var existing = await CustomUserAsync([BackofficeRoles.Analista], areaId: area);
        (await Post($"{Areas}/{area}/deactivate", admin)).Dispose();

        using (var create = await Post("/api/backoffice/users", admin,
            new { name = "Nuevo", email = $"n{Guid.NewGuid():N}@test.local", areaId = area, roles = new[] { BackofficeRoles.Analista } }))
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);

        using var detail = await Get($"/api/backoffice/users/{existing.Id}", admin);
        Assert.Equal(area, (await Json(detail)).GetProperty("areaId").GetGuid());
        using var me = await Get("/api/backoffice/auth/me", existing);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Auditor_CanListButNotCreate()
    {
        var auditor = await UserAsync(BackofficeRoles.Auditor);

        using (var list = await Get(Areas, auditor)) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var create = await Post(Areas, auditor, new { name = "No" })) Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }
}
