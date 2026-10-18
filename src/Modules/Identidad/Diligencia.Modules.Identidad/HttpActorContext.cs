using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Diligencia.Modules.Identidad;

internal sealed class HttpActorContext(IHttpContextAccessor accessor, IHostEnvironment env) : IActorContext
{
    public string ActorId => Authenticated()?.FindFirst("sub")?.Value is { Length: > 0 } sub
        ? sub
        : $"system:{env.ApplicationName}";

    // Cada superficie es un grupo de rutas con su propio esquema JWT (Principio V): el grupo que atendió la
    // solicitud autenticada determina la superficie. (El AuthenticationType de JwtBearer no es el nombre del esquema.)
    public ActorSurface Surface => Authenticated() is null
        ? ActorSurface.System
        : accessor.HttpContext!.Request.Path switch
        {
            var p when p.StartsWithSegments("/api/backoffice") => ActorSurface.Backoffice,
            var p when p.StartsWithSegments("/api/portal") => ActorSurface.Portal,
            _ => ActorSurface.System,
        };

    private System.Security.Claims.ClaimsPrincipal? Authenticated() =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
