using Diligencia.Modules.Casos.Persistence;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class FechaEstimada
{
    // Una etapa vencida termina hoy (FR-013); a eso se suman los plazos de las etapas que faltan.
    public static DateOnly? Calcular(EstadoCaso estado, DateTime estadoDesdeUtc, DateOnly hoy)
    {
        int[] restantes = estado switch
        {
            EstadoCaso.EsperaTercero => [StagePlazos.EsperaTercero, StagePlazos.EnVerificacion, StagePlazos.EnAprobacion],
            EstadoCaso.EnVerificacion => [StagePlazos.EnVerificacion, StagePlazos.EnAprobacion],
            EstadoCaso.EnAprobacion => [StagePlazos.EnAprobacion],
            _ => [],
        };
        if (restantes.Length == 0) return null;

        var fin = BusinessCalendar.AddBusinessDays(BusinessCalendar.ToLocalDate(estadoDesdeUtc), restantes[0]);
        if (fin < hoy) fin = hoy;
        foreach (var plazo in restantes.Skip(1)) fin = BusinessCalendar.AddBusinessDays(fin, plazo);
        return fin;
    }
}
