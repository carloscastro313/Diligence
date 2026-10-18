using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.CreateArea;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.RenameArea;

internal sealed class RenameAreaHandler(IdentidadDbContext db, IAuditLog audit)
{
    public async Task<IResult> HandleAsync(Guid id, AreaRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        return await db.InTransactionAsync(async (tx, ct) =>
        {
            var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (area is null) return ApiProblems.NotFound("El área no existe.");
            if (await db.Areas.AnyAsync(a => a.Id != id && a.Name == name, ct)) return ApiProblems.Conflict("Ya existe un área con ese nombre.");

            if (area.Name != name)
            {
                var old = area.Name;
                area.Name = name;
                await db.SaveChangesAsync(false, ct);
                await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Area", id.ToString(),
                    [new AuditChange("Nombre", old, name)]), tx, ct);
            }
            return Results.Ok(AreaDto.From(area));
        }, cancellationToken);
    }
}

internal static class RenameAreaEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/areas/{id:guid}", Handle).RequireAuthorization(AuthPolicies.Administrator);

    private static async Task<IResult> Handle(
        Guid id, AreaRequest request, IValidator<AreaRequest> validator, RenameAreaHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(id, request, ct) : ApiProblems.Validation(validation);
    }
}
