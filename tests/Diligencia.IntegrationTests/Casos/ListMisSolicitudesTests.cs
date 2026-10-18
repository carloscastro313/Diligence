using System.Net;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Casos;

// US3: mis solicitudes (FR-012..015, SC-005).
public class ListMisSolicitudesTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private const string Path = CreateSolicitudTests.Path;

    private async Task<Guid> CreateAsync(TestUser user, string cuit, string? razonSocial = null)
    {
        var body = CreateSolicitudTests.Body(cuit);
        if (razonSocial is not null)
            body = new
            {
                tipoTercero = "Proveedor", tipoPersona = "Juridica", cuit, razonSocial, contactoNombre = "Ana Pérez",
                contactoEmail = $"contacto{Guid.NewGuid():N}@acme.test", contactoTelefono = (string?)null,
                montoAnualEstimado = 1000m, motivo = "Servicios",
            };
        using var res = await Post(Path, user, body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("casoId").GetGuid();
    }

    private Task SqlAsync(string sql, params (string, object)[] args) => CreateSolicitudTests.RowsAsync(MigratorConnection, sql, args);

    private async Task<JsonElement[]> ListAsync(TestUser user, string query = "")
    {
        using var res = await Get(Path + query, user);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await Json(res)).EnumerateArray().ToArray();
    }

    private static JsonElement ById(JsonElement[] items, Guid id) => items.Single(i => i.GetProperty("casoId").GetGuid() == id);

    [Fact]
    public async Task ShowsCreatedAndJoined_WithEsOriginal_AndNeverOthers()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var b = await UserAsync(BackofficeRoles.Solicitante);
        var cuitShared = TestCuits.New();
        var propia = await CreateAsync(a, cuitShared);
        var deB = await CreateAsync(b, TestCuits.New());
        var deB2 = await CreateAsync(b, TestCuits.New());
        using (var join = await Post(Path, a, CreateSolicitudTests.Body(TestCuits.New()))) { Assert.Equal(HttpStatusCode.Created, join.StatusCode); }
        // A se suma al caso abierto de B por su CUIT.
        var cuitB = (await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT Cuit FROM casos.Casos WHERE Id = @i", ("@i", deB))).Single()["Cuit"]!.ToString()!;
        using (var join = await Post(Path, a, CreateSolicitudTests.Body(cuitB)))
        {
            Assert.Equal(HttpStatusCode.Created, join.StatusCode);
            Assert.Equal("SumadoACasoAbierto", (await Json(join)).GetProperty("resultado").GetString());
        }

        var mine = await ListAsync(a);

        Assert.True(ById(mine, propia).GetProperty("esOriginal").GetBoolean());
        Assert.False(ById(mine, deB).GetProperty("esOriginal").GetBoolean());
        Assert.DoesNotContain(mine, i => i.GetProperty("casoId").GetGuid() == deB2);
        Assert.Equal(3, mine.Length); // propia, otra propia y la sumada

        var theirs = await ListAsync(b);
        Assert.DoesNotContain(theirs, i => i.GetProperty("casoId").GetGuid() == propia);
        Assert.Equal(2, theirs.Length);
    }

    [Fact]
    public async Task UserWithSolicitanteAndAnalista_SeesOnlyOwn()
    {
        var other = await UserAsync(BackofficeRoles.Solicitante);
        var both = await UserAsync(BackofficeRoles.Solicitante, BackofficeRoles.Analista);
        await CreateAsync(other, TestCuits.New());
        var own = await CreateAsync(both, TestCuits.New());

        var items = await ListAsync(both);

        Assert.Equal(own, Assert.Single(items).GetProperty("casoId").GetGuid());
    }

    [Fact]
    public async Task OrderedByCreadaUtcDescending_AndJoinedUsesTheJoinDate()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var b = await UserAsync(BackofficeRoles.Solicitante);
        var deB = await CreateAsync(b, TestCuits.New());
        var propia = await CreateAsync(a, TestCuits.New());
        // Avanzar el reloj vencería el JWT de prueba: se envejece la solicitud propia por SQL.
        await SqlAsync("UPDATE casos.Solicitudes SET CreatedAtUtc = DATEADD(day, -4, CreatedAtUtc) WHERE CasoId = @i", ("@i", propia));
        var cuitB = (await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT Cuit FROM casos.Casos WHERE Id = @i", ("@i", deB))).Single()["Cuit"]!.ToString()!;
        var joinedAt = Clock.GetUtcNow().UtcDateTime;
        using (var join = await Post(Path, a, CreateSolicitudTests.Body(cuitB))) Assert.Equal(HttpStatusCode.Created, join.StatusCode);

        var items = await ListAsync(a);

        Assert.Equal([deB, propia], items.Select(i => i.GetProperty("casoId").GetGuid()));
        Assert.Equal(joinedAt, ById(items, deB).GetProperty("creadaUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FilterByEstado()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var espera = await CreateAsync(a, TestCuits.New());
        var verif = await CreateAsync(a, TestCuits.New());
        await SqlAsync("UPDATE casos.Casos SET Estado = 'EnVerificacion' WHERE Id = @i", ("@i", verif));

        var items = await ListAsync(a, "?estado=EnVerificacion");

        Assert.Equal(verif, Assert.Single(items).GetProperty("casoId").GetGuid());
        Assert.Equal("EnVerificacion", items[0].GetProperty("estado").GetString());
        Assert.Equal(2, (await ListAsync(a)).Length);
        Assert.Equal(espera, Assert.Single(await ListAsync(a, "?estado=EsperaTercero")).GetProperty("casoId").GetGuid());
    }

    [Fact]
    public async Task SearchByRazonSocial_ContainsIgnoringCase()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var hit = await CreateAsync(a, TestCuits.New(), $"Transportes {tag} SA");
        await CreateAsync(a, TestCuits.New(), "Otra Empresa SRL");

        var items = await ListAsync(a, $"?q={tag.ToUpperInvariant()[..6]}");

        Assert.Equal(hit, Assert.Single(items).GetProperty("casoId").GetGuid());
    }

    [Fact]
    public async Task SearchByCuit_WithOrWithoutHyphens()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        var hit = await CreateAsync(a, cuit);
        await CreateAsync(a, TestCuits.New());
        var formatted = $"{cuit[..2]}-{cuit[2..10]}-{cuit[10..]}";

        var plain = await ListAsync(a, $"?q={cuit}");
        var hyphens = await ListAsync(a, $"?q={formatted}");

        Assert.Equal(hit, Assert.Single(plain).GetProperty("casoId").GetGuid());
        Assert.Equal(hit, Assert.Single(hyphens).GetProperty("casoId").GetGuid());
    }

    [Theory]
    [InlineData("Aprobado")]
    [InlineData("Rechazado")]
    [InlineData("Abandonado")]
    public async Task FinalState_HasCerradoUtc_AndNoFechaEstimada(string estado)
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var id = await CreateAsync(a, TestCuits.New());
        var cerrado = new DateTime(2026, 3, 4, 15, 30, 0, DateTimeKind.Utc);
        await SqlAsync("UPDATE casos.Casos SET Estado = @e, CerradoUtc = @c WHERE Id = @i", ("@e", estado), ("@c", cerrado), ("@i", id));

        var item = Assert.Single(await ListAsync(a));

        Assert.Equal(estado, item.GetProperty("estado").GetString());
        Assert.Equal(cerrado, item.GetProperty("cerradoUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("fechaEstimada").ValueKind);
    }

    [Fact]
    public async Task OpenCase_HasFechaEstimada_AndNoCerradoUtc()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        await CreateAsync(a, TestCuits.New());

        var item = Assert.Single(await ListAsync(a));

        Assert.Equal(JsonValueKind.Null, item.GetProperty("cerradoUtc").ValueKind);
        Assert.True(DateOnly.TryParse(item.GetProperty("fechaEstimada").GetString(), out _));
    }

    [Fact]
    public async Task FailedLink_SetsEnlaceNoEntregado()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var ok = await CreateAsync(a, TestCuits.New());
        var failed = await CreateAsync(a, TestCuits.New());
        await SqlAsync("UPDATE casos.EnlacesPortal SET Entrega = 'Fallido', Intentos = 5 WHERE CasoId = @i", ("@i", failed));

        var items = await ListAsync(a);

        Assert.True(ById(items, failed).GetProperty("enlaceNoEntregado").GetBoolean());
        Assert.False(ById(items, ok).GetProperty("enlaceNoEntregado").GetBoolean());
    }

    [Fact]
    public async Task Json_HasOnlyTheMiSolicitudKeys()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        await CreateAsync(a, TestCuits.New());

        var item = Assert.Single(await ListAsync(a));

        var keys = item.EnumerateObject().Select(p => p.Name).Order().ToArray();
        string[] expected = ["casoId", "cerradoUtc", "creadaUtc", "cuit", "enlaceNoEntregado", "esOriginal", "estado", "fechaEstimada", "razonSocial", "tipoTercero"];
        Assert.Equal(expected, keys);
    }

    [Fact]
    public async Task InvalidEstado_Returns400_WithEstadoError()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get(Path + "?estado=Inventado", a);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await Json(res)).GetProperty("errors").TryGetProperty("estado", out _));
    }

    [Fact]
    public async Task QLongerThan200_Returns400_WithQError()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get(Path + "?q=" + new string('a', 201), a);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await Json(res)).GetProperty("errors").TryGetProperty("q", out _));
    }
}
