using System.Security.Claims;
using Diligencia.BuildingBlocks.Configuration;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Diligencia.Modules.Identidad.Authentication;

// Firma con el mismo emisor, audiencia y clave que valida el esquema Backoffice.
internal sealed class TokenIssuer(IConfiguration configuration)
{
    private readonly string _prefix = $"Authentication:Schemes:{AuthSchemes.Backoffice}";

    public string Issue(Guid userId, Guid sessionId, DateTime expiresUtc)
    {
        var key = Convert.FromBase64String(configuration.GetRequired($"{_prefix}:SigningKeys:0:Value"));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuration.GetRequired($"{_prefix}:ValidIssuer"),
            Audience = configuration.GetRequired($"{_prefix}:ValidAudiences:0"),
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("sid", sessionId.ToString())]),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        });
    }
}
