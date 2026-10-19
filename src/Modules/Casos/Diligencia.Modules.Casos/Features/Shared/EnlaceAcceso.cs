using Diligencia.Modules.Casos.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.Shared;

internal enum EnlaceStatus { Valid, Invalid, Locked }

internal sealed record EnlaceLookup(EnlaceStatus Status, EnlacePortal? Enlace = null, Caso? Caso = null);

internal static class PortalMessages
{
    // Mismo mensaje para inexistente, vencido o de un caso que ya no espera al tercero: no revela datos (FR-004).
    public const string EnlaceInvalido = "El enlace no es válido o venció. Comunicate con quien te pidió el alta.";
}

internal static class EnlaceAcceso
{
    // Dentro de la transacción del llamador: deja la fila del enlace bloqueada (UPDLOCK) hasta confirmar, así dos
    // requests simultáneas no superan los límites de códigos ni de intentos (R4).
    public static async Task<EnlaceLookup> FindValidAsync(CasosDbContext db, string token, DateTime now, CancellationToken ct)
    {
        var hash = PortalToken.Hash(token);
        var enlace = (await db.Set<EnlacePortal>()
            .FromSql($"SELECT * FROM casos.EnlacesPortal WITH (UPDLOCK, ROWLOCK) WHERE TokenHash = {hash}")
            .ToListAsync(ct)).SingleOrDefault();
        if (enlace is null || enlace.VenceUtc <= now) return new(EnlaceStatus.Invalid);

        var caso = await db.Set<Caso>().SingleAsync(c => c.Id == enlace.CasoId, ct);
        if (caso.Estado != EstadoCaso.EsperaTercero) return new(EnlaceStatus.Invalid);
        if (enlace.BloqueadoHastaUtc > now) return new(EnlaceStatus.Locked, enlace, caso);
        return new(EnlaceStatus.Valid, enlace, caso);
    }

    public static int SecondsUntil(DateTime until, DateTime now) => (int)Math.Ceiling((until - now).TotalSeconds);
}
