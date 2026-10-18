using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.ListUsers;

internal static class ListUsersEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/users", Handle).RequireAuthorization(AuthPolicies.UserDirectoryReader);

    private static async Task<IResult> Handle(string? role, Guid? areaId, string? status, IdentidadDbContext db, CancellationToken ct)
    {
        if (role is not null && !DirectoryQueries.ValidRoles.Contains(role)) return ApiProblems.BadRequest("'role' no es un rol válido.");
        bool? active;
        try { active = DirectoryQueries.ParseStatus(status); }
        catch (FormatException) { return ApiProblems.BadRequest("'status' debe ser 'active' o 'inactive'."); }

        var users = db.Users.AsNoTracking();
        if (role is not null) users = users.Where(u => u.Roles.Any(r => r.Role == role));
        if (areaId is { } a) users = users.Where(u => u.AreaId == a);
        if (active is { } s) users = users.Where(u => u.IsActive == s);

        return Results.Ok(await users.OrderBy(u => u.Name).ThenBy(u => u.Email).ToDtos(db).ToListAsync(ct));
    }
}
