using System.Net;
using System.Security.Cryptography;
using System.Text;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Diligencia.IntegrationTests.Portal;

// FR-018 / FR-021: la bitácora y los logs registran cada carga, reemplazo y eliminación sin los bytes del archivo.
public class DocumentosAuditTests(SqlServerFixture sql) : PortalApiTest(sql)
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

    // PDF válido con un marcador único reconocible dentro del contenido.
    private static byte[] PdfWith(string marker) =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n%{marker}\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    private async Task<string> DocIdAsync(Guid casoId, byte[] file) =>
        ((Guid)Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Id FROM documentos.Documentos WHERE CasoId = @c AND Sha256 = @h", ("@c", casoId), ("@h", SHA256.HashData(file))))["Id"]!).ToString();

    [Fact]
    public async Task UploadReplaceAndDelete_AreAudited_WithoutTheFileBytes()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var (m1, m2) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        var (first, second) = (PdfWith(m1), PdfWith(m2));

        using (var r = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(first, "uno.pdf"))) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(second, "dos.pdf"))) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Portal(HttpMethod.Delete, "/caso/documentos/Estatuto", cookie)) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var (id1, id2) = (await DocIdAsync(caso.CasoId, first), await DocIdAsync(caso.CasoId, second));

        var created1 = Assert.Single(await AuditAsync("Documento", id1, "Created")).GetProperty("newValues").GetString()!;
        Assert.Equal($"tercero:{caso.TerceroId}", Assert.Single(await AuditAsync("Documento", id1, "Created")).GetProperty("actorId").GetString());
        Assert.Contains("Estatuto", created1);
        Assert.Contains("uno.pdf", created1);
        Assert.Contains(first.Length.ToString(), created1);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(first)), created1);

        var created2 = Assert.Single(await AuditAsync("Documento", id2, "Created")).GetProperty("newValues").GetString()!;
        Assert.Contains("dos.pdf", created2);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(second)), created2);

        var replaced = Assert.Single(await AuditAsync("Documento", id1, "Updated")).GetProperty("newValues").GetString()!;
        Assert.Contains("Reemplazado", replaced);
        Assert.Contains("uno.pdf", replaced);
        Assert.Contains(first.Length.ToString(), replaced);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(first)), replaced);

        var deleted = Assert.Single(await AuditAsync("Documento", id2, "Updated")).GetProperty("newValues").GetString()!;
        Assert.Contains("Eliminado", deleted);
        Assert.Contains("Estatuto", deleted);
        Assert.Contains(second.Length.ToString(), deleted);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(second)), deleted);

        // Ninguna fila de bitácora (de estas entidades ni de todo el sistema) lleva los bytes, ni en claro ni en base64.
        foreach (var file in new[] { first, second })
        {
            var marker = Encoding.ASCII.GetString(file).Split('\n')[1][1..];
            var rows = await CreateSolicitudTests.RowsAsync(MigratorConnection,
                "SELECT Id FROM auditoria.AuditEvents WHERE NewValues LIKE @m OR OldValues LIKE @m OR NewValues LIKE @b OR OldValues LIKE @b OR EntityId LIKE @m",
                ("@m", "%" + marker + "%"), ("@b", "%" + Convert.ToBase64String(file)[..40] + "%"));
            Assert.Empty(rows);
        }

        var logs = string.Join('\n', Factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.Message + " " + r.Exception + " " + string.Join(' ', r.Scopes.Select(s => s?.ToString()))
                + " " + string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])));
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(m1, logs);
        Assert.DoesNotContain(m2, logs);
    }
}
