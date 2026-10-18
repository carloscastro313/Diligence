using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class ForgotPasswordTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Forgot = "/api/backoffice/auth/password/forgot";
    private const string Reset = "/api/backoffice/auth/password/reset";
    private const string NewPassword = "nueva-contrasena-larga-55";

    [Fact]
    public async Task Forgot_Answers202ForAnyAddress_AndMailsOnlyActiveUsers()
    {
        var active = await UserWithPasswordAsync(BackofficeRoles.Analista);
        var disabled = await CustomUserAsync([BackofficeRoles.Analista], active: false, password: TestUsers.DefaultPassword);
        var unknown = $"nadie{Guid.NewGuid():N}@test.local";

        var bodies = new List<string>();
        foreach (var email in new[] { active.Email, disabled.Email, unknown })
        {
            using var res = await Post(Forgot, body: new { email = email.ToUpperInvariant() });
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            bodies.Add(await res.Content.ReadAsStringAsync(Ct));
        }

        Assert.Single(bodies.Distinct());
        Assert.Single(Emails.To(active.Email));
        Assert.Empty(Emails.To(disabled.Email));
        Assert.Empty(Emails.To(unknown));
        Assert.Single(await AuditAsync("Usuario", active.Id.ToString(), "PasswordResetRequested"));
        Assert.Empty(await AuditAsync("Usuario", disabled.Id.ToString(), "PasswordResetRequested"));

        using var invalid = await Post(Forgot, body: new { email = "no-es-correo" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Link_IsSingleUse_Expires60Minutes_RevokesSessionsAndClearsTheLockout()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);
        for (var i = 0; i < 5; i++) (await Login(user.Email, "incorrecta")).Dispose();
        using (var locked = await Login(user.Email, user.Password!)) Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        (await Post(Forgot, body: new { email = user.Email })).Dispose();
        var token = Emails.LastTokenFor(user.Email);
        using (var ok = await Post(Reset, body: new { token, newPassword = NewPassword })) Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        // Sesión previa revocada, bloqueo limpio, contraseña nueva vigente, enlace gastado.
        using (var old = await Get("/api/backoffice/auth/me", user)) Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using (var login = await Login(user.Email, NewPassword)) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var reuse = await Post(Reset, body: new { token, newPassword = NewPassword + "x" })) Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        var set = Assert.Single(await AuditAsync("Usuario", user.Id.ToString(), "PasswordSet"));
        Assert.Contains("Reset", set.GetProperty("newValues").GetString());

        (await Post(Forgot, body: new { email = user.Email })).Dispose();
        var late = Emails.LastTokenFor(user.Email);
        Clock.Advance(TimeSpan.FromMinutes(61));
        using var expired = await Post(Reset, body: new { token = late, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task Link_StillWorksJustBefore60Minutes()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);
        (await Post(Forgot, body: new { email = user.Email })).Dispose();
        var token = Emails.LastTokenFor(user.Email);

        Clock.Advance(TimeSpan.FromMinutes(59));

        using var ok = await Post(Reset, body: new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }
}
