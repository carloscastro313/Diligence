using System.Text.Json;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Identidad;

// SC-005 / FR-019: cada cambio de usuarios y áreas deja evento con autor, valor anterior y nuevo.
public class UserAuditTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    private static Dictionary<string, string?> Values(JsonElement ev, string property) =>
        ev.GetProperty(property).GetString() is { } json ? JsonSerializer.Deserialize<Dictionary<string, string?>>(json)! : [];

    [Fact]
    public async Task UserLifecycle_IsAudited_WithActorAndOldAndNewValues()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var area1 = await NewAreaAsync(admin);
        var area2 = await NewAreaAsync(admin);
        var email = $"audit{Guid.NewGuid():N}@test.local";

        using var created = await Post("/api/backoffice/users", admin,
            new { name = "Ana", email, areaId = area1, roles = new[] { BackofficeRoles.Analista, BackofficeRoles.Auditor } });
        var id = (await Json(created)).GetProperty("id").GetString()!;
        (await Put($"/api/backoffice/users/{id}", admin,
            new { name = "Ana María", email, areaId = area2, roles = new[] { BackofficeRoles.Aprobador } })).Dispose();
        (await Post($"/api/backoffice/users/{id}/deactivate", admin)).Dispose();
        (await Post($"/api/backoffice/users/{id}/reactivate", admin)).Dispose();
        (await Post($"/api/backoffice/users/{id}/reactivate", admin)).Dispose(); // idempotente: sin evento nuevo

        var events = await AuditAsync("Usuario", id);
        Assert.Equal(["Created", "Updated", "Updated", "Updated"], events.Select(e => e.GetProperty("action").GetString()));
        Assert.All(events, e => Assert.Equal(admin.Id.ToString(), e.GetProperty("actorId").GetString()));

        var create = Values(events[0], "newValues");
        Assert.Equal("Ana", create["Nombre"]);
        Assert.Equal(email, create["Correo"]);
        Assert.Equal($"{BackofficeRoles.Analista},{BackofficeRoles.Auditor}", create["Roles"]);
        Assert.Equal("Activo", create["Estado"]);

        var edit = (Old: Values(events[1], "oldValues"), New: Values(events[1], "newValues"));
        Assert.Equal("Ana", edit.Old["Nombre"]);
        Assert.Equal("Ana María", edit.New["Nombre"]);
        Assert.Equal($"{BackofficeRoles.Analista},{BackofficeRoles.Auditor}", edit.Old["Roles"]);
        Assert.Equal(BackofficeRoles.Aprobador, edit.New["Roles"]);
        Assert.NotEqual(edit.Old["Area"], edit.New["Area"]);
        Assert.False(edit.New.ContainsKey("Correo")); // solo lo que cambió

        Assert.Equal(("Activo", "Desactivado"), (Values(events[2], "oldValues")["Estado"], Values(events[2], "newValues")["Estado"]));
        Assert.Equal(("Desactivado", "Activo"), (Values(events[3], "oldValues")["Estado"], Values(events[3], "newValues")["Estado"]));

        // Nunca contraseñas, hashes ni tokens.
        var all = string.Join(' ', events.Select(e => e.ToString()));
        Assert.DoesNotContain(Emails.LastTokenFor(email), all);
        Assert.DoesNotContain("assword", all);
    }

    [Fact]
    public async Task AreaChanges_AreAudited()
    {
        var admin = await UserAsync(BackofficeRoles.Administrador);
        var name = "Legal " + Guid.NewGuid().ToString("N");

        using var created = await Post("/api/backoffice/areas", admin, new { name });
        var id = (await Json(created)).GetProperty("id").GetString()!;
        (await Put($"/api/backoffice/areas/{id}", admin, new { name = name + "!" })).Dispose();
        (await Post($"/api/backoffice/areas/{id}/deactivate", admin)).Dispose();

        var events = await AuditAsync("Area", id);
        Assert.Equal(["Created", "Updated", "Updated"], events.Select(e => e.GetProperty("action").GetString()));
        Assert.Equal(name, Values(events[1], "oldValues")["Nombre"]);
        Assert.Equal(name + "!", Values(events[1], "newValues")["Nombre"]);
        Assert.Equal("Inactiva", Values(events[2], "newValues")["Estado"]);
        Assert.All(events, e => Assert.Equal(admin.Id.ToString(), e.GetProperty("actorId").GetString()));
    }
}
