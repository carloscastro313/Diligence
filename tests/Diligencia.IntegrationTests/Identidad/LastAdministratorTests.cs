using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

// FR-010. Base propia: la regla cuenta todos los administradores activos y la base compartida tiene los de otros tests.
public class LastAdministratorTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Users = "/api/backoffice/users";

    public override async ValueTask InitializeAsync()
    {
        var db = await Sql.CreateDatabaseAsync();
        RuntimeConnection = db.Runtime;
        MigratorConnection = db.Migrator;
        await base.InitializeAsync();
    }

    private async Task<(Guid Area, object Body)> SelfBodyAsync(TestUser user, params string[] roles)
    {
        var area = await SqlServerFixture.ScalarAsync<Guid>(MigratorConnection, $"SELECT AreaId FROM identidad.Users WHERE Id = '{user.Id}'");
        return (area, new { name = "Admin", email = user.Email, areaId = area, roles });
    }

    [Fact]
    public async Task TheOnlyAdministrator_CannotBeDeactivated_NorLoseTheRole()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);

        using (var off = await Post($"{Users}/{admin.Id}/deactivate", admin)) Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);

        var (_, body) = await SelfBodyAsync(admin, BackofficeRoles.Auditor);
        using (var demote = await Put($"{Users}/{admin.Id}", admin, body)) Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);

        // Cambiar otros datos conservando el rol sí se puede.
        var (area, _) = await SelfBodyAsync(admin);
        using var rename = await Put($"{Users}/{admin.Id}", admin, new { name = "Otro", email = admin.Email, areaId = area, roles = new[] { BackofficeRoles.Administrador } });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task WithTwoAdministrators_OneCanGo_ButNotTheLast()
    {
        var a = await UserAsync(BackofficeRoles.Administrador);
        var b = await UserAsync(BackofficeRoles.Administrador);

        using (var off = await Post($"{Users}/{b.Id}/deactivate", a)) Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        using (var self = await Post($"{Users}/{a.Id}/deactivate", a)) Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);

        // Un administrador desactivado no cuenta, pero al reactivarlo vuelve a haber dos.
        (await Post($"{Users}/{b.Id}/reactivate", a)).Dispose();
        using var again = await Post($"{Users}/{a.Id}/deactivate", a);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task TwoAdministratorsDeactivatingEachOtherAtOnce_NeverLeaveZero()
    {
        var a = await UserAsync(BackofficeRoles.Administrador);
        var b = await UserAsync(BackofficeRoles.Administrador);

        var results = await Task.WhenAll(
            Post($"{Users}/{b.Id}/deactivate", a),
            Post($"{Users}/{a.Id}/deactivate", b));
        var codes = results.Select(r => r.StatusCode).ToList();
        foreach (var r in results) r.Dispose();

        Assert.Single(codes, HttpStatusCode.NoContent);
        // El perdedor recibe 409 por la regla, o 401 si la otra desactivación ya revocó su sesión.
        Assert.Single(codes, c => c is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(MigratorConnection,
            "SELECT COUNT(*) FROM identidad.Users u JOIN identidad.UserRoles r ON r.UserId = u.Id WHERE u.IsActive = 1 AND r.Role = 'Administrador'"));
    }
}
