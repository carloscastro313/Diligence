using Diligencia.BuildingBlocks.Email;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Diligencia.Modules.Casos.Features.SolicitarCodigo;

internal sealed record SolicitarCodigoRequest(string? Token);

internal sealed class SolicitarCodigoValidator : AbstractValidator<SolicitarCodigoRequest>
{
    public SolicitarCodigoValidator() => RuleFor(r => r.Token).NotEmpty().WithMessage("El enlace no es válido.").MaximumLength(100);
}

internal sealed class SolicitarCodigoHandler(
    CasosDbContext db, IAuditLog audit, IEmailTransport email, TimeProvider clock, ILogger<SolicitarCodigoHandler> logger)
{
    public const int VigenciaMinutos = 10;
    public const int MaxEnviosPorHora = 5;

    public async Task<IResult> HandleAsync(SolicitarCodigoRequest request, CancellationToken cancellationToken)
    {
        IResult? failure = null;
        Guid codigoId = default;
        var codigo = "";
        var destinatario = "";

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            failure = null;
            var now = clock.GetUtcNow().UtcDateTime;
            var lookup = await EnlaceAcceso.FindValidAsync(db, request.Token!, now, ct);
            if (lookup.Status == EnlaceStatus.Invalid) { failure = ApiProblems.NotFound(PortalMessages.EnlaceInvalido); return; }
            var enlace = lookup.Enlace!;
            if (lookup.Status == EnlaceStatus.Locked)
            {
                failure = ApiProblems.TooManyRequests("Demasiados intentos. Probá de nuevo más tarde.",
                    EnlaceAcceso.SecondsUntil(enlace.BloqueadoHastaUtc!.Value, now));
                return;
            }

            var recientes = await db.Set<CodigoIngreso>()
                .Where(c => c.EnlaceId == enlace.Id && c.CreatedAtUtc > now.AddHours(-1))
                .OrderBy(c => c.CreatedAtUtc).Select(c => c.CreatedAtUtc).ToListAsync(ct);
            if (recientes.Count >= MaxEnviosPorHora)
            {
                await audit.RecordAsync(new AuditEntry(AuditAction.AccessDenied, "EnlacePortal", enlace.Id.ToString(),
                    [new AuditChange("Motivo", null, "LimiteCodigos")]), tx, ct);
                failure = ApiProblems.TooManyRequests("Pediste demasiados códigos. Probá de nuevo más tarde.",
                    EnlaceAcceso.SecondsUntil(recientes[0].AddHours(1), now));
                return;
            }

            foreach (var vigente in await db.Set<CodigoIngreso>().Where(c => c.EnlaceId == enlace.Id && c.Estado == EstadoCodigo.Vigente).ToListAsync(ct))
                vigente.Estado = EstadoCodigo.Invalidado;

            codigo = AccessCode.Generate();
            var nuevo = new CodigoIngreso { EnlaceId = enlace.Id, CodigoHash = AccessCode.Hash(enlace.Id, codigo), VenceUtc = now.AddMinutes(VigenciaMinutos) };
            db.Add(nuevo);
            await db.SaveChangesAsync(false, ct);

            // Nunca el código: solo el enlace y el vencimiento.
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "CodigoIngreso", nuevo.Id.ToString(),
            [
                new AuditChange("Enlace", null, enlace.Id.ToString()),
                new AuditChange("VenceUtc", null, nuevo.VenceUtc.ToString("O")),
            ]), tx, ct);
            codigoId = nuevo.Id;
            destinatario = enlace.Destinatario;
        }, cancellationToken);

        if (failure is not null) return failure;

        // shortcut: envío inline como ForgotPassword; pasar a cola si el SMTP deja de ser local
        try
        {
            await email.SendAsync(destinatario, "Tu código de ingreso al portal",
                $"""
                Hola,

                Tu código de ingreso es: {codigo}

                Vence en {VigenciaMinutos} minutos. No lo compartas con nadie.
                """, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca se registra el código ni el cuerpo.
            logger.LogWarning(ex, "Falló el envío del código de ingreso {CodigoId}", codigoId);
            await Invalidar(codigoId, cancellationToken);
            return ApiProblems.ServiceUnavailable("No pudimos enviar el código. Probá de nuevo en unos minutos.");
        }

        return Results.Accepted(value: new { correo = EmailMask.Mask(destinatario), venceEnMinutos = VigenciaMinutos });
    }

    private Task Invalidar(Guid codigoId, CancellationToken cancellationToken) =>
        db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Set<CodigoIngreso>().Where(c => c.Id == codigoId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, EstadoCodigo.Invalidado).SetProperty(c => c.UpdatedAtUtc, now), ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "CodigoIngreso", codigoId.ToString(),
            [
                new AuditChange("Estado", EstadoCodigo.Vigente.ToString(), EstadoCodigo.Invalidado.ToString()),
                new AuditChange("Motivo", null, "EnvioFallido"),
            ]), tx, ct);
        }, cancellationToken);
}

internal static class SolicitarCodigoEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapPost("/acceso/codigo", Handle)
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.PortalAcceso);

    private static async Task<IResult> Handle(
        SolicitarCodigoRequest request, IValidator<SolicitarCodigoRequest> validator, SolicitarCodigoHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
