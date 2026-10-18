using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.Shared;

// R9: serializa los cambios que podrían dejar cero administradores activos.
internal static class LastAdministratorGuard
{
    /// <summary>Debe llamarse dentro de la transacción del cambio. True = otro administrador activo seguirá existiendo.</summary>
    public static async Task<bool> AnotherActiveAdministratorExistsAsync(IdentidadDbContext db, Guid userId, CancellationToken ct)
    {
        var others = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value]
            FROM [identidad].[UserRoles] r WITH (UPDLOCK, HOLDLOCK)
            JOIN [identidad].[Users] u WITH (UPDLOCK, HOLDLOCK) ON u.[Id] = r.[UserId]
            WHERE r.[Role] = {BackofficeRoles.Administrador} AND u.[IsActive] = 1 AND u.[Id] <> {userId}
            """).SingleAsync(ct);
        return others > 0;
    }

    public const string Conflict = "No se puede dejar el sistema sin un administrador activo.";
}
