using System.Text.Json;
using Diligencia.Modules.Casos.Features.Formulario;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.GetFormulario;

internal sealed record FormularioResponse(string Estado, FormularioDatos Datos, string? Version, DateTime? GuardadoUtc, DateTime? EnviadoUtc)
{
    // Sin fila: borrador vacío y sin versión.
    public static FormularioResponse From(FormularioTercero? f) => f is null
        ? new("Borrador", new FormularioDatos(), null, null, null)
        : new(f.Estado.ToString(), JsonSerializer.Deserialize<FormularioDatos>(f.Datos, FormularioJson.Options) ?? new FormularioDatos(),
            Convert.ToBase64String(f.Version), f.GuardadoUtc.AsUtc(), f.EnviadoUtc.AsUtc());
}

// Los enums del formulario viajan como texto: el cuerpo y la respuesta usan las opciones del formulario, no las globales.
internal static class FormularioHttp
{
    public static IResult Ok(object value) => Results.Json(value, FormularioJson.Options);

    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, FormularioJson.Options, ct) is { } value
                ? (value, null)
                : (null, ApiProblems.BadRequest("El cuerpo de la solicitud es obligatorio."));
        }
        catch (JsonException)
        {
            return (null, ApiProblems.BadRequest("El cuerpo de la solicitud no es válido."));
        }
    }

    // El contrato nombra los errores por campo del formulario (`domicilioFiscal.calle`), sin el wrapper `datos`.
    public static ValidationResult StripDatosPrefix(ValidationResult result) => new(
        [.. result.Errors.Select(e => new ValidationFailure(
            e.PropertyName.StartsWith("Datos.", StringComparison.Ordinal) ? e.PropertyName["Datos.".Length..] : e.PropertyName, e.ErrorMessage))]);
}

internal sealed class GetFormularioHandler(CasosDbContext db, IPortalActor actor)
{
    public async Task<FormularioResponse> HandleAsync(CancellationToken ct) =>
        FormularioResponse.From(await db.Set<FormularioTercero>().AsNoTracking().SingleOrDefaultAsync(f => f.CasoId == actor.CasoId, ct));
}

internal static class GetFormularioEndpoint
{
    public static void Map(RouteGroupBuilder portal) => portal.MapGet("/caso/formulario",
        async (GetFormularioHandler handler, CancellationToken ct) => FormularioHttp.Ok(await handler.HandleAsync(ct)));
}
