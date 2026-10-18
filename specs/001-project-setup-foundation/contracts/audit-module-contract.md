# Contrato público: Auditoría e Identidad

Interfaces en proceso que los módulos pueden referenciar (Principio III). Son API: un cambio
incompatible actualiza a todos los consumidores en el mismo cambio. Modelo de datos en
[../data-model.md](../data-model.md).

## `Diligencia.Modules.Auditoria.Contracts`

Solo depende de la BCL (no de EF Core).

```csharp
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
```

**Lo que el llamador NO envía** (lo resuelve Auditoría): fecha (`SYSUTCDATETIME()` de la base),
actor y superficie (`IActorContext`), identificador de correlación (`CorrelationContext`).

**Garantías**

- Atomicidad con la acción de negocio cuando se usa `RecordAsync` (misma transacción).
- Solo se guardan campos con `OldValue != NewValue`; sensibles y nombres con
  `password`/`secret`/`token` se guardan como `"***"`.
- Entrada inválida (`EntityType`/`EntityId` vacíos o > 128, transacción nula) →
  `ArgumentException`.
- El evento no puede modificarse ni borrarse después (aplicación y base).

**Uso esperado desde un módulo** (con la estrategia de reintento de EF activa):

```csharp
await db.ExecuteInTransactionAsync(async (tx, ct) =>
{
    tercero.Rename(nuevoNombre);
    await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);
    await auditLog.RecordAsync(
        new AuditEntry(AuditAction.Updated, "Tercero", tercero.Id.ToString(),
            [new AuditChange("Nombre", anterior, nuevoNombre)]),
        tx, ct);
}, cancellationToken);
```

`ExecuteInTransactionAsync` (BuildingBlocks) envuelve la estrategia de ejecución, abre la
transacción, pasa `DbTransaction` al delegado, confirma y luego `AcceptAllChanges()`.

## `Diligencia.Modules.Identidad.Contracts`

```csharp
namespace Diligencia.Modules.Identidad.Contracts;

public interface IActorContext
{
    string ActorId { get; }        // claim "sub" o "system:<ApplicationName>"
    ActorSurface Surface { get; }
}

public enum ActorSurface { Backoffice, Portal, System }

public static class AuthSchemes
{
    public const string Backoffice = "Backoffice";
    public const string Portal = "Portal";
}

public static class BackofficeRoles
{
    public const string Solicitante = "Solicitante";
    public const string Analista = "Analista";
    public const string Aprobador = "Aprobador";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";
}

public static class AuthPolicies
{
    public const string Backoffice = "Backoffice";        // esquema Backoffice + autenticado (policy del grupo)
    public const string Portal = "Portal";                // esquema Portal + autenticado (policy del grupo)
    public const string AuditReader = "AuditReader";      // Backoffice + rol Auditor o Administrador
    public const string Administrator = "Administrator";  // Backoffice + rol Administrador
}
```

## Clase de entrada de cada módulo (único tipo público fuera de Contracts)

```csharp
namespace Diligencia.Modules.<Modulo>;

public static class <Modulo>Module
{
    public static IServiceCollection Add<Modulo>Module(this IServiceCollection services, IConfiguration configuration);
    public static ApiSurfaces Map<Modulo>Endpoints(this ApiSurfaces surfaces); // sus features eligen Backoffice o Portal
}
```

`ApiSurfaces` (BuildingBlocks) = `record ApiSurfaces(RouteGroupBuilder Backoffice, RouteGroupBuilder Portal)`.
Los módulos vacíos implementan `Map…Endpoints` como no-op. El worker y el MigrationService solo
llaman `Add…Module`.
