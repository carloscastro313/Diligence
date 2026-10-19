using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Terceros.Contracts;
using FluentValidation.Results;

namespace Diligencia.UnitTests.Casos;

public class EnviarFormularioValidatorTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 18);
    private const string NoCorresponde = "No corresponde al tipo de persona.";

    private static ValidationResult Validate(FormularioDatos d, TipoPersona p) => new FormularioEnvioValidator(p, Hoy).Validate(d);

    private static void AssertError(FormularioDatos d, TipoPersona p, string key, string? message = null)
    {
        var r = Validate(d, p);
        Assert.False(r.IsValid);
        Assert.True(r.Errors.Any(e => string.Equals(e.PropertyName, key, StringComparison.OrdinalIgnoreCase)
                && (message is null || e.ErrorMessage == message)),
            $"Se esperaba error en '{key}' y hubo: {string.Join(", ", r.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
    }

    private static void AssertJuridica(FormularioDatos d, string key, string? message = null) => AssertError(d, TipoPersona.Juridica, key, message);
    private static void AssertHumana(FormularioDatos d, string key, string? message = null) => AssertError(d, TipoPersona.Humana, key, message);

    private static readonly Domicilio Dom = new("Calle", "123", null, null, "CABA", "Buenos Aires", "C1000", "AR");
    private static readonly Pep NoPep = new(false);

    private static FormularioDatos Juridica => new(
        CondicionIva: CondicionIva.ResponsableInscripto, FechaConstitucion: new DateOnly(2010, 3, 4), NumeroInscripcion: "IGJ 123",
        DomicilioFiscal: Dom, DomicilioLegalIgualFiscal: true, Actividad: new Actividad("Servicios", "AR"),
        Beneficiarios: [new Beneficiario("Ana", "20123456", "AR", 60, NoPep), new Beneficiario("Luis", "20123457", "AR", 40, NoPep)],
        Representantes: [new Representante("Eva", "30123456", NoPep)], OrigenFondos: "Ventas de servicios");

    private static FormularioDatos Humana => new(
        CondicionIva: CondicionIva.Monotributo, FechaNacimiento: new DateOnly(1990, 5, 1), Nacionalidad: "AR",
        Documento: new DocumentoPersonal(TipoDocumentoPersonal.DNI, "12345678"), DomicilioFiscal: Dom,
        Actividad: new Actividad("Consultoría", "AR"), Pep: NoPep, OrigenFondos: "Honorarios profesionales");

    [Fact]
    public void Juridica_Complete_Passes() => Assert.True(Validate(Juridica, TipoPersona.Juridica).IsValid);

    [Fact]
    public void Humana_Complete_Passes() => Assert.True(Validate(Humana, TipoPersona.Humana).IsValid);

    [Fact]
    public void Juridica_SinBeneficiarios_WithExplanation_Passes() =>
        Assert.True(Validate(Juridica with { Beneficiarios = null, SinBeneficiarios = new SinBeneficiarios(new string('a', 20)) }, TipoPersona.Juridica).IsValid);

    [Fact]
    public void Juridica_DomicilioLegal_InsteadOfIgualFiscal_Passes() =>
        Assert.True(Validate(Juridica with { DomicilioLegalIgualFiscal = null, DomicilioLegal = Dom }, TipoPersona.Juridica).IsValid);

    [Fact]
    public void Juridica_Empty_ReportsRequiredFields()
    {
        var r = Validate(new FormularioDatos(), TipoPersona.Juridica);
        string[] expected = ["CondicionIva", "FechaConstitucion", "NumeroInscripcion", "DomicilioFiscal", "DomicilioLegal", "Actividad",
            "Beneficiarios", "Representantes", "OrigenFondos"];
        foreach (var key in expected) Assert.Contains(r.Errors, e => e.PropertyName == key);
    }

    [Fact]
    public void Humana_Empty_ReportsRequiredFields()
    {
        var r = Validate(new FormularioDatos(), TipoPersona.Humana);
        string[] expected = ["CondicionIva", "FechaNacimiento", "Nacionalidad", "Documento", "DomicilioFiscal", "Actividad", "Pep", "OrigenFondos"];
        foreach (var key in expected) Assert.Contains(r.Errors, e => e.PropertyName == key);
    }

    [Fact]
    public void Humana_WithJuridicaFields_Fails()
    {
        AssertHumana(Humana with { Beneficiarios = [new Beneficiario()] }, "Beneficiarios", NoCorresponde);
        AssertHumana(Humana with { SinBeneficiarios = new SinBeneficiarios("x") }, "SinBeneficiarios", NoCorresponde);
        AssertHumana(Humana with { Representantes = [new Representante()] }, "Representantes", NoCorresponde);
        AssertHumana(Humana with { FechaConstitucion = Hoy }, "FechaConstitucion", NoCorresponde);
        AssertHumana(Humana with { NumeroInscripcion = "1" }, "NumeroInscripcion", NoCorresponde);
        AssertHumana(Humana with { DomicilioLegal = Dom }, "DomicilioLegal", NoCorresponde);
    }

    [Fact]
    public void Juridica_WithHumanaFields_Fails()
    {
        AssertJuridica(Juridica with { FechaNacimiento = new DateOnly(1990, 1, 1) }, "FechaNacimiento", NoCorresponde);
        AssertJuridica(Juridica with { Nacionalidad = "AR" }, "Nacionalidad", NoCorresponde);
        AssertJuridica(Juridica with { Documento = new DocumentoPersonal(TipoDocumentoPersonal.DNI, "12345678") }, "Documento", NoCorresponde);
        AssertJuridica(Juridica with { Pep = NoPep }, "Pep", NoCorresponde);
    }

    [Fact]
    public void Juridica_WithoutDomicilioLegalNorIgualFiscal_Fails() =>
        AssertJuridica(Juridica with { DomicilioLegalIgualFiscal = null }, "DomicilioLegal");

    [Fact]
    public void Domicilio_MissingFields_FailOnTheirPath()
    {
        AssertJuridica(Juridica with { DomicilioFiscal = Dom with { Calle = " " } }, "DomicilioFiscal.Calle");
        AssertJuridica(Juridica with { DomicilioFiscal = Dom with { CodigoPostal = null } }, "DomicilioFiscal.CodigoPostal");
    }

    [Fact]
    public void Beneficiarios_SumAbove100_FailsOnBeneficiarios() =>
        AssertJuridica(Juridica with { Beneficiarios = [new Beneficiario("A", "1", "AR", 60, NoPep), new Beneficiario("B", "2", "AR", 40.01m, NoPep)] }, "Beneficiarios");

    [Theory]
    [InlineData(0)]
    [InlineData(100.01)]
    public void Beneficiario_PorcentajeOutOfRange_Fails(double p) =>
        AssertJuridica(Juridica with { Beneficiarios = [new Beneficiario("A", "1", "AR", (decimal)p, NoPep)] }, "Beneficiarios[0].Porcentaje");

    [Fact]
    public void Beneficiarios_NoneAndNoSinBeneficiarios_Fails() =>
        AssertJuridica(Juridica with { Beneficiarios = [] }, "Beneficiarios");

    [Fact]
    public void Beneficiarios_AndSinBeneficiarios_AreExclusive() =>
        AssertJuridica(Juridica with { SinBeneficiarios = new SinBeneficiarios(new string('a', 20)) }, "SinBeneficiarios");

    [Theory]
    [InlineData(null)]
    [InlineData("corta")]
    public void SinBeneficiarios_ExplicacionTooShort_Fails(string? explicacion) =>
        AssertJuridica(Juridica with { Beneficiarios = null, SinBeneficiarios = new SinBeneficiarios(explicacion) }, "SinBeneficiarios.Explicacion");

    [Fact]
    public void Beneficiarios_MoreThan20_Fails() =>
        AssertJuridica(Juridica with { Beneficiarios = Enumerable.Range(0, 21).Select(_ => new Beneficiario("A", "1", "AR", 1, NoPep)).ToList() }, "Beneficiarios");

    [Fact]
    public void Representantes_MoreThan10_Fails() =>
        AssertJuridica(Juridica with { Representantes = Enumerable.Range(0, 11).Select(_ => new Representante("E", "1", NoPep)).ToList() }, "Representantes");

    [Fact]
    public void Representantes_Empty_Fails() => AssertJuridica(Juridica with { Representantes = [] }, "Representantes");

    [Fact]
    public void Pep_WithoutCargoOrOrganismo_Fails()
    {
        var pep = new Pep(true);
        AssertHumana(Humana with { Pep = pep }, "Pep.Cargo");
        AssertHumana(Humana with { Pep = pep }, "Pep.Organismo");
        AssertJuridica(Juridica with { Beneficiarios = [new Beneficiario("A", "1", "AR", 50, pep)] }, "Beneficiarios[0].Pep.Cargo");
        AssertJuridica(Juridica with { Beneficiarios = [new Beneficiario("A", "1", "AR", 50, pep)] }, "Beneficiarios[0].Pep.Organismo");
        AssertJuridica(Juridica with { Representantes = [new Representante("E", "1", pep)] }, "Representantes[0].Pep.Cargo");
        AssertJuridica(Juridica with { Representantes = [new Representante("E", "1", pep)] }, "Representantes[0].Pep.Organismo");
    }

    [Fact]
    public void Pep_WithCargoAndOrganismo_Passes() =>
        Assert.True(Validate(Humana with { Pep = new Pep(true, "Ministro", "Ministerio de Economía") }, TipoPersona.Humana).IsValid);

    [Fact]
    public void FechaConstitucion_Future_Fails() =>
        AssertJuridica(Juridica with { FechaConstitucion = Hoy.AddDays(1) }, "FechaConstitucion");

    [Fact]
    public void FechaConstitucion_Today_Passes() =>
        Assert.True(Validate(Juridica with { FechaConstitucion = Hoy }, TipoPersona.Juridica).IsValid);

    [Fact]
    public void Edad_Limits()
    {
        Assert.True(Validate(Humana with { FechaNacimiento = Hoy.AddYears(-18) }, TipoPersona.Humana).IsValid);
        Assert.True(Validate(Humana with { FechaNacimiento = Hoy.AddYears(-120) }, TipoPersona.Humana).IsValid);
        AssertHumana(Humana with { FechaNacimiento = Hoy.AddYears(-18).AddDays(1) }, "FechaNacimiento");
        AssertHumana(Humana with { FechaNacimiento = Hoy.AddYears(-121) }, "FechaNacimiento");
    }

    [Fact]
    public void Pais_NotIso_Fails()
    {
        AssertHumana(Humana with { Nacionalidad = "XX" }, "Nacionalidad");
        AssertHumana(Humana with { Actividad = new Actividad("x", "ar") }, "Actividad.Pais");
        AssertJuridica(Juridica with { DomicilioFiscal = Dom with { Pais = "ZZ" } }, "DomicilioFiscal.Pais");
        AssertJuridica(Juridica with { Beneficiarios = [new Beneficiario("A", "1", "ARG", 50, NoPep)] }, "Beneficiarios[0].Nacionalidad");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("12.345.678")]
    public void Dni_NotSevenOrEightDigits_Fails(string numero) =>
        AssertHumana(Humana with { Documento = new DocumentoPersonal(TipoDocumentoPersonal.DNI, numero) }, "Documento.Numero");

    [Fact]
    public void Pasaporte_AnyNumber_Passes() =>
        Assert.True(Validate(Humana with { Documento = new DocumentoPersonal(TipoDocumentoPersonal.Pasaporte, "AB123456") }, TipoPersona.Humana).IsValid);

    [Fact]
    public void OrigenFondos_TooShort_Fails() => AssertHumana(Humana with { OrigenFondos = "corto" }, "OrigenFondos");
}
