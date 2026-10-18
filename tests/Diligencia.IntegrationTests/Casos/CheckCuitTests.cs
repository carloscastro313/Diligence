using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Data.SqlClient;

namespace Diligencia.IntegrationTests.Casos;

// US2: aviso de tercero existente (FR-004/005).
public class CheckCuitTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    internal const string CheckPath = CreateSolicitudTests.Path + "/tercero-existente?cuit=";

    internal static readonly string[] AvisoKeys = ["existe", "razonSocial", "estadoTercero", "etapaCasoAbierto", "yaLoSigue", "admiteSolicitud"];

    /// <summary>UPDATE por SQL directo (login de migraciones).</summary>
    internal static async Task ExecAsync(string connection, string sql, params (string, object)[] args)
    {
        await using var c = new SqlConnection(connection);
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task UnknownCuit_ReturnsExisteFalse()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get(CheckPath + TestCuits.New(), user);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Json(res);
        Assert.False(json.GetProperty("existe").GetBoolean());
        Assert.All(json.EnumerateObject(), p => Assert.Contains(p.Name, AvisoKeys));
    }

    [Fact]
    public async Task ExistingWithOpenCase_ReturnsSummary_AndYaLoSigueOnlyForCreator()
    {
        var creator = await UserAsync(BackofficeRoles.Solicitante);
        var other = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        var razon = "ACME " + Guid.NewGuid().ToString("N");
        using var created = await Post(CreateSolicitudTests.Path, creator, new
        {
            tipoTercero = "Proveedor", tipoPersona = "Juridica", cuit, razonSocial = razon,
            contactoNombre = "Ana", contactoEmail = $"c{Guid.NewGuid():N}@acme.test", montoAnualEstimado = 100m, motivo = "m",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var formatted = Uri.EscapeDataString($"{cuit[..2]}-{cuit[2..10]}-{cuit[10..]}");

        using var mine = await Get(CheckPath + formatted, creator);
        using var theirs = await Get(CheckPath + cuit, other);

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var m = await Json(mine);
        Assert.True(m.GetProperty("existe").GetBoolean());
        Assert.Equal(razon, m.GetProperty("razonSocial").GetString());
        Assert.Equal("EnAlta", m.GetProperty("estadoTercero").GetString());
        Assert.Equal("EsperaTercero", m.GetProperty("etapaCasoAbierto").GetString());
        Assert.True(m.GetProperty("yaLoSigue").GetBoolean());
        Assert.False(m.GetProperty("admiteSolicitud").GetBoolean());
        Assert.Equal(AvisoKeys.Order(), m.EnumerateObject().Select(p => p.Name).Order());

        var t = await Json(theirs);
        Assert.True(t.GetProperty("existe").GetBoolean());
        Assert.False(t.GetProperty("yaLoSigue").GetBoolean());
        Assert.True(t.GetProperty("admiteSolicitud").GetBoolean());
        Assert.Equal(AvisoKeys.Order(), t.EnumerateObject().Select(p => p.Name).Order());
    }

    [Theory]
    [InlineData("Activo")]
    [InlineData("Bloqueado")]
    public async Task ActiveOrBlockedTercero_DoesNotAdmitSolicitud(string estado)
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        using var created = await Post(CreateSolicitudTests.Path, user, CreateSolicitudTests.Body(cuit));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ExecAsync(MigratorConnection, "UPDATE terceros.Terceros SET Estado = @e WHERE Cuit = @c", ("@e", estado), ("@c", cuit));
        var other = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get(CheckPath + cuit, other);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await Json(res);
        Assert.True(json.GetProperty("existe").GetBoolean());
        Assert.Equal(estado, json.GetProperty("estadoTercero").GetString());
        Assert.False(json.GetProperty("admiteSolicitud").GetBoolean());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("30-12345678-0")] // dígito verificador inválido
    [InlineData("")]
    public async Task InvalidCuit_Returns400(string cuit)
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Get(CheckPath + Uri.EscapeDataString(cuit), user);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
