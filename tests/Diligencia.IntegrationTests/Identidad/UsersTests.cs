using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class UsersTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Users = "/api/backoffice/users";

    private static object Body(string name, string email, Guid areaId, params string[] roles) => new { name, email, areaId, roles };

    [Fact]
    public async Task Create_NormalizesEmail_StartsActiveWithoutPassword_AndSendsInvitation()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var email = $"nuevo{Guid.NewGuid():N}@test.local";

        using var res = await Post(Users, admin, Body("  Nuevo Usuario ", $" {email.ToUpperInvariant()} ", area, BackofficeRoles.Analista));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var user = await Json(res);
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Nuevo Usuario", user.GetProperty("name").GetString());
        Assert.True(user.GetProperty("isActive").GetBoolean());
        Assert.False(user.GetProperty("hasPassword").GetBoolean());
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.EndsWith(user.GetProperty("id").GetString()!, res.Headers.Location!.ToString());

        var mail = Assert.Single(Emails.To(email));
        Assert.Contains("http://test.local/restablecer?token=", mail.Body);
    }

    [Fact]
    public async Task Create_RejectsDuplicateEmail_InactiveArea_AndMissingRoles()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var inactiveArea = await NewAreaAsync(admin, active: false);
        var email = $"dup{Guid.NewGuid():N}@test.local";
        (await Post(Users, admin, Body("Uno", email, area, BackofficeRoles.Analista))).Dispose();

        using (var dup = await Post(Users, admin, Body("Dos", $"  {email.ToUpperInvariant()}", area, BackofficeRoles.Auditor)))
            Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        using (var inactive = await Post(Users, admin, Body("Tres", $"x{email}", inactiveArea, BackofficeRoles.Auditor)))
            Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        using (var noRoles = await Post(Users, admin, Body("Cuatro", $"y{email}", area)))
            Assert.Equal(HttpStatusCode.BadRequest, noRoles.StatusCode);
        using (var badRole = await Post(Users, admin, Body("Cinco", $"z{email}", area, "Dios")))
            Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        using (var repeated = await Post(Users, admin, Body("Seis", $"w{email}", area, BackofficeRoles.Auditor, BackofficeRoles.Auditor)))
            Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
    }

    [Fact]
    public async Task RoleAndAreaChanges_TakeEffectOnTheNextRequest_AndDeactivationRevokesSessions()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var otherArea = await NewAreaAsync(admin);
        var user = await UserAsync(BackofficeRoles.Analista);
        using (var before = await Get("/api/backoffice/audit/events", user)) Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        using (var edit = await Put($"{Users}/{user.Id}", admin, Body("Renombrado", user.Email, otherArea, BackofficeRoles.Auditor)))
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        using (var after = await Get("/api/backoffice/audit/events", user)) Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using (var me = await Get("/api/backoffice/auth/me", user))
        {
            var body = await Json(me);
            Assert.Equal("Renombrado", body.GetProperty("name").GetString());
            Assert.Equal([BackofficeRoles.Auditor], body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
        }

        using (var off = await Post($"{Users}/{user.Id}/deactivate", admin)) Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        using (var denied = await Get("/api/backoffice/auth/me", user)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Reactivate_RestoresThePreviousRoles()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var user = await CustomUserAsync([BackofficeRoles.Analista, BackofficeRoles.Auditor], password: TestUsers.DefaultPassword);

        (await Post($"{Users}/{user.Id}/deactivate", admin)).Dispose();
        using (var blocked = await Login(user.Email, user.Password!)) Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        (await Post($"{Users}/{user.Id}/reactivate", admin)).Dispose();
        (await Post($"{Users}/{user.Id}/reactivate", admin)).Dispose(); // idempotente

        using var login = await Login(user.Email, user.Password!);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var detail = await Get($"{Users}/{user.Id}", admin);
        Assert.Equal([BackofficeRoles.Analista, BackofficeRoles.Auditor],
            (await Json(detail)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }

    [Fact]
    public async Task Edit_WithoutChangingAnInactiveArea_IsAllowed_ButMovingToOneIsNot()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var inactive = await NewAreaAsync(admin, active: false);
        var user = await CustomUserAsync([BackofficeRoles.Analista], areaId: area);
        (await Post($"/api/backoffice/areas/{area}/deactivate", admin)).Dispose();

        using (var rename = await Put($"{Users}/{user.Id}", admin, Body("Otro nombre", user.Email, area, BackofficeRoles.Analista)))
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        using (var move = await Put($"{Users}/{user.Id}", admin, Body("Otro nombre", user.Email, inactive, BackofficeRoles.Analista)))
            Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
    }

    [Fact]
    public async Task Edit_RejectsDuplicateEmail_AndUnknownUser()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var a = await UserAsync(BackofficeRoles.Analista);
        var b = await UserAsync(BackofficeRoles.Analista);

        using (var dup = await Put($"{Users}/{a.Id}", admin, Body("A", b.Email.ToUpperInvariant(), area, BackofficeRoles.Analista)))
            Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        using (var missing = await Put($"{Users}/{Guid.NewGuid()}", admin, Body("A", "q@test.local", area, BackofficeRoles.Analista)))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByRoleAreaAndStatus()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var areaX = await NewAreaAsync(admin);
        var areaY = await NewAreaAsync(admin);
        var a = await CustomUserAsync([BackofficeRoles.Analista], areaId: areaX);
        var b = await CustomUserAsync([BackofficeRoles.Auditor], active: false, areaId: areaX);
        var c = await CustomUserAsync([BackofficeRoles.Analista], areaId: areaY);

        async Task<Guid[]> Ids(string query)
        {
            using var res = await Get($"{Users}?{query}", admin);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return (await Json(res)).EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToArray();
        }

        Assert.Equal(new[] { a.Id, b.Id }.Order(), (await Ids($"areaId={areaX}")).Order());
        Assert.Equal([a.Id], await Ids($"areaId={areaX}&role={BackofficeRoles.Analista}"));
        Assert.Equal([b.Id], await Ids($"areaId={areaX}&status=inactive"));
        Assert.Equal([a.Id], await Ids($"areaId={areaX}&status=active"));
        var analysts = await Ids($"role={BackofficeRoles.Analista}");
        Assert.Contains(a.Id, analysts);
        Assert.Contains(c.Id, analysts);
        Assert.DoesNotContain(b.Id, analysts);

        using var bad = await Get($"{Users}?status=otro", admin);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Auditor_CanRead_ButNotWrite()
    {
        var auditor = await UserAsync(BackofficeRoles.Auditor);
        var someone = await UserAsync(BackofficeRoles.Analista);

        using (var list = await Get(Users, auditor)) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var one = await Get($"{Users}/{someone.Id}", auditor)) Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        using (var off = await Post($"{Users}/{someone.Id}/deactivate", auditor)) Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
    }
}
