using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Http;

namespace Diligencia.Modules.Identidad;

internal sealed class HttpPortalActor(IHttpContextAccessor accessor) : IPortalActor
{
    public Guid CasoId => Claim("caso");
    public Guid TerceroId => Claim("tercero");

    private Guid Claim(string type) =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user && Guid.TryParse(user.FindFirst(type)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No hay una sesión de portal autenticada en esta request.");
}
