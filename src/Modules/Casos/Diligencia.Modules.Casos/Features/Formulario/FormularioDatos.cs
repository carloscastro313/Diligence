using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diligencia.Modules.Casos.Features.Formulario;

internal enum CondicionIva { ResponsableInscripto, Monotributo, Exento, NoAlcanzado, ConsumidorFinal }

internal enum TipoDocumentoPersonal { DNI, Pasaporte }

// Todo es opcional: un borrador puede estar a medias en cualquier campo. Las reglas viven en los validadores.
internal sealed record FormularioDatos(
    CondicionIva? CondicionIva = null,
    DateOnly? FechaConstitucion = null,
    string? NumeroInscripcion = null,
    DateOnly? FechaNacimiento = null,
    string? Nacionalidad = null,
    DocumentoPersonal? Documento = null,
    Domicilio? DomicilioFiscal = null,
    bool? DomicilioLegalIgualFiscal = null,
    Domicilio? DomicilioLegal = null,
    Actividad? Actividad = null,
    IReadOnlyList<Beneficiario>? Beneficiarios = null,
    SinBeneficiarios? SinBeneficiarios = null,
    IReadOnlyList<Representante>? Representantes = null,
    Pep? Pep = null,
    string? OrigenFondos = null);

internal sealed record Domicilio(
    string? Calle = null, string? Numero = null, string? Piso = null, string? Depto = null,
    string? Localidad = null, string? Provincia = null, string? CodigoPostal = null, string? Pais = null);

internal sealed record Beneficiario(
    string? Nombre = null, string? Documento = null, string? Nacionalidad = null, decimal? Porcentaje = null, Pep? Pep = null);

internal sealed record Pep(bool? EsPep = null, string? Cargo = null, string? Organismo = null);

internal sealed record DocumentoPersonal(TipoDocumentoPersonal? Tipo = null, string? Numero = null);

internal sealed record Representante(string? Nombre = null, string? Documento = null, Pep? Pep = null);

internal sealed record Actividad(string? Descripcion = null, string? Pais = null);

internal sealed record SinBeneficiarios(string? Explicacion = null);

internal static class FormularioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
