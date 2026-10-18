using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

public class LockoutTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private async Task<HttpStatusCode> Attempt(TestUser user, string password)
    {
        using var res = await Login(user.Email, password);
        return res.StatusCode;
    }

    [Fact]
    public async Task FiveFailures_LockFor15Minutes_AndTheLockIsIndistinguishable()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, await Attempt(user, "incorrecta-" + i));

        // Bloqueada: ni la contraseña correcta entra, y el mensaje es el mismo.
        using (var locked = await Login(user.Email, user.Password!))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
            Assert.Equal("Correo o contraseña incorrectos.", (await Json(locked)).GetProperty("title").GetString());
        }

        Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(HttpStatusCode.Unauthorized, await Attempt(user, user.Password!));
        Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.NoContent, await Attempt(user, user.Password!));

        var failures = await AuditAsync("Usuario", user.Id.ToString(), "LoginFailed");
        Assert.Equal(5, failures.Count(f => f.GetProperty("newValues").GetString()!.Contains("CredencialInvalida")));
        Assert.Equal(2, failures.Count(f => f.GetProperty("newValues").GetString()!.Contains("Bloqueado")));
        Assert.Single(await AuditAsync("Usuario", user.Id.ToString(), "AccountLocked"));
    }

    [Fact]
    public async Task ASuccessfulLogin_ResetsTheFailureCounter()
    {
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);

        for (var i = 0; i < 4; i++) await Attempt(user, "incorrecta");
        Assert.Equal(HttpStatusCode.NoContent, await Attempt(user, user.Password!));
        for (var i = 0; i < 4; i++) await Attempt(user, "incorrecta");

        // 8 fallos en total pero nunca 5 seguidos: sigue sin estar bloqueada.
        Assert.Equal(HttpStatusCode.NoContent, await Attempt(user, user.Password!));
        Assert.Empty(await AuditAsync("Usuario", user.Id.ToString(), "AccountLocked"));
    }
}
