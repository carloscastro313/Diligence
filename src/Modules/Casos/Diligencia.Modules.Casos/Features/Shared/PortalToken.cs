using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Diligencia.Modules.Casos.Features.Shared;

// 256 bits aleatorios: alcanza con SHA-256 para guardarlo (misma forma que OpaqueToken de Identidad).
internal static class PortalToken
{
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
