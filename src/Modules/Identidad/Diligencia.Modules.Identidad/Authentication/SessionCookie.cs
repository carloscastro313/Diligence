using Microsoft.AspNetCore.Http;

namespace Diligencia.Modules.Identidad.Authentication;

internal static class SessionCookie
{
    public const string Name = "dlg_bo";

    // SameSite=Strict cubre CSRF mientras SPA y API vivan en el mismo sitio (R2).
    public static CookieOptions Options(DateTime? expiresUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/backoffice",
        Expires = expiresUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiresUtc.Value, DateTimeKind.Utc)),
    };
}
