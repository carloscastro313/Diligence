using System.Security.Claims;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Diligencia.IntegrationTests.Support;

// Claves y emisores de test, distintos por superficie (FR-004).
public static class TestTokens
{
    public sealed record SchemeSettings(string Issuer, string Audience, byte[] Key);

    public static SchemeSettings For(ActorSurface surface) => surface switch
    {
        ActorSurface.Backoffice => new("test-backoffice", "test-backoffice-aud", Enumerable.Repeat((byte)0xB0, 32).ToArray()),
        ActorSurface.Portal => new("test-portal", "test-portal-aud", Enumerable.Repeat((byte)0xF0, 32).ToArray()),
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    public static string Create(ActorSurface surface, string sub, params string[] roles)
    {
        var s = For(surface);
        var claims = new List<Claim> { new("sub", sub) };
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = s.Issuer,
            Audience = s.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(s.Key), SecurityAlgorithms.HmacSha256),
        });
    }
}
