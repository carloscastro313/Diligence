using Diligencia.BuildingBlocks.Email;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Casos.Features.SendPortalLink;

internal interface IPortalLinkDelivery
{
    // El atributo va en la interfaz: Hangfire lee los filtros del tipo con que se encola (R6).
    [AutomaticRetry(Attempts = 5)]
    Task SendAsync(Guid enlaceId, CancellationToken cancellationToken);
}

internal static class PortalLinkEmail
{
    public static (string Subject, string Body) Build(string url, DateTime venceUtc) => (
        "Acceso al portal de terceros",
        $"""
        Hola,

        Te invitamos a completar el alta en nuestro portal. Ingresá con este enlace personal:

        {url}

        El enlace vence el {BusinessCalendar.ToLocalDate(venceUtc):dd/MM/yyyy}. No lo compartas con nadie.
        """);
}

internal sealed class PortalLinkDelivery(
    CasosDbContext db, IEmailTransport email, IAuditLog audit, TimeProvider clock,
    IOptions<CasosOptions> options, ILogger<PortalLinkDelivery> logger) : IPortalLinkDelivery
{
    private const int MaxIntentos = 5;

    public async Task SendAsync(Guid enlaceId, CancellationToken cancellationToken)
    {
        var enlace = await db.Set<EnlacePortal>().SingleOrDefaultAsync(e => e.Id == enlaceId, cancellationToken);
        if (enlace is null || enlace.Entrega is EntregaEnlace.Enviado or EntregaEnlace.Fallido) return;

        var token = PortalToken.Generate();
        enlace.TokenHash = PortalToken.Hash(token);
        enlace.Intentos++;
        enlace.UltimoIntentoUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        var anterior = enlace.Entrega;
        try
        {
            var (subject, body) = PortalLinkEmail.Build($"{options.Value.PortalBaseUrl.TrimEnd('/')}/acceso#t={token}", enlace.VenceUtc);
            await email.SendAsync(enlace.Destinatario, subject, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca se registra el cuerpo ni la URL.
            logger.LogWarning(ex, "Falló el envío del enlace {EnlaceId} (intento {Intentos})", enlace.Id, enlace.Intentos);
            if (enlace.Intentos < MaxIntentos)
            {
                await Mark(enlace, anterior, EntregaEnlace.Reintentando, cancellationToken);
                throw;
            }
            await Mark(enlace, anterior, EntregaEnlace.Fallido, cancellationToken);
            return;
        }
        await Mark(enlace, anterior, EntregaEnlace.Enviado, cancellationToken);
    }

    private Task Mark(EnlacePortal enlace, EntregaEnlace anterior, EntregaEnlace nuevo, CancellationToken cancellationToken)
    {
        enlace.Entrega = nuevo;
        return db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await db.SaveChangesAsync(false, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "EnlacePortal", enlace.Id.ToString(),
            [
                new AuditChange("Entrega", anterior.ToString(), nuevo.ToString()),
                new AuditChange("Intentos", null, enlace.Intentos.ToString()),
                new AuditChange("VenceUtc", null, enlace.VenceUtc.ToString("O")),
            ]), tx, ct);
        }, cancellationToken);
    }
}
