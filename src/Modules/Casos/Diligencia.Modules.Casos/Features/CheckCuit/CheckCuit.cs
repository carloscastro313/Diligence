using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.CheckCuit;

internal sealed record CheckCuitQuery(string? Cuit);

internal sealed class CheckCuitQueryValidator : AbstractValidator<CheckCuitQuery>
{
    public CheckCuitQueryValidator() =>
        RuleFor(q => q.Cuit).NotEmpty().WithMessage("El CUIT es obligatorio.").MaximumLength(20)
            .Must(Cuit.IsValid).WithMessage("El CUIT no es válido.");
}

internal sealed record AvisoTercero(
    bool Existe, string? RazonSocial, string? EstadoTercero, string? EtapaCasoAbierto, bool YaLoSigue, bool AdmiteSolicitud);

internal sealed class CheckCuitHandler(ITerceroRegistry terceros, CasosDbContext db, IActorContext actor)
{
    public async Task<IResult> HandleAsync(CheckCuitQuery query, CancellationToken ct)
    {
        var tercero = await terceros.FindByCuitAsync(Cuit.Normalize(query.Cuit), ct);
        if (tercero is null) return Results.Ok(new AvisoTercero(false, null, null, null, false, true));

        var abierto = await CasoQueries.FindOpenCasoAsync(db, tercero.Id, ct);
        var yaLoSigue = abierto is not null &&
            await db.Set<Solicitud>().AnyAsync(s => s.CasoId == abierto.Id && s.SolicitanteId == actor.ActorId, ct);
        var admite = tercero.Estado is not (EstadoTercero.Activo or EstadoTercero.Bloqueado) && !yaLoSigue;
        return Results.Ok(new AvisoTercero(true, tercero.RazonSocial, tercero.Estado.ToString(), abierto?.Estado.ToString(), yaLoSigue, admite));
    }
}

internal static class CheckCuitEndpoint
{
    public static void Map(RouteGroupBuilder solicitudes) => solicitudes.MapGet("/tercero-existente", Handle);

    private static async Task<IResult> Handle(
        [AsParameters] CheckCuitQuery query, IValidator<CheckCuitQuery> validator, CheckCuitHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        return validation.IsValid ? await handler.HandleAsync(query, ct) : ApiProblems.Validation(validation);
    }
}
