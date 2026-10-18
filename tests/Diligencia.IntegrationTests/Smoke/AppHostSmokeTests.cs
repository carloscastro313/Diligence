extern alias apphost;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Smoke;

public class AppHostSmokeTests
{
    [Fact]
    public async Task AppHost_StartsAllResources_Healthy()
    {
        var ct = TestContext.Current.CancellationToken;

        // La API exige claves de firma del portal (en local vienen de user-jwts); la del backoffice la fija el AppHost.
        var portal = TestUsers.For(ActorSurface.Portal);
        Environment.SetEnvironmentVariable($"Authentication__Schemes__{AuthSchemes.Portal}__ValidIssuer", portal.Issuer);
        Environment.SetEnvironmentVariable($"Authentication__Schemes__{AuthSchemes.Portal}__ValidAudiences__0", portal.Audience);
        Environment.SetEnvironmentVariable($"Authentication__Schemes__{AuthSchemes.Portal}__SigningKeys__0__Value", Convert.ToBase64String(portal.Key));

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<apphost::Program>(["--Diligencia:EphemeralDatabase=true"], ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        try
        {
            foreach (var resource in new[] { "api", "worker", "backoffice", "portal" })
                await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, ct)
                    .WaitAsync(TimeSpan.FromSeconds(150), ct);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message + Environment.NewLine + States(app), ex);
        }
    }

    // Para diagnosticar: las últimas líneas del recurso que no arrancó.
    private static async Task<string> LogsAsync(DistributedApplication app, string resource)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        var lines = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var batch in logs.WatchAsync(resource).WithCancellation(cts.Token))
                lines.AddRange(batch.Select(l => l.Content));
        }
        catch (OperationCanceledException) { }
        return $"--- logs de {resource} ---{Environment.NewLine}{string.Join(Environment.NewLine, lines.TakeLast(40))}";
    }

    private static string States(DistributedApplication app)
    {
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        return string.Join(Environment.NewLine, new[] { "sql", "migrations", "api", "worker", "backoffice", "portal" }.Select(n =>
            notifications.TryGetCurrentState(n, out var e)
                ? $"{n}: {e.Snapshot.State?.Text} exit={e.Snapshot.ExitCode} props={string.Join("; ", e.Snapshot.Properties.Where(p => p.Name.Contains("message", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("error", StringComparison.OrdinalIgnoreCase)).Select(p => $"{p.Name}={p.Value}"))}"
                : $"{n}: sin estado"));
    }
}
