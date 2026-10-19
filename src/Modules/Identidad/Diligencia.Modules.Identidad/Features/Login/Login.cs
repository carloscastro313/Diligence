using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Identidad.Features.Login;

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(1000);
    }
}

internal sealed record LoginResult(string Token, DateTime ExpiresAtUtc);

internal sealed class LoginHandler(
    IdentidadDbContext db, IAuditLog audit, TokenIssuer tokens, TimeProvider clock,
    IOptions<IdentidadOptions> options, IHttpContextAccessor http)
{
    // Null = rechazo; el motivo real queda solo en la bitácora (FR-002, R4).
    public async Task<LoginResult?> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var email = Emails.Normalize(request.Email!);
        LoginResult? result = null;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
            string? reason = null;

            if (user is null) { PasswordHashing.VerifyDummy(request.Password!); reason = "CredencialInvalida"; }
            else if (!user.IsActive) { PasswordHashing.VerifyDummy(request.Password!); reason = "Desactivado"; }
            else if (user.LockoutEndUtc > now) { PasswordHashing.VerifyDummy(request.Password!); reason = "Bloqueado"; }
            else if (user.PasswordHash is null) { PasswordHashing.VerifyDummy(request.Password!); reason = "SinContraseña"; }
            else if (!PasswordHashing.Verify(request.Password!, user.PasswordHash)) reason = "CredencialInvalida";

            if (reason is null)
            {
                user!.FailedLoginCount = 0;
                user.LockoutEndUtc = null;
                user.LastLoginAtUtc = now;
                var session = new Session
                {
                    UserId = user.Id, CreatedAtUtc = now, LastActivityUtc = now,
                    ExpiresAtUtc = now.AddHours(opt.SessionAbsoluteHours),
                };
                db.Sessions.Add(session);
                await db.SaveChangesAsync(false, ct);
                http.HttpContext!.ActAs(user.Id);
                await audit.RecordAsync(new AuditEntry(AuditAction.LoginSucceeded, "Usuario", user.Id.ToString()), tx, ct);
                result = new LoginResult(tokens.Issue(AuthSchemes.Backoffice, user.Id.ToString(), session.Id, session.ExpiresAtUtc), session.ExpiresAtUtc);
                return;
            }

            var locked = false;
            if (reason == "CredencialInvalida" && user is not null && ++user.FailedLoginCount >= opt.MaxFailedLogins)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndUtc = now.AddMinutes(opt.LockoutMinutes);
                locked = true;
            }
            await db.SaveChangesAsync(false, ct);

            var entityId = user?.Id.ToString() ?? "desconocido";
            await audit.RecordAsync(new AuditEntry(AuditAction.LoginFailed, "Usuario", entityId,
                [new AuditChange("Motivo", null, reason)]), tx, ct);
            if (locked)
                await audit.RecordAsync(new AuditEntry(AuditAction.AccountLocked, "Usuario", entityId,
                    [new AuditChange("BloqueadoHasta", null, user!.LockoutEndUtc!.Value.ToString("O"))]), tx, ct);
        }, cancellationToken);

        return result;
    }
}

internal static class LoginEndpoint
{
    public const string FailureTitle = "Correo o contraseña incorrectos.";

    public static void Map(RouteGroupBuilder group) => group.MapPost("/auth/login", Handle)
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Anonymous);

    private static async Task<IResult> Handle(
        LoginRequest request, IValidator<LoginRequest> validator, LoginHandler handler, HttpContext http, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ApiProblems.Validation(validation);

        if (await handler.HandleAsync(request, ct) is not { } result) return ApiProblems.Unauthorized(FailureTitle);

        http.Response.Cookies.Append(SessionCookie.Name, result.Token, SessionCookie.Options(result.ExpiresAtUtc));
        return Results.NoContent();
    }
}
