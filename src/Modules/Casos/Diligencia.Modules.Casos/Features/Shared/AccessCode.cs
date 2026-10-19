using System.Security.Cryptography;
using System.Text;

namespace Diligencia.Modules.Casos.Features.Shared;

// Código de ingreso de 6 dígitos. Solo se guarda el hash ligado al enlace: sirve junto con el token (256 bits) y vive 10 minutos (R3).
internal static class AccessCode
{
    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static byte[] Hash(Guid enlaceId, string code) =>
        SHA256.HashData([.. enlaceId.ToByteArray(), .. Encoding.ASCII.GetBytes(code)]);

    public static bool Matches(Guid enlaceId, string code, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(enlaceId, code), storedHash);
}
