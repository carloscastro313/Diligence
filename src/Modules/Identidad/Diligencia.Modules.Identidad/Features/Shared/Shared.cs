using System.Security.Claims;
using Diligencia.BuildingBlocks.Correlation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Diligencia.Modules.Identidad.Features.Shared;

internal static class ApiProblems
{
    private static Dictionary<string, object?> Extensions => new() { ["correlationId"] = CorrelationContext.Current };

    public static IResult Unauthorized(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status401Unauthorized, extensions: Extensions);
    public static IResult BadRequest(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status400BadRequest, extensions: Extensions);
    public static IResult NotFound(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound, extensions: Extensions);
    public static IResult Conflict(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict, extensions: Extensions);
    public static IResult Validation(ValidationResult result) => Results.ValidationProblem(result.ToDictionary(), extensions: Extensions);
}

internal static class Emails
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

internal static class HttpContextExtensions
{
    // Los flujos anónimos (ingreso, restablecimiento) prueban quién es el usuario: la bitácora debe atribuirle el evento.
    public static void ActAs(this HttpContext http, Guid userId) =>
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "identidad"));

    public static Guid UserId(this HttpContext http) => Guid.Parse(http.User.FindFirstValue("sub")!);
    public static Guid SessionId(this HttpContext http) => Guid.Parse(http.User.FindFirstValue("sid")!);
}
