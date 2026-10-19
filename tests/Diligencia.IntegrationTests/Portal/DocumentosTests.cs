using System.Net;
using System.Text.Json;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.IntegrationTests.Portal;

// US3-1..6: documentos del portal (FR-013..017, SC-005/006). El máximo es una fila compartida: los tests que lo
// cambian viven en DocumentosMaximoTests, con base propia.
public class DocumentosTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private const long Mb = 1024 * 1024;

    private async Task<HttpResponseMessage> PutAsync(string cookie, string tipo, byte[] bytes, string nombre = "archivo.pdf") =>
        await Portal(HttpMethod.Put, $"/caso/documentos/{tipo}", cookie, File(bytes, nombre));

    private Task<List<Dictionary<string, object?>>> RowsAsync(Guid casoId, string tipo) =>
        CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT * FROM documentos.Documentos WHERE CasoId = @c AND Tipo = @t ORDER BY CargadoUtc, BajaUtc", ("@c", casoId), ("@t", tipo));

    private static async Task<string> TitleAsync(HttpResponseMessage res) => (await Body(res)).GetProperty("title").GetString()!;

    [Theory]
    [InlineData(TipoPersona.Juridica, "Estatuto,ConstanciaArca,ActaAutoridades,DocumentoRepresentante,UltimoBalance", "true,true,true,true,false")]
    [InlineData(TipoPersona.Humana, "DniFrente,DniDorso,ConstanciaArca,ConstanciaDomicilio", "true,true,true,false")]
    public async Task List_ReturnsTheRequiredDocuments_OfThePersonType(TipoPersona persona, string tipos, string obligatorios)
    {
        var caso = await SeedAsync(persona);
        var cookie = await SignInAsync(caso);

        using var res = await PortalGet("/caso/documentos", cookie);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Body(res);
        Assert.Equal(10 * Mb, json.GetProperty("tamanoMaximoBytes").GetInt64());
        Assert.Equal(["PDF", "JPG", "PNG"], json.GetProperty("formatos").EnumerateArray().Select(f => f.GetString()));
        var docs = json.GetProperty("documentos").EnumerateArray().ToList();
        Assert.Equal(tipos.Split(','), docs.Select(d => d.GetProperty("tipo").GetString()));
        Assert.Equal(obligatorios.Split(',').Select(bool.Parse), docs.Select(d => d.GetProperty("obligatorio").GetBoolean()));
        Assert.All(docs, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.GetProperty("nombre").GetString()));
            Assert.Equal(JsonValueKind.Null, d.GetProperty("cargado").ValueKind);
        });
    }

    [Theory]
    [InlineData("application/pdf", "contrato.pdf")]
    [InlineData("image/jpeg", "frente.jpg")]
    [InlineData("image/png", "frente.png")]
    public async Task Upload_ValidFile_Returns200_WithDetectedType(string contentType, string nombre)
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var bytes = contentType switch { "application/pdf" => SampleFiles.Pdf(), "image/jpeg" => SampleFiles.Jpeg(), _ => SampleFiles.Png() };

        using var res = await PutAsync(cookie, "Estatuto", bytes, nombre);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Body(res);
        Assert.Equal(contentType, json.GetProperty("contentType").GetString());
        Assert.Equal(nombre, json.GetProperty("nombreOriginal").GetString());
        Assert.Equal(bytes.Length, json.GetProperty("tamanoBytes").GetInt64());
        Assert.True(json.GetProperty("cargadoUtc").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));
        // El tipo lo decide el contenido, no la extensión ni el Content-Type declarado.
        var list = await Body(await PortalGet("/caso/documentos", cookie));
        var estatuto = list.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("tipo").GetString() == "Estatuto");
        Assert.Equal(contentType, estatuto.GetProperty("cargado").GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Upload_RecordsTheThirdParty_AsUploader()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using (var res = await PutAsync(cookie, "Estatuto", SampleFiles.Pdf())) Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var row = Assert.Single(await RowsAsync(caso.CasoId, "Estatuto"));
        Assert.Equal($"tercero:{caso.TerceroId}", row["CargadoPor"]);
        Assert.Equal(caso.TerceroId, row["TerceroId"]);
        Assert.Null(row["BajaUtc"]);
    }

    [Fact]
    public async Task Upload_TextRenamedAsPdf_Returns400()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.TextoRenombrado(), "estatuto.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Formatos aceptados: PDF, JPG, PNG", await TitleAsync(res));
    }

    [Fact]
    public async Task Upload_Gif_Returns400()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.Gif(), "animado.gif");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Formatos aceptados: PDF, JPG, PNG", await TitleAsync(res));
    }

    [Fact]
    public async Task Upload_PdfWithOwnerPassword_IsAccepted()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.PdfCifrado());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", (await Body(res)).GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Upload_TruncatedPdf_Returns400_AndStoresNothing()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.PdfTruncado());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("No se puede leer", await TitleAsync(res));
        Assert.Empty(await RowsAsync(caso.CasoId, "Estatuto"));
    }

    [Fact]
    public async Task Upload_LargerThanTheDefaultMaximum_Returns413_WithTheLimitInMb()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.Grande(10 * Mb + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.Contains("10 MB", await TitleAsync(res));
    }

    [Fact]
    public async Task Upload_TypeOfTheOtherPersonKind_Returns400()
    {
        var caso = await SeedAsync(TipoPersona.Humana);
        var cookie = await SignInAsync(caso);

        using var res = await PutAsync(cookie, "Estatuto", SampleFiles.Pdf());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(await RowsAsync(caso.CasoId, "Estatuto"));
        using var unknown = await PutAsync(cookie, "NoExiste", SampleFiles.Pdf());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Replace_LeavesASingleCurrentOne_AndMarksThePreviousAsReplaced()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        using (var first = await PutAsync(cookie, "Estatuto", SampleFiles.Pdf(), "v1.pdf")) Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PutAsync(cookie, "Estatuto", SampleFiles.Png(), "v2.png");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var rows = await RowsAsync(caso.CasoId, "Estatuto");
        Assert.Equal(2, rows.Count);
        var vigente = Assert.Single(rows, r => r["BajaUtc"] is null);
        Assert.Equal("v2.png", vigente["NombreOriginal"]);
        Assert.Null(vigente["Baja"]);
        var anterior = Assert.Single(rows, r => r["BajaUtc"] is not null);
        Assert.Equal("v1.pdf", anterior["NombreOriginal"]);
        Assert.Equal("Reemplazado", anterior["Baja"]);
        var list = await Body(await PortalGet("/caso/documentos", cookie));
        var cargado = list.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("tipo").GetString() == "Estatuto").GetProperty("cargado");
        Assert.Equal("v2.png", cargado.GetProperty("nombreOriginal").GetString());
    }

    [Fact]
    public async Task Delete_Returns204_AndMarksItAsDeleted()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        using (var up = await PutAsync(cookie, "Estatuto", SampleFiles.Pdf())) Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        using var res = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var row = Assert.Single(await RowsAsync(caso.CasoId, "Estatuto"));
        Assert.Equal("Eliminado", row["Baja"]);
        Assert.NotNull(row["BajaUtc"]);
        var list = await Body(await PortalGet("/caso/documentos", cookie));
        var doc = list.GetProperty("documentos").EnumerateArray().Single(d => d.GetProperty("tipo").GetString() == "Estatuto");
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("cargado").ValueKind);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var cookie = await SignInAsync(await SeedAsync());

        using var res = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task AnotherThirdParty_NeitherSeesNorTouchesTheDocuments()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();
        var firstCookie = await SignInAsync(first);
        var secondCookie = await SignInAsync(second);
        using (var up = await PutAsync(firstCookie, "Estatuto", SampleFiles.Pdf())) Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        var list = await Body(await PortalGet("/caso/documentos", secondCookie));
        Assert.All(list.GetProperty("documentos").EnumerateArray(), d => Assert.Equal(JsonValueKind.Null, d.GetProperty("cargado").ValueKind));
        using (var del = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", secondCookie)) Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);

        // Lo del primero sigue vigente, y subir en el segundo no lo reemplaza.
        using (var up = await PutAsync(secondCookie, "Estatuto", SampleFiles.Png(), "otro.png")) Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var row = Assert.Single(await RowsAsync(first.CasoId, "Estatuto"));
        Assert.Null(row["BajaUtc"]);
        Assert.Equal("archivo.pdf", row["NombreOriginal"]);
    }

    [Fact]
    public async Task NoCookie_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso/documentos", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", null, File(SampleFiles.Pdf()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", null)).StatusCode);
    }

    [Fact]
    public async Task Caso_Pendientes_ListsTheMissingMandatoryDocuments_AndShrinksOnUpload()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        async Task<string[]> PendientesAsync() =>
            [.. (await Body(await PortalGet("/caso", cookie))).GetProperty("pendientes").EnumerateArray().Select(p => p.GetString()!).Where(p => p.StartsWith("Subir: "))];

        var before = await PendientesAsync();
        Assert.Equal(
            ["Subir: Estatuto o contrato social", "Subir: Constancia de inscripción en ARCA",
             "Subir: Acta de designación de autoridades", "Subir: Documento del representante legal"],
            before);

        using (var up = await PutAsync(cookie, "Estatuto", SampleFiles.Pdf())) Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal(before.Skip(1), await PendientesAsync());

        using (var del = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie)) Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(before, await PendientesAsync());
    }
}

