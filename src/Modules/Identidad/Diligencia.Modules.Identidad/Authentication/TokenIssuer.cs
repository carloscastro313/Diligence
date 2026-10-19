using System.Security.Claims;
using Diligencia.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Diligencia.Modules.Identidad.Authentication;

// Firma con el mismo emisor, audiencia y clave que valida el esquema indicado.
internal sealed class TokenIssuer(IConfiguration configuration)
{
    public string Issue(string scheme, string subject, Guid sessionId, DateTime expiresUtc)
    {
        var prefix = $"Authentication:Schemes:{scheme}";
        var key = Convert.FromBase64String(configuration.GetRequired($"{prefix}:SigningKeys:0:Value"));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuration.GetRequired($"{prefix}:ValidIssuer"),
            Audience = configuration.GetRequired($"{prefix}:ValidAudiences:0"),
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("sid", sessionId.ToString())]),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        });
    }
}
