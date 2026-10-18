using System.Text.Json;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Auditoria.Features.RecordEvent;

namespace Diligencia.UnitTests.Auditoria;

public class AuditValueMaskerTests
{
    private static Dictionary<string, string?> Json(string? json) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(json!)!;

    [Fact]
    public void NoChanges_ReturnsNull()
    {
        Assert.Equal((null, null), AuditValueMasker.Build(null));
        Assert.Equal((null, null), AuditValueMasker.Build([new AuditChange("Nombre", "a", "a")]));
    }

    [Fact]
    public void OnlyChangedFields_AreKept()
    {
        var (old, @new) = AuditValueMasker.Build(
        [
            new AuditChange("Nombre", "Ana", "Beto"),
            new AuditChange("Pais", "CL", "CL"),
        ]);

        Assert.Equal(new Dictionary<string, string?> { ["Nombre"] = "Ana" }, Json(old));
        Assert.Equal(new Dictionary<string, string?> { ["Nombre"] = "Beto" }, Json(@new));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("clienteSecret")]
    [InlineData("ApiTOKEN")]
    public void SensitiveNames_AreMasked(string field)
    {
        var (old, @new) = AuditValueMasker.Build([new AuditChange(field, "viejo", "nuevo")]);

        Assert.Equal("***", Json(old)[field]);
        Assert.Equal("***", Json(@new)[field]);
        Assert.DoesNotContain("viejo", old);
        Assert.DoesNotContain("nuevo", @new);
    }

    [Fact]
    public void IsSensitiveFlag_Masks()
    {
        var (old, @new) = AuditValueMasker.Build([new AuditChange("Documento", "123", "456", IsSensitive: true)]);

        Assert.Equal("***", Json(old)["Documento"]);
        Assert.Equal("***", Json(@new)["Documento"]);
    }

    [Fact]
    public void LongValues_AreTruncated()
    {
        var (_, @new) = AuditValueMasker.Build([new AuditChange("Notas", null, new string('x', 5000))]);

        var value = Json(@new)["Notas"]!;
        Assert.Equal(4000 + AuditValueMasker.TruncatedSuffix.Length, value.Length);
        Assert.EndsWith(AuditValueMasker.TruncatedSuffix, value);
    }

    [Fact]
    public void NullValues_AreKeptAsNull()
    {
        var (old, @new) = AuditValueMasker.Build([new AuditChange("Nombre", null, "Ana")]);

        Assert.Null(Json(old)["Nombre"]);
        Assert.Equal("Ana", Json(@new)["Nombre"]);
    }

    [Fact]
    public void EmptyOrRepeatedField_Throws()
    {
        Assert.Throws<ArgumentException>(() => AuditValueMasker.Build([new AuditChange(" ", "a", "b")]));
        Assert.Throws<ArgumentException>(() => AuditValueMasker.Build(
            [new AuditChange("A", "1", "2"), new AuditChange("A", "3", "4")]));
    }
}
