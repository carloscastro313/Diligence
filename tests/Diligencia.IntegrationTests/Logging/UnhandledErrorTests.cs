using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Diligencia.IntegrationTests.Logging;

// US3-5 / FR-016: un 500 devuelve ProblemDetails genérico con correlationId, sin detalle interno; el log sí lo registra.
public class UnhandledErrorTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly BoomApiFactory factory = new(sql.RuntimeConnectionString);

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    private const string Secret = "secreto-interno-xyz";

    public sealed class BoomApiFactory(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
            {
                s.AddFakeLogging();
                s.AddTransient<IStartupFilter, BoomFilter>();
            });
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }

        private sealed class BoomFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                next(app);
                var routes = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;
                routes.MapGroup("/api/backoffice").RequireAuthorization(AuthPolicies.Backoffice)
                    .MapGet("/_test/boom", () => { throw new InvalidOperationException(Secret); });
            };
        }
    }

    private async Task<(HttpResponseMessage Res, string Raw)> Get(string path)
    {
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        var user = await TestUsers.CreateAsync(sql.RuntimeConnectionString, factory.Clock, [BackofficeRoles.Analista]);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        var res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        return (res, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForcedException_Returns500GenericProblemWithCorrelationId_AndLogsException()
    {
        var (res, raw) = await Get("/api/backoffice/_test/boom");
        using var _ = res;

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        var header = Assert.Single(res.Headers.GetValues("X-Correlation-Id"));
        Assert.Equal(header, body.GetProperty("correlationId").GetString());

        Assert.DoesNotContain(Secret, raw);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);
        Assert.DoesNotContain("   at ", raw);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(factory.Services.GetFakeLogCollector().GetSnapshot(),
            r => r.Exception is InvalidOperationException e && e.Message == Secret);
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemWithCorrelationId()
    {
        var (res, raw) = await Get("/api/backoffice/_test/no-existe");
        using var _ = res;

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var header = Assert.Single(res.Headers.GetValues("X-Correlation-Id"));
        Assert.Equal(header, JsonDocument.Parse(raw).RootElement.GetProperty("correlationId").GetString());
    }
}
