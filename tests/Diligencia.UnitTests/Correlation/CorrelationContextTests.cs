using Diligencia.BuildingBlocks.Correlation;

namespace Diligencia.UnitTests.Correlation;

public class CorrelationContextTests
{
    [Theory]
    [InlineData("abc-123_X.y:z", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("bad/char", false)]
    public void IsValid_ChecksFormat(string? id, bool expected) =>
        Assert.Equal(expected, CorrelationContext.IsValid(id));

    [Fact]
    public void IsValid_Rejects65Chars() =>
        Assert.False(CorrelationContext.IsValid(new string('a', 65)));

    [Fact]
    public void Set_InvalidId_Throws() =>
        Assert.Throws<ArgumentException>(() => CorrelationContext.Set("no válido"));

    [Fact]
    public async Task Current_WithoutSet_GeneratesValidId()
    {
        // Hilo nuevo: AsyncLocal limpio, sin Activity.
        var id = await Task.Run(() => CorrelationContext.Current);
        Assert.True(CorrelationContext.IsValid(id));
    }

    [Fact]
    public async Task Current_AfterSet_ReturnsIt()
    {
        var id = await Task.Run(() => { CorrelationContext.Set("req-1"); return CorrelationContext.Current; });
        Assert.Equal("req-1", id);
    }
}
