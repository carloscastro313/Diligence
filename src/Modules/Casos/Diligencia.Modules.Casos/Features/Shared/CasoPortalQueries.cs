using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.Shared;

// Único camino para leer el caso de una sesión de portal: el id sale de IPortalActor, nunca de la request (FR-006).
internal static class CasoPortalQueries
{
    public static Task<Caso> LoadSessionCasoAsync(CasosDbContext db, IPortalActor actor, CancellationToken ct) =>
        db.Set<Caso>().AsNoTracking().SingleAsync(c => c.Id == actor.CasoId, ct);

    // Dentro de la transacción del llamador: serializa envío, guardado y carga de documentos del mismo caso (R7).
    public static async Task<Caso> LockCasoAsync(CasosDbContext db, Guid casoId, CancellationToken ct) =>
        (await db.Set<Caso>()
            .FromSql($"SELECT * FROM casos.Casos WITH (UPDLOCK, ROWLOCK) WHERE Id = {casoId}")
            .ToListAsync(ct)).Single();

    public static bool IsEditable(Caso caso, FormularioTercero? formulario) =>
        caso.Estado == EstadoCaso.EsperaTercero && formulario?.Estado != EstadoFormulario.Enviado;
}
