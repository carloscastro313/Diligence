using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.ListMisSolicitudes;

internal sealed record MisSolicitudesQuery(string? Estado, string? Q);

internal sealed class MisSolicitudesQueryValidator : AbstractValidator<MisSolicitudesQuery>
{
    public MisSolicitudesQueryValidator()
    {
        RuleFor(q => q.Estado).Must(e => Enum.TryParse<EstadoCaso>(e, out var v) && Enum.IsDefined(v))
            .When(q => !string.IsNullOrEmpty(q.Estado)).WithMessage("El estado no es válido.");
        RuleFor(q => q.Q).MaximumLength(200);
    }
}

internal sealed record MiSolicitud(
    Guid CasoId, string Cuit, string RazonSocial, string TipoTercero, string Estado, DateTime CreadaUtc,
    bool EsOriginal, DateOnly? FechaEstimada, DateTime? CerradoUtc, bool EnlaceNoEntregado);

internal sealed class ListMisSolicitudesHandler(CasosDbContext db, IActorContext actor, TimeProvider clock)
{
    public async Task<IResult> HandleAsync(MisSolicitudesQuery query, CancellationToken ct)
    {
        var q = db.Set<Solicitud>().Where(s => s.SolicitanteId == actor.ActorId)
            .Join(db.Set<Caso>(), s => s.CasoId, c => c.Id, (s, c) => new { s, c });

        if (Enum.TryParse<EstadoCaso>(query.Estado, out var estado)) q = q.Where(x => x.c.Estado == estado);
        var text = query.Q?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var digits = Cuit.Normalize(text);
            q = text.All(ch => char.IsAsciiDigit(ch) || ch == '-') && digits.Length > 0
                ? q.Where(x => x.c.Cuit.StartsWith(digits))
                : q.Where(x => x.c.RazonSocial.Contains(text));
        }

        var rows = await q.OrderByDescending(x => x.s.CreatedAtUtc)
            .Select(x => new
            {
                x.c.Id, x.c.Cuit, x.c.RazonSocial, x.c.TipoTercero, x.c.Estado, x.c.EstadoDesdeUtc, x.c.CerradoUtc,
                Creada = x.s.CreatedAtUtc, x.s.EsOriginal,
                NoEntregado = db.Set<EnlacePortal>().Any(e => e.CasoId == x.c.Id && e.Entrega == EntregaEnlace.Fallido),
            }).ToListAsync(ct);

        var hoy = BusinessCalendar.Today(clock);
        return Results.Ok(rows.Select(r => new MiSolicitud(
            r.Id, r.Cuit, r.RazonSocial, r.TipoTercero.ToString(), r.Estado.ToString(),
            DateTime.SpecifyKind(r.Creada, DateTimeKind.Utc), r.EsOriginal,
            FechaEstimada.Calcular(r.Estado, r.EstadoDesdeUtc, hoy),
            r.CerradoUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null, r.NoEntregado)));
    }
}

internal static class ListMisSolicitudesEndpoint
{
    // shortcut: sin paginado (decenas por solicitante); paginar si un usuario supera ~500
    public static void Map(RouteGroupBuilder solicitudes) => solicitudes.MapGet("", Handle);

    private static async Task<IResult> Handle(
        [AsParameters] MisSolicitudesQuery query, IValidator<MisSolicitudesQuery> validator,
        ListMisSolicitudesHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        return validation.IsValid ? await handler.HandleAsync(query, ct) : ApiProblems.Validation(validation);
    }
}