// Base propia: cambiar el máximo tocaría la fila de configuración que comparten todos los tests de la base común.
public class DocumentosMaximoTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private const long Mb = 1024 * 1024;

    public override async ValueTask InitializeAsync()
    {
        var db = await Sql.CreateDatabaseAsync();
        RuntimeConnection = db.Runtime;
        MigratorConnection = db.Migrator;
        await base.InitializeAsync();
    }

    [Fact]
    public async Task LoweringTheMaximum_Rejects3MbWith413_AndKeepsWhatWasAlreadyUploaded()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        using (var up = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(SampleFiles.Grande(3 * Mb))))
            Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        await SqlServerFixture.ExecuteAsync(MigratorConnection, $"UPDATE documentos.Configuracion SET TamanoMaximoBytes = {2 * Mb}");

        using var big = await Portal(HttpMethod.Put, "/caso/documentos/ConstanciaArca", cookie, File(SampleFiles.Grande(3 * Mb)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Contains("2 MB", (await Body(big)).GetProperty("title").GetString());

        var list = await Body(await PortalGet("/caso/documentos", cookie));
        Assert.Equal(2 * Mb, list.GetProperty("tamanoMaximoBytes").GetInt64());
        var docs = list.GetProperty("documentos").EnumerateArray().ToDictionary(d => d.GetProperty("tipo").GetString()!);
        Assert.Equal(3 * Mb, docs["Estatuto"].GetProperty("cargado").GetProperty("tamanoBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, docs["ConstanciaArca"].GetProperty("cargado").ValueKind);
        // Un archivo dentro del nuevo máximo sí entra.
        using var small = await Portal(HttpMethod.Put, "/caso/documentos/ConstanciaArca", cookie, File(SampleFiles.Grande(1 * Mb)));
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
    }
}
