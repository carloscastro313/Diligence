using System.Data.Common;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Terceros.Contracts;
using Diligencia.Modules.Terceros.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Terceros.Registry;

internal sealed class TerceroRegistry(TercerosDbContext db, IAuditLog audit, TimestampsInterceptor timestamps) : ITerceroRegistry
{
    public async Task<TerceroInfo?> FindByCuitAsync(string cuit, CancellationToken cancellationToken = default)
    {
        var t = await db.Terceros.AsNoTracking().SingleOrDefaultAsync(x => x.Cuit == cuit, cancellationToken);
        return t is null ? null : ToInfo(t);
    }

    public async Task<(TerceroInfo Tercero, bool Created)> GetOrCreateForOnboardingAsync(
        NuevoTercero nuevo, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new ArgumentException("La transacción debe tener una conexión abierta.", nameof(transaction));

        // Contexto efímero sobre la tx del llamador (como IAuditLog); no confirma ni revierte.
        var options = new DbContextOptionsBuilder<TercerosDbContext>().UseSqlServer(connection).AddInterceptors(timestamps).Options;
        await using var ctx = new TercerosDbContext(options);
        await ctx.Database.UseTransactionAsync(transaction, cancellationToken);

        var cuit = nuevo.Cuit;
        var existing = (await ctx.Terceros
            .FromSql($"SELECT * FROM terceros.Terceros WITH (UPDLOCK, HOLDLOCK) WHERE Cuit = {cuit}")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (existing is not null) return (ToInfo(existing), false);

        var tercero = new Tercero
        {
            Cuit = cuit, TipoTercero = nuevo.Tipo.ToString(), TipoPersona = nuevo.Persona.ToString(),
            RazonSocial = nuevo.RazonSocial, Estado = EstadoTercero.EnAlta.ToString(),
        };
        ctx.Terceros.Add(tercero);
        await ctx.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Tercero", tercero.Id.ToString(),
        [
            new AuditChange("Cuit", null, tercero.Cuit),
            new AuditChange("TipoTercero", null, tercero.TipoTercero),
            new AuditChange("TipoPersona", null, tercero.TipoPersona),
            new AuditChange("RazonSocial", null, tercero.RazonSocial),
            new AuditChange("Estado", null, tercero.Estado),
        ]), transaction, cancellationToken);
        return (ToInfo(tercero), true);
    }

    private static TerceroInfo ToInfo(Tercero t) => new(
        t.Id, t.Cuit, Enum.Parse<TipoTercero>(t.TipoTercero), Enum.Parse<TipoPersona>(t.TipoPersona),
        t.RazonSocial, Enum.Parse<EstadoTercero>(t.Estado));
}
