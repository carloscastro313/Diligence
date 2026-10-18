namespace Diligencia.Modules.Casos.Features.Shared;

// Lunes a viernes, sin feriados (R8).
internal static class BusinessCalendar
{
    // shortcut: UTC-3 fijo sin horario de verano; pasar a TimeZoneInfo si Argentina vuelve a tenerlo
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    public static DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(Offset));

    public static DateOnly Today(TimeProvider time) => ToLocalDate(time.GetUtcNow().UtcDateTime);

    public static DateOnly AddBusinessDays(DateOnly start, int days)
    {
        var d = start;
        while (days > 0)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
        }
        return d;
    }
}

// Plazos en días hábiles por etapa, fijos hasta RF-30.
internal static class StagePlazos
{
    public const int EsperaTercero = 15;
    public const int EnVerificacion = 5;
    public const int EnAprobacion = 3;
}
