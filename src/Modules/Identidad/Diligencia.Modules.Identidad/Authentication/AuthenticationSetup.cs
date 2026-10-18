using Diligencia.BuildingBlocks.Configuration;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            });
        }
        return services;
    }

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
