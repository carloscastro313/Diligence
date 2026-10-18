using Diligencia.Modules.Casos.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class CasoQueries
{
    public static readonly EstadoCaso[] EstadosAbiertos = [EstadoCaso.EsperaTercero, EstadoCaso.EnVerificacion, EstadoCaso.EnAprobacion];

    public static Task<Caso?> FindOpenCasoAsync(CasosDbContext db, Guid terceroId, CancellationToken ct) =>
        db.Set<Caso>().SingleOrDefaultAsync(c => c.TerceroId == terceroId && EstadosAbiertos.Contains(c.Estado), ct);

    public static Task<Caso?> LastCasoAsync(CasosDbContext db, Guid terceroId, CancellationToken ct) =>
        db.Set<Caso>().Where(c => c.TerceroId == terceroId).OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct);
}
