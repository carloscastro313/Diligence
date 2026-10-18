using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.CreateArea;

// También lo usa RenameArea: el cuerpo de alta y de edición es el mismo.
internal sealed record AreaRequest(string? Name);

internal sealed class AreaRequestValidator : AbstractValidator<AreaRequest>
{
    public AreaRequestValidator() => RuleFor(r => r.Name).Must(n => !string.IsNullOrWhiteSpace(n))
        .WithMessage("El nombre es obligatorio.").MaximumLength(100);
}

internal sealed class CreateAreaHandler(IdentidadDbContext db, IAuditLog audit)
{
    public async Task<IResult> HandleAsync(AreaRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        return await db.InTransactionAsync(async (tx, ct) =>
        {
            if (await db.Areas.AnyAsync(a => a.Name == name, ct)) return ApiProblems.Conflict("Ya existe un área con ese nombre.");

            var area = new Area { Name = name };
            db.Areas.Add(area);
            await db.SaveChangesAsync(false, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Area", area.Id.ToString(),
                [new AuditChange("Nombre", null, area.Name), new AuditChange("Estado", null, "Activa")]), tx, ct);
            return Results.Created($"/api/backoffice/areas/{area.Id}", AreaDto.From(area));
        }, cancellationToken);
    }
}

internal static class CreateAreaEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/areas", Handle).RequireAuthorization(AuthPolicies.Administrator);

    private static async Task<IResult> Handle(
        AreaRequest request, IValidator<AreaRequest> validator, CreateAreaHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
