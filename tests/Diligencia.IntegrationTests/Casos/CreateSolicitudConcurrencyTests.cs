using System.Net;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Identidad.Contracts;

namespace Diligencia.IntegrationTests.Casos;

// US2 / SC-003 / R3: dos solicitantes en paralelo con el mismo CUIT nuevo.
public class CreateSolicitudConcurrencyTests(SqlServerFixture sql) : IdentidadApiTest(sql)
{
    [Fact]
    public async Task ParallelRequests_SameNewCuit_YieldOneCase_AndTwoSolicitudes()
    {
        var a = await UserAsync(BackofficeRoles.Solicitante);
        var b = await UserAsync(BackofficeRoles.Solicitante);

        for (var i = 1; i <= 5; i++)
        {
            var cuit = TestCuits.New();

            var results = await Task.WhenAll(
                Post(CreateSolicitudTests.Path, a, CreateSolicitudTests.Body(cuit)),
                Post(CreateSolicitudTests.Path, b, CreateSolicitudTests.Body(cuit)));

            var outcomes = new List<(string? Resultado, Guid CasoId)>();
            foreach (var res in results)
            {
                using (res)
                {
                    Assert.Equal(HttpStatusCode.Created, res.StatusCode);
                    var json = await Json(res);
                    outcomes.Add((json.GetProperty("resultado").GetString(), json.GetProperty("casoId").GetGuid()));
                }
            }

            Assert.Single(outcomes, o => o.Resultado == "CasoAbierto");
            Assert.Single(outcomes, o => o.Resultado == "SumadoACasoAbierto");
            Assert.Single(outcomes.Select(o => o.CasoId).Distinct());

            Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT 1 AS X FROM terceros.Terceros WHERE Cuit = @c", ("@c", cuit)));
            Assert.Single(await CreateSolicitudTests.RowsAsync(MigratorConnection, "SELECT 1 AS X FROM casos.Casos WHERE Cuit = @c", ("@c", cuit)));
            Assert.Equal(2, (await CreateSolicitudTests.RowsAsync(MigratorConnection,
                "SELECT 1 AS X FROM casos.Solicitudes WHERE CasoId = @i", ("@i", outcomes[0].CasoId))).Count);
            Assert.Equal(i, Jobs.Jobs.Count); // un envío de enlace por CUIT
        }
    }
}
