using System.Data.Common;
using Diligencia.BuildingBlocks.Correlation;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Auditoria.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Auditoria.Features.RecordEvent;

internal sealed class AuditLog(IActorContext actor, AuditDbContext standaloneDb) : IAuditLog
{
    private const int MaxEntityLength = 128;

    public async Task RecordAsync(AuditEntry entry, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        Validate(entry);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection is not { State: System.Data.ConnectionState.Open } connection)
            throw new ArgumentException("La transacción debe tener una conexión abierta.", nameof(transaction));

        // Contexto efímero sobre la conexión y la transacción del llamador; este no confirma ni revierte.
        // Sin estrategia de reintento propia: el llamador reintenta la unidad completa (R5).
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(connection).Options;
        await using var db = new AuditDbContext(options);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        db.Events.Add(Create(entry));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordStandaloneAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Validate(entry);
        standaloneDb.Events.Add(Create(entry));
        await standaloneDb.SaveChangesAsync(cancellationToken);
    }

    private AuditEvent Create(AuditEntry entry)
    {
        var (oldValues, newValues) = AuditValueMasker.Build(entry.Changes);
        return new AuditEvent
        {
            ActorId = actor.ActorId,
            Surface = actor.Surface.ToString(),
            Action = entry.Action.ToString(),
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldValues = oldValues,
            NewValues = newValues,
            CorrelationId = CorrelationContext.Current,
        };
    }

    private static void Validate(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Enum.IsDefined(entry.Action))
            throw new ArgumentException("Acción de auditoría desconocida.", nameof(entry));
        Require(entry.EntityType, nameof(entry.EntityType));
        Require(entry.EntityId, nameof(entry.EntityId));
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxEntityLength)
            throw new ArgumentException($"{name} es obligatorio y admite hasta {MaxEntityLength} caracteres.", "entry");
    }
}
