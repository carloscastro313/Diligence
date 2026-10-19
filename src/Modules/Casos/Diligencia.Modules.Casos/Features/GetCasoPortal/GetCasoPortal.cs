using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.GetCasoPortal;

// Solo lo que el tercero puede ver: nunca hallazgos, verificaciones, puntajes ni notas internas (FR-006).
internal sealed record CasoPortal(
    string RazonSocial, string Cuit, string TipoPersona, string Estado, bool Editable, IReadOnlyList<string> Pendientes);

internal sealed class GetCasoPortalHandler(CasosDbContext db, IPortalActor actor, ITerceroRegistry terceros, IDocumentStore documentos)
{
    public async Task<CasoPortal> HandleAsync(CancellationToken ct)
    {
        var caso = await CasoPortalQueries.LoadSessionCasoAsync(db, actor, ct);
        var persona = (await terceros.FindByCuitAsync(caso.Cuit, ct))!.Persona;
        var formulario = await db.Set<FormularioTercero>().AsNoTracking().SingleOrDefaultAsync(f => f.CasoId == caso.Id, ct);
        var editable = CasoPortalQueries.IsEditable(caso, formulario);

        var pendientes = new List<string>();
        if (editable)
        {
            pendientes.Add("Completar el formulario");
            var cargados = (await documentos.ListVigentesAsync(caso.Id, null, ct)).Select(d => d.Tipo).ToHashSet();
            pendientes.AddRange(DocumentosRequeridos.For(persona).Where(r => r.Obligatorio && !cargados.Contains(r.Tipo)).Select(r => $"Subir: {r.Nombre}"));
        }

        return new CasoPortal(caso.RazonSocial, caso.Cuit, persona.ToString(), caso.Estado.ToString(), editable, pendientes);
    }
}

internal static class GetCasoPortalEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapGet("/caso", async (GetCasoPortalHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct)));
}
