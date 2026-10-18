# Data Model: Setup base del proyecto

Fase 1 de [plan.md](plan.md). Decisiones en [research.md](research.md).

## 1. Base de datos y schemas

Una base `diligencia`. Cada módulo es dueño exclusivo de su schema (Principio II).

| Schema | Dueño | Contenido en esta feature |
|---|---|---|
| `terceros` | Terceros | solo `__EFMigrationsHistory` |
| `casos` | Casos | solo `__EFMigrationsHistory` |
| `riesgo` | Riesgo | solo `__EFMigrationsHistory` |
| `aprobaciones` | Aprobaciones | solo `__EFMigrationsHistory` |
| `documentos` | Documentos | solo `__EFMigrationsHistory` |
| `identidad` | Identidad | solo `__EFMigrationsHistory` |
| `auditoria` | Auditoría | `AuditEvents`, `__EFMigrationsHistory` |
| `hangfire` | Infraestructura (worker) | tablas de Hangfire (instaladas por MigrationService) |

Cada `<schema>.__EFMigrationsHistory` es el historial propio del módulo (FR-008).

## 2. Entidad `AuditEvent` (`auditoria.AuditEvents`)

Registro inmutable de una acción (FR-017). No implementa `ITimestamped`: su única fecha es
`OccurredAtUtc`.

| Columna | Tipo SQL | Nulo | Regla |
|---|---|---|---|
| `Id` | `bigint IDENTITY` | no | PK agrupada; orden de inserción |
| `OccurredAtUtc` | `datetime2(7)` | no | `DEFAULT SYSUTCDATETIME()`; la asigna la base, nunca la app |
| `ActorId` | `nvarchar(200)` | no | claim `sub` del usuario o `system:<servicio>`; nunca vacío |
| `Surface` | `nvarchar(16)` | no | `Backoffice` \| `Portal` \| `System` |
| `Action` | `nvarchar(32)` | no | valor de `AuditAction` como texto |
| `EntityType` | `nvarchar(128)` | no | nombre lógico, p. ej. `Tercero`; no es FK |
| `EntityId` | `nvarchar(128)` | no | id de la entidad como texto; no es FK |
| `OldValues` | `nvarchar(max)` | sí | objeto JSON `{campo: valor}` solo con campos cambiados; sensibles = `"***"` |
| `NewValues` | `nvarchar(max)` | sí | ídem |
| `CorrelationId` | `nvarchar(64)` | no | del `CorrelationContext` |

**Índices**

- `IX_AuditEvents_Entity` (`EntityType`, `EntityId`, `OccurredAtUtc`, `Id`)
- `IX_AuditEvents_Actor` (`ActorId`, `OccurredAtUtc`, `Id`)
- `IX_AuditEvents_OccurredAt` (`OccurredAtUtc`, `Id`)

**Validación al registrar** (en `AuditLog`, antes de escribir; un fallo lanza y la transacción del
llamador no se confirma):

- `EntityType` y `EntityId`: no vacíos, ≤ 128 caracteres.
- `transaction` no nula y con `Connection` abierta (`RecordAsync`).
- `Changes`: se descartan los pares con `OldValue == NewValue`; `Field` no vacío y único.
- Enmascarado: `IsSensitive == true` o nombre de campo que contiene `password`, `secret` o `token`
  → ambos valores `"***"`.

**Ciclo de vida**: solo `Insert`. No hay transiciones. `Update`/`Delete` se rechazan en
`AuditDbContext.SaveChanges*` (excepción) y en la base (`DENY`, error 229).

**Relaciones**: ninguna FK. Referencia entidades de cualquier módulo por `EntityType` + `EntityId`.

### Enum `AuditAction`

| Valor | Uso |
|---|---|
| `Created` | alta de una entidad |
| `Updated` | cambio de una entidad |
| `SensitiveDataViewed` | consulta de datos sensibles (incluye cada consulta a la bitácora, FR-022) |
| `Downloaded` | descarga de un documento o expediente |
| `Decision` | aprobación, rechazo u otra decisión |

