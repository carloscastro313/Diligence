using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Casos.Features.GuardarFormulario;
using FluentValidation.Results;

namespace Diligencia.UnitTests.Casos;

public class GuardarFormularioValidatorTests
{
    private static readonly FormularioGuardadoValidator Validator = new();

    private static ValidationResult Validate(FormularioDatos datos) => Validator.Validate(new GuardarFormularioRequest(datos, null));

    private static void AssertError(FormularioDatos datos, string key)
    {
        var r = Validate(datos);
        Assert.False(r.IsValid);
        Assert.True(r.Errors.Any(e => string.Equals(e.PropertyName, key, StringComparison.OrdinalIgnoreCase)),
            $"Se esperaba error en '{key}' y hubo: {string.Join(", ", r.Errors.Select(e => e.PropertyName))}");
    }

    private static FormularioDatos Con(params decimal[] porcentajes) =>
        new(Beneficiarios: porcentajes.Select(p => new Beneficiario(Porcentaje: p)).ToList());

    [Fact]
    public void Empty_IsValid() => Assert.True(Validate(new FormularioDatos()).IsValid);

    [Fact]
    public void Partial_WithFutureDate_IsValid() =>
        Assert.True(Validate(new FormularioDatos(FechaConstitucion: new DateOnly(2999, 1, 1), Pep: new Pep(true))).IsValid);

    [Fact]
    public void Datos_Null_Fails() =>
        Assert.False(Validator.Validate(new GuardarFormularioRequest(null!, null)).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Porcentaje_OutOfRange_Fails(double p) => AssertError(Con((decimal)p), "Datos.Beneficiarios[0].Porcentaje");

    [Fact]
    public void Porcentaje_MoreThanTwoDecimals_Fails() => AssertError(Con(10.123m), "Datos.Beneficiarios[0].Porcentaje");

    [Fact]
    public void Suma_Above100_FailsOnBeneficiarios() => AssertError(Con(60, 40.01m), "Datos.Beneficiarios");

    [Fact]
    public void Suma_Exactly100_Passes() => Assert.True(Validate(Con(60, 40)).IsValid);

    [Theory]
    [InlineData("ar")]
    [InlineData("XX")]
    [InlineData("ARG")]
    public void Pais_NotIso_Fails(string pais)
    {
        AssertError(new FormularioDatos(Nacionalidad: pais), "Datos.Nacionalidad");
        AssertError(new FormularioDatos(DomicilioFiscal: new Domicilio(Pais: pais)), "Datos.DomicilioFiscal.Pais");
        AssertError(new FormularioDatos(Actividad: new Actividad(Pais: pais)), "Datos.Actividad.Pais");
        AssertError(new FormularioDatos(Beneficiarios: [new Beneficiario(Nacionalidad: pais)]), "Datos.Beneficiarios[0].Nacionalidad");
    }

    [Fact]
    public void Pais_Iso_Passes() => Assert.True(Validate(new FormularioDatos(Nacionalidad: "AR")).IsValid);

    [Fact]
    public void Lengths_AboveMax_Fail()
    {
        AssertError(new FormularioDatos(NumeroInscripcion: new string('1', 51)), "Datos.NumeroInscripcion");
        AssertError(new FormularioDatos(OrigenFondos: new string('a', 2001)), "Datos.OrigenFondos");
        AssertError(new FormularioDatos(DomicilioFiscal: new Domicilio(Calle: new string('a', 201))), "Datos.DomicilioFiscal.Calle");
        AssertError(new FormularioDatos(DomicilioLegal: new Domicilio(Piso: new string('1', 11))), "Datos.DomicilioLegal.Piso");
        AssertError(new FormularioDatos(Documento: new DocumentoPersonal(Numero: new string('1', 21))), "Datos.Documento.Numero");
        AssertError(new FormularioDatos(SinBeneficiarios: new SinBeneficiarios(new string('a', 1001))), "Datos.SinBeneficiarios.Explicacion");
        AssertError(new FormularioDatos(Pep: new Pep(true, Cargo: new string('a', 201))), "Datos.Pep.Cargo");
        AssertError(new FormularioDatos(Representantes: [new Representante(Nombre: new string('a', 201))]), "Datos.Representantes[0].Nombre");
    }

    [Fact]
    public void ListSizes_AboveMax_Fail()
    {
        AssertError(new FormularioDatos(Beneficiarios: Enumerable.Range(0, 21).Select(_ => new Beneficiario(Porcentaje: 1)).ToList()), "Datos.Beneficiarios");
        AssertError(new FormularioDatos(Representantes: Enumerable.Range(0, 11).Select(_ => new Representante()).ToList()), "Datos.Representantes");
    }
}
