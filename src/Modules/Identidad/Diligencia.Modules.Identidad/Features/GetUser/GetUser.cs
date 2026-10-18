using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.GetUser;

internal static class GetUserEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/users/{id:guid}", Handle).RequireAuthorization(AuthPolicies.UserDirectoryReader);

    private static async Task<IResult> Handle(Guid id, IdentidadDbContext db, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == id).ToDtos(db).SingleOrDefaultAsync(ct) is { } user
            ? Results.Ok(user)
            : ApiProblems.NotFound("El usuario no existe.");
}
