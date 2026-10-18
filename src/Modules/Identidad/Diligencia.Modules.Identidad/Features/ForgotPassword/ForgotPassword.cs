using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Email;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Identidad.Features.ForgotPassword;

internal sealed record ForgotPasswordRequest(string? Email);

internal sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

internal sealed class ForgotPasswordHandler(
    IdentidadDbContext db, IAuditLog audit, IEmailSender email, TimeProvider clock, IOptions<IdentidadOptions> options)
{
    // shortcut: el envío inline hace que un correo existente tarde más que uno inexistente (canal de tiempo);
    // pasar a una cola cuando el envío deje de ser local, sin guardar el token en claro en los argumentos del job.
    public async Task HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var address = Emails.Normalize(request.Email!);
        var token = OpaqueToken.Generate();

        var user = await db.InTransactionAsync(async (tx, ct) =>
        {
            var found = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == address && u.IsActive, ct);
            if (found is null) return null;

            var now = clock.GetUtcNow().UtcDateTime;
            db.PasswordTokens.Add(new PasswordToken
            {
                UserId = found.Id, Purpose = PasswordPurpose.Reset, TokenHash = OpaqueToken.Hash(token),
                CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(opt.ResetTokenMinutes),
            });
            await db.SaveChangesAsync(false, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.PasswordResetRequested, "Usuario", found.Id.ToString()), tx, ct);
            return found;
        }, cancellationToken);

        if (user is null) return;
        var (subject, body) = EmailTemplates.Reset(
            user.Name, $"{opt.BackofficeBaseUrl.TrimEnd('/')}/restablecer?token={token}", opt.ResetTokenMinutes);
        await email.SendAsync(user.Email, subject, body, cancellationToken);
    }
}

internal static class ForgotPasswordEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("/auth/password/forgot", Handle)
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);

    // Siempre 202 si la solicitud es válida: no revela si el correo existe (SC-006).
    private static async Task<IResult> Handle(
        ForgotPasswordRequest request, IValidator<ForgotPasswordRequest> validator, ForgotPasswordHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ApiProblems.Validation(validation);
        await handler.HandleAsync(request, ct);
        return Results.Accepted();
    }
}
