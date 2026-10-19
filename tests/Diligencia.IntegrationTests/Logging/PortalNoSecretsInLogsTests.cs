using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Diligencia.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Logging;

// FR-018/FR-021: pedir código, ingresar, guardar, subir un archivo y enviar no dejan en los logs
// el token del enlace, los códigos, la cookie de sesión ni bytes del archivo.
public class PortalNoSecretsInLogsTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private sealed class LoggingApiFactory(string connectionString) : DbApiFactory(connectionString)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddFakeLogging());
            builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace));
        }
    }

    protected override DbApiFactory CreateFactory() => new LoggingApiFactory(RuntimeConnection);

    [Fact]
    public async Task Token_Codes_Cookie_AndFileBytes_NeverReachLogs()
    {
        var caso = await SeedAsync();
        var wrongCodes = new[] { "718293", "364051" };

        using (var res = await RequestCodeAsync(caso.Token)) Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        foreach (var wrong in wrongCodes)
            using (var res = await EnterAsync(caso.Token, wrong)) Assert.False(res.IsSuccessStatusCode);

        var cookie = await SignInAsync(caso);
        Assert.False(string.IsNullOrEmpty(cookie));

        using (var res = await Portal(HttpMethod.Put, "/caso/formulario", cookie,
                   new { datos = new { origenFondos = "Venta de servicios de consultoría" } }))
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync(Ct));

        var marker = Guid.NewGuid().ToString("N");
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\n%{marker}\n%%EOF\n");
        using (var res = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(pdf, "estatuto.pdf")))
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync(Ct));

        // Puede responder 400 por datos incompletos: se busca ausencia de secretos, no éxito.
        using (await Portal(HttpMethod.Post, "/caso/formulario/envio", cookie)) { }

        var issued = EmailTransport.Sent.Where(m => m.To == caso.Email)
            .Select(m => Regex.Match(m.Body, @"\b(\d{6})\b").Groups[1].Value)
            .Where(c => c.Length == 6).Concat(wrongCodes).Distinct().ToList();
        Assert.True(issued.Count >= 3);

        var logs = string.Join('\n', Factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.Message + " " + r.Exception + " " + string.Join(' ', r.Scopes.Select(s => s?.ToString()))
                + " " + string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])));
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(caso.Token, logs);
        Assert.DoesNotContain(cookie, logs);
        Assert.DoesNotContain(marker, logs);
        Assert.DoesNotMatch(new Regex($@"(?<!\d)({string.Join('|', issued)})(?!\d)"), logs);
    }
}
