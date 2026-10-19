using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Casos.Features.ListDocumentosPortal;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.SubirDocumento;

internal sealed record SubirDocumentoRequest(string Tipo, IFormFile? Archivo);

internal sealed class SubirDocumentoValidator : AbstractValidator<SubirDocumentoRequest>
{
    public SubirDocumentoValidator()
    {
        RuleFor(r => r.Archivo).Must(a => a is { Length: > 0 }).WithMessage("Elegí un archivo para subir.");
        RuleFor(r => r.Archivo!.FileName).MaximumLength(255).WithMessage("El nombre del archivo es demasiado largo.")
            .When(r => r.Archivo is not null);
    }
}

internal sealed class SubirDocumentoHandler(
    CasosDbContext db, IPortalActor actor, ITerceroRegistry terceros, IDocumentStore documentos)
{
    private const long Mb = 1024 * 1024;

    public async Task<IResult> HandleAsync(SubirDocumentoRequest request, CancellationToken cancellationToken)
    {
        var archivo = request.Archivo!;
        // Corta antes de leer nada: IFormFile.Length ya se conoce. El almacén vuelve a medir los bytes leídos.
        var maximo = await documentos.GetTamanoMaximoBytesAsync(cancellationToken);
        if (archivo.Length > maximo) return TooBig(maximo);

        IResult? result = null;
        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            result = null;
            var caso = await CasoPortalQueries.LockCasoAsync(db, actor.CasoId, ct);
            var formulario = await db.Set<FormularioTercero>().AsNoTracking().SingleOrDefaultAsync(f => f.CasoId == caso.Id, ct);
            if (!CasoPortalQueries.IsEditable(caso, formulario)) { result = ApiProblems.Conflict("El formulario ya fue enviado: no se puede modificar."); return; }

            var persona = (await terceros.FindByCuitAsync(caso.Cuit, ct))!.Persona;
            if (DocumentosRequeridos.For(persona).All(d => d.Tipo != request.Tipo))
            {
                result = ApiProblems.Validation(new ValidationResult([new ValidationFailure("Tipo", "Ese documento no corresponde a tu tipo de persona.")]));
                return;
            }

            await using var contenido = archivo.OpenReadStream();
            var (documento, rechazo) = await documentos.PutAsync(
                new NuevoDocumento(caso.Id, actor.TerceroId, request.Tipo, archivo.FileName), contenido, tx, ct);
            result = rechazo switch
            {
                null => Results.Ok(DocumentoCargado.From(documento!)),
                { Motivo: MotivoRechazo.DemasiadoGrande } r => TooBig(r.TamanoMaximoBytes),
                { Motivo: MotivoRechazo.Ilegible } => ApiProblems.BadRequest("No se puede leer el archivo. Subí una copia completa."),
                _ => ApiProblems.BadRequest("Formato no permitido. Formatos aceptados: PDF, JPG, PNG."),
            };
        }, cancellationToken);

        return result!;
    }

    private static IResult TooBig(long maximo) => ApiProblems.PayloadTooLarge($"El archivo supera el máximo permitido de {maximo / Mb} MB.");
}

internal static class SubirDocumentoEndpoint
{
    // Antiforgery desactivado: la cookie SameSite=Strict cubre CSRF, como en el backoffice.
    public static void Map(RouteGroupBuilder portal) => portal.MapPut("/caso/documentos/{tipo}", Handle)
        .DisableAntiforgery()
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(55 * 1024 * 1024));

    private static async Task<IResult> Handle(
        string tipo, IFormFile? archivo, IValidator<SubirDocumentoRequest> validator, SubirDocumentoHandler handler, CancellationToken ct)
    {
        var request = new SubirDocumentoRequest(tipo, archivo);
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
