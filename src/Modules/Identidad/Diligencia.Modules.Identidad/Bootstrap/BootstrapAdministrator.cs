using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Identidad.Bootstrap;

// Instalación inicial (FR-014, R8): crea el primer administrador, sin contraseña, solo si no hay ninguno.
internal static class BootstrapAdministrator
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<IdentidadOptions>>().Value.BootstrapAdmin;

        if (string.IsNullOrWhiteSpace(settings.Email)) return;
        if (await db.UserRoles.AnyAsync(r => r.Role == BackofficeRoles.Administrador, cancellationToken)) return;

        var email = Emails.Normalize(settings.Email);
        var areaName = string.IsNullOrWhiteSpace(settings.Area) ? "Compliance" : settings.Area.Trim();
        var name = string.IsNullOrWhiteSpace(settings.Name) ? "Administrador inicial" : settings.Name.Trim();

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            var area = await db.Areas.SingleOrDefaultAsync(a => a.Name == areaName, ct);
            var newArea = area is null;
            if (newArea)
            {
                area = new Area { Name = areaName };
                db.Areas.Add(area);
            }

            var user = new User
            {
                Name = name, Email = email, AreaId = area!.Id,
                Roles = [new UserRole { Role = BackofficeRoles.Administrador }],
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(false, ct);

            if (newArea)
                await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Area", area.Id.ToString(),
                    [new AuditChange("Nombre", null, area.Name), new AuditChange("Estado", null, "Activa")]), tx, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Usuario", user.Id.ToString(),
            [
                new AuditChange("Nombre", null, user.Name),
                new AuditChange("Correo", null, user.Email),
                new AuditChange("Area", null, area.Name),
                new AuditChange("Roles", null, BackofficeRoles.Administrador),
                new AuditChange("Estado", null, "Activo"),
            ]), tx, ct);
        }, cancellationToken);
    }
}
