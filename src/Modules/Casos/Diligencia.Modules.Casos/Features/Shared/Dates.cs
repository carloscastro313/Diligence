namespace Diligencia.Modules.Casos.Features.Shared;

internal static class Dates
{
    // Casos guarda UTC pero EF devuelve Kind=Unspecified: sin esto el JSON no lleva la "Z".
    public static DateTime AsUtc(this DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    public static DateTime? AsUtc(this DateTime? d) => d?.AsUtc();
}
