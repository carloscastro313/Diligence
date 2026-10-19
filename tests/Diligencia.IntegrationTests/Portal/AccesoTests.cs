using System.Net;
using System.Text.RegularExpressions;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Casos.Persistence;

namespace Diligencia.IntegrationTests.Portal;

// US1-1..5: enlace + código (FR-001, FR-002, FR-004).
public class AccesoTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private const string EnlaceInvalido = "El enlace no es válido o venció. Comunicate con quien te pidió el alta.";

    private Task<List<Dictionary<string, object?>>> Codigos(Guid enlaceId) =>
        CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT * FROM casos.CodigosIngreso WHERE EnlaceId = @e ORDER BY CreatedAtUtc", ("@e", enlaceId));

    [Fact]
    public async Task ValidLink_Returns202_WithMaskedEmail_AndOnlyTheHashIsStored()
    {
        var caso = await SeedAsync(email: "contacto@example.com");

        using var res = await RequestCodeAsync(caso.Token);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var json = await Body(res);
        Assert.Equal("c***@example.com", json.GetProperty("correo").GetString());
        Assert.Equal(10, json.GetProperty("venceEnMinutos").GetInt32());

        var mail = Assert.Single(EmailTransport.Sent, m => m.To == caso.Email);
        var code = LastCode(caso.Email);
        Assert.Matches(@"^\d{6}$", code);
        Assert.DoesNotContain("/acceso#t=", mail.Body); // el correo del código no lleva el enlace
        var row = Assert.Single(await Codigos(caso.EnlaceId));
        Assert.Equal("Vigente", row["Estado"]);
        Assert.Equal(32, ((byte[])row["CodigoHash"]!).Length);
        Assert.NotEqual(code, Convert.ToHexString((byte[])row["CodigoHash"]!));
    }

    [Fact]
    public async Task CorrectCode_Returns204_WithHardenedSessionCookie_AndMarksTheCodeUsed()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();

        using var res = await EnterAsync(caso.Token, LastCode(caso.Email));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var setCookie = Assert.Single(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith("dlg_pt="));
        Assert.Matches(new Regex("httponly", RegexOptions.IgnoreCase), setCookie);
        Assert.Matches(new Regex("secure", RegexOptions.IgnoreCase), setCookie);
        Assert.Matches(new Regex("samesite=strict", RegexOptions.IgnoreCase), setCookie);
        Assert.Matches(new Regex("path=/api/portal", RegexOptions.IgnoreCase), setCookie);
        Assert.Equal("Usado", Assert.Single(await Codigos(caso.EnlaceId))["Estado"]);

        // El código es de un solo uso.
        using var again = await EnterAsync(caso.Token, LastCode(caso.Email));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task FiveWrongCodes_InvalidateIt_AndTheCorrectOneNoLongerWorks()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var correct = LastCode(caso.Email);
        var wrong = correct == "000000" ? "111111" : "000000";

        for (var i = 1; i <= 5; i++)
        {
            using var res = await EnterAsync(caso.Token, wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            var invalidated = (await Body(res)).TryGetProperty("codigoInvalidado", out var flag) && flag.GetBoolean();
            Assert.Equal(i == 5, invalidated);
        }

        using var late = await EnterAsync(caso.Token, correct);
        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
        Assert.True((await Body(late)).GetProperty("codigoInvalidado").GetBoolean());
        Assert.Equal("Invalidado", Assert.Single(await Codigos(caso.EnlaceId))["Estado"]);
    }

    [Fact]
    public async Task ExpiredCode_Returns401_JustAfterTenMinutes()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var code = LastCode(caso.Email);

        Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        using var res = await EnterAsync(caso.Token, code);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CodeJustBeforeExpiry_StillWorks()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var code = LastCode(caso.Email);

        Clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        using var res = await EnterAsync(caso.Token, code);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Resend_InvalidatesThePreviousCode()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var first = LastCode(caso.Email);
        Clock.Advance(TimeSpan.FromSeconds(1)); // el orden de los códigos se lee por fecha de creación
        (await RequestCodeAsync(caso.Token)).Dispose();
        var second = LastCode(caso.Email);
        Assert.NotEqual(first, second);

        var rows = await Codigos(caso.EnlaceId);
        Assert.Equal(["Invalidado", "Vigente"], rows.Select(r => (string)r["Estado"]!));
        using (var old = await EnterAsync(caso.Token, first)) Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using (var fresh = await EnterAsync(caso.Token, second)) Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
    }

    [Fact]
    public async Task UnknownExpiredAndNonWaitingLinks_GetTheSame404()
    {
        var expired = await SeedAsync(venceUtc: Clock.GetUtcNow().UtcDateTime.AddMinutes(-1));
        var enVerificacion = await PortalSeed.CreateCasoAsync(Factory.Services, estado: EstadoCaso.EnVerificacion);

        foreach (var token in new[] { "no-existe", expired.Token, enVerificacion.Token })
        {
            using var codigo = await RequestCodeAsync(token);
            using var ingreso = await EnterAsync(token, "123456");
            foreach (var res in new[] { codigo, ingreso })
            {
                Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
                Assert.Equal(EnlaceInvalido, (await Body(res)).GetProperty("title").GetString());
            }
        }
        Assert.Empty(EmailTransport.Sent);
    }

    [Fact]
    public async Task SmtpFailure_Returns503_AndTheIssuedCodeIsInvalidated()
    {
        var caso = await SeedAsync();
        EmailTransport.FailNext(1);

        using var res = await RequestCodeAsync(caso.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Empty(EmailTransport.Sent);
        Assert.Equal("Invalidado", Assert.Single(await Codigos(caso.EnlaceId))["Estado"]);

        // El reintento funciona.
        using var retry = await RequestCodeAsync(caso.Token);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    [Theory]
    [InlineData("", "123456")]
    [InlineData("abc", "12345")]
    [InlineData("abc", "abcdef")]
    [InlineData("abc", "1234567")]
    [InlineData("abc", "")]
    public async Task InvalidBody_Returns400(string token, string codigo)
    {
        using var res = await EnterAsync(token, codigo);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task RequestCode_WithEmptyOrHugeToken_Returns400()
    {
        using (var empty = await RequestCodeAsync("")) Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using (var huge = await RequestCodeAsync(new string('a', 101))) Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
    }
}
