using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.ResetPassword;

internal sealed record ResetPasswordRequest(string? Token, string? NewPassword);

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().MaximumLength(200);
        RuleFor(r => r.NewPassword).Must(PasswordHashing.IsValidLength)
            .WithMessage($"La contraseña debe tener entre {PasswordHashing.MinLength} y {PasswordHashing.MaxLength} caracteres.");
    }
}

// Consume enlaces de invitación y de recuperación (R6).
internal sealed class ResetPasswordHandler(IdentidadDbContext db, IAuditLog audit, TimeProvider clock, IHttpContextAccessor http)
{
    public const string InvalidLink = "El enlace no es válido o ya venció.";

    public Task<IResult> HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        db.InTransactionAsync(async (tx, ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var hash = OpaqueToken.Hash(request.Token!);
            var token = await db.PasswordTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is null || token.UsedAtUtc is not null || token.ExpiresAtUtc <= now) return ApiProblems.BadRequest(InvalidLink);

            var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);
            if (!user.IsActive) return ApiProblems.BadRequest(InvalidLink);

            // Un solo uso, también entre solicitudes simultáneas: gana quien marca el token primero.
            var claimed = await db.PasswordTokens.Where(t => t.Id == token.Id && t.UsedAtUtc == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.UsedAtUtc, now), ct);
            if (claimed == 0) return ApiProblems.BadRequest(InvalidLink);

            await db.PasswordTokens.Where(t => t.UserId == user.Id && t.UsedAtUtc == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.UsedAtUtc, now), ct);
            await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);

            user.PasswordHash = PasswordHashing.Hash(request.NewPassword!);
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            await db.SaveChangesAsync(false, ct);

            http.HttpContext!.ActAs(user.Id);
            await audit.RecordAsync(new AuditEntry(AuditAction.PasswordSet, "Usuario", user.Id.ToString(),
                [new AuditChange("Origen", null, token.Purpose)]), tx, ct);
            return Results.NoContent();
        }, cancellationToken);
}

internal static class ResetPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("/auth/password/reset", Handle)
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);

    private static async Task<IResult> Handle(
        ResetPasswordRequest request, IValidator<ResetPasswordRequest> validator, ResetPasswordHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
