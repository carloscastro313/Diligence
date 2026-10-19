using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Documentos;

// US3-6 / FR-014: el tamaño máximo es una fila compartida por todo el sistema; la clase usa su propia base.
public class ConfiguracionTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Path = "/api/backoffice/documentos/configuracion";

    public override async ValueTask InitializeAsync()
    {
        var db = await Sql.CreateDatabaseAsync();
        RuntimeConnection = db.Runtime;
        MigratorConnection = db.Migrator;
        await base.InitializeAsync();
    }

    private async Task<int> CurrentAsync(TestUser admin)
    {
        using var res = await Get(Path, admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await Json(res)).GetProperty("tamanoMaximoMb").GetInt32();
    }

    [Fact]
    public async Task Default_Is10Mb()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);

        Assert.Equal(10, await CurrentAsync(admin));
    }

    [Fact]
    public async Task Put_ChangesTheValue_AndAudits()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);

        using (var put = await Put(Path, admin, new { tamanoMaximoMb = 2 }))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.Equal(2, (await Json(put)).GetProperty("tamanoMaximoMb").GetInt32());
        }

        Assert.Equal(2, await CurrentAsync(admin));
        var ev = Assert.Single(await AuditAsync("ConfiguracionDocumentos", action: "Updated"));
        Assert.Equal(admin.Id.ToString(), ev.GetProperty("actorId").GetString());
        Assert.Contains("TamanoMaximoMb", ev.GetProperty("newValues").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(null)]
    public async Task OutOfRangeOrMissing_Is400WithErrors_AndKeepsTheValue(int? mb)
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);

        using var res = await Put(Path, admin, mb is null ? new { } : new { tamanoMaximoMb = mb });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await Json(res)).GetProperty("errors").TryGetProperty("tamanoMaximoMb", out _));
        Assert.Equal(10, await CurrentAsync(admin));
        Assert.Empty(await AuditAsync("ConfiguracionDocumentos"));
    }

    [Fact]
    public async Task NonAdministrator_Gets403_AndAnonymousGets401()
    {
        var analista = await UserAsync(BackofficeRoles.Analista);

        using (var get = await Get(Path, analista)) Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        using (var put = await Put(Path, analista, new { tamanoMaximoMb = 2 })) Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        using (var get = await Get(Path)) Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using (var put = await Put(Path, null, new { tamanoMaximoMb = 2 })) Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }
}
