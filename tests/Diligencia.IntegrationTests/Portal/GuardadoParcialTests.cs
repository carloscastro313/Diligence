using System.Net;
using System.Text;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Portal;

// US4-1/2/4: guardado parcial del formulario (FR-019, FR-021, SC-007).
public class GuardadoParcialTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private static readonly string[] DocumentosJuridica = ["Estatuto", "ConstanciaArca", "ActaAutoridades", "DocumentoRepresentante"];

    private Task<HttpResponseMessage> Guardar(string cookie, object datos, string? version = null) =>
        Portal(HttpMethod.Put, "/caso/formulario", cookie, new { datos, version });

    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        using (res)
        {
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return await Body(res);
        }
    }

    private async Task<JsonElement> Formulario(string cookie) => await Ok(await PortalGet("/caso/formulario", cookie));

    private static object Domicilio => new
    {
        calle = "Av. Siempreviva", numero = "742", localidad = "CABA", provincia = "Buenos Aires", codigoPostal = "C1000", pais = "AR",
    };

    private static object PepNo => new { esPep = false };

    private static object FormularioCompletoJuridica => new
    {
        condicionIva = "ResponsableInscripto",
        fechaConstitucion = "2010-01-15",
        numeroInscripcion = "IGJ-12345",
        domicilioFiscal = Domicilio,
        domicilioLegalIgualFiscal = true,
        actividad = new { descripcion = "Servicios de consultoría", pais = "AR" },
        beneficiarios = new[] { new { nombre = "Juan Pérez", documento = "20123456", nacionalidad = "AR", porcentaje = 100m, pep = PepNo } },
        representantes = new[] { new { nombre = "Ana Gómez", documento = "27123456", pep = PepNo } },
        origenFondos = "Ingresos por ventas de servicios profesionales",
    };

    private async Task SubirDocumentosAsync(string cookie)
    {
        foreach (var tipo in DocumentosJuridica)
            using (var r = await Portal(HttpMethod.Put, $"/caso/documentos/{tipo}", cookie, File(SampleFiles.Pdf())))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("""{ "origenFondos": "Ventas de servicios" }""")]
    [InlineData("{}")]
    public async Task Save_WithEmptyRequiredFields_Returns200_WithVersionAndTimestamp(string datosJson)
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using var datos = JsonDocument.Parse(datosJson);
        var json = await Ok(await Guardar(cookie, datos.RootElement));

        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        Assert.True(json.GetProperty("guardadoUtc").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        Assert.False(json.GetProperty("sobrescrito").GetBoolean());
        var form = await Formulario(cookie);
        Assert.Equal("Borrador", form.GetProperty("estado").GetString());
        Assert.Equal(json.GetProperty("version").GetString(), form.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AnotherSession_SeesTheSameData_AndTheUploadedDocuments()
    {
        var caso = await SeedAsync();
        var first = await SignInAsync(caso);
        using (var r = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", first, File(SampleFiles.Pdf(), "estatuto.pdf")))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        (await Guardar(first, new { origenFondos = "Ventas de servicios", numeroInscripcion = "IGJ-1" })).Dispose();

        var second = await SignInAsync(caso);

        Assert.NotEqual(first, second);
        var datos = (await Formulario(second)).GetProperty("datos");
        Assert.Equal("Ventas de servicios", datos.GetProperty("origenFondos").GetString());
        Assert.Equal("IGJ-1", datos.GetProperty("numeroInscripcion").GetString());
        var docs = (await Body(await PortalGet("/caso/documentos", second))).GetProperty("documentos").EnumerateArray();
        var estatuto = docs.Single(d => d.GetProperty("tipo").GetString() == "Estatuto");
        Assert.Equal("estatuto.pdf", estatuto.GetProperty("cargado").GetProperty("nombreOriginal").GetString());
    }

    [Fact]
    public async Task StaleVersion_Returns200_WithSobrescrito_AndTheLastSaveWins()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var v1 = (await Ok(await Guardar(cookie, new { origenFondos = "Primera versión del texto" }))).GetProperty("version").GetString();
        var second = await Ok(await Guardar(cookie, new { origenFondos = "Segunda versión del texto" }, v1));
        Assert.False(second.GetProperty("sobrescrito").GetBoolean()); // v1 era la vigente

        var third = await Ok(await Guardar(cookie, new { origenFondos = "Tercera versión del texto" }, v1)); // v1 ya es vieja

        Assert.True(third.GetProperty("sobrescrito").GetBoolean());
        var form = await Formulario(cookie);
        Assert.Equal("Tercera versión del texto", form.GetProperty("datos").GetProperty("origenFondos").GetString());
        Assert.Equal(third.GetProperty("version").GetString(), form.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Save_AfterSubmission_Returns409_AndKeepsTheSubmittedData()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        await SubirDocumentosAsync(cookie);
        using (var envio = await Portal(HttpMethod.Post, "/caso/formulario/envio", cookie, new { datos = FormularioCompletoJuridica }))
            Assert.Equal(HttpStatusCode.OK, envio.StatusCode);

        using var res = await Guardar(cookie, new { origenFondos = "Intento de cambio posterior al envío" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var form = await Formulario(cookie);
        Assert.Equal("Enviado", form.GetProperty("estado").GetString());
        Assert.Equal("Ingresos por ventas de servicios profesionales", form.GetProperty("datos").GetProperty("origenFondos").GetString());
    }

    [Fact]
    public async Task ExpiredLink_AfterSaving_CannotBeUsedToSignInAgain()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        (await Guardar(cookie, new { origenFondos = "Ventas de servicios" })).Dispose();

        Clock.Advance(TimeSpan.FromDays(22)); // el enlace vence a los 21

        using var res = await RequestCodeAsync(caso.Token);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Save_AuditsOneUpdatedEvent_WithPathOldAndNewValue_AndNothingWhenUnchanged()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var id = caso.CasoId.ToString();

        (await Guardar(cookie, new { origenFondos = "Texto original del origen" })).Dispose();
        var created = Assert.Single(await AuditAsync("FormularioTercero", id, "Updated"));
        Assert.Contains("origenFondos", created.GetProperty("newValues").GetString());
        Assert.Contains("Texto original del origen", created.GetProperty("newValues").GetString());
        Assert.Equal($"tercero:{caso.TerceroId}", created.GetProperty("actorId").GetString());

        (await Guardar(cookie, new { origenFondos = "Texto modificado del origen", numeroInscripcion = "IGJ-9" })).Dispose();
        var events = await AuditAsync("FormularioTercero", id, "Updated");
        Assert.Equal(2, events.Count);
        var (old, @new) = (events[1].GetProperty("oldValues").GetString()!, events[1].GetProperty("newValues").GetString()!);
        Assert.Contains("origenFondos", old);
        Assert.Contains("Texto original del origen", old);
        Assert.Contains("origenFondos", @new);
        Assert.Contains("Texto modificado del origen", @new);
        Assert.Contains("numeroInscripcion", @new);
        Assert.Contains("IGJ-9", @new);

        (await Guardar(cookie, new { origenFondos = "Texto modificado del origen", numeroInscripcion = "IGJ-9" })).Dispose();
        Assert.Equal(2, (await AuditAsync("FormularioTercero", id, "Updated")).Count);
    }

    [Fact]
    public async Task Save_OnlyAffectsTheCasoOfTheSession()
    {
        var one = await SeedAsync();
        var two = await SeedAsync();
        var cookieOne = await SignInAsync(one);
        var cookieTwo = await SignInAsync(two);
        (await Guardar(cookieOne, new { origenFondos = "Datos del primer tercero" })).Dispose();

        (await Guardar(cookieTwo, new { origenFondos = "Datos del segundo tercero" })).Dispose();

        Assert.Equal("Datos del primer tercero", (await Formulario(cookieOne)).GetProperty("datos").GetProperty("origenFondos").GetString());
        Assert.Equal("Datos del segundo tercero", (await Formulario(cookieTwo)).GetProperty("datos").GetProperty("origenFondos").GetString());
        Assert.Single(await AuditAsync("FormularioTercero", one.CasoId.ToString(), "Updated"));
    }

    [Theory]
    [InlineData("""{ "datos": { "condicionIva": "Inexistente" } }""")]
    [InlineData("""{ "datos": { "fechaConstitucion": "31/12/2010" } }""")]
    [InlineData("""{ "datos": { "fechaConstitucion": "2010-02-30" } }""")]
    [InlineData("""{ "datos": "no es un objeto" }""")]
    public async Task InvalidEnumOrDate_Returns400(string body)
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using var res = await Portal(HttpMethod.Put, "/caso/formulario", cookie, new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Borrador", (await Formulario(cookie)).GetProperty("estado").GetString());
    }

    [Theory]
    [InlineData("beneficiarios[0].porcentaje")]
    [InlineData("actividad.pais")]
    [InlineData("beneficiarios")]
    public async Task InvalidShape_Returns400_WithTheFieldPathWithoutDatosPrefix(string path)
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        object datos = path switch
        {
            "beneficiarios[0].porcentaje" => new { beneficiarios = new[] { new { nombre = "Juan", porcentaje = 150m } } },
            "actividad.pais" => new { actividad = new { pais = "XX" } },
            _ => new { beneficiarios = new[] { new { nombre = "A", porcentaje = 60m }, new { nombre = "B", porcentaje = 60m } } },
        };

        using var res = await Guardar(cookie, datos);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await Body(res)).GetProperty("errors");
        Assert.True(errors.TryGetProperty(path, out var messages), $"falta '{path}' en: {errors}");
        Assert.NotEmpty(messages.EnumerateArray());
        Assert.DoesNotContain(errors.EnumerateObject(), e => e.Name.StartsWith("datos.", StringComparison.OrdinalIgnoreCase));
    }
}
