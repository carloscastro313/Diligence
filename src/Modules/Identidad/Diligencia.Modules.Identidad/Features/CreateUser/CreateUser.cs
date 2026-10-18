using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Email;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Identidad.Features.CreateUser;

// También lo usa UpdateUser: el cuerpo de alta y de edición es el mismo.
internal sealed record UserRequest(string? Name, string? Email, Guid? AreaId, string[]? Roles);

internal sealed class UserRequestValidator : AbstractValidator<UserRequest>
{
    public UserRequestValidator()
    {
        RuleFor(r => r.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre es obligatorio.").MaximumLength(200);
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(r => r.AreaId).NotNull().NotEqual(Guid.Empty).WithMessage("El área es obligatoria.");
        RuleFor(r => r.Roles).NotEmpty().WithMessage("El usuario debe tener al menos un rol.");
        RuleForEach(r => r.Roles).Must(role => DirectoryQueries.ValidRoles.Contains(role)).WithMessage("Rol no válido.");
        RuleFor(r => r.Roles).Must(roles => roles!.Distinct().Count() == roles!.Length).When(r => r.Roles is not null)
            .WithMessage("Los roles no pueden repetirse.");
    }
}

internal sealed class CreateUserHandler(
    IdentidadDbContext db, IAuditLog audit, IEmailSender email, TimeProvider clock, IOptions<IdentidadOptions> options)
{
    public async Task<IResult> HandleAsync(UserRequest request, CancellationToken cancellationToken)
    {
        var opt = options.Value;
        var address = Emails.Normalize(request.Email!);
        var token = OpaqueToken.Generate();
        User? created = null;

        var result = await db.InTransactionAsync(async (tx, ct) =>
        {
            var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == request.AreaId, ct);
            if (area is not { IsActive: true }) return ApiProblems.Conflict("El área no existe o está desactivada.");
            if (await db.Users.AnyAsync(u => u.Email == address, ct)) return ApiProblems.Conflict("Ya existe un usuario con ese correo.");

            var now = clock.GetUtcNow().UtcDateTime;
            var user = new User
            {
                Name = request.Name!.Trim(), Email = address, AreaId = area.Id,
                Roles = [.. request.Roles!.Select(r => new UserRole { Role = r })],
            };
            db.Users.Add(user);
            db.PasswordTokens.Add(new PasswordToken
            {
                UserId = user.Id, Purpose = PasswordPurpose.Invitation, TokenHash = OpaqueToken.Hash(token),
                CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(opt.InvitationTokenHours),
            });
            await db.SaveChangesAsync(false, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Usuario", user.Id.ToString(),
            [
                new AuditChange("Nombre", null, user.Name),
                new AuditChange("Correo", null, user.Email),
                new AuditChange("Area", null, area.Name),
                new AuditChange("Roles", null, DirectoryQueries.RolesText(request.Roles!)),
                new AuditChange("Estado", null, "Activo"),
            ]), tx, ct);
            created = user;
            return Results.Ok();
        }, cancellationToken);

        if (created is null) return result;

        // Después de confirmar: si el envío falla el usuario existe y puede usar "olvidé mi contraseña".
        var (subject, body) = EmailTemplates.Invitation(
            created.Name, $"{opt.BackofficeBaseUrl.TrimEnd('/')}/restablecer?token={token}", opt.InvitationTokenHours);
        await email.SendAsync(created.Email, subject, body, cancellationToken);

        var dto = await db.Users.Where(u => u.Id == created.Id).ToDtos(db).SingleAsync(cancellationToken);
        return Results.Created($"/api/backoffice/users/{dto.Id}", dto);
    }
}

internal static class CreateUserEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/users", Handle).RequireAuthorization(AuthPolicies.Administrator);

    private static async Task<IResult> Handle(
        UserRequest request, IValidator<UserRequest> validator, CreateUserHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
