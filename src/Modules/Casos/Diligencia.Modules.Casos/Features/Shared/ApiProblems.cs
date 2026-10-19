using System.Text;
using Diligencia.BuildingBlocks.Correlation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class ApiProblems
{
    private static Dictionary<string, object?> Extensions => new() { ["correlationId"] = CorrelationContext.Current };

    public static IResult Conflict(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict, extensions: Extensions);

    public static IResult NotFound(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound, extensions: Extensions);

    public static IResult BadRequest(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status400BadRequest, extensions: Extensions);

    public static IResult PayloadTooLarge(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status413PayloadTooLarge, extensions: Extensions);

    public static IResult ServiceUnavailable(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status503ServiceUnavailable, extensions: Extensions);

    // `extra` suma campos al ProblemDetails (p. ej. codigoInvalidado).
    public static IResult Unauthorized(string title, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var extensions = Extensions;
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>()) extensions[key] = value;
        return Results.Problem(title: title, statusCode: StatusCodes.Status401Unauthorized, extensions: extensions);
    }

    public static IResult TooManyRequests(string title, int? retryAfterSeconds = null)
    {
        var problem = Results.Problem(title: title, statusCode: StatusCodes.Status429TooManyRequests, extensions: Extensions);
        return retryAfterSeconds is { } s ? new WithHeader(problem, "Retry-After", Math.Max(1, s).ToString()) : problem;
    }

    // Las claves van en camelCase en cada segmento de la ruta (`Domicilio.Calle` → `domicilio.calle`,
    // `Beneficiarios[0].Porcentaje` → `beneficiarios[0].porcentaje`), como los campos del JSON del request.
    public static IResult Validation(ValidationResult result) => Results.ValidationProblem(
        result.Errors.GroupBy(e => CamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
        extensions: Extensions);

    internal static string CamelCasePath(string path)
    {
        var sb = new StringBuilder(path.Length);
        var segmentStart = true;
        foreach (var c in path)
        {
            sb.Append(segmentStart ? char.ToLowerInvariant(c) : c);
            segmentStart = c == '.';
        }
        return sb.ToString();
    }

    private sealed class WithHeader(IResult inner, string name, string value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
