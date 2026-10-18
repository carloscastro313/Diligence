using System.Data.Common;

namespace Diligencia.Modules.Auditoria.Contracts;

public interface IAuditLog
{
    // Escribe el evento dentro de la conexión y transacción del llamador.
    // No confirma ni revierte: si lanza, el llamador no debe confirmar (FR-018).
    Task RecordAsync(AuditEntry entry, DbTransaction transaction, CancellationToken cancellationToken = default);

    // Para acciones sin escritura de negocio (consulta de datos sensibles, descarga).
    // Usa su propia conexión y se confirma sola.
    Task RecordStandaloneAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(
    AuditAction Action,
    string EntityType,
    string EntityId,
    IReadOnlyList<AuditChange>? Changes = null);

public sealed record AuditChange(
    string Field,
    string? OldValue,
    string? NewValue,
    bool IsSensitive = false);

public enum AuditAction { Created, Updated, SensitiveDataViewed, Downloaded, Decision }
