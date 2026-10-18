using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.UnitTests.Casos;

public class CuitTests
{
    [Theory]
    [InlineData("30-71234567-1", true)]
    [InlineData("30-71234567-2", false)]
    [InlineData(" 30 71234567 1 ", true)]
    [InlineData("30712345671", true)]
    [InlineData("3071234567", false)]
    [InlineData("307123456711", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid(string? cuit, bool expected) => Assert.Equal(expected, Cuit.IsValid(cuit));

    [Fact]
    public void Normalize_KeepsOnlyDigits() => Assert.Equal("30712345671", Cuit.Normalize("30-7123 4567.1"));

    [Theory]
    [InlineData("20-11111111-1", TipoPersona.Humana, true)]
    [InlineData("27-11111111-1", TipoPersona.Humana, true)]
    [InlineData("20-11111111-1", TipoPersona.Juridica, false)]
    [InlineData("30-71234567-1", TipoPersona.Juridica, true)]
    [InlineData("34-71234567-1", TipoPersona.Juridica, true)]
    [InlineData("30-71234567-1", TipoPersona.Humana, false)]
    [InlineData("99-71234567-1", TipoPersona.Humana, false)]
    public void MatchesPersona(string cuit, TipoPersona persona, bool expected) =>
        Assert.Equal(expected, Cuit.MatchesPersona(cuit, persona));
}
