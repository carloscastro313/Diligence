using Diligencia.Modules.Casos.Features.Shared;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.UnitTests.Casos;

public class ApiProblemsTests
{
    [Theory]
    [InlineData("Cuit", "cuit")]
    [InlineData("Domicilio.Calle", "domicilio.calle")]
    [InlineData("Beneficiarios[0].Porcentaje", "beneficiarios[0].porcentaje")]
    [InlineData("DomicilioFiscal.Pep.EsPep", "domicilioFiscal.pep.esPep")]
    public void CamelCasePath_LowersTheFirstLetterOfEverySegment(string path, string expected) =>
        Assert.Equal(expected, ApiProblems.CamelCasePath(path));

    [Fact]
    public void Validation_UsesCamelCasePathsAsKeys_AndGroupsMessages()
    {
        var result = new ValidationResult(
        [
            new ValidationFailure("Datos.Beneficiarios[1].Porcentaje", "Mal"),
            new ValidationFailure("Datos.Beneficiarios[1].Porcentaje", "Peor"),
            new ValidationFailure("Datos.Domicilio.Calle", "Falta"),
        ]);

        var problem = Assert.IsType<ProblemHttpResult>(ApiProblems.Validation(result));

        var errors = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails).Errors;
        Assert.Equal(["Mal", "Peor"], errors["datos.beneficiarios[1].porcentaje"]);
        Assert.Equal(["Falta"], errors["datos.domicilio.calle"]);
    }

    [Fact]
    public async Task TooManyRequests_SetsRetryAfter_AndUnauthorizedCarriesExtraFields()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        await ApiProblems.TooManyRequests("Esperá", 90).ExecuteAsync(http);
        Assert.Equal("90", http.Response.Headers["Retry-After"].ToString());
        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);

        var unauthorized = Assert.IsType<ProblemHttpResult>(
            ApiProblems.Unauthorized("No", new Dictionary<string, object?> { ["codigoInvalidado"] = true }));
        Assert.Equal(true, unauthorized.ProblemDetails.Extensions["codigoInvalidado"]);
    }
}
