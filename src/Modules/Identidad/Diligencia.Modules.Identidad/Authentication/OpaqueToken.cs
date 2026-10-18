using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Diligencia.Modules.Identidad.Authentication;

// 256 bits aleatorios: alcanza con SHA-256 para guardarlo (no es una contraseña elegida por una persona).
internal static class OpaqueToken
{
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
