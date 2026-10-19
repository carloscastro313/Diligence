using System.Net.Http.Json;
using System.Net;
using Diligencia.IntegrationTests.Casos;
using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Portal;

// US1-5: límites de envío, bloqueo ante adivinación y límite por IP (FR-003, R4).
public class AccesoLimitesTests(SqlServerFixture sql) : PortalApiTest(sql)
{
    private Task<List<Dictionary<string, object?>>> Enlace(Guid id) =>
        CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT * FROM casos.EnlacesPortal WHERE Id = @i", ("@i", id));

    // Pide un código y gasta sus 5 intentos con uno incorrecto.
    private async Task BurnFiveAsync(SeededCaso caso)
    {
        (await RequestCodeAsync(caso.Token)).Dispose();
        var wrong = LastCode(caso.Email) == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++) (await EnterAsync(caso.Token, wrong)).Dispose();
    }

    [Fact]
    public async Task SixthCodeInAnHour_Gets429_WithRetryAfter_AndIsAudited()
    {
        var caso = await SeedAsync();
        for (var i = 0; i < 5; i++)
        {
            using var ok = await RequestCodeAsync(caso.Token);
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        using var res = await RequestCodeAsync(caso.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.InRange(int.Parse(res.Headers.GetValues("Retry-After").Single()), 1, 3600);
        Assert.Equal(5, EmailTransport.Sent.Count);
        var denied = Assert.Single(await AuditAsync("EnlacePortal", caso.EnlaceId.ToString(), "AccessDenied"));
        Assert.Contains("LimiteCodigos", denied.GetProperty("newValues").GetString());

        // Pasada la hora, se puede volver a pedir.
        Clock.Advance(TimeSpan.FromMinutes(61));
        using var later = await RequestCodeAsync(caso.Token);
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
    }

    [Fact]
    public async Task TenFailuresAcrossTwoCodes_LockTheLink_UntilThirtyMinutesPass()
    {
        var caso = await SeedAsync();
        await BurnFiveAsync(caso);
        (await RequestCodeAsync(caso.Token)).Dispose();
        var correct = LastCode(caso.Email);
        var wrong = correct == "000000" ? "111111" : "000000";
        for (var i = 0; i < 4; i++) (await EnterAsync(caso.Token, wrong)).Dispose();

        using (var tenth = await EnterAsync(caso.Token, wrong))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, tenth.StatusCode);
            Assert.Equal(1800, int.Parse(tenth.Headers.GetValues("Retry-After").Single()));
        }

        // Bloqueado: ni el código correcto ni pedir otro sirven.
        using (var blockedEnter = await EnterAsync(caso.Token, correct)) Assert.Equal(HttpStatusCode.TooManyRequests, blockedEnter.StatusCode);
        using (var blockedCode = await RequestCodeAsync(caso.Token)) Assert.Equal(HttpStatusCode.TooManyRequests, blockedCode.StatusCode);
        Assert.Single(await AuditAsync("EnlacePortal", caso.EnlaceId.ToString(), "AccountLocked"));

        Clock.Advance(TimeSpan.FromMinutes(31));
        using (var codeAgain = await RequestCodeAsync(caso.Token)) Assert.Equal(HttpStatusCode.Accepted, codeAgain.StatusCode);
        using var enter = await EnterAsync(caso.Token, LastCode(caso.Email));
        Assert.Equal(HttpStatusCode.NoContent, enter.StatusCode);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheConsecutiveFailures()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var correct = LastCode(caso.Email);
        var wrong = correct == "000000" ? "111111" : "000000";
        for (var i = 0; i < 3; i++) (await EnterAsync(caso.Token, wrong)).Dispose();
        Assert.Equal(3, (int)Assert.Single(await Enlace(caso.EnlaceId))["FallidosConsecutivos"]!);

        using var res = await EnterAsync(caso.Token, correct);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(0, (int)Assert.Single(await Enlace(caso.EnlaceId))["FallidosConsecutivos"]!);
    }

    [Fact]
    public async Task SimultaneousWrongCodes_AreBothCounted()
    {
        var caso = await SeedAsync();
        (await RequestCodeAsync(caso.Token)).Dispose();
        var wrong = LastCode(caso.Email) == "000000" ? "111111" : "000000";

        var results = await Task.WhenAll(EnterAsync(caso.Token, wrong), EnterAsync(caso.Token, wrong));
        foreach (var r in results) r.Dispose();

        Assert.Equal(2, (int)Assert.Single(await Enlace(caso.EnlaceId))["FallidosConsecutivos"]!);
    }

    private sealed class RealIpLimit(string connectionString) : DbApiFactory(connectionString)
    {
        protected override int AccesoRateLimitPerMinute => 10;
    }

    [Fact]
    public async Task PerIpLimit_Returns429_OnTheAnonymousPortalEndpoints()
    {
        await using var factory = new RealIpLimit(RuntimeConnection);
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var ok = await client.PostAsJsonAsync("/api/portal/acceso/codigo", new { token = "no-existe" }, Ct);
            Assert.Equal(HttpStatusCode.NotFound, ok.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/portal/acceso/codigo", new { token = "no-existe" }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        // La política es compartida por los dos endpoints.
        using var other = await client.PostAsJsonAsync("/api/portal/acceso", new { token = "x", codigo = "123456" }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, other.StatusCode);
    }
}
