using System.Text.Json;
using Diligencia.Modules.Auditoria.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Auditoria.Features.QueryEvents;

internal sealed record QueryEventsRequest(
    string? EntityType,
    string? EntityId,
    string? ActorId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int? Page,
    int? PageSize)
{
    public const int DefaultPageSize = 50;
    public int PageOrDefault => Page ?? 1;
    public int PageSizeOrDefault => PageSize ?? DefaultPageSize;
}

internal sealed class QueryEventsValidator : AbstractValidator<QueryEventsRequest>
{
    public QueryEventsValidator()
    {
        RuleFor(r => r.EntityType).MaximumLength(128);
        RuleFor(r => r.EntityId).MaximumLength(128);
        RuleFor(r => r.EntityId).Empty().When(r => string.IsNullOrEmpty(r.EntityType))
            .WithMessage("'entityId' requiere 'entityType'.");
        RuleFor(r => r.ActorId).MaximumLength(200);
        RuleFor(r => r.ToUtc).GreaterThan(r => r.FromUtc)
            .When(r => r.FromUtc.HasValue && r.ToUtc.HasValue)
            .WithMessage("'toUtc' debe ser mayor que 'fromUtc'.");
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1).When(r => r.Page.HasValue);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100).When(r => r.PageSize.HasValue);
    }
}

internal sealed record AuditEventDto(
    long Id,
    DateTime OccurredAtUtc,
    string ActorId,
    string Surface,
    string Action,
    string EntityType,
    string EntityId,
    Dictionary<string, string?>? OldValues,
    Dictionary<string, string?>? NewValues,
    string CorrelationId);

internal sealed record AuditEventPage(IReadOnlyList<AuditEventDto> Items, int Page, int PageSize, bool HasNextPage);

internal sealed class QueryEventsHandler(AuditDbContext db)
{
    public async Task<AuditEventPage> HandleAsync(QueryEventsRequest r, CancellationToken ct)
    {
        var query = db.Events.AsNoTracking();
        if (!string.IsNullOrEmpty(r.EntityType)) query = query.Where(e => e.EntityType == r.EntityType);
        if (!string.IsNullOrEmpty(r.EntityId)) query = query.Where(e => e.EntityId == r.EntityId);
        if (!string.IsNullOrEmpty(r.ActorId)) query = query.Where(e => e.ActorId == r.ActorId);
        if (r.FromUtc is { } from) query = query.Where(e => e.OccurredAtUtc >= from.UtcDateTime);
        if (r.ToUtc is { } to) query = query.Where(e => e.OccurredAtUtc < to.UtcDateTime);

        int page = r.PageOrDefault, size = r.PageSizeOrDefault;

        // pageSize + 1 filas dicen si hay otra página sin un COUNT(*) sobre toda la tabla (R7).
        var rows = await query
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Skip((page - 1) * size).Take(size + 1)
            .ToListAsync(ct);

        var items = rows.Take(size).Select(e => new AuditEventDto(
            e.Id, DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc), e.ActorId, e.Surface, e.Action,
            e.EntityType, e.EntityId, Parse(e.OldValues), Parse(e.NewValues), e.CorrelationId)).ToList();

        return new AuditEventPage(items, page, size, rows.Count > size);
    }

    private static Dictionary<string, string?>? Parse(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
}
