using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.SetUserStatus;

internal sealed class SetUserStatusHandler(IdentidadDbContext db, IAuditLog audit, TimeProvider clock)
{
    // Idempotente. Desactivar conserva los roles (reactivar los recupera) y revoca las sesiones abiertas.
    public Task<IResult> HandleAsync(Guid id, bool active, CancellationToken cancellationToken) =>
        db.InTransactionAsync(async (tx, ct) =>
        {
            var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return ApiProblems.NotFound("El usuario no existe.");
            if (user.IsActive == active) return Results.NoContent();

            if (!active && user.Roles.Any(r => r.Role == BackofficeRoles.Administrador)
                && !await LastAdministratorGuard.AnotherActiveAdministratorExistsAsync(db, id, ct))
                return ApiProblems.Conflict(LastAdministratorGuard.Conflict);

            user.IsActive = active;
            await db.SaveChangesAsync(false, ct);
            if (!active)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                await db.Sessions.Where(s => s.UserId == id && s.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
            }
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Usuario", id.ToString(),
                [new AuditChange("Estado", active ? "Desactivado" : "Activo", active ? "Activo" : "Desactivado")]), tx, ct);
            return Results.NoContent();
        }, cancellationToken);
}

internal static class SetUserStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/users/{id:guid}/deactivate", (Guid id, SetUserStatusHandler h, CancellationToken ct) => h.HandleAsync(id, false, ct))
            .RequireAuthorization(AuthPolicies.Administrator);
        group.MapPost("/users/{id:guid}/reactivate", (Guid id, SetUserStatusHandler h, CancellationToken ct) => h.HandleAsync(id, true, ct))
            .RequireAuthorization(AuthPolicies.Administrator);
    }
}
