extern alias worker;
using Diligencia.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Diligencia.IntegrationTests.Api;

// FR-006: sin la cadena de conexión el proceso no arranca y el mensaje nombra la clave, nunca un valor.
public class MissingConfigurationTests
{
    private const string Expected = "Falta la configuración obligatoria 'ConnectionStrings:diligencia'.";

    private sealed class ApiWithoutConnection : ApiFactory
    {
        protected override string? ConnectionString => null;
    }

    private sealed class WorkerWithoutConnection : WebApplicationFactory<worker::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { }
    }

    [Fact]
    public void Api_WithoutConnectionString_FailsAtStartup()
    {
        using var factory = new ApiWithoutConnection();
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Equal(Expected, ex.Message);
    }

    [Fact]
    public void Worker_WithoutConnectionString_FailsAtStartup()
    {
        using var factory = new WorkerWithoutConnection();
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Equal(Expected, ex.Message);
    }
}
