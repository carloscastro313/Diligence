using Diligencia.BuildingBlocks.Correlation;
using Diligencia.BuildingBlocks.Http;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Auditoria.Features.QueryEvents;
using Diligencia.Modules.Auditoria.Features.RecordEvent;
using Diligencia.Modules.Auditoria.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Auditoria;

public static class AuditoriaModule
{
    public static IServiceCollection AddAuditoriaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(configuration, AuditDbContext.Schema);
        services.AddHealthChecks().AddDbContextCheck<AuditDbContext>("database");
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<QueryEventsHandler>();
        services.AddSingleton<IValidator<QueryEventsRequest>, QueryEventsValidator>();
        return services;
    }

    public static ApiSurfaces MapAuditoriaEndpoints(this ApiSurfaces surfaces)
    {
        surfaces.Backoffice.MapGet("/audit/events", QueryEvents).RequireAuthorization(AuthPolicies.AuditReader);
        return surfaces;
    }

    private static async Task<IResult> QueryEvents(
        [AsParameters] QueryEventsRequest request,
        IValidator<QueryEventsRequest> validator,
        QueryEventsHandler handler,
        IAuditLog audit,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(
                validation.ToDictionary(),
                extensions: new Dictionary<string, object?> { ["correlationId"] = CorrelationContext.Current });

        // La consulta de la bitácora también se audita (FR-022); si no se puede registrar, no se devuelven datos.
        await audit.RecordStandaloneAsync(new AuditEntry(
            AuditAction.SensitiveDataViewed, "AuditLog", "query", Filters(request)), ct);

        return Results.Ok(await handler.HandleAsync(request, ct));
    }

    private static List<AuditChange> Filters(QueryEventsRequest r)
    {
        var filters = new List<AuditChange>();
        void Add(string name, string? value) { if (value is not null) filters.Add(new AuditChange(name, null, value)); }
        Add("entityType", r.EntityType);
        Add("entityId", r.EntityId);
        Add("actorId", r.ActorId);
        Add("fromUtc", r.FromUtc?.UtcDateTime.ToString("O"));
        Add("toUtc", r.ToUtc?.UtcDateTime.ToString("O"));
        Add("page", r.PageOrDefault.ToString());
        Add("pageSize", r.PageSizeOrDefault.ToString());
        return filters;
    }
}
