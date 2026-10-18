using Diligencia.Modules.Casos.Features.Shared;

namespace Diligencia.UnitTests.Casos;

public class BusinessCalendarTests
{
    // 2026-10-19 es lunes.
    [Theory]
    [InlineData("2026-10-19", 0, "2026-10-19")]
    [InlineData("2026-10-19", 4, "2026-10-23")]
    [InlineData("2026-10-23", 1, "2026-10-26")] // viernes cruza el fin de semana
    [InlineData("2026-10-21", 5, "2026-10-28")]
    [InlineData("2026-10-24", 1, "2026-10-26")] // sábado
    [InlineData("2026-10-25", 1, "2026-10-26")] // domingo
    [InlineData("2026-10-19", 15, "2026-11-09")]
    public void AddBusinessDays(string start, int days, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), BusinessCalendar.AddBusinessDays(DateOnly.Parse(start), days));

    [Fact]
    public void ToLocalDate_IsUtcMinus3()
    {
        Assert.Equal(new DateOnly(2026, 10, 18), BusinessCalendar.ToLocalDate(new DateTime(2026, 10, 19, 2, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 19), BusinessCalendar.ToLocalDate(new DateTime(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Today_UsesTimeProvider()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 18), BusinessCalendar.Today(time));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