Se guarda como texto para que la bitácora sea legible sin la aplicación. Agregar valores es
compatible; renombrar no.

### Evento que genera la consulta de la bitácora (FR-022)

`Action = SensitiveDataViewed`, `EntityType = "AuditLog"`, `EntityId = "query"`,
`NewValues` = filtros usados (`entityType`, `entityId`, `actorId`, `fromUtc`, `toUtc`, `page`,
`pageSize`). Se registra con `RecordStandaloneAsync` antes de devolver resultados; si falla, la
consulta responde error y no devuelve datos.

## 3. Convención de timestamps (FR-011)

Toda entidad de negocio futura implementa `ITimestamped`:

| Propiedad | Tipo | Regla |
|---|---|---|
| `CreatedAtUtc` | `DateTime` (`datetime2`) | la fija `TimestampsInterceptor` en `Added` |
| `UpdatedAtUtc` | `DateTime` (`datetime2`) | la fija el interceptor en `Added` y `Modified` |

El test de arquitectura R16-6 falla si una entidad mapeada no la implementa (excepción: `AuditEvent`).

## 4. Actor (no persistido como entidad propia)

Resuelto por `IActorContext` (Identidad) en cada evento:

| Origen | `ActorId` | `Surface` |
|---|---|---|
| Request autenticado en `/api/backoffice` | claim `sub` | `Backoffice` |
| Request autenticado en `/api/portal` | claim `sub` | `Portal` |
| Job del worker / proceso sin usuario | `system:<ApplicationName>` (p. ej. `system:Diligencia.Worker`) | `System` |

Roles de backoffice (`Identidad.Contracts.BackofficeRoles`): `Solicitante`, `Analista`,
`Aprobador`, `Administrador`, `Auditor`. El portal no usa roles en esta feature.

## 5. Identificador de correlación

| Atributo | Regla |
|---|---|
| Formato aceptado de entrada | `^[A-Za-z0-9._:-]{1,64}$` en header `X-Correlation-Id` |
| Si falta o es inválido | se genera (TraceId de la actividad actual o GUID `N`) |
| Dónde viaja | header de respuesta, scope de log, extensión `correlationId` de ProblemDetails, parámetro `CorrelationId` del job de Hangfire, columna `AuditEvents.CorrelationId` |

## 6. Identidades de base de datos y permisos (FR-019)

| Principal | Tipo | Membresía | Lo usa |
|---|---|---|---|
| `sa` (o admin del entorno) | login | sysadmin | solo bootstrap de MigrationService en local/CI |
| `diligencia_migrator` | login + usuario | `db_owner`; dueño de los schemas que crea | MigrationService |
| `diligencia_app` | login + usuario | rol `diligencia_runtime`; sin propiedad de schemas | API y worker |
| `diligencia_runtime` | rol de base | — | — |

**Permisos del rol `diligencia_runtime`**

| Objeto | Permiso |
|---|---|
| `SCHEMA::<cada schema de módulo>` y `SCHEMA::hangfire` | `GRANT SELECT, INSERT, UPDATE, DELETE` |
| `auditoria.AuditEvents` | `DENY UPDATE, DELETE` (en la migración de Auditoría) |
| `<cada schema>.__EFMigrationsHistory` | `DENY UPDATE, DELETE` |
| DDL (`CREATE`, `ALTER`, `DROP`, `TRUNCATE`) | ninguno |

## 7. Datos de Hangfire relevantes

| Dato | Regla |
|---|---|
| Parámetro de job `CorrelationId` | lo escribe `CorrelationJobFilter` al encolar; lo restaura al ejecutar |
| Schema | `hangfire`; `PrepareSchemaIfNecessary = false` en API y worker (lo instala MigrationService) |
