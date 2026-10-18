using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.Me;

internal sealed record MeResponse(Guid Id, string Name, string Email, string AreaName, IReadOnlyList<string> Roles);

internal static class MeEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("/auth/me", Handle);

    private static async Task<IResult> Handle(IdentidadDbContext db, HttpContext http, CancellationToken ct)
    {
        var id = http.UserId();
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new
        {
            u.Id, u.Name, u.Email,
            AreaName = db.Areas.Where(a => a.Id == u.AreaId).Select(a => a.Name).First(),
            Roles = u.Roles.Select(r => r.Role).OrderBy(r => r).ToList(),
        }).SingleAsync(ct);
        return Results.Ok(new MeResponse(user.Id, user.Name, user.Email, user.AreaName, user.Roles));
    }
}
