namespace Diligencia.Modules.Casos.Features.Shared;

internal static class EmailMask
{
    // "contacto@example.com" → "c***@example.com": largo fijo para no revelar el del correo; con un solo carácter no se muestra ninguno.
    public static string Mask(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return "***";
        return (at == 1 ? "" : email[..1]) + "***" + email[at..];
    }
}
