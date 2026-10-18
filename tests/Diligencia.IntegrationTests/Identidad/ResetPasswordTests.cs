using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class ResetPasswordTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Reset = "/api/backoffice/auth/password/reset";
    private const string NewPassword = "otra-contrasena-larga-77";

    private async Task<(string Email, string Id, string Token)> InviteAsync()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area = await NewAreaAsync(admin);
        var email = $"inv{Guid.NewGuid():N}@test.local";
        using var created = await Post("/api/backoffice/users", admin,
            new { name = "Invitada", email, areaId = area, roles = new[] { BackofficeRoles.Analista } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (email, (await Json(created)).GetProperty("id").GetString()!, Emails.LastTokenFor(email));
    }

    [Fact]
    public async Task InvitationToken_SetsThePassword_OnlyOnce_AndIsStoredHashed()
    {
        var (email, id, token) = await InviteAsync();
        using (var blocked = await Login(email, NewPassword)) Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        using (var ok = await Post(Reset, body: new { token, newPassword = NewPassword })) Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        using (var login = await Login(email, NewPassword)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var reuse = await Post(Reset, body: new { token, newPassword = NewPassword + "x" })) Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        // En la base solo está el SHA-256 del token (columna binary(32)), nunca el token.
        await using var c = new Microsoft.Data.SqlClient.SqlConnection(MigratorConnection);
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM identidad.PasswordTokens WHERE TokenHash = @h AND UsedAtUtc IS NOT NULL";
        cmd.Parameters.AddWithValue("@h", OpaqueToken.Hash(token));
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync(Ct))!);

        var set = Assert.Single(await AuditAsync("Usuario", id, "PasswordSet"));
        Assert.Contains("Invitation", set.GetProperty("newValues").GetString());
        Assert.DoesNotContain(token, set.ToString());
        Assert.DoesNotContain(NewPassword, set.ToString());
    }

    [Fact]
    public async Task ExpiredToken_IsRejected_After72Hours()
    {
        var (email, _, token) = await InviteAsync();

        Clock.Advance(TimeSpan.FromHours(73));

        using var res = await Post(Reset, body: new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var login = await Login(email, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task InvalidToken_AndWeakPassword_AreRejected()
    {
        var (_, _, token) = await InviteAsync();

        using (var unknown = await Post(Reset, body: new { token = OpaqueToken.Generate(), newPassword = NewPassword }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal("El enlace no es válido o ya venció.", (await Json(unknown)).GetProperty("title").GetString());
        }
        using (var weak = await Post(Reset, body: new { token, newPassword = "corta" })) Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        // La contraseña débil no gastó el token.
        using var ok = await Post(Reset, body: new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task SettingThePassword_RevokesOpenSessions_AndClearsTheLockout()
    {
        var (email, id, token) = await InviteAsync();
        await SqlServerFixture.ExecuteAsync(MigratorConnection,
            $"UPDATE identidad.Users SET FailedLoginCount = 3, LockoutEndUtc = DATEADD(HOUR, 1, SYSUTCDATETIME()) WHERE Id = '{id}'");
        var session = await TestUsers.CreateAsync(RuntimeConnection, Clock, [BackofficeRoles.Analista], email: $"x{email}");
        await SqlServerFixture.ExecuteAsync(MigratorConnection,
            $"UPDATE identidad.Sessions SET UserId = '{id}' WHERE Id = '{session.SessionId}'");
        var asInvited = TestUsers.BackofficeToken(Guid.Parse(id), session.SessionId, DateTime.UtcNow.AddHours(1));
        using (var before = await SendRaw(HttpMethod.Get, "/api/backoffice/auth/me", asInvited)) Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        (await Post(Reset, body: new { token, newPassword = NewPassword })).Dispose();

        using (var after = await SendRaw(HttpMethod.Get, "/api/backoffice/auth/me", asInvited)) Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(MigratorConnection,
            $"SELECT COUNT(*) FROM identidad.Users WHERE Id = '{id}' AND (FailedLoginCount <> 0 OR LockoutEndUtc IS NOT NULL)"));
    }
}
