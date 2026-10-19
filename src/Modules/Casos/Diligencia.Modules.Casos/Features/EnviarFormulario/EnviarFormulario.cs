using System.Text.Json;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Casos.Features.GetFormulario;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.EnviarFormulario;

// Sin versión: el envío no compara con otras pestañas, lo que manda es lo que queda.
internal sealed record EnviarFormularioRequest(FormularioDatos Datos);

internal sealed class EnviarFormularioHandler(
    CasosDbContext db, IPortalActor portal, IActorContext actor, ITerceroRegistry terceros, IDocumentStore documentos,
    IAuditLog audit, TimeProvider clock)
{
    public async Task<IResult> HandleAsync(EnviarFormularioRequest request, CancellationToken cancellationToken)
    {
        IResult? result = null;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            result = null;
            var now = clock.GetUtcNow().UtcDateTime;
            // Mismo bloqueo que la carga de documentos: ningún documento entra o sale después de enviar (R7).
            var caso = await CasoPortalQueries.LockCasoAsync(db, portal.CasoId, ct);
            var formulario = await db.Set<FormularioTercero>().SingleOrDefaultAsync(f => f.CasoId == caso.Id, ct);
            if (!CasoPortalQueries.IsEditable(caso, formulario)) { result = ApiProblems.Conflict("El formulario ya fue enviado: no se puede modificar."); return; }

            var persona = (await terceros.FindByCuitAsync(caso.Cuit, ct))!.Persona;
            var validation = new FormularioEnvioValidator(persona, BusinessCalendar.Today(clock)).Validate(request.Datos);
            var errores = new List<ValidationFailure>(validation.Errors);
            var cargados = (await documentos.ListVigentesAsync(caso.Id, tx, ct)).Select(d => d.Tipo).ToHashSet();
            errores.AddRange(DocumentosRequeridos.For(persona).Where(d => d.Obligatorio && !cargados.Contains(d.Tipo))
                .Select(d => new ValidationFailure("Documentos", $"Falta subir: {d.Nombre}.")));
            if (errores.Count > 0) { result = ApiProblems.Validation(new ValidationResult(errores)); return; }

            var json = JsonSerializer.Serialize(request.Datos, FormularioJson.Options);
            var cambios = JsonDiff.Diff(formulario?.Datos, json);
            var estadoAnterior = formulario?.Estado.ToString();
            if (formulario is null) db.Add(formulario = new FormularioTercero { CasoId = caso.Id });
            formulario.Datos = json;
            formulario.Estado = EstadoFormulario.Enviado;
            formulario.GuardadoUtc = now;
            formulario.EnviadoUtc = now;
            formulario.EnviadoPor = actor.ActorId;
            var estadoCaso = caso.Estado.ToString();
            caso.Estado = EstadoCaso.EnVerificacion;
            caso.EstadoDesdeUtc = now;
            await db.SaveChangesAsync(false, ct);

            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "FormularioTercero", caso.Id.ToString(),
                [.. cambios, new AuditChange("Estado", estadoAnterior, nameof(EstadoFormulario.Enviado))]), tx, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Caso", caso.Id.ToString(),
                [new AuditChange("Estado", estadoCaso, nameof(EstadoCaso.EnVerificacion))]), tx, ct);
            // Con acceptAllChanges=false la entidad conserva su `Version` anterior: se relee la fila para devolver el rowversion nuevo.
            result = FormularioHttp.Ok(FormularioResponse.From(
                await db.Set<FormularioTercero>().AsNoTracking().SingleAsync(f => f.CasoId == caso.Id, ct)));
        }, cancellationToken);

        return result!;
    }
}

internal static class EnviarFormularioEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapPost("/caso/formulario/envio", Handle);

    private static async Task<IResult> Handle(HttpRequest http, EnviarFormularioHandler handler, CancellationToken ct)
    {
        var (request, error) = await FormularioHttp.ReadAsync<EnviarFormularioRequest>(http, ct);
        if (error is not null) return error;
        return request!.Datos is null
            ? ApiProblems.Validation(new ValidationResult([new ValidationFailure("Datos", "Los datos del formulario son obligatorios.")]))
            : await handler.HandleAsync(request, ct);
    }
}
