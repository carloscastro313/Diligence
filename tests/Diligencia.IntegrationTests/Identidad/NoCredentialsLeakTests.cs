using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Identidad;

// FR-003: ni los logs ni la bitácora de los flujos de ingreso y recuperación contienen contraseñas, hashes ni tokens.
public class NoCredentialsLeakTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private sealed class Logged(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    protected override DbApiFactory CreateFactory() => new Logged(RuntimeConnection);

    [Fact]
    public async Task LoginAndRecoveryFlows_NeverLogOrAuditCredentials()
    {
        const string wrongPassword = "contrasena-equivocada-3321";
        const string newPassword = "contrasena-nueva-segura-90";
        var user = await UserWithPasswordAsync(BackofficeRoles.Analista);
        var hash = await SqlServerFixture.ScalarAsync<string>(MigratorConnection, $"SELECT PasswordHash FROM identidad.Users WHERE Id = '{user.Id}'");

        (await Login(user.Email, wrongPassword)).Dispose();
        (await Login(user.Email, user.Password!)).Dispose();
        (await Post("/api/backoffice/auth/password/forgot", body: new { email = user.Email })).Dispose();
        var token = Emails.LastTokenFor(user.Email);
        (await Post("/api/backoffice/auth/password/reset", body: new { token, newPassword })).Dispose();
        (await Post("/api/backoffice/auth/password/reset", body: new { token = "token-invalido-xyz", newPassword })).Dispose();

        var secrets = new[] { wrongPassword, user.Password!, newPassword, token, "token-invalido-xyz", hash!, user.Token };

        var logs = string.Join('\n', Factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.Message + " " + r.Exception + " " + string.Join(' ', r.Scopes.Select(s => s?.ToString()))
                + " " + string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])));
        Assert.NotEmpty(logs);

        var audit = string.Join('\n', await SqlServerFixture.ScalarAsync<string>(MigratorConnection,
            $"SELECT STRING_AGG(CONCAT(Action, EntityId, ISNULL(OldValues, ''), ISNULL(NewValues, '')), CHAR(10)) FROM auditoria.AuditEvents WHERE EntityId = '{user.Id}'"));
        Assert.NotEmpty(audit);

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, logs);
            Assert.DoesNotContain(secret, audit);
        }
    }
}
