using System.Data.Common;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.Shared;

internal sealed record AreaDto(Guid Id, string Name, bool IsActive)
{
    public static AreaDto From(Area a) => new(a.Id, a.Name, a.IsActive);
}

internal sealed record UserDto(
    Guid Id, string Name, string Email, Guid AreaId, string AreaName,
    IReadOnlyList<string> Roles, bool IsActive, bool HasPassword, DateTime? LastLoginAtUtc);

internal static class DirectoryQueries
{
    public static readonly string[] ValidRoles =
    [
        BackofficeRoles.Solicitante, BackofficeRoles.Analista, BackofficeRoles.Aprobador,
        BackofficeRoles.Administrador, BackofficeRoles.Auditor,
    ];

    // PasswordHash nunca sale del módulo: solo se expone si existe (FR-003).
    public static IQueryable<UserDto> ToDtos(this IQueryable<User> users, IdentidadDbContext db) => users.AsNoTracking().Select(u => new UserDto(
        u.Id, u.Name, u.Email, u.AreaId,
        db.Areas.Where(a => a.Id == u.AreaId).Select(a => a.Name).First(),
        u.Roles.Select(r => r.Role).OrderBy(r => r).ToList(),
        u.IsActive, u.PasswordHash != null, u.LastLoginAtUtc));

    // Valores de bitácora: lista ordenada y separada por comas.
    public static string RolesText(IEnumerable<string> roles) => string.Join(",", roles.OrderBy(r => r, StringComparer.Ordinal));

    public static bool? ParseStatus(string? status) => status switch
    {
        null or "" => null,
        "active" => true,
        "inactive" => false,
        _ => throw new FormatException("status"),
    };

    public static async Task<T> InTransactionAsync<T>(
        this DbContext db, Func<DbTransaction, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        T result = default!;
        await db.ExecuteInTransactionAsync(async (tx, ct) => result = await work(tx, ct), cancellationToken);
        return result;
    }
}
