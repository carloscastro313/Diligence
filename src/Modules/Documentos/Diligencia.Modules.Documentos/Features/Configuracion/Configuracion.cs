using Diligencia.BuildingBlocks.Correlation;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Documentos.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Documentos.Features.Configuracion;

internal sealed record ConfiguracionDocumentosRequest(int? TamanoMaximoMb);

internal sealed class ConfiguracionDocumentosValidator : AbstractValidator<ConfiguracionDocumentosRequest>
{
    public const int MinMb = 1;
    public const int MaxMb = 50;

    public ConfiguracionDocumentosValidator() =>
        RuleFor(r => r.TamanoMaximoMb).NotNull().WithMessage("El tamaño máximo es obligatorio.")
            .InclusiveBetween(MinMb, MaxMb).WithMessage($"El tamaño máximo debe estar entre {MinMb} y {MaxMb} MB.");
}

internal sealed class ConfiguracionDocumentosHandler(DocumentosDbContext db, IAuditLog audit, TimeProvider clock)
{
    private const long Mb = 1024 * 1024;

    public async Task<int> GetAsync(CancellationToken ct) =>
        (int)((await db.Parametros.AsNoTracking().SingleAsync(ct)).TamanoMaximoBytes / Mb);

    public async Task<int> SetAsync(int tamanoMaximoMb, CancellationToken cancellationToken)
    {
        await db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            var parametros = await db.Parametros.SingleAsync(ct);
            var anterior = parametros.TamanoMaximoBytes / Mb;
            parametros.TamanoMaximoBytes = tamanoMaximoMb * Mb;
            parametros.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(false, ct);
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "ConfiguracionDocumentos", parametros.Id.ToString(),
                [new AuditChange("TamanoMaximoMb", anterior.ToString(), tamanoMaximoMb.ToString())]), tx, ct);
        }, cancellationToken);
        return tamanoMaximoMb;
    }
}

internal static class ConfiguracionDocumentosEndpoint
{
    public static void Map(RouteGroupBuilder documentos)
    {
        documentos.MapGet("/configuracion", async (ConfiguracionDocumentosHandler handler, CancellationToken ct) =>
            Results.Ok(new { tamanoMaximoMb = await handler.GetAsync(ct) }));
        documentos.MapPut("/configuracion", Put);
    }

    private static async Task<IResult> Put(
        ConfiguracionDocumentosRequest request, IValidator<ConfiguracionDocumentosRequest> validator,
        ConfiguracionDocumentosHandler handler, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.ValidationProblem(
                validation.Errors.GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                extensions: new Dictionary<string, object?> { ["correlationId"] = CorrelationContext.Current });
        return Results.Ok(new { tamanoMaximoMb = await handler.SetAsync(request.TamanoMaximoMb!.Value, ct) });
    }
}
