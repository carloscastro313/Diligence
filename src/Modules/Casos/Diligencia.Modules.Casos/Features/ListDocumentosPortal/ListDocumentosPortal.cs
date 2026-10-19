using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Diligencia.Modules.Casos.Features.ListDocumentosPortal;

internal sealed record DocumentoCargado(Guid Id, string NombreOriginal, string ContentType, long TamanoBytes, DateTime CargadoUtc)
{
    public static DocumentoCargado From(DocumentoInfo d) => new(d.Id, d.NombreOriginal, d.ContentType, d.TamanoBytes, d.CargadoUtc);
}

internal sealed record DocumentoRequeridoPortal(string Tipo, string Nombre, bool Obligatorio, DocumentoCargado? Cargado);

internal sealed record DocumentosPortal(long TamanoMaximoBytes, string[] Formatos, IReadOnlyList<DocumentoRequeridoPortal> Documentos);

internal sealed class ListDocumentosPortalHandler(CasosDbContext db, IPortalActor actor, ITerceroRegistry terceros, IDocumentStore documentos)
{
    public static readonly string[] Formatos = ["PDF", "JPG", "PNG"];

    public async Task<DocumentosPortal> HandleAsync(CancellationToken ct)
    {
        var caso = await CasoPortalQueries.LoadSessionCasoAsync(db, actor, ct);
        var persona = (await terceros.FindByCuitAsync(caso.Cuit, ct))!.Persona;
        var vigentes = (await documentos.ListVigentesAsync(caso.Id, null, ct)).ToDictionary(d => d.Tipo);

        return new DocumentosPortal(
            await documentos.GetTamanoMaximoBytesAsync(ct), Formatos,
            [.. DocumentosRequeridos.For(persona).Select(r => new DocumentoRequeridoPortal(
                r.Tipo, r.Nombre, r.Obligatorio, vigentes.TryGetValue(r.Tipo, out var d) ? DocumentoCargado.From(d) : null))]);
    }
}

internal static class ListDocumentosPortalEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapGet("/caso/documentos",
        async (ListDocumentosPortalHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)));
}
