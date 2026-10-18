using System.Reflection;
using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.IntegrationTests.Casos;

// US1-6 / FR-010 / FR-017: bitácora del alta y del envío del enlace; nunca el token ni la URL.
public class SolicitudAuditTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private static Dictionary<string, string?> Values(JsonElement ev, string property) =>
        ev.GetProperty(property).GetString() is { } json
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.ToDictionary(kv => kv.Key, kv => (string?)(kv.Value.ValueKind == JsonValueKind.Null ? null : kv.Value.ToString()))
            : [];

    [Fact]
    public async Task Create_AndSend_AreAudited_WithoutTokenOrUrl()
    {
        var user = await UserAsync(BackofficeRoles.Solicitante);
        var cuit = TestCuits.New();
        var email = $"audit{Guid.NewGuid():N}@acme.test";

        using var res = await Post(CreateSolicitudTests.Path, user, CreateSolicitudTests.Body(cuit, email));
        var casoId = (await Json(res)).GetProperty("casoId").GetString()!;

        // Ejecuta lo que quedó encolado, como lo haría el worker (se resuelve por el tipo del job, sin tocar internals).
        var job = Assert.Single(Jobs.Jobs);
        using (var scope = Factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService(job.Type);
            var args = job.Method.GetParameters().Select((p, i) => p.ParameterType == typeof(CancellationToken) ? Ct : job.Args[i]).ToArray();
            await (Task)job.Method.Invoke(svc, args)!;
        }

        var terceroId = (string)(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT Id FROM terceros.Terceros WHERE Cuit = @c", ("@c", cuit)))[0]["Id"]!.ToString()!;
        var solicitudId = (await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT Id FROM casos.Solicitudes WHERE CasoId = @i", ("@i", Guid.Parse(casoId))))[0]["Id"]!.ToString()!;
        var enlaceId = (await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT Id FROM casos.EnlacesPortal WHERE CasoId = @i", ("@i", Guid.Parse(casoId))))[0]["Id"]!.ToString()!;

        var tercero = Assert.Single(await AuditAsync("Tercero", terceroId));
        Assert.Equal("Created", tercero.GetProperty("action").GetString());
        Assert.Equal(user.Id.ToString(), tercero.GetProperty("actorId").GetString());
        var t = Values(tercero, "newValues");
        Assert.Equal(cuit, t["Cuit"]);
        Assert.Equal("EnAlta", t["Estado"]);

        var caso = Assert.Single(await AuditAsync("Caso", casoId));
        Assert.Equal("Created", caso.GetProperty("action").GetString());
        Assert.Equal(user.Id.ToString(), caso.GetProperty("actorId").GetString());
        Assert.Null(Values(caso, "oldValues").GetValueOrDefault("Estado"));
        Assert.Equal("EsperaTercero", Values(caso, "newValues")["Estado"]);

        var solicitud = Assert.Single(await AuditAsync("Solicitud", solicitudId));
        Assert.Equal("Created", solicitud.GetProperty("action").GetString());
        Assert.Equal(user.Id.ToString(), solicitud.GetProperty("actorId").GetString());
        var s = string.Join('|', Values(solicitud, "newValues").Values);
        Assert.Contains("Ana Pérez", s);
        Assert.Contains(email, s);
        Assert.Contains("Servicios de limpieza", s);

        var enlace = Assert.Single(await AuditAsync("EnlacePortal", enlaceId));
        Assert.Equal("Updated", enlace.GetProperty("action").GetString());
        Assert.StartsWith("system:", enlace.GetProperty("actorId").GetString());
        Assert.Equal("Pendiente", Values(enlace, "oldValues")["Entrega"]);
        Assert.Equal("Enviado", Values(enlace, "newValues")["Entrega"]);

        // Ninguna fila de bitácora contiene el token ni la URL del enlace.
        var body = Assert.Single(EmailTransport.Sent, m => m.To == email).Body;
        var token = body[(body.IndexOf("#t=", StringComparison.Ordinal) + 3)..].TakeWhile(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        Assert.True(token.Length >= 20, "no se encontró el token en el correo");
        var leaks = await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Id FROM auditoria.AuditEvents WHERE NewValues LIKE @t OR OldValues LIKE @t OR NewValues LIKE '%/acceso%' OR OldValues LIKE '%/acceso%'",
            ("@t", "%" + new string(token) + "%"));
        Assert.Empty(leaks);
    }
}
