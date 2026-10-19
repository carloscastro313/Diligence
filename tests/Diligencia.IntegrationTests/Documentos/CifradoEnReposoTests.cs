using System.Net;
using System.Security.Cryptography;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Documentos.Features.Store;
using Microsoft.Extensions.Configuration;

namespace Diligencia.IntegrationTests.Documentos;

// FR-018 / R8: lo guardado en SQL es el texto cifrado (AES-GCM); el hash es el del archivo en claro.
public class CifradoEnReposoTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    [Fact]
    public async Task StoredContent_IsCiphertext_ThatDecryptsToTheUploadedBytes_AndShaMatches()
    {
        var caso = await SeedAsync();
        var cookie = await SignInAsync(caso);
        var pdf = SampleFiles.Pdf();

        using (var put = await Portal(HttpMethod.Put, "/caso/documentos/Estatuto", cookie, File(pdf, "estatuto.pdf")))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var docs = await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Id, Sha256 FROM documentos.Documentos WHERE CasoId = @c", ("@c", caso.CasoId));
        var doc = Assert.Single(docs);
        Assert.Equal(SHA256.HashData(pdf), (byte[])doc["Sha256"]!);

        var content = Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Nonce, Tag, Cifrado FROM documentos.DocumentoContenidos WHERE DocumentoId = @d", ("@d", doc["Id"]!)));
        var (nonce, tag, cifrado) = ((byte[])content["Nonce"]!, (byte[])content["Tag"]!, (byte[])content["Cifrado"]!);
        Assert.NotEqual(pdf, cifrado);

        // Misma clave que usa ApiFactory.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Documentos:EncryptionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)0xD0, 32).ToArray()),
        }).Build();
        Assert.Equal(pdf, new DocumentCipher(config).Decrypt(nonce, tag, cifrado));
    }
}
