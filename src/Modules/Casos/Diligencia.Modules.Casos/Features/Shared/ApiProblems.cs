using Diligencia.BuildingBlocks.Correlation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class ApiProblems
{
    private static Dictionary<string, object?> Extensions => new() { ["correlationId"] = CorrelationContext.Current };

    public static IResult Conflict(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict, extensions: Extensions);

    // Las claves van en camelCase, como los campos del JSON del request.
    public static IResult Validation(ValidationResult result) => Results.ValidationProblem(
        result.Errors.GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
        extensions: Extensions);
}
