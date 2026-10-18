using Diligencia.Modules.Casos.Features.CreateSolicitud;
using FluentValidation.Results;

namespace Diligencia.UnitTests.Casos;

public class CrearSolicitudValidatorTests
{
    private static readonly CrearSolicitudValidator Validator = new();

    // CUIT con DV válido: jurídica (30) y humana (20).
    private const string Juridica = "30712345671";
    private const string Humana = "20123456786";

    private static CrearSolicitudRequest Valid => new("Proveedor", "Juridica", Juridica, "ACME SA", "Ana Pérez",
        "ana@acme.test", "+54 11 5555-0000", 1500000.50m, "Servicios de limpieza");

    // La clave es la del request en camelCase; se compara sin distinguir mayúsculas para no atar el test al casing.
    private static bool HasError(ValidationResult r, string key) =>
        r.Errors.Any(e => string.Equals(e.PropertyName, key, StringComparison.OrdinalIgnoreCase));

    private static void AssertOnlyError(CrearSolicitudRequest request, string key)
    {
        var r = Validator.Validate(request);
        Assert.False(r.IsValid);
        Assert.True(HasError(r, key), $"Se esperaba error en '{key}' y hubo: {string.Join(", ", r.Errors.Select(e => e.PropertyName))}");
    }

    [Fact]
    public void ValidRequest_Passes() => Assert.True(Validator.Validate(Valid).IsValid);

    [Fact]
    public void Humana_WithPersonPrefix_Passes() =>
        Assert.True(Validator.Validate(Valid with { TipoPersona = "Humana", Cuit = Humana }).IsValid);

    [Theory]
    [InlineData("30-71234567-1")]
    [InlineData(" 30 71234567 1 ")]
    public void Cuit_WithSeparators_Passes(string cuit) => Assert.True(Validator.Validate(Valid with { Cuit = cuit }).IsValid);

    [Fact]
    public void Contacto_Telefono_IsOptional() => Assert.True(Validator.Validate(Valid with { ContactoTelefono = null }).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiredFields_Empty_FailOnTheirOwnKey(string? empty)
    {
        AssertOnlyError(Valid with { TipoTercero = empty }, "tipoTercero");
        AssertOnlyError(Valid with { TipoPersona = empty }, "tipoPersona");
        AssertOnlyError(Valid with { Cuit = empty }, "cuit");
        AssertOnlyError(Valid with { RazonSocial = empty }, "razonSocial");
        AssertOnlyError(Valid with { ContactoNombre = empty }, "contactoNombre");
        AssertOnlyError(Valid with { ContactoEmail = empty }, "contactoEmail");
        AssertOnlyError(Valid with { Motivo = empty }, "motivo");
    }

    [Fact]
    public void Monto_IsRequired() => AssertOnlyError(Valid with { MontoAnualEstimado = null }, "montoAnualEstimado");

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    public void Email_MalFormed_Fails(string email) => AssertOnlyError(Valid with { ContactoEmail = email }, "contactoEmail");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Monto_ZeroOrNegative_Fails(double monto) =>
        AssertOnlyError(Valid with { MontoAnualEstimado = (decimal)monto }, "montoAnualEstimado");

    [Theory]
    [InlineData("30712345672")] // DV incorrecto
    [InlineData("3071234567")]  // 10 dígitos
    [InlineData("307123456711")] // 12 dígitos
    [InlineData("3071234567a")]
    public void Cuit_Invalid_Fails(string cuit) => AssertOnlyError(Valid with { Cuit = cuit }, "cuit");

    [Fact]
    public void Cuit_PrefixMustMatchTipoPersona()
    {
        AssertOnlyError(Valid with { TipoPersona = "Humana", Cuit = Juridica }, "cuit"); // 30 es de empresa
        AssertOnlyError(Valid with { TipoPersona = "Juridica", Cuit = Humana }, "cuit"); // 20 es de persona
    }

    [Fact]
    public void Enums_OutOfRange_Fail()
    {
        AssertOnlyError(Valid with { TipoTercero = "Socio" }, "tipoTercero");
        AssertOnlyError(Valid with { TipoPersona = "Fisica" }, "tipoPersona");
    }

    [Fact]
    public void Fields_AboveMaxLength_Fail()
    {
        AssertOnlyError(Valid with { RazonSocial = new string('a', 201) }, "razonSocial");
        AssertOnlyError(Valid with { ContactoNombre = new string('a', 201) }, "contactoNombre");
        AssertOnlyError(Valid with { ContactoEmail = new string('a', 250) + "@x.co" }, "contactoEmail");
        AssertOnlyError(Valid with { ContactoTelefono = new string('1', 51) }, "contactoTelefono");
        AssertOnlyError(Valid with { Motivo = new string('a', 1001) }, "motivo");
    }
}
