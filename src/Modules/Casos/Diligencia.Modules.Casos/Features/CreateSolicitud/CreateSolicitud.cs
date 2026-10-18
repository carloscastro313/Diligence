using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Casos.Features.SendPortalLink;
using Diligencia.Modules.Casos.Features.Shared;
using Diligencia.Modules.Casos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Casos.Features.CreateSolicitud;

internal sealed record CrearSolicitudRequest(
    string? TipoTercero, string? TipoPersona, string? Cuit, string? RazonSocial,
    string? ContactoNombre, string? ContactoEmail, string? ContactoTelefono,
    decimal? MontoAnualEstimado, string? Motivo);

internal sealed class CrearSolicitudValidator : AbstractValidator<CrearSolicitudRequest>
{
    public CrearSolicitudValidator()
    {
        RuleFor(r => r.TipoTercero).Must(v => Enum.TryParse<TipoTercero>(v, out var e) && Enum.IsDefined(e)).WithMessage("El tipo de tercero no es válido.");
        RuleFor(r => r.TipoPersona).Must(v => Enum.TryParse<TipoPersona>(v, out var e) && Enum.IsDefined(e)).WithMessage("El tipo de persona no es válido.");
        RuleFor(r => r.Cuit).NotEmpty().WithMessage("El CUIT es obligatorio.").MaximumLength(20)
            .Must(Shared.Cuit.IsValid).WithMessage("El CUIT no es válido.");
        RuleFor(r => r.Cuit)
            .Must((r, c) => Shared.Cuit.MatchesPersona(c, Enum.Parse<TipoPersona>(r.TipoPersona!)))
            .WithMessage("El CUIT no corresponde al tipo de persona.")
            .When(r => Shared.Cuit.IsValid(r.Cuit) && Enum.TryParse<TipoPersona>(r.TipoPersona, out _));
        RuleFor(r => r.RazonSocial).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("La razón social es obligatoria.").MaximumLength(200);
        RuleFor(r => r.ContactoNombre).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("El nombre del contacto es obligatorio.").MaximumLength(200);
        RuleFor(r => r.ContactoEmail).NotEmpty().WithMessage("El correo del contacto es obligatorio.").EmailAddress().MaximumLength(254);
        RuleFor(r => r.ContactoTelefono).MaximumLength(50);
        RuleFor(r => r.MontoAnualEstimado).NotNull().GreaterThan(0).WithMessage("El monto anual estimado debe ser mayor que cero.");
        RuleFor(r => r.Motivo).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("El motivo es obligatorio.").MaximumLength(1000);
    }
}

