using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;

namespace Diligencia.UnitTests.Casos;

// FR-013 / R8: fin_e = max(hoy, AddBusinessDays(inicio, plazo_e)), más los plazos de las etapas siguientes.
public class FechaEstimadaTests
{
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc); // 09:00 en Argentina, mismo día

    // 2026-10-19 es lunes.
    private static readonly DateOnly Lunes = new(2026, 10, 19);

    [Fact]
    public void EsperaTercero_JustOpened_IsToday_Plus23BusinessDays() =>
        Assert.Equal(BusinessCalendar.AddBusinessDays(Lunes, 23), FechaEstimada.Calcular(EstadoCaso.EsperaTercero, Utc(2026, 10, 19), Lunes));

    [Fact]
    public void EsperaTercero_PastDue_IsToday_Plus8BusinessDays() =>
        Assert.Equal(BusinessCalendar.AddBusinessDays(Lunes, 8), FechaEstimada.Calcular(EstadoCaso.EsperaTercero, Utc(2026, 9, 1), Lunes));

    [Fact]
    public void EnVerificacion_StartedTwoBusinessDaysAgo_IsEndOfVerification_Plus3()
    {
        var hoy = new DateOnly(2026, 10, 16); // viernes
        var finVerificacion = BusinessCalendar.AddBusinessDays(new DateOnly(2026, 10, 14), 5); // miércoles 14 + 5 = miércoles 21

        Assert.Equal(BusinessCalendar.AddBusinessDays(finVerificacion, 3), FechaEstimada.Calcular(EstadoCaso.EnVerificacion, Utc(2026, 10, 14), hoy));
    }

    [Fact]
    public void EnAprobacion_PastDue_IsToday() =>
        Assert.Equal(Lunes, FechaEstimada.Calcular(EstadoCaso.EnAprobacion, Utc(2026, 10, 1), Lunes));

    [Theory]
    [InlineData("Aprobado")]
    [InlineData("Rechazado")]
    [InlineData("Abandonado")]
    public void FinalStates_ReturnNull(string estado) =>
        Assert.Null(FechaEstimada.Calcular(Enum.Parse<EstadoCaso>(estado), Utc(2026, 10, 1), Lunes));
}
