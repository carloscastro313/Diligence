using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Logging;

// FR-010: tras el alta y el envío del enlace, ni los logs ni la bitácora contienen el token ni la URL.
public class SolicitudNoSecretsInLogsTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private sealed class LoggingApiFactory(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    protected override DbApiFactory CreateFactory() => new LoggingApiFactory(RuntimeConnection);

    [Fact]
    public async Task PortalToken_AndUrl_NeverReachLogsOrAuditTrail()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var email = $"logs{Guid.NewGuid():N}@acme.test";

        using var res = await Post(CreateSolicitudTests.Path, user, CreateSolicitudTests.Body(TestCuits.New(), email));
        Assert.True(res.IsSuccessStatusCode);

        var job = Assert.Single(Jobs.Jobs);
        using (var scope = Factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService(job.Type);
            var args = job.Method.GetParameters().Select((p, i) => p.ParameterType == typeof(CancellationToken) ? Ct : job.Args[i]).ToArray();
            await (Task)job.Method.Invoke(svc, args)!;
        }

        var body = Assert.Single(EmailTransport.Sent, m => m.To == email).Body;
        var token = new string(body[(body.IndexOf("#t=", StringComparison.Ordinal) + 3)..]
            .TakeWhile(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        Assert.True(token.Length >= 20, "no se encontró el token en el correo");

        var logs = string.Join('\n', Factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.Message + " " + r.Exception + " " + string.Join(' ', r.Scopes.Select(s => s?.ToString()))
                + " " + string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])));
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(token, logs);
        Assert.DoesNotContain("/acceso#t=", logs);

        var audit = await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Id FROM auditoria.AuditEvents WHERE NewValues LIKE @t OR OldValues LIKE @t OR NewValues LIKE '%/acceso#t=%' OR OldValues LIKE '%/acceso#t=%'",
            ("@t", "%" + token + "%"));
        Assert.Empty(audit);
    }
}
