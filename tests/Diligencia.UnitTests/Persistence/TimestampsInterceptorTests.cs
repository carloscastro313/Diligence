using Diligencia.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Diligencia.UnitTests.Persistence;

public class TimestampsInterceptorTests
{
    private sealed class Thing : ITimestamped
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Thing> Things => Set<Thing>();
    }

    private static TestDb Create(FakeTimeProvider clock) => new(
        new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TimestampsInterceptor(clock))
            .Options);

    [Fact]
    public async Task Added_SetsBoth_Modified_SetsOnlyUpdated()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        await using var db = Create(clock);
        var t = new Thing { Name = "a" };
        db.Things.Add(t);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var created = clock.GetUtcNow().UtcDateTime;
        Assert.Equal(created, t.CreatedAtUtc);
        Assert.Equal(created, t.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, t.CreatedAtUtc.Kind);

        clock.Advance(TimeSpan.FromHours(1));
        t.Name = "b";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(created, t.CreatedAtUtc);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, t.UpdatedAtUtc);
    }
}
