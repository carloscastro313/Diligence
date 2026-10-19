using System.Text.Json;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Casos.Features.GetFormulario;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.GuardarFormulario;

internal sealed record GuardarFormularioRequest(FormularioDatos Datos, string? Version);

internal sealed class GuardarFormularioHandler(CasosDbContext db, IPortalActor portal, IAuditLog audit, TimeProvider clock)
{
    public async Task<IResult> HandleAsync(GuardarFormularioRequest request, CancellationToken cancellationToken)
    {
        IResult? result = null;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            result = null;
            var now = clock.GetUtcNow().UtcDateTime;
            var caso = await CasoPortalQueries.LockCasoAsync(db, portal.CasoId, ct);
            var formulario = await db.Set<FormularioTercero>().SingleOrDefaultAsync(f => f.CasoId == caso.Id, ct);
            if (!CasoPortalQueries.IsEditable(caso, formulario)) { result = ApiProblems.Conflict("El formulario ya fue enviado: no se puede modificar."); return; }

            // Gana el último guardado; solo se avisa si otra pestaña había guardado desde que se leyó `version` (R7).
            var sobrescrito = request.Version is not null && formulario is not null && request.Version != Convert.ToBase64String(formulario.Version);
            var json = JsonSerializer.Serialize(request.Datos, FormularioJson.Options);
            var cambios = JsonDiff.Diff(formulario?.Datos, json);
            if (formulario is null) db.Add(formulario = new FormularioTercero { CasoId = caso.Id });
            formulario.Datos = json;
            formulario.GuardadoUtc = now;
            await db.SaveChangesAsync(false, ct);
            // Con acceptAllChanges=false la entidad conserva su `Version` anterior (vacía en el alta): se lee el rowversion recién escrito.
            var version = await db.Set<FormularioTercero>().AsNoTracking().Where(f => f.CasoId == caso.Id).Select(f => f.Version).SingleAsync(ct);

            if (cambios.Count > 0)
                await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "FormularioTercero", caso.Id.ToString(), cambios), tx, ct);
            result = Results.Ok(new { version = Convert.ToBase64String(version), guardadoUtc = now, sobrescrito });
        }, cancellationToken);

        return result!;
    }
}

internal static class GuardarFormularioEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapPut("/caso/formulario", Handle);

    private static async Task<IResult> Handle(
        HttpRequest http, IValidator<GuardarFormularioRequest> validator, GuardarFormularioHandler handler, CancellationToken ct)
    {
        var (request, error) = await FormularioHttp.ReadAsync<GuardarFormularioRequest>(http, ct);
        if (error is not null) return error;
        var validation = await validator.ValidateAsync(request!, ct);
        return validation.IsValid ? await handler.HandleAsync(request!, ct) : ApiProblems.Validation(FormularioHttp.StripDatosPrefix(validation));
    }
}
