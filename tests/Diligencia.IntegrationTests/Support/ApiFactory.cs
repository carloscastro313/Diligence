using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Email;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Support;

public class ApiFactory : WebApplicationFactory<Program>
{
    // Los tests que no tocan la base usan una cadena que nunca se conecta; null = falta la clave (FR-006).
    protected virtual string? ConnectionString => "Server=127.0.0.1,1;Database=unused;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=true";

    // En TestServer la IP remota es nula y todos los tests comparten la partición del limitador (R12).
    protected virtual int AuthRateLimitPerMinute => 100_000;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identidad:BackofficeBaseUrl", "http://test.local");
        builder.UseSetting("Identidad:AuthRateLimitPerMinute", AuthRateLimitPerMinute.ToString());
        if (ConnectionString is not null) builder.UseSetting("ConnectionStrings:diligencia", ConnectionString);
        foreach (var (surface, scheme) in new[] { (ActorSurface.Backoffice, AuthSchemes.Backoffice), (ActorSurface.Portal, AuthSchemes.Portal) })
        {
            var s = TestUsers.For(surface);
            var p = $"Authentication:Schemes:{scheme}";
            builder.UseSetting($"{p}:ValidIssuer", s.Issuer);
            builder.UseSetting($"{p}:ValidAudiences:0", s.Audience);
            builder.UseSetting($"{p}:SigningKeys:0:Value", Convert.ToBase64String(s.Key));
        }

        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestRoutesFilter>());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    // Rutas de prueba bajo cada superficie (la feature aún no tiene endpoints de negocio). Se agregan al mismo
    // conjunto de endpoints de la app, así pasan por su pipeline real de autenticación y autorización.
    private sealed class TestRoutesFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var routes = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;
            var backoffice = routes.MapGroup("/api/backoffice").RequireAuthorization(AuthPolicies.Backoffice);
            backoffice.MapGet("/_test/ping", () => Results.Ok());
            backoffice.MapGet("/_test/admin", () => Results.Ok()).RequireAuthorization(AuthPolicies.Administrator);
            routes.MapGroup("/api/portal").RequireAuthorization(AuthPolicies.Portal)
                .MapGet("/_test/ping", () => Results.Ok());
        };
    }
}
