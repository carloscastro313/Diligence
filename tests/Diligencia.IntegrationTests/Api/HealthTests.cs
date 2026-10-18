extern alias worker;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.MigrationService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace Diligencia.IntegrationTests.Api;

// FR-005 / SC-002: con la base caída /health da 503 y /alive 200; al volver, se recupera sin reiniciar.
// Contenedor propio con puerto fijo (si cambiara al reiniciar, la cadena de conexión quedaría vieja).
public class HealthTests
{
    private const string Password = "Health-Pass-123!";

    private sealed class HealthApiFactory(string connectionString) : ApiFactory
    {
        protected override string? ConnectionString => connectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> Get(HttpClient client, string path)
    {
        using var res = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var raw = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (res.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    [Fact]
    public async Task DatabaseDown_HealthIs503_AliveIs200_AndRecovers()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = FreePort();
        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPortBinding(port, 1433).WithPassword(Password).Build();
        await container.StartAsync(ct);

        string Conn(string user, string pwd, string db) => new SqlConnectionStringBuilder
        {
            DataSource = $"127.0.0.1,{port}", UserID = user, Password = pwd, InitialCatalog = db,
            TrustServerCertificate = true, ConnectTimeout = 3,
        }.ConnectionString;
        var runtime = Conn("diligencia_app", "Runtime-Pass-123!", "diligencia");
        await MigrationEntryPoint.RunAsync([], new Dictionary<string, string?>
        {
            ["ConnectionStrings:diligencia"] = Conn("diligencia_migrator", "Migrator-Pass-123!", "diligencia"),
            ["ConnectionStrings:diligencia-runtime"] = runtime,
            ["ConnectionStrings:diligencia-admin"] = Conn("sa", Password, "master"),
        });

        await using var factory = new HealthApiFactory(runtime);
        using var client = factory.CreateClient();

        var healthy = await Get(client, "/health");
        Assert.Equal(HttpStatusCode.OK, healthy.Status);
        Assert.Equal("Healthy", healthy.Body.GetProperty("checks").GetProperty("database").GetString());

        await container.StopAsync(ct);

        var down = await Get(client, "/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.Status);
        Assert.Equal("Unhealthy", down.Body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", down.Body.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Healthy", down.Body.GetProperty("checks").GetProperty("self").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/alive")).Status);

        // Sin credenciales ni cadena de conexión en el cuerpo ni en los logs.
        var logs = string.Join('\n', factory.Services.GetFakeLogCollector().GetSnapshot().Select(r => r.Message + r.Exception));
        foreach (var secret in new[] { "Runtime-Pass-123!", "diligencia_app", "Password=" })
        {
            Assert.DoesNotContain(secret, down.Raw);
            Assert.DoesNotContain(secret, logs);
        }

        await container.StartAsync(ct);
        for (var i = 0; i < 30; i++)
        {
            if ((await Get(client, "/health")).Status == HttpStatusCode.OK) return;
            await Task.Delay(2000, ct);
        }
        Assert.Fail("/health no se recuperó tras reiniciar la base.");
    }

    private sealed class WorkerFactory(string connectionString) : WebApplicationFactory<worker::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:diligencia", connectionString);
    }

    [Fact]
    public async Task Worker_ReportsDatabaseSeparately_AliveStaysUp()
    {
        // Base inalcanzable: el proceso sigue vivo y /health lo informa sin exponer la cadena.
        const string unreachable = "Server=127.0.0.1,1;Database=x;User Id=u;Password=Secreto-123!;Connect Timeout=1;TrustServerCertificate=true";
        await using var factory = new WorkerFactory(unreachable);
        using var client = factory.CreateClient();

        var health = await Get(client, "/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.Status);
        Assert.Equal("Unhealthy", health.Body.GetProperty("checks").GetProperty("database").GetString());
        Assert.DoesNotContain("Secreto-123!", health.Raw);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/alive")).Status);
    }
}
