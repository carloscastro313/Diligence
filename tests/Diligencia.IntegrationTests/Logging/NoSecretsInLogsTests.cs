using System.Net.Http.Headers;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Logging;

// SC-005 / FR-014: ni los logs ni la bitácora contienen contraseñas, tokens, archivos ni cadenas de conexión.
public class NoSecretsInLogsTests(SqlServerFixture sql)
{
    private const string Password = "P4ss-en-query-8841";
    private const string FileContent = "CONTENIDO-CONFIDENCIAL-DEL-DOCUMENTO-7731";

    private sealed class UploadApiFactory(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
            {
                s.AddFakeLogging();
                s.AddTransient<IStartupFilter, UploadFilter>();
            });
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }

        // La feature no tiene endpoints de carga: se registra una ruta de prueba protegida que lee el archivo.
        private sealed class UploadFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                next(app);
                var routes = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;
                routes.MapGroup("/api/backoffice").RequireAuthorization(AuthPolicies.Backoffice)
                    .MapPost("/_test/upload", async (HttpRequest request) =>
                    {
                        var form = await request.ReadFormAsync();
                        return Results.Ok(form.Files.Count);
                    }).DisableAntiforgery();
            };
        }
    }

    [Fact]
    public async Task PasswordInQuery_BearerToken_AndUploadedFile_NeverReachLogsOrAuditTrail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UploadApiFactory(sql.RuntimeConnectionString);
        using var client = factory.CreateClient();
        var token = (await TestUsers.CreateAsync(sql.RuntimeConnectionString, factory.Clock, [BackofficeRoles.Auditor])).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(FileContent)), "file", "contrato.pdf" },
        };
        (await client.PostAsync($"/api/backoffice/_test/upload?password={Password}", form, ct)).Dispose();
        (await client.GetAsync($"/api/backoffice/audit/events?entityType=X&password={Password}", ct)).Dispose();
        // Una solicitud que falla también se registra.
        (await client.GetAsync($"/api/backoffice/no-existe?token={Password}", ct)).Dispose();

        var secrets = new[]
        {
            Password, token, FileContent, "Runtime-Pass-123!", "Migrator-Pass-123!", "diligencia_app", "Password=",
        };

        var logs = string.Join('\n', factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.Message + " " + r.Exception + " " + string.Join(' ', r.Scopes.Select(s => s?.ToString()))
                + " " + string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])));
        Assert.NotEmpty(logs);

        var audit = new List<string>();
        await using (var c = new SqlConnection(sql.MigratorConnectionString))
        {
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ISNULL(OldValues,'') + ISNULL(NewValues,'') + ActorId + EntityType + EntityId FROM auditoria.AuditEvents";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) audit.Add(r.GetString(0));
        }

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, logs);
            Assert.DoesNotContain(audit, a => a.Contains(secret));
        }
    }
}
