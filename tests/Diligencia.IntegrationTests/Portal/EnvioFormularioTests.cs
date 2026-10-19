using System.Net;
using System.Text.Json;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.IntegrationTests.Portal;

// US2-1..7: envío del formulario (FR-006, FR-011, FR-012, FR-021, SC-004/005).
public class EnvioFormularioTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private static readonly string[] ObligatoriosJuridica = ["Estatuto", "ConstanciaArca", "ActaAutoridades", "DocumentoRepresentante"];
    private static readonly string[] ObligatoriosHumana = ["DniFrente", "DniDorso", "ConstanciaArca"];

    private static object Domicilio() => new
    {
        calle = "Av. Siempre Viva", numero = "742", localidad = "CABA", provincia = "Buenos Aires", codigoPostal = "1000", pais = "AR",
    };

    private static object Pep() => new { esPep = false };

    private static object Juridica() => new
    {
        condicionIva = "ResponsableInscripto",
        fechaConstitucion = "2015-03-10",
        numeroInscripcion = "IGJ-12345",
        domicilioFiscal = Domicilio(),
        domicilioLegalIgualFiscal = true,
        actividad = new { descripcion = "Servicios de consultoría", pais = "AR" },
        beneficiarios = new object[]
        {
            new { nombre = "Ana Gómez", documento = "20111222", nacionalidad = "AR", porcentaje = 60, pep = Pep() },
            new { nombre = "Luis Pérez", documento = "20333444", nacionalidad = "AR", porcentaje = 40, pep = Pep() },
        },
        representantes = new object[] { new { nombre = "Marta Ruiz", documento = "20555666", pep = Pep() } },
        origenFondos = "Ventas de servicios profesionales",
    };

    private static object Humana() => new
    {
        condicionIva = "Monotributo",
        fechaNacimiento = "1985-06-15",
        nacionalidad = "AR",
        documento = new { tipo = "DNI", numero = "30111222" },
        domicilioFiscal = Domicilio(),
        actividad = new { descripcion = "Desarrollo de software", pais = "AR" },
        pep = Pep(),
        origenFondos = "Honorarios por servicios",
    };

    private Task<HttpResponseMessage> Envio(string cookie, object datos) =>
        Portal(HttpMethod.Post, "/caso/formulario/envio", cookie, new { datos });

    private async Task SubirAsync(string cookie, params string[] tipos)
    {
        foreach (var tipo in tipos)
        {
            using var res = await Portal(HttpMethod.Put, $"/caso/documentos/{tipo}", cookie, File(SampleFiles.Pdf()));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }

    private async Task<(SeededCaso Caso, string Cookie)> JuridicaConDocumentosAsync()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        await SubirAsync(cookie, ObligatoriosJuridica);
        return (caso, cookie);
    }

    private async Task<Dictionary<string, object?>> CasoRow(Guid casoId) =>
        Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT * FROM casos.Casos WHERE Id = @i", ("@i", casoId)));

    private Task<List<Dictionary<string, object?>>> FormularioRows(Guid casoId) =>
        CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT * FROM casos.FormulariosTercero WHERE CasoId = @i", ("@i", casoId));

    private async Task<List<string>> VigentesAsync(Guid casoId) =>
        [.. (await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Tipo FROM documentos.Documentos WHERE CasoId = @c AND BajaUtc IS NULL ORDER BY Tipo", ("@c", casoId))).Select(r => (string)r["Tipo"]!)];

    [Fact]
    public async Task Get_WithoutSavedData_ReturnsEmptyBorrador_WithoutVersion()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await PortalGet("/caso/formulario", cookie);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Body(res);
        Assert.Equal("Borrador", json.GetProperty("estado").GetString());
        Assert.True(!json.TryGetProperty("version", out var version) || version.ValueKind == JsonValueKind.Null); // los nulos se omiten
        Assert.Empty(json.GetProperty("datos").EnumerateObject());
    }

    [Fact]
    public async Task Envio_CompleteJuridica_Returns200_MovesTheCaseToEnVerificacion_AndLocksTheForm()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();
        var before = Clock.GetUtcNow().UtcDateTime;
        Clock.Advance(TimeSpan.FromMinutes(3));
        var now = Clock.GetUtcNow().UtcDateTime;

        using var res = await Envio(cookie, Juridica());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Body(res);
        Assert.Equal("Enviado", json.GetProperty("estado").GetString());
        Assert.Equal("ResponsableInscripto", json.GetProperty("datos").GetProperty("condicionIva").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("enviadoUtc").ValueKind);

        var row = await CasoRow(caso.CasoId);
        Assert.Equal("EnVerificacion", row["Estado"]);
        Assert.Equal(now, (DateTime)row["EstadoDesdeUtc"]!, TimeSpan.FromSeconds(1));
        Assert.True((DateTime)row["EstadoDesdeUtc"]! > before);

        var form = Assert.Single(await FormularioRows(caso.CasoId));
        Assert.Equal("Enviado", form["Estado"]);
        Assert.Equal($"tercero:{caso.TerceroId}", form["EnviadoPor"]);
        Assert.Equal(now, (DateTime)form["EnviadoUtc"]!, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Envio_CompleteHumana_Returns200_AndTheCaseIsEnVerificacion()
    {
        var caso = await SeedAsync(TipoPersona.Humana);
        var cookie = await SignInAsync(caso);
        await SubirAsync(cookie, ObligatoriosHumana);

        using var res = await Envio(cookie, Humana());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("EnVerificacion", (await CasoRow(caso.CasoId))["Estado"]);
        var form = Assert.Single(await FormularioRows(caso.CasoId));
        Assert.Equal("Enviado", form["Estado"]);
        Assert.Equal($"tercero:{caso.TerceroId}", form["EnviadoPor"]);
    }

    [Fact]
    public async Task Envio_WithAMissingRequiredDocument_Returns400_AndTheCaseDoesNotChange()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        await SubirAsync(cookie, ObligatoriosJuridica[1..]); // falta Estatuto

        using var res = await Envio(cookie, Juridica());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await Body(res)).GetProperty("errors");
        Assert.Contains("Estatuto", errors.GetProperty("documentos").EnumerateArray().Single().GetString());
        Assert.Equal("EsperaTercero", (await CasoRow(caso.CasoId))["Estado"]);
        Assert.Empty(await FormularioRows(caso.CasoId));
    }

    [Fact]
    public async Task Envio_WithInvalidData_Returns400_WithNestedCamelCasePaths()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();
        var datos = new
        {
            condicionIva = "ResponsableInscripto",
            fechaConstitucion = "2015-03-10",
            numeroInscripcion = "IGJ-12345",
            domicilioFiscal = new { numero = "742", localidad = "CABA", provincia = "Buenos Aires", codigoPostal = "1000", pais = "AR" }, // sin calle
            domicilioLegalIgualFiscal = true,
            actividad = new { descripcion = "Servicios", pais = "AR" },
            beneficiarios = new object[]
            {
                new { nombre = "Ana", documento = "1", nacionalidad = "AR", porcentaje = 60, pep = Pep() },
                new { nombre = "Luis", documento = "2", nacionalidad = "AR", porcentaje = 0, pep = Pep() },
            },
            representantes = new object[] { new { nombre = "Marta", documento = "3", pep = Pep() } },
            origenFondos = "Ventas de servicios",
        };

        using var res = await Envio(cookie, datos);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await Body(res)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("domicilioFiscal.calle", out _));
        Assert.True(errors.TryGetProperty("beneficiarios[1].porcentaje", out _));
        Assert.Equal("EsperaTercero", (await CasoRow(caso.CasoId))["Estado"]);
    }

    [Fact]
    public async Task AfterTheSubmission_EverythingIsLocked_ButReadsStillWork()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();
        using (var ok = await Envio(cookie, Juridica())) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using (var r = await Portal(HttpMethod.Put, "/caso/documentos/UltimoBalance", cookie, File(SampleFiles.Pdf()))) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(SampleFiles.Pdf()))) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await Envio(cookie, Juridica())) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await Portal(HttpMethod.Put, "/caso/formulario", cookie, new { datos = Juridica() })) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        using var form = await PortalGet("/caso/formulario", cookie);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal("Enviado", (await Body(form)).GetProperty("estado").GetString());

        using var casoRes = await PortalGet("/caso", cookie);
        Assert.Equal(HttpStatusCode.OK, casoRes.StatusCode);
        var json = await Body(casoRes);
        Assert.False(json.GetProperty("editable").GetBoolean());
        Assert.Empty(json.GetProperty("pendientes").EnumerateArray());

        using var code = await RequestCodeAsync(caso.Token);
        Assert.Equal(HttpStatusCode.NotFound, code.StatusCode);
        Assert.Equal(ObligatoriosJuridica.Order(), await VigentesAsync(caso.CasoId));
    }

    [Fact]
    public async Task Concurrent_SubmissionAndOptionalUpload_NeverBothSucceedAfterTheSubmission()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();

        var envio = Envio(cookie, Juridica());
        var carga = Portal(HttpMethod.Put, "/caso/documentos/UltimoBalance", cookie, File(SampleFiles.Pdf()));
        await Task.WhenAll(envio, carga);
        using var envioRes = await envio;
        using var cargaRes = await carga;

        Assert.Equal(HttpStatusCode.OK, envioRes.StatusCode);
        Assert.Contains(cargaRes.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        // O el balance entró antes del envío, o la carga recibió 409 y no queda nada.
        var vigentes = await VigentesAsync(caso.CasoId);
        Assert.Equal(cargaRes.StatusCode == HttpStatusCode.OK, vigentes.Contains("UltimoBalance"));
        Assert.Equal("EnVerificacion", (await CasoRow(caso.CasoId))["Estado"]);
    }

    [Fact]
    public async Task Concurrent_SubmissionAndRequiredDocumentDelete_KeepTheRequiredDocumentsOfTheSubmission()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();

        var envio = Envio(cookie, Juridica());
        var baja = Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie);
        await Task.WhenAll(envio, baja);
        using var envioRes = await envio;
        using var bajaRes = await baja;

        var estado = (string)(await CasoRow(caso.CasoId))["Estado"]!;
        var vigentes = await VigentesAsync(caso.CasoId);
        if (estado == "EnVerificacion")
        {
            Assert.Equal(HttpStatusCode.OK, envioRes.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, bajaRes.StatusCode);
            Assert.Equal(ObligatoriosJuridica.Order(), vigentes);
        }
        else
        {
            Assert.Equal("EsperaTercero", estado);
            Assert.Equal(HttpStatusCode.BadRequest, envioRes.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, bajaRes.StatusCode);
            Assert.DoesNotContain("Estatuto", vigentes);
        }
    }

    [Fact]
    public async Task Envio_IsAudited_OnTheFormAndOnTheCase_WithTheThirdPartyAsActor()
    {
        var (caso, cookie) = await JuridicaConDocumentosAsync();
        using (var ok = await Envio(cookie, Juridica())) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var actor = $"tercero:{caso.TerceroId}";

        var form = Assert.Single(await AuditAsync("FormularioTercero", caso.CasoId.ToString(), "Updated"));
        Assert.Equal(actor, form.GetProperty("actorId").GetString());
        Assert.Contains("Enviado", form.GetProperty("newValues").GetString());

        var casoEvent = Assert.Single(await AuditAsync("Caso", caso.CasoId.ToString(), "Updated"));
        Assert.Equal(actor, casoEvent.GetProperty("actorId").GetString());
        Assert.Contains("EsperaTercero", casoEvent.GetProperty("oldValues").GetString());
        Assert.Contains("EnVerificacion", casoEvent.GetProperty("newValues").GetString());
    }

    [Fact]
    public async Task AnotherThirdParty_CannotReadNorSubmitTheFormOfTheFirst()
    {
        var (primero, cookie1) = await JuridicaConDocumentosAsync();
        using (var save = await Portal(HttpMethod.Put, "/caso/formulario", cookie1, new { datos = new { origenFondos = "Solo del primero" } }))
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var segundo = await SeedAsync();
        var cookie2 = await SignInAsync(segundo);

        using (var get = await PortalGet("/caso/formulario", cookie2))
        {
            var json = await Body(get);
            Assert.Equal("Borrador", json.GetProperty("estado").GetString());
            Assert.Empty(json.GetProperty("datos").EnumerateObject());
        }

        // Su envío solo afecta a SU caso: sin documentos subidos, falla por documentos y no toca el del primero.
        using (var envio = await Envio(cookie2, Juridica()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, envio.StatusCode);
            Assert.True((await Body(envio)).GetProperty("errors").TryGetProperty("documentos", out _));
        }
        Assert.Equal("EsperaTercero", (await CasoRow(primero.CasoId))["Estado"]);
        Assert.Equal("EsperaTercero", (await CasoRow(segundo.CasoId))["Estado"]);
        Assert.Equal("Borrador", Assert.Single(await FormularioRows(primero.CasoId))["Estado"]);

        await SubirAsync(cookie2, ObligatoriosJuridica);
        using (var envio = await Envio(cookie2, Juridica())) Assert.Equal(HttpStatusCode.OK, envio.StatusCode);
        Assert.Equal("EnVerificacion", (await CasoRow(segundo.CasoId))["Estado"]);
        Assert.Equal("EsperaTercero", (await CasoRow(primero.CasoId))["Estado"]);
    }

    [Fact]
    public async Task GetCaso_AfterUploadingTheDocuments_ListsOnlyTheForm_AsPending()
    {
        var (_, cookie) = await JuridicaConDocumentosAsync();

        using var res = await PortalGet("/caso", cookie);

        var json = await Body(res);
        Assert.True(json.GetProperty("editable").GetBoolean());
        Assert.Equal(["Completar el formulario"], json.GetProperty("pendientes").EnumerateArray().Select(p => p.GetString()));
    }
}
