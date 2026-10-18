using System.Net.Http.Headers;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Logging;

// T088 / FR-012 / FR-014: log de solicitud sin datos sensibles, scopes de correlación y nivel configurable.
public class RequestLoggingTests(SqlServerFixture sql)
{
    private const string HttpLoggingCategory = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";
    private const string Ping = "/api/backoffice/_test/ping";

    private sealed class LoggedFactory(string connectionString, string? httpLoggingLevel = null) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            if (httpLoggingLevel is not null)
                builder.UseSetting($"Logging:LogLevel:{HttpLoggingCategory}", httpLoggingLevel);
        }
    }

    private async Task<string> Send(LoggedFactory factory, string query, string correlationId)
    {
        var token = (await TestUsers.CreateAsync(sql.RuntimeConnectionString, factory.Clock, [BackofficeRoles.Analista])).Token;
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, Ping + query);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Correlation-Id", correlationId);
        req.Headers.Add("X-Custom-Header", "header-value-xyz");
        using var res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        res.EnsureSuccessStatusCode();
        return token;
    }

    private static List<FakeLogRecord> HttpLogs(LoggedFactory factory) =>
        factory.Services.GetFakeLogCollector().GetSnapshot().Where(r => r.Category == HttpLoggingCategory).ToList();

    [Fact]
    public async Task RequestLog_HasMethodPathStatusDuration_AndNoSensitiveData()
    {
        await using var factory = new LoggedFactory(sql.RuntimeConnectionString);
        var token = await Send(factory, "?secret=query-value-123", "corr-log-1");

        var record = Assert.Single(HttpLogs(factory));
        var text = record.Message;
        Assert.Contains("Method: GET", text);
        Assert.Contains($"Path: {Ping}", text);
        Assert.Contains("StatusCode: 200", text);
        Assert.Contains("Duration:", text);

        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain("Bearer", text);
        Assert.DoesNotContain("Authorization", text);
        Assert.DoesNotContain("query-value-123", text);
        Assert.DoesNotContain("secret=", text);
        Assert.DoesNotContain("header-value-xyz", text);
        Assert.DoesNotContain("X-Custom-Header", text);
        Assert.DoesNotContain("User-Agent", text);
    }

    [Fact]
    public async Task RequestLogs_CarryCorrelationIdAndServiceScopes()
    {
        await using var factory = new LoggedFactory(sql.RuntimeConnectionString);
        await Send(factory, "", "corr-scope-42");

        var record = Assert.Single(HttpLogs(factory));
        var values = record.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>()
            .SelectMany(s => s).ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        Assert.Equal("corr-scope-42", values["CorrelationId"]);
        Assert.Equal("Diligencia.Api", values["Service"]);
    }

    [Fact]
    public async Task LogLevelConfiguration_ChangesVolume_WithoutCodeChanges()
    {
        await using var info = new LoggedFactory(sql.RuntimeConnectionString, "Information");
        await Send(info, "", "corr-vol-1");
        await using var warn = new LoggedFactory(sql.RuntimeConnectionString, "Warning");
        await Send(warn, "", "corr-vol-2");

        Assert.NotEmpty(HttpLogs(info));
        Assert.Empty(HttpLogs(warn));
    }

    [Fact]
    public void AppSettings_ConfigureJsonConsoleWithUtcTimestamp()
    {
        var path = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var console = doc.RootElement.GetProperty("Logging").GetProperty("Console");
        Assert.Equal("json", console.GetProperty("FormatterName").GetString());
        var options = console.GetProperty("FormatterOptions");
        Assert.True(options.GetProperty("IncludeScopes").GetBoolean());
        Assert.True(options.GetProperty("UseUtcTimestamp").GetBoolean());
        Assert.Equal("O", options.GetProperty("TimestampFormat").GetString());
    }
}
