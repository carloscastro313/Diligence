extern alias worker;
using System.Net;
using System.Net.Http.Headers;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Logging;

// US3-3 / SC-004: el X-Correlation-Id de la solicitud llega a los logs de la API y al job que corre en el worker.
public class CorrelationEndToEndTests(SqlServerFixture sql)
{
    private sealed class ApiWithLogs(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    private sealed class WorkerWithLogs(string connectionString) : WebApplicationFactory<worker::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:diligencia", connectionString);
            builder.UseSetting("Hangfire:QueuePollSeconds", "1");
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    private static bool HasCorrelation(FakeLogRecord r, string id) =>
        r.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>()
            .Any(scope => scope.Any(kv => kv.Key == "CorrelationId" && Equals(kv.Value, id)));

    [Fact]
    public async Task CorrelationId_FlowsFromApiRequestToWorkerJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = "e2e-" + Guid.NewGuid().ToString("N")[..12];

        await using var api = new ApiWithLogs(sql.RuntimeConnectionString);
        await using var worker = new WorkerWithLogs(sql.RuntimeConnectionString);
        using var apiClient = api.CreateClient();
        using var workerClient = worker.CreateClient(); // arranca el host y el servidor de Hangfire

        var admin = await TestUsers.CreateAsync(sql.RuntimeConnectionString, api.Clock, [BackofficeRoles.Administrador]);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/backoffice/diagnostics/jobs");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", id);
        using var res = await apiClient.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(id, res.Headers.GetValues("X-Correlation-Id").Single());

        // Log de la API con el id en su scope.
        Assert.Contains(api.Services.GetFakeLogCollector().GetSnapshot(), r => HasCorrelation(r, id));

        // El worker ejecuta el job y su log lleva el mismo id.
        var collector = worker.Services.GetFakeLogCollector();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (collector.GetSnapshot().Any(r => r.Message == "Diagnostic job executed" && HasCorrelation(r, id)))
                return;
            await Task.Delay(500, ct);
        }
        var seen = string.Join(Environment.NewLine, collector.GetSnapshot().TakeLast(25)
            .Select(r => $"{r.Category} | {r.Message} | scopes: {string.Join(',', r.Scopes.Select(sc => sc is IEnumerable<KeyValuePair<string, object>> kv ? string.Join(';', kv) : sc?.ToString()))}"));
        var parameters = await SqlServerFixture.ScalarAsync<string>(sql.MigratorConnectionString,
            "SELECT STRING_AGG(Name + '=' + ISNULL(Value,''), ' ; ') FROM hangfire.JobParameter");
        Assert.Fail("params: " + parameters + Environment.NewLine + "El log del job en el worker no llevó el CorrelationId de la solicitud." + Environment.NewLine + seen);
    }
}
