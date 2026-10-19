using System.Text.RegularExpressions;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Portal;

// US1-7: la bitácora registra cada ingreso y cada código, sin el código ni el enlace (FR-021).
public class AccesoAuditTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    [Fact]
    public async Task SuccessfulLogin_IsAudited_WithTheThirdPartyAsAuthor()
    {
        var caso = await SeedAsync();

        await SignInAsync(caso);

        var ev = Assert.Single(await AuditAsync("EnlacePortal", caso.EnlaceId.ToString(), "LoginSucceeded"));
        Assert.Equal($"tercero:{caso.TerceroId}", ev.GetProperty("actorId").GetString());
    }

    [Theory]
    [InlineData("CodigoIncorrecto")]
    [InlineData("CodigoVencido")]
    [InlineData("SinCodigo")]
    public async Task FailedLogin_IsAudited_WithTheReason(string motivo)
    {
        var caso = await SeedAsync();
        if (motivo != "SinCodigo") (await RequestCodeAsync(caso.Token)).Dispose();
        var code = motivo == "SinCodigo" ? "123456" : LastCode(caso.Email);
        if (motivo == "CodigoVencido") Clock.Advance(TimeSpan.FromMinutes(11));
        if (motivo == "CodigoIncorrecto") code = code == "000000" ? "111111" : "000000";

        (await EnterAsync(caso.Token, code)).Dispose();

        var ev = Assert.Single(await AuditAsync("EnlacePortal", caso.EnlaceId.ToString(), "LoginFailed"));
        Assert.Contains(motivo, ev.GetProperty("newValues").GetString());
    }

    [Fact]
    public async Task UnknownLink_IsAuditedAsDesconocido()
    {
        var before = (await AuditAsync("EnlacePortal", "desconocido", "LoginFailed")).Count;

        (await EnterAsync("no-existe-" + Guid.NewGuid().ToString("N"), "123456")).Dispose();

        var events = await AuditAsync("EnlacePortal", "desconocido", "LoginFailed");
        Assert.Equal(before + 1, events.Count);
        Assert.Contains("EnlaceInvalido", events[^1].GetProperty("newValues").GetString());
    }

    [Fact]
    public async Task SentCode_IsAuditedAsCreated_AndNothingStoredHoldsTheTokenTheCodeOrTheUrl()
    {
        var caso = await SeedAsync();
        await SignInAsync(caso);
        var code = LastCode(caso.Email);
        var wrong = code == "000000" ? "111111" : "000000";
        (await EnterAsync(caso.Token, wrong)).Dispose();

        var codigos = await CreateSolicitudTests.RowsAsync(MigratorConnection,
            "SELECT Id FROM casos.CodigosIngreso WHERE EnlaceId = @e", ("@e", caso.EnlaceId));
        var codigoId = ((Guid)Assert.Single(codigos)["Id"]!).ToString();
        Assert.Single(await AuditAsync("CodigoIngreso", codigoId, "Created"));

        // Toda la bitácora de este enlace y su código, como texto.
        var rows = await CreateSolicitudTests.RowsAsync(MigratorConnection, """
            SELECT CONCAT(ActorId, '|', EntityType, '|', EntityId, '|', OldValues, '|', NewValues) AS T
            FROM auditoria.AuditEvents
            WHERE (EntityType = 'EnlacePortal' AND EntityId = @e) OR (EntityType = 'CodigoIngreso' AND EntityId = @c)
            """, ("@e", caso.EnlaceId.ToString()), ("@c", codigoId));
        Assert.NotEmpty(rows);
        var all = string.Join("\n", rows.Select(r => (string)r["T"]!));
        Assert.DoesNotContain(caso.Token, all);
        Assert.DoesNotContain("/acceso", all);
        Assert.DoesNotContain("dlg_pt", all);
        Assert.False(Regex.IsMatch(all, $"(?<![0-9A-Za-z]){code}(?![0-9A-Za-z])"), "la bitácora contiene el código");
        Assert.False(Regex.IsMatch(all, $"(?<![0-9A-Za-z]){wrong}(?![0-9A-Za-z])"), "la bitácora contiene un código ingresado");
    }
}
