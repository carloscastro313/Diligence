using System.Text;

namespace Diligencia.Modules.Identidad.Authentication;

internal static class PasswordHashing
{
    public const int MinLength = 12;
    public const int MaxLength = 64;
    public const int MaxBytes = 72; // límite de BCrypt: más allá trunca en silencio
    private const int WorkFactor = 12;

    // Hash válido de una contraseña descartada: iguala el tiempo de respuesta cuando el usuario no existe (R4).
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-not-used", WorkFactor);

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public static void VerifyDummy(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash);

    public static bool IsValidLength(string? password) =>
        password is { Length: >= MinLength and <= MaxLength } && Encoding.UTF8.GetByteCount(password) <= MaxBytes;
}
