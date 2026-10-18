using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Casos.Features.SendPortalLink;
using Diligencia.Modules.Casos.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Casos;

public partial class SendPortalLinkTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    [GeneratedRegex(@"/acceso#t=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenRx();

    private async Task<(Guid EnlaceId, string Email)> SeedAsync(int intentos = 0, EntregaEnlace entrega = EntregaEnlace.Pendiente)
    {
        var email = $"contacto{Guid.NewGuid():N}@tercero.test";
        var now = Clock.GetUtcNow().UtcDateTime;
        var caso = new Caso
        {
            TerceroId = Guid.NewGuid(), Cuit = TestCuits.New(), RazonSocial = "Tercero SA",
            EstadoDesdeUtc = now, SolicitanteId = Guid.NewGuid().ToString(),
        };
        var solicitud = new Solicitud
        {
            CasoId = caso.Id, SolicitanteId = caso.SolicitanteId, EsOriginal = true, ContactoNombre = "Ana",
            ContactoEmail = email, MontoAnualEstimado = 1000m, Motivo = "Proveedor",
        };
        var enlace = new EnlacePortal
        {
            CasoId = caso.Id, Destinatario = email, VenceUtc = now.AddDays(21), Entrega = entrega, Intentos = intentos,
        };
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CasosDbContext>();
        db.AddRange(caso, solicitud, enlace);
        await db.SaveChangesAsync(Ct);
        return (enlace.Id, email);
    }

    private async Task RunAsync(Guid enlaceId)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPortalLinkDelivery>().SendAsync(enlaceId, Ct);
    }

    private async Task<EnlacePortal> LoadAsync(Guid enlaceId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CasosDbContext>()
            .Set<EnlacePortal>().AsNoTracking().SingleAsync(e => e.Id == enlaceId, Ct);
    }

    private static byte[] HashOfMail(string body)
    {
        var m = TokenRx().Match(body);
        Assert.True(m.Success, "el correo no contiene /acceso#t=");
        return SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value));
    }

    [Fact]
    public async Task Success_MarksSent_MailsContact_AndStoresTheHashOfTheMailedToken()
    {
        var (id, email) = await SeedAsync();

        await RunAsync(id);

        var enlace = await LoadAsync(id);
        Assert.Equal(EntregaEnlace.Enviado, enlace.Entrega);
        Assert.Equal(1, enlace.Intentos);
        Assert.NotNull(enlace.UltimoIntentoUtc);
        var mail = Assert.Single(EmailTransport.Sent, m => m.To == email);
        Assert.Contains("http://portal.test/acceso#t=", mail.Body);
        Assert.Equal(HashOfMail(mail.Body), enlace.TokenHash);
    }

    [Fact]
    public async Task TwoCases_GetDifferentTokens()
    {
        var (a, emailA) = await SeedAsync();
        var (b, emailB) = await SeedAsync();

        await RunAsync(a);
        await RunAsync(b);

        var ha = (await LoadAsync(a)).TokenHash;
        var hb = (await LoadAsync(b)).TokenHash;
        Assert.NotNull(ha);
        Assert.NotNull(hb);
        Assert.NotEqual(ha, hb);
        Assert.NotEqual(HashOfMail(EmailTransport.Sent.Single(m => m.To == emailA).Body),
            HashOfMail(EmailTransport.Sent.Single(m => m.To == emailB).Body));
    }

    [Fact]
    public async Task SecondRunOnSent_SendsNothing_AndKeepsTheState()
    {
        var (id, email) = await SeedAsync();
        await RunAsync(id);
        var before = await LoadAsync(id);

        await RunAsync(id);

        var after = await LoadAsync(id);
        Assert.Single(EmailTransport.Sent, m => m.To == email);
        Assert.Equal(EntregaEnlace.Enviado, after.Entrega);
        Assert.Equal(1, after.Intentos);
        Assert.Equal(before.TokenHash, after.TokenHash);
    }

    [Fact]
    public async Task FailureBelowFiveAttempts_MarksRetrying_AndThrows()
    {
        var (id, email) = await SeedAsync();
        EmailTransport.FailNext(1);

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(id));

        var enlace = await LoadAsync(id);
        Assert.Equal(EntregaEnlace.Reintentando, enlace.Entrega);
        Assert.Equal(1, enlace.Intentos);
        Assert.DoesNotContain(EmailTransport.Sent, m => m.To == email);

        // Hangfire reintenta: el segundo intento ya envía.
        await RunAsync(id);
        enlace = await LoadAsync(id);
        Assert.Equal(EntregaEnlace.Enviado, enlace.Entrega);
        Assert.Equal(2, enlace.Intentos);
        Assert.Equal(HashOfMail(EmailTransport.Sent.Single(m => m.To == email).Body), enlace.TokenHash);
    }

    [Fact]
    public async Task FifthFailure_MarksFailed_AndDoesNotThrow()
    {
        var (id, email) = await SeedAsync(intentos: 4, entrega: EntregaEnlace.Reintentando);
        EmailTransport.FailNext(1);

        await RunAsync(id);

        var enlace = await LoadAsync(id);
        Assert.Equal(EntregaEnlace.Fallido, enlace.Entrega);
        Assert.Equal(5, enlace.Intentos);
        Assert.DoesNotContain(EmailTransport.Sent, m => m.To == email);

        // Fallido es terminal: otra ejecución no envía ni cambia nada.
        await RunAsync(id);
        Assert.Equal(5, (await LoadAsync(id)).Intentos);
        Assert.DoesNotContain(EmailTransport.Sent, m => m.To == email);
    }
}
