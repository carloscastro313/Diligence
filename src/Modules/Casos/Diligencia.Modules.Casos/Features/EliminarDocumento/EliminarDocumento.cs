using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.EliminarDocumento;

internal sealed class EliminarDocumentoHandler(CasosDbContext db, IPortalActor actor, IDocumentStore documentos)
{
    public async Task<IResult> HandleAsync(string tipo, CancellationToken cancellationToken)
    {
        IResult? result = null;
        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            result = null;
            var caso = await CasoPortalQueries.LockCasoAsync(db, actor.CasoId, ct);
            var formulario = await db.Set<FormularioTercero>().AsNoTracking().SingleOrDefaultAsync(f => f.CasoId == caso.Id, ct);
            if (!CasoPortalQueries.IsEditable(caso, formulario)) { result = ApiProblems.Conflict("El formulario ya fue enviado: no se puede modificar."); return; }

            result = await documentos.RemoveAsync(caso.Id, tipo, tx, ct)
                ? Results.NoContent()
                : ApiProblems.NotFound("No hay un documento cargado de ese tipo.");
        }, cancellationToken);
        return result!;
    }
}

internal static class EliminarDocumentoEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapDelete("/caso/documentos/{tipo}",
        (string tipo, EliminarDocumentoHandler handler, CancellationToken ct) => handler.HandleAsync(tipo, ct));
}
