using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Portal;

// US1-2/6: la sesión del portal ve solo su caso (FR-005, FR-006, SC-005).
public class SesionTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    [Fact]
    public async Task GetCaso_ReturnsOnlyThePortalFields_OfTheSessionsCase()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        using var res = await PortalGet("/caso", cookie);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Body(res);
        Assert.Equal(caso.RazonSocial, json.GetProperty("razonSocial").GetString());
        Assert.Equal(caso.Cuit, json.GetProperty("cuit").GetString());
        Assert.Equal("Juridica", json.GetProperty("tipoPersona").GetString());
        Assert.Equal("EsperaTercero", json.GetProperty("estado").GetString());
        Assert.True(json.GetProperty("editable").GetBoolean());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("pendientes").ValueKind);
        // Nada de hallazgos, puntajes ni notas internas: el JSON tiene exactamente estas claves.
        Assert.Equal(["cuit", "editable", "estado", "pendientes", "razonSocial", "tipoPersona"],
            json.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task NoCookie_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", null)).StatusCode);

    [Fact]
    public async Task IdleThirtyMinutes_Expires_ButActivityAt29MinutesKeepsItAlive()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await PortalGet("/caso", cookie)).StatusCode);
        Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await PortalGet("/caso", cookie)).StatusCode);

        Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", cookie)).StatusCode);
    }

    [Fact]
    public async Task AbsoluteEightHours_Expires_EvenWithActivity()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        for (var i = 0; i < 17; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(29));
            if (i < 16) Assert.Equal(HttpStatusCode.OK, (await PortalGet("/caso", cookie)).StatusCode);
        }

        // 17 × 29 min > 8 h.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", cookie)).StatusCode);
    }

    [Fact]
    public async Task PortalSession_IsNotAcceptedByTheBackoffice()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var admin = await UserAsync(BackofficeRoles.Administrador);

        // La cookie de portal no viaja al backoffice (otra Path y otro nombre), pero aunque se mande no sirve...
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/backoffice/areas");
        req.Headers.Add("Cookie", $"dlg_pt={cookie}; dlg_bo={cookie}");
        using (var res = await Client.SendAsync(req, Ct)) Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // ...ni como bearer.
        using (var res = await SendRaw(HttpMethod.Get, "/api/backoffice/areas", cookie)) Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // Y el token del backoffice tampoco sirve en el portal, ni como bearer ni como cookie de portal.
        using (var res = await SendRaw(HttpMethod.Get, "/api/portal/caso", admin.Token)) Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", admin.Token)).StatusCode);
    }

    [Fact]
    public async Task TwoThirdParties_EachSeeOnlyTheirOwnCase()
    {
        var first = await SeedAsync();
        var second = await SeedAsync(TipoPersonaHumana);
        var firstCookie = await SignInAsync(first);
        var secondCookie = await SignInAsync(second);

        var a = await Body(await PortalGet("/caso", firstCookie));
        var b = await Body(await PortalGet("/caso", secondCookie));

        Assert.Equal(first.RazonSocial, a.GetProperty("razonSocial").GetString());
        Assert.Equal(second.RazonSocial, b.GetProperty("razonSocial").GetString());
        Assert.Equal("Humana", b.GetProperty("tipoPersona").GetString());
        // No hay forma de pedir otro caso: la ruta no recibe id y un id en la query se ignora.
        var other = await Body(await PortalGet($"/caso?casoId={second.CasoId}", firstCookie));
        Assert.Equal(first.RazonSocial, other.GetProperty("razonSocial").GetString());
    }

    [Fact]
    public async Task TamperedSessionToken_Returns401()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);

        // Token firmado con otra clave pero con una sesión existente.
        var forged = TestUsers.Sign(ActorSurface.Backoffice,
            [new System.Security.Claims.Claim("sub", $"tercero:{caso.TerceroId}"), new System.Security.Claims.Claim("sid", Guid.NewGuid().ToString())],
            DateTime.UtcNow.AddHours(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalGet("/caso", cookie + "x")).StatusCode);
    }

    private const Diligencia.Modules.Terceros.Contracts.TipoPersona TipoPersonaHumana = Diligencia.Modules.Terceros.Contracts.TipoPersona.Humana;
}
