using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.SetAreaStatus;

internal sealed class SetAreaStatusHandler(IdentidadDbContext db, IAuditLog audit)
{
    // Idempotente: repetir la operación no cambia nada ni deja evento.
    public Task<IResult> HandleAsync(Guid id, bool active, CancellationToken cancellationToken) =>
        db.InTransactionAsync(async (tx, ct) =>
        {
            var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (area is null) return ApiProblems.NotFound("El área no existe.");
            if (area.IsActive != active)
            {
                area.IsActive = active;
                await db.SaveChangesAsync(false, ct);
                await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Area", id.ToString(),
                    [new AuditChange("Estado", active ? "Inactiva" : "Activa", active ? "Activa" : "Inactiva")]), tx, ct);
            }
            return Results.NoContent();
        }, cancellationToken);
}

internal static class SetAreaStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/areas/{id:guid}/deactivate", (Guid id, SetAreaStatusHandler h, CancellationToken ct) => h.HandleAsync(id, false, ct))
            .RequireAuthorization(AuthPolicies.Administrator);
        group.MapPost("/areas/{id:guid}/reactivate", (Guid id, SetAreaStatusHandler h, CancellationToken ct) => h.HandleAsync(id, true, ct))
            .RequireAuthorization(AuthPolicies.Administrator);
    }
}
