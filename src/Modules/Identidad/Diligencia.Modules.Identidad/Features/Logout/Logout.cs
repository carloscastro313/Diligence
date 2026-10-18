using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Features.Shared;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Identidad.Features.Logout;

internal sealed class LogoutHandler(IdentidadDbContext db, IAuditLog audit, TimeProvider clock)
{
    public Task HandleAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Sessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.LoggedOut, "Usuario", userId.ToString()), tx, ct);
        }, cancellationToken);
}

internal static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("/auth/logout", Handle);

    private static async Task<IResult> Handle(LogoutHandler handler, HttpContext http, CancellationToken ct)
    {
        await handler.HandleAsync(http.UserId(), http.SessionId(), ct);
        http.Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Options(null));
        return Results.NoContent();
    }
}
