using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Casos.Features.Shared;
using System.Text.Json;

namespace Diligencia.UnitTests.Casos;

public class JsonDiffTests
{
    [Fact]
    public void Change_InsideArray_UsesIndexedPath()
    {
        var diff = JsonDiff.Diff(
            """{"beneficiarios":[{"nombre":"Ana","porcentaje":50},{"nombre":"Luis","porcentaje":30}]}""",
            """{"beneficiarios":[{"nombre":"Ana","porcentaje":50},{"nombre":"Luis","porcentaje":40}]}""");

        var change = Assert.Single(diff);
        Assert.Equal("beneficiarios[1].porcentaje", change.Field);
        Assert.Equal("30", change.OldValue);
        Assert.Equal("40", change.NewValue);
    }

    [Fact]
    public void AddedField_HasNullOldValue()
    {
        var change = Assert.Single(JsonDiff.Diff("""{"a":"x"}""", """{"a":"x","b":{"c":true}}"""));
        Assert.Equal("b.c", change.Field);
        Assert.Null(change.OldValue);
        Assert.Equal("true", change.NewValue);
    }

    [Fact]
    public void RemovedField_HasNullNewValue()
    {
        var change = Assert.Single(JsonDiff.Diff("""{"a":"x","b":"y"}""", """{"a":"x"}"""));
        Assert.Equal("b", change.Field);
        Assert.Equal("y", change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void NoChanges_IsEmpty() => Assert.Empty(JsonDiff.Diff("""{"a":1,"l":[1,2]}""", """{"l":[1,2],"a":1}"""));

    [Fact]
    public void FirstSave_FromNull_ListsEveryField()
    {
        var json = JsonSerializer.Serialize(new FormularioDatos(OrigenFondos: "ventas", Pep: new Pep(false)), FormularioJson.Options);
        Assert.Equal(["origenFondos", "pep.esPep"], JsonDiff.Diff(null, json).Select(c => c.Field).Order().ToArray());
    }

    [Fact]
    public void FormularioJson_RoundTrips_CamelCase_TextEnums_AndOmitsNulls()
    {
        var datos = new FormularioDatos(CondicionIva: CondicionIva.Monotributo, FechaNacimiento: new DateOnly(1990, 5, 1),
            Documento: new DocumentoPersonal(TipoDocumentoPersonal.DNI, "12345678"));
        var json = JsonSerializer.Serialize(datos, FormularioJson.Options);

        Assert.Equal("""{"condicionIva":"Monotributo","fechaNacimiento":"1990-05-01","documento":{"tipo":"DNI","numero":"12345678"}}""", json);
        Assert.Equal(datos, JsonSerializer.Deserialize<FormularioDatos>(json, FormularioJson.Options));
    }
}
