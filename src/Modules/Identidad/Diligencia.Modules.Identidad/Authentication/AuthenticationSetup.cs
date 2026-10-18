using Diligencia.BuildingBlocks.Configuration;
using Diligencia.Modules.Identidad.Contracts;
using System.Security.Claims;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Diligencia.Modules.Identidad.Authentication;

internal static class AuthenticationSetup
{
    public static IServiceCollection AddBearerSchemes(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = services.AddAuthentication();
        foreach (var scheme in new[] { AuthSchemes.Backoffice, AuthSchemes.Portal })
        {
            var prefix = $"Authentication:Schemes:{scheme}";
            var issuer = configuration.GetRequired($"{prefix}:ValidIssuer");
            var audiences = Required(configuration, $"{prefix}:ValidAudiences");
            var keys = Required(configuration, $"{prefix}:SigningKeys", "Value")
                .Select(v => (SecurityKey)new SymmetricSecurityKey(Convert.FromBase64String(v)))
                .ToList();

            auth.AddJwtBearer(scheme, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = keys,
                    ValidateLifetime = true,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
                if (scheme == AuthSchemes.Backoffice) o.Events = BackofficeEvents();
            });
        }
        return services;
    }

    // El JWT solo identifica la sesión: la validez de la sesión y los roles salen de la base en cada request (R1, R2).
    private static JwtBearerEvents BackofficeEvents() => new()
    {
        OnMessageReceived = ctx =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization")
                && ctx.Request.Cookies.TryGetValue(SessionCookie.Name, out var token))
                ctx.Token = token;
            return Task.CompletedTask;
        },
        OnTokenValidated = async ctx =>
        {
            var sp = ctx.HttpContext.RequestServices;
            var db = sp.GetRequiredService<IdentidadDbContext>();
            var options = sp.GetRequiredService<IOptions<IdentidadOptions>>().Value;
            var now = sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            var ct = ctx.HttpContext.RequestAborted;

            var principal = ctx.Principal!;
            if (!Guid.TryParse(principal.FindFirstValue("sid"), out var sid)
                || !Guid.TryParse(principal.FindFirstValue("sub"), out var sub))
            {
                ctx.Fail("Token sin sesión.");
                return;
            }

            var session = await db.Sessions.AsNoTracking().Where(s => s.Id == sid).Select(s => new
            {
                s.UserId, s.ExpiresAtUtc, s.LastActivityUtc, s.RevokedAtUtc,
                UserActive = db.Users.Where(u => u.Id == s.UserId).Select(u => u.IsActive).FirstOrDefault(),
                Roles = db.UserRoles.Where(r => r.UserId == s.UserId).Select(r => r.Role).ToList(),
            }).FirstOrDefaultAsync(ct);

            if (session is null || session.UserId != sub || session.RevokedAtUtc is not null || !session.UserActive
                || now >= session.ExpiresAtUtc
                || now - session.LastActivityUtc >= TimeSpan.FromMinutes(options.SessionIdleMinutes))
            {
                ctx.Fail("Sesión inválida.");
                return;
            }

            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (var claim in identity.FindAll("role").ToList()) identity.RemoveClaim(claim);
            foreach (var role in session.Roles) identity.AddClaim(new Claim("role", role));

            // Como mucho una escritura por minuto por sesión.
            if (now - session.LastActivityUtc > TimeSpan.FromSeconds(60))
                await db.Sessions.Where(s => s.Id == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityUtc, now), ct);
        },
    };

    // Lee una lista de strings (clave:0, clave:1…) o, con `child`, el hijo de cada elemento (formato user-jwts).
    private static List<string> Required(IConfiguration configuration, string key, string? child = null)
    {
        var values = configuration.GetSection(key).GetChildren()
            .Select(c => child is null ? c.Value : c[child])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        return values.Count > 0
            ? values
            : throw new InvalidOperationException($"Falta la configuración obligatoria '{key}'.");
    }
}
