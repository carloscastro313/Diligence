using Diligencia.Modules.Casos.Features.Shared;

namespace Diligencia.UnitTests.Casos;

public class AccessCodeTests
{
    [Fact]
    public void Generate_ReturnsSixDigits_IncludingLeadingZeros()
    {
        var codes = Enumerable.Range(0, 3000).Select(_ => AccessCode.Generate()).ToList();

        Assert.All(codes, c => Assert.Matches(@"^\d{6}$", c));
        Assert.Contains(codes, c => c.StartsWith('0'));
        Assert.True(codes.Distinct().Count() > 2500);
    }

    [Fact]
    public void Hash_DependsOnTheLink_AndOnTheCode()
    {
        var link = Guid.NewGuid();

        Assert.Equal(32, AccessCode.Hash(link, "123456").Length);
        Assert.Equal(AccessCode.Hash(link, "123456"), AccessCode.Hash(link, "123456"));
        Assert.NotEqual(AccessCode.Hash(link, "123456"), AccessCode.Hash(Guid.NewGuid(), "123456"));
        Assert.NotEqual(AccessCode.Hash(link, "123456"), AccessCode.Hash(link, "654321"));
    }

    [Fact]
    public void Matches_AcceptsOnlyTheSameCodeOfTheSameLink()
    {
        var link = Guid.NewGuid();
        var stored = AccessCode.Hash(link, "000123");

        Assert.True(AccessCode.Matches(link, "000123", stored));
        Assert.False(AccessCode.Matches(link, "000124", stored));
        Assert.False(AccessCode.Matches(Guid.NewGuid(), "000123", stored));
    }
}