internal sealed class CreateSolicitudHandler(
    CasosDbContext db, ITerceroRegistry terceros, IAuditLog audit, IActorContext actor,
    IBackgroundJobClient jobs, TimeProvider clock)
{
    public async Task<IResult> HandleAsync(CrearSolicitudRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        IResult? failure = null;
        Guid casoId = default;
        Guid? enlaceId = null;

        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            failure = null;
            enlaceId = null;
            var (tercero, created) = await terceros.GetOrCreateForOnboardingAsync(new NuevoTercero(
                Shared.Cuit.Normalize(request.Cuit), Enum.Parse<TipoTercero>(request.TipoTercero!),
                Enum.Parse<TipoPersona>(request.TipoPersona!), request.RazonSocial!.Trim()), tx, ct);

            // El registro mantiene bloqueado el CUIT hasta confirmar: la decisión de abajo no compite con otro alta.
            Caso? abierto = null;
            if (!created)
            {
                if (tercero.Estado is EstadoTercero.Activo or EstadoTercero.Bloqueado)
                {
                    failure = ApiProblems.Conflict(tercero.Estado == EstadoTercero.Activo
                        ? "El tercero ya está activo: no se puede abrir un alta."
                        : "El tercero está bloqueado: no se puede abrir un alta.");
                    return;
                }
                abierto = await CasoQueries.FindOpenCasoAsync(db, tercero.Id, ct);
                if (abierto is not null && await db.Set<Solicitud>().AnyAsync(s => s.CasoId == abierto.Id && s.SolicitanteId == actor.ActorId, ct))
                {
                    failure = ApiProblems.Conflict("Ya seguís este caso.");
                    return;
                }
            }

            var caso = abierto;
            if (caso is null)
            {
                caso = new Caso
                {
                    TerceroId = tercero.Id, Cuit = tercero.Cuit, RazonSocial = tercero.RazonSocial, TipoTercero = tercero.Tipo,
                    EstadoDesdeUtc = now, SolicitanteId = actor.ActorId,
                };
                db.Add(caso);
            }
            var solicitud = new Solicitud
            {
                CasoId = caso.Id, SolicitanteId = actor.ActorId, EsOriginal = abierto is null,
                ContactoNombre = request.ContactoNombre!.Trim(), ContactoEmail = request.ContactoEmail!.Trim().ToLowerInvariant(),
                ContactoTelefono = string.IsNullOrWhiteSpace(request.ContactoTelefono) ? null : request.ContactoTelefono.Trim(),
                MontoAnualEstimado = request.MontoAnualEstimado!.Value, Motivo = request.Motivo!.Trim(), CreatedAtUtc = now,
            };
            db.Add(solicitud);
            EnlacePortal? enlace = null;
            if (abierto is null)
            {
                var vence = BusinessCalendar.AddBusinessDays(BusinessCalendar.Today(clock), StagePlazos.EsperaTercero)
                    .ToDateTime(new TimeOnly(23, 59, 59)).AddHours(3);
                enlace = new EnlacePortal { CasoId = caso.Id, Destinatario = solicitud.ContactoEmail, VenceUtc = vence };
                db.Add(enlace);
            }
            await db.SaveChangesAsync(false, ct);

            if (abierto is null)
                await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Caso", caso.Id.ToString(),
                [
                    new AuditChange("Tercero", null, tercero.Id.ToString()),
                    new AuditChange("Tipo", null, caso.Tipo.ToString()),
                    new AuditChange("Estado", null, caso.Estado.ToString()),
                    new AuditChange("Solicitante", null, actor.ActorId),
                ]), tx, ct);
            await audit.RecordAsync(SolicitudCreated(solicitud), tx, ct);
            casoId = caso.Id;
            enlaceId = enlace?.Id;
        }, cancellationToken);

        if (failure is not null) return failure;
        if (enlaceId is not { } id)
            return Results.Created($"/api/backoffice/solicitudes/{casoId}", new { casoId, resultado = "SumadoACasoAbierto" });

        // shortcut: si encolar falla tras confirmar, el enlace queda Pendiente; agregar barrido recurrente con la feature de plazos
        jobs.Enqueue<IPortalLinkDelivery>(d => d.SendAsync(id, CancellationToken.None));
        return Results.Created($"/api/backoffice/solicitudes/{casoId}", new { casoId, resultado = "CasoAbierto" });
    }

    internal static AuditEntry SolicitudCreated(Solicitud s) => new(AuditAction.Created, "Solicitud", s.Id.ToString(),
    [
        new AuditChange("Caso", null, s.CasoId.ToString()),
        new AuditChange("EsOriginal", null, s.EsOriginal.ToString()),
        new AuditChange("ContactoNombre", null, s.ContactoNombre),
        new AuditChange("ContactoEmail", null, s.ContactoEmail),
        new AuditChange("ContactoTelefono", null, s.ContactoTelefono),
        new AuditChange("MontoAnualEstimado", null, s.MontoAnualEstimado.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
        new AuditChange("Motivo", null, s.Motivo),
    ]);
}

internal static class CreateSolicitudEndpoint
{
    public static void Map(RouteGroupBuilder solicitudes) => solicitudes.MapPost("", Handle);

    private static async Task<IResult> Handle(
        CrearSolicitudRequest request, IValidator<CrearSolicitudRequest> validator, CreateSolicitudHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? await handler.HandleAsync(request, ct) : ApiProblems.Validation(validation);
    }
}
