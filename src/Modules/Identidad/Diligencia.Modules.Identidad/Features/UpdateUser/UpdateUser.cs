using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.CreateUser;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.UpdateUser;

internal sealed class UpdateUserHandler(IdentidadDbContext db, IAuditLog audit)
{
    public async Task<IResult> HandleAsync(Guid id, UserRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        var address = Emails.Normalize(request.Email!);
        var newRoles = request.Roles!.ToHashSet();

        var outcome = await db.InTransactionAsync(async (tx, ct) =>
        {
            var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return ApiProblems.NotFound("El usuario no existe.");

            // El área solo se valida si cambia: un usuario cuya área ya fue desactivada puede editarse sin cambiarla.
            var oldArea = await db.Areas.SingleAsync(a => a.Id == user.AreaId, ct);
            var area = oldArea;
            if (request.AreaId != user.AreaId)
            {
                area = await db.Areas.SingleOrDefaultAsync(a => a.Id == request.AreaId, ct);
                if (area is not { IsActive: true }) return ApiProblems.Conflict("El área no existe o está desactivada.");
            }
            if (address != user.Email && await db.Users.AnyAsync(u => u.Email == address, ct))
                return ApiProblems.Conflict("Ya existe un usuario con ese correo.");

            var oldRoles = user.Roles.Select(r => r.Role).ToHashSet();
            if (user.IsActive && oldRoles.Contains(BackofficeRoles.Administrador) && !newRoles.Contains(BackofficeRoles.Administrador)
                && !await LastAdministratorGuard.AnotherActiveAdministratorExistsAsync(db, id, ct))
                return ApiProblems.Conflict(LastAdministratorGuard.Conflict);

            var changes = new List<AuditChange>();
            if (user.Name != name) { changes.Add(new AuditChange("Nombre", user.Name, name)); user.Name = name; }
            if (user.Email != address) { changes.Add(new AuditChange("Correo", user.Email, address)); user.Email = address; }
            if (user.AreaId != area.Id) { changes.Add(new AuditChange("Area", oldArea.Name, area.Name)); user.AreaId = area.Id; }
            if (!oldRoles.SetEquals(newRoles))
            {
                changes.Add(new AuditChange("Roles", DirectoryQueries.RolesText(oldRoles), DirectoryQueries.RolesText(newRoles)));
                db.UserRoles.RemoveRange(user.Roles.Where(r => !newRoles.Contains(r.Role)));
                db.UserRoles.AddRange(newRoles.Where(r => !oldRoles.Contains(r)).Select(r => new UserRole { UserId = id, Role = r }));
            }

            if (changes.Count > 0)
            {
                await db.SaveChangesAsync(false, ct);
                await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Usuario", id.ToString(), changes), tx, ct);
            }
            return Results.Ok();
        }, cancellationToken);

        // Un 200 sin cuerpo marca éxito; el DTO se lee ya confirmado.
        return outcome is Microsoft.AspNetCore.Http.HttpResults.Ok
            ? Results.Ok(await db.Users.Where(u => u.Id == id).ToDtos(db).SingleAsync(cancellationToken))
            : outcome;
    }
}

internal static class UpdateUserEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/users/{id:guid}", Handle).RequireAuthorization(AuthPolicies.Administrator);

    private static async Task<IResult> Handle(
        Guid id, UserRequest request, IValidator<UserRequest> validator, UpdateUserHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(id, request, ct) : ApiProblems.Validation(validation);
    }
}
