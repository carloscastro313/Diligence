using System.Data.Common;
using System.Security.Claims;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Diligencia.Modules.Identidad.Authentication;

internal sealed class PortalSignIn(
    TokenIssuer tokens, TimeProvider clock, IOptions<IdentidadOptions> options, IHttpContextAccessor http) : IPortalSignIn
{
    public async Task SignInAsync(Guid casoId, Guid terceroId, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new ArgumentException("La transacción debe tener una conexión abierta.", nameof(transaction));

        // Contexto efímero sobre la tx del llamador (como TerceroRegistry); no confirma ni revierte.
        await using var ctx = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>().UseSqlServer(connection).Options);
        await ctx.Database.UseTransactionAsync(transaction, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var session = new PortalSession
        {
            CasoId = casoId, TerceroId = terceroId, CreatedAtUtc = now, LastActivityUtc = now,
            ExpiresAtUtc = now.AddHours(options.Value.PortalSessionAbsoluteHours),
        };
        ctx.PortalSessions.Add(session);
        await ctx.SaveChangesAsync(cancellationToken);

        var subject = $"tercero:{terceroId}";
        var context = http.HttpContext!;
        context.Response.Cookies.Append(SessionCookie.PortalName,
            tokens.Issue(AuthSchemes.Portal, subject, session.Id, session.ExpiresAtUtc), SessionCookie.PortalOptions(session.ExpiresAtUtc));
        // La bitácora del ingreso necesita autor: el resto de la request actúa como el tercero.
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "identidad"));
    }
}
