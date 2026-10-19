using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Terceros.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Support;

public sealed record SeededCaso(Guid CasoId, Guid TerceroId, Guid EnlaceId, string Token, string Email, string Cuit, string RazonSocial);

/// <summary>Un caso en "Espera del tercero" con su enlace de portal de token conocido, listo para ingresar.</summary>
internal static class PortalSeed
{
    // El registro de terceros bloquea el rango del CUIT (UPDLOCK, HOLDLOCK): altas simultáneas de CUIT vecinos de tests
    // en paralelo pueden terminar en deadlock. En producción lo reintenta el llamador; acá se reintenta la siembra.
    public static async Task<SeededCaso> CreateCasoAsync(
        IServiceProvider services, TipoPersona persona = TipoPersona.Juridica, DateTime? venceUtc = null,
        string? email = null, EstadoCaso estado = EstadoCaso.EsperaTercero)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await TryCreateAsync(services, persona, venceUtc, email, estado); }
            catch (Exception ex) when (attempt < 5 && IsDeadlock(ex)) { await Task.Delay(50 * attempt); }
        }
    }

    private static bool IsDeadlock(Exception? ex) =>
        ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 } || (ex is not null && IsDeadlock(ex.InnerException));

    private static async Task<SeededCaso> TryCreateAsync(
        IServiceProvider services, TipoPersona persona, DateTime? venceUtc, string? email, EstadoCaso estado)
    {
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        email ??= $"contacto{Guid.NewGuid():N}@tercero.test";
        var cuit = TestCuits.New(persona == TipoPersona.Juridica ? "30" : "20");
        var razonSocial = "Tercero " + Guid.NewGuid().ToString("N")[..8];
        var token = PortalToken.Generate();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CasosDbContext>();
        var terceros = scope.ServiceProvider.GetRequiredService<ITerceroRegistry>();
        Caso caso = null!;
        EnlacePortal enlace = null!;
        TerceroInfo tercero = null!;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            (tercero, _) = await terceros.GetOrCreateForOnboardingAsync(
                new NuevoTercero(cuit, TipoTercero.Proveedor, persona, razonSocial), tx, ct);
            caso = new Caso
            {
                TerceroId = tercero.Id, Cuit = cuit, RazonSocial = razonSocial, Estado = estado,
                EstadoDesdeUtc = now, SolicitanteId = Guid.NewGuid().ToString(),
            };
            var solicitud = new Solicitud
            {
                CasoId = caso.Id, SolicitanteId = caso.SolicitanteId, EsOriginal = true, ContactoNombre = "Ana Pérez",
                ContactoEmail = email, MontoAnualEstimado = 1000m, Motivo = "Proveedor",
            };
            enlace = new EnlacePortal
            {
                CasoId = caso.Id, Destinatario = email, VenceUtc = venceUtc ?? now.AddDays(21),
                TokenHash = PortalToken.Hash(token), Entrega = EntregaEnlace.Enviado,
            };
            db.AddRange(caso, solicitud, enlace);
            await db.SaveChangesAsync(false, ct);
        });

        return new SeededCaso(caso.Id, tercero.Id, enlace.Id, token, email, cuit, razonSocial);
    }
}
