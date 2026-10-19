using System.Data.Common;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.Ingresar;

internal sealed record IngresarRequest(string? Token, string? Codigo);

internal sealed class IngresarValidator : AbstractValidator<IngresarRequest>
{
    public IngresarValidator()
    {
        RuleFor(r => r.Token).NotEmpty().WithMessage("El enlace no es válido.").MaximumLength(100);
        RuleFor(r => r.Codigo).NotEmpty().WithMessage("Ingresá el código.")
            .Matches(@"^\d{6}$").WithMessage("El código debe tener 6 dígitos.");
    }
}

internal sealed class IngresarHandler(CasosDbContext db, IAuditLog audit, IPortalSignIn signIn, TimeProvider clock)
{
    public const int MaxIntentosPorCodigo = 5;
    public const int MaxFallidosPorEnlace = 10;
    public const int BloqueoMinutos = 30;

    private static readonly Dictionary<string, object?> CodigoInvalidado = new() { ["codigoInvalidado"] = true };

    public async Task<IResult> HandleAsync(IngresarRequest request, CancellationToken cancellationToken)
    {
        IResult? result = null;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            result = null;
            var now = clock.GetUtcNow().UtcDateTime;
            var lookup = await EnlaceAcceso.FindValidAsync(db, request.Token!, now, ct);
            if (lookup.Status == EnlaceStatus.Invalid)
            {
                await Failed(tx, "desconocido", "EnlaceInvalido", ct);
                result = ApiProblems.NotFound(PortalMessages.EnlaceInvalido);
                return;
            }
            var enlace = lookup.Enlace!;
            if (lookup.Status == EnlaceStatus.Locked)
            {
                result = ApiProblems.TooManyRequests("Demasiados intentos. Probá de nuevo más tarde.",
                    EnlaceAcceso.SecondsUntil(enlace.BloqueadoHastaUtc!.Value, now));
                return;
            }

            var codigo = await db.Set<CodigoIngreso>().Where(c => c.EnlaceId == enlace.Id && c.Estado == EstadoCodigo.Vigente)
                .OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if (codigo is null || codigo.VenceUtc <= now)
            {
                await Failed(tx, enlace.Id.ToString(), codigo is null ? "SinCodigo" : "CodigoVencido", ct);
                result = ApiProblems.Unauthorized("El código venció. Pedí uno nuevo.", CodigoInvalidado);
                return;
            }

            if (AccessCode.Matches(enlace.Id, request.Codigo!, codigo.CodigoHash))
            {
                codigo.Estado = EstadoCodigo.Usado;
                enlace.FallidosConsecutivos = 0;
                await db.SaveChangesAsync(false, ct);
                await signIn.SignInAsync(lookup.Caso!.Id, lookup.Caso.TerceroId, tx, ct);
                await audit.RecordAsync(new AuditEntry(AuditAction.LoginSucceeded, "EnlacePortal", enlace.Id.ToString()), tx, ct);
                result = Results.NoContent();
                return;
            }

            var invalidado = ++codigo.IntentosFallidos >= MaxIntentosPorCodigo;
            if (invalidado) codigo.Estado = EstadoCodigo.Invalidado;
            var bloqueado = ++enlace.FallidosConsecutivos >= MaxFallidosPorEnlace;
            if (bloqueado)
            {
                enlace.FallidosConsecutivos = 0;
                enlace.BloqueadoHastaUtc = now.AddMinutes(BloqueoMinutos);
            }
            await db.SaveChangesAsync(false, ct);

            await Failed(tx, enlace.Id.ToString(), "CodigoIncorrecto", ct);
            if (bloqueado)
                await audit.RecordAsync(new AuditEntry(AuditAction.AccountLocked, "EnlacePortal", enlace.Id.ToString(),
                    [new AuditChange("BloqueadoHasta", null, enlace.BloqueadoHastaUtc!.Value.ToString("O"))]), tx, ct);

            result = bloqueado
                ? ApiProblems.TooManyRequests("Demasiados intentos. Probá de nuevo más tarde.", BloqueoMinutos * 60)
                : ApiProblems.Unauthorized(invalidado ? "El código no es válido. Pedí uno nuevo." : "El código no es válido.",
                    invalidado ? CodigoInvalidado : null);
        }, cancellationToken);

        return result!;
    }

    private Task Failed(DbTransaction tx, string entityId, string motivo, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(AuditAction.LoginFailed, "EnlacePortal", entityId, [new AuditChange("Motivo", null, motivo)]), tx, ct);
}

internal static class IngresarEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapPost("/acceso", Handle)
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.PortalAcceso);

    private static async Task<IResult> Handle(
        IngresarRequest request, IValidator<IngresarRequest> validator, IngresarHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
