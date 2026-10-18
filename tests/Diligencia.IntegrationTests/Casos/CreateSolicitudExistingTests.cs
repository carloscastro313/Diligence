using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Casos;

// US2: solicitud sobre un tercero que ya existe (FR-006, FR-007).
public class CreateSolicitudExistingTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private static string Path => CreateSolicitudTests.Path;

    [Fact]
    public async Task SecondRequester_OnOpenCase_JoinsIt_WithoutNewJob()
    {
        var first = await UserAsync(BackofficeRoles.Solicitante);
        var second = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        using var r1 = await Post(Path, first, CreateSolicitudTests.Body(cuit));
        var casoId = (await Json(r1)).GetProperty("casoId").GetGuid();

        using var r2 = await Post(Path, second, new
        {
            tipoTercero = "Proveedor", tipoPersona = "Juridica", cuit, razonSocial = "Otra razon",
            contactoNombre = "Beto", contactoEmail = $"segundo{Guid.NewGuid():N}@acme.test", montoAnualEstimado = 777.25m, motivo = "Motivo del segundo",
        });

        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var json = await Json(r2);
        Assert.Equal("SumadoACasoAbierto", json.GetProperty("resultado").GetString());
        Assert.Equal(casoId, json.GetProperty("casoId").GetGuid());

        Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT 1 AS X FROM casos.Casos WHERE Cuit = @c", ("@c", cuit)));
        var sols = await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT * FROM casos.Solicitudes WHERE CasoId = @i ORDER BY EsOriginal DESC", ("@i", casoId));
        Assert.Equal(2, sols.Count);
        var added = sols[1];
        Assert.False((bool)added["EsOriginal"]!);
        Assert.Equal(second.Id.ToString(), added["SolicitanteId"]);
        Assert.Equal("Motivo del segundo", added["Motivo"]);
        Assert.Equal(777.25m, (decimal)added["MontoAnualEstimado"]!);

        var audit = Assert.Single(await AuditAsync("Solicitud", added["Id"]!.ToString()));
        Assert.Equal("Created", audit.GetProperty("action").GetString());
        Assert.Equal(second.Id.ToString(), audit.GetProperty("actorId").GetString());

        Assert.Single(Jobs.Jobs); // solo el del primer alta
    }

    [Fact]
    public async Task SameUserAgain_Returns409()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        (await Post(Path, user, CreateSolicitudTests.Body(cuit))).Dispose();

        using var res = await Post(Path, user, CreateSolicitudTests.Body(cuit));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT 1 AS X FROM casos.Solicitudes s JOIN casos.Casos c ON c.Id = s.CasoId WHERE c.Cuit = @c", ("@c", cuit)));
        Assert.Single(Jobs.Jobs);
    }

    [Theory]
    [InlineData("Activo")]
    [InlineData("Bloqueado")]
    public async Task ActiveOrBlockedTercero_Returns409(string estado)
    {
        var first = await UserAsync(BackofficeRoles.Solicitante);
        var second = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        (await Post(Path, first, CreateSolicitudTests.Body(cuit))).Dispose();
        await CheckCuitTests.ExecAsync(MigratorConnection, "UPDATE terceros.Terceros SET Estado = @e WHERE Cuit = @c", ("@e", estado), ("@c", cuit));

        using var res = await Post(Path, second, CreateSolicitudTests.Body(cuit));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT 1 AS X FROM casos.Casos WHERE Cuit = @c", ("@c", cuit)));
    }

    [Theory]
    [InlineData("Abandonado")]
    [InlineData("Rechazado")]
    public async Task LastCaseClosedWithoutApproval_OpensNewCase_OnSameTercero(string estadoCaso)
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        using var r1 = await Post(Path, user, CreateSolicitudTests.Body(cuit));
        var oldCaso = (await Json(r1)).GetProperty("casoId").GetGuid();
        await CheckCuitTests.ExecAsync(MigratorConnection, "UPDATE casos.Casos SET Estado = @e WHERE Id = @i", ("@e", estadoCaso), ("@i", oldCaso));

        using var r2 = await Post(Path, user, CreateSolicitudTests.Body(cuit));

        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var json = await Json(r2);
        Assert.Equal("CasoAbierto", json.GetProperty("resultado").GetString());
        var newCaso = json.GetProperty("casoId").GetGuid();
        Assert.NotEqual(oldCaso, newCaso);

        var casos = await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT * FROM casos.Casos WHERE Cuit = @c", ("@c", cuit));
        Assert.Equal(2, casos.Count);
        Assert.Single(casos.Select(c => c["TerceroId"]).Distinct());
        Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT 1 AS X FROM terceros.Terceros WHERE Cuit = @c", ("@c", cuit)));
        Assert.Equal("EsperaTercero", casos.Single(c => (Guid)c["Id"]! == newCaso)["Estado"]);
        Assert.Equal(2, Jobs.Jobs.Count);
    }
}
