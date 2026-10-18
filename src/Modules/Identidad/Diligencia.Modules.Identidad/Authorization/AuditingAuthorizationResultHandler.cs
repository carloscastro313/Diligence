using Diligencia.Modules.Auditoria.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Identidad.Authorization;

// Registra los 403 del backoffice en la bitácora (FR-020) y deja la respuesta al handler por defecto.
internal sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private const int MaxEntityLength = 128;
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Forbidden && context.Request.Path.StartsWithSegments("/api/backoffice"))
        {
            var path = context.Request.Path.Value!;
            await context.RequestServices.GetRequiredService<IAuditLog>().RecordStandaloneAsync(
                new AuditEntry(AuditAction.AccessDenied, "Endpoint", path.Length > MaxEntityLength ? path[..MaxEntityLength] : path),
                context.RequestAborted);
        }
        await _default.HandleAsync(next, context, policy, result);
    }
}
