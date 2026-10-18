using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.ListAreas;

internal static class ListAreasEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/areas", Handle).RequireAuthorization(AuthPolicies.UserDirectoryReader);

    private static async Task<IResult> Handle(string? status, IdentidadDbContext db, CancellationToken ct)
    {
        bool? active;
        try { active = DirectoryQueries.ParseStatus(status); }
        catch (FormatException) { return ApiProblems.BadRequest("'status' debe ser 'active' o 'inactive'."); }

        var query = db.Areas.AsNoTracking();
        if (active is { } a) query = query.Where(x => x.IsActive == a);
        var areas = await query.OrderBy(x => x.Name).ToListAsync(ct);
        return Results.Ok(areas.Select(AreaDto.From));
    }
}
