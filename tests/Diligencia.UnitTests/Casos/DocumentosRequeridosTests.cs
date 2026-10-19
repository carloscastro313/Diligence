using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.UnitTests.Casos;

public class DocumentosRequeridosTests
{
    [Fact]
    public void Juridica_HasFourRequired_AndBalanceOptional()
    {
        var docs = DocumentosRequeridos.For(TipoPersona.Juridica);

        Assert.Equal(["ActaAutoridades", "ConstanciaArca", "DocumentoRepresentante", "Estatuto"],
            docs.Where(d => d.Obligatorio).Select(d => d.Tipo).Order());
        Assert.False(docs.Single(d => d.Tipo == "UltimoBalance").Obligatorio);
        Assert.Equal(5, docs.Count);
    }

    [Fact]
    public void Humana_RequiresDniFrontBackAndArca_AndDomicilioOptional()
    {
        var docs = DocumentosRequeridos.For(TipoPersona.Humana);

        Assert.Equal(["ConstanciaArca", "DniDorso", "DniFrente"], docs.Where(d => d.Obligatorio).Select(d => d.Tipo).Order());
        Assert.False(docs.Single(d => d.Tipo == "ConstanciaDomicilio").Obligatorio);
        Assert.Equal(4, docs.Count);
    }

    [Theory]
    [InlineData("Estatuto")]
    [InlineData("UltimoBalance")]
    public void TypeOfAnotherPersona_IsNotInTheHumanaList(string tipo) =>
        Assert.DoesNotContain(DocumentosRequeridos.For(TipoPersona.Humana), d => d.Tipo == tipo);

    [Theory]
    [InlineData("DniFrente")]
    [InlineData("DniDorso")]
    [InlineData("ConstanciaDomicilio")]
    public void TypeOfAnotherPersona_IsNotInTheJuridicaList(string tipo) =>
        Assert.DoesNotContain(DocumentosRequeridos.For(TipoPersona.Juridica), d => d.Tipo == tipo);
}
