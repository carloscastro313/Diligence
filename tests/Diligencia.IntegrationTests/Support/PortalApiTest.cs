using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.IntegrationTests.Support;

/// <summary>Base de los tests del portal: caso sembrado, enlace + código por la API real y cookie <c>dlg_pt</c> a mano.</summary>
public abstract partial class PortalApiTest(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    public const string CookieName = "dlg_pt";

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex CodeRx();

    protected Task<SeededCaso> SeedAsync(TipoPersona persona = TipoPersona.Juridica, DateTime? venceUtc = null, string? email = null) =>
        PortalSeed.CreateCasoAsync(Factory.Services, persona, venceUtc, email);

    protected Task<HttpResponseMessage> RequestCodeAsync(string token) =>
        Client.PostAsJsonAsync("/api/portal/acceso/codigo", new { token }, Ct);

    protected Task<HttpResponseMessage> EnterAsync(string token, string codigo) =>
        Client.PostAsJsonAsync("/api/portal/acceso", new { token, codigo }, Ct);

    /// <summary>El último código enviado a ese correo (de lo que recibió el transporte falso).</summary>
    protected string LastCode(string email)
    {
        var mail = EmailTransport.Sent.Last(m => m.To == email);
        return CodeRx().Match(mail.Body).Groups[1].Value;
    }

    /// <summary>Pide el código, ingresa y devuelve el valor de la cookie de sesión.</summary>
    protected async Task<string> SignInAsync(SeededCaso caso)
    {
        using (var code = await RequestCodeAsync(caso.Token)) Assert.Equal(HttpStatusCode.Accepted, code.StatusCode);
        using var res = await EnterAsync(caso.Token, LastCode(caso.Email));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        return CookieOf(res)!;
    }

    protected static string? CookieOf(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith(CookieName + "="))?[(CookieName.Length + 1)..]
            : null;

    protected Task<HttpResponseMessage> Portal(HttpMethod method, string path, string? cookie, object? body = null) =>
        Portal(method, path, cookie, body is null ? null : JsonContent.Create(body));

    protected Task<HttpResponseMessage> Portal(HttpMethod method, string path, string? cookie, HttpContent? content)
    {
        var req = new HttpRequestMessage(method, "/api/portal" + path) { Content = content };
        if (cookie is not null) req.Headers.Add("Cookie", $"{CookieName}={cookie}");
        return Client.SendAsync(req, Ct);
    }

    protected Task<HttpResponseMessage> PortalGet(string path, string? cookie) => Portal(HttpMethod.Get, path, cookie);

    protected static MultipartFormDataContent File(byte[] bytes, string fileName = "archivo.pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "archivo", fileName);
        return form;
    }

    protected static async Task<JsonElement> Body(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>(Ct)).Clone();
}
