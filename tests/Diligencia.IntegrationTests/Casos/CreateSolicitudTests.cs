using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Casos;

// US1: alta de solicitud (FR-001..003, FR-008).
public class CreateSolicitudTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    internal const string Path = "/api/backoffice/solicitudes";

    internal static object Body(string cuit, string? email = null, string tipoPersona = "Juridica") => new
    {
        tipoTercero = "Proveedor", tipoPersona, cuit, razonSocial = "ACME " + Guid.NewGuid().ToString("N"),
        contactoNombre = "Ana Pérez", contactoEmail = email ?? $"contacto{Guid.NewGuid():N}@acme.test",
        contactoTelefono = "+54 11 5555-0000", montoAnualEstimado = 1500000.50m, motivo = "Servicios de limpieza",
    };

    /// <summary>Una fila por consulta (login de migraciones, sin permisos de la API); columnas por nombre.</summary>
    internal static async Task<List<Dictionary<string, object?>>> RowsAsync(string connection, string sql, params (string, object)[] args)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var c = new SqlConnection(connection);
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        while (await r.ReadAsync(Ct))
            rows.Add(Enumerable.Range(0, r.FieldCount).ToDictionary(i => r.GetName(i), i => r.IsDBNull(i) ? null : r.GetValue(i)));
        return rows;
    }

    [Fact]
    public async Task ValidRequest_OpensTheCase_AndPersistsEverything()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        var formatted = $"{cuit[..2]}-{cuit[2..10]}-{cuit[10..]}";
        var email = $"Contacto{Guid.NewGuid():N}@Acme.Test";
        var opened = Clock.GetUtcNow().UtcDateTime;

        using var res = await Post(Path, user, Body(formatted, email));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await Json(res);
        var casoId = json.GetProperty("casoId").GetGuid();
        Assert.Equal("CasoAbierto", json.GetProperty("resultado").GetString());
        Assert.EndsWith($"{Path}/{casoId}", res.Headers.Location!.ToString());

        var tercero = Assert.Single(await RowsAsync(MigratorConnection, "SELECT * FROM terceros.Terceros WHERE Cuit = @c", ("@c", cuit)));
        Assert.Equal("EnAlta", tercero["Estado"]);
        Assert.Equal("Proveedor", tercero["TipoTercero"]);
        Assert.Equal("Juridica", tercero["TipoPersona"]);
        Assert.Equal(opened, (DateTime)tercero["CreatedAtUtc"]!, TimeSpan.FromSeconds(1));

        var caso = Assert.Single(await RowsAsync(MigratorConnection, "SELECT * FROM casos.Casos WHERE Id = @i", ("@i", casoId)));
        Assert.Equal("EsperaTercero", caso["Estado"]);
        Assert.Equal(user.Id.ToString(), caso["SolicitanteId"]);
        Assert.Equal(tercero["Id"], caso["TerceroId"]);
        Assert.Equal(cuit, caso["Cuit"]); // normalizado, sin guiones

        var sol = Assert.Single(await RowsAsync(MigratorConnection, "SELECT * FROM casos.Solicitudes WHERE CasoId = @i", ("@i", casoId)));
        Assert.True((bool)sol["EsOriginal"]!);
        Assert.Equal(user.Id.ToString(), sol["SolicitanteId"]);
        Assert.Equal(email.ToLowerInvariant(), sol["ContactoEmail"]);

        var link = Assert.Single(await RowsAsync(MigratorConnection, "SELECT * FROM casos.EnlacesPortal WHERE CasoId = @i", ("@i", casoId)));
        Assert.Equal("Pendiente", link["Entrega"]);
        Assert.Null(link["TokenHash"]);
        Assert.Equal(0, (int)link["Intentos"]!);
        Assert.Equal(email.ToLowerInvariant(), link["Destinatario"]);

        // Vence a las 23:59:59 hora Argentina (UTC-3) de un día hábil, 15 días hábiles después (entre 21 y ~30 corridos).
        var vence = (DateTime)link["VenceUtc"]!;
        var local = vence.AddHours(-3);
        Assert.Equal(new TimeSpan(23, 59, 59), local.TimeOfDay);
        Assert.NotEqual(DayOfWeek.Saturday, local.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, local.DayOfWeek);
        Assert.InRange((vence - opened).TotalDays, 19, 30);

        var job = Assert.Single(Jobs.Jobs);
        Assert.Equal("IPortalLinkDelivery", job.Type.Name);
        Assert.Equal(link["Id"], job.Args[0]);
    }

    [Fact]
    public async Task InvalidRequest_Returns400_WithErrorsByField_AndPersistsNothing()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Post(Path, user, new
        {
            tipoTercero = "Socio", tipoPersona = "Juridica", cuit = "30712345672", razonSocial = "",
            contactoNombre = "Ana", contactoEmail = "no-es-correo", montoAnualEstimado = 0, motivo = "x",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await Json(res)).GetProperty("errors");
        foreach (var key in new[] { "tipoTercero", "cuit", "razonSocial", "contactoEmail", "montoAnualEstimado" })
            Assert.True(errors.TryGetProperty(key, out _), $"falta el error de '{key}'");
        Assert.False(errors.TryGetProperty("motivo", out _));
        Assert.Empty(Jobs.Jobs);
    }

    [Fact]
    public async Task Cuit_WithWrongPrefixForTipoPersona_Returns400OnCuit()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);

        using var res = await Post(Path, user, Body(TestCuits.New("30"), tipoPersona: "Humana"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await Json(res)).GetProperty("errors").TryGetProperty("cuit", out _));
    }
}
