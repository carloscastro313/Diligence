extern alias worker;
using Diligencia.MigrationService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Api;

// Spec 004 T061: los handlers de Casos (IPortalSignIn, IPortalActor, IDocumentStore, IEmailTransport) deben resolverse en
// worker y MigrationService con ValidateOnBuild, sin claves JWT ni Documentos:EncryptionKey (lectura perezosa).
public class HostsValidateOnBuildTests
{
    private const string Conn = "Server=127.0.0.1,1;Database=unused;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=true";
    private const string ValidationError = "Error while validating the service descriptor";

    private sealed class ValidatedWorker : WebApplicationFactory<worker::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:diligencia", Conn);
            builder.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
        }
    }

    [Fact]
    public void Worker_BuildsWithValidateOnBuild_WithoutJwtNorEncryptionKey()
    {
        using var factory = new ValidatedWorker();
        // Services construye y arranca el host; ValidateOnBuild lanza aquí si falta alguna dependencia.
        Assert.NotNull(factory.Services);
    }

    [Fact]
    public async Task MigrationService_BuildsWithValidateOnBuild_WithoutJwtNorEncryptionKey()
    {
        // `--environment=Development` activa ValidateOnBuild/ValidateScopes por defecto. Sin base real, tras construir el
        // host falla la conexión: lo único que se descarta aquí es un error de resolución de dependencias.
        var ex = await Record.ExceptionAsync(() => MigrationEntryPoint.RunAsync(["--environment=Development"],
            new Dictionary<string, string?> { ["ConnectionStrings:diligencia"] = Conn }));
        Assert.DoesNotContain(ValidationError, ex?.ToString() ?? "");
    }
}
