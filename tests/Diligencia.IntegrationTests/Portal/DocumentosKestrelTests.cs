using System.Net;
using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Portal;

// TestServer ignora [RequestSizeLimit]: este test corre sobre Kestrel real para comprobar que el máximo configurable
// (hasta 50 MB) no choca con el límite por defecto de 30 MB del servidor (FR-014, research R10).
public class DocumentosKestrelTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private const long Mb = 1024 * 1024;

    private sealed class KestrelFactory(string connectionString) : DbApiFactory(connectionString)
    {
        public KestrelFactory Start()
        {
            UseKestrel(0);
            StartServer();
            return this;
        }
    }

    public override async ValueTask InitializeAsync()
    {
        var db = await Sql.CreateDatabaseAsync();
        RuntimeConnection = db.Runtime;
        MigratorConnection = db.Migrator;
        await base.InitializeAsync();
        // Con Kestrel real el cliente por defecto apunta al puerto del servidor (CreateClient usa localhost:80).
        Client.Dispose();
        Client = Factory.CreateDefaultClient();
        Client.Timeout = TimeSpan.FromMinutes(2);
        await SqlServerFixture.ExecuteAsync(MigratorConnection, $"UPDATE documentos.Configuracion SET TamanoMaximoBytes = {50 * Mb}");
    }

    protected override DbApiFactory CreateFactory() => new KestrelFactory(RuntimeConnection).Start();

    [Fact]
    public async Task FortyFiveMegabytes_AreAccepted_AndMoreThanTheMaximumGets413_OnRealKestrel()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using (var ok = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(SampleFiles.Grande(45 * Mb))))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var tooBig = await Portal(HttpMethod.Put, "/caso/documentos/ConstanciaArca", cookie, File(SampleFiles.Grande(51 * Mb)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Contains("50 MB", (await Body(tooBig)).GetProperty("title").GetString());
    }
}
