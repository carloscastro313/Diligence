# Data Model: Autenticación y RBAC del backoffice

Todo vive en el schema `identidad`, dueño el módulo **Identidad** (`IdentidadDbContext`, una
migración nueva `AddUsersAndSessions`). El login de runtime ya tiene `SELECT, INSERT, UPDATE,
DELETE` sobre el schema (DatabaseProvisioner de la 001); no hacen falta grants nuevos. Las fechas
son `datetime2(7)` UTC. `User` y `Area` implementan `ITimestamped` (CreatedAtUtc/UpdatedAtUtc).

## Area

| Campo | Tipo | Reglas |
|---|---|---|
| Id | `uniqueidentifier` PK | |
| Name | `nvarchar(100)` | obligatorio, recortado; único (índice único) |
| IsActive | `bit` | default `1` |
| CreatedAtUtc / UpdatedAtUtc | `datetime2` | `ITimestamped` |

- Sin borrado (FR-011 solo pide crear, renombrar, desactivar y reactivar). Por eso "un área con
  usuarios no se elimina" se cumple sin código.
- Un área inactiva no puede asignarse en un alta ni en un cambio de área (validación en el
  handler: `409`). Los usuarios que ya la tienen la conservan.

## User

| Campo | Tipo | Reglas |
|---|---|---|
| Id | `uniqueidentifier` PK | también es el `sub` del JWT y el `ActorId` en la bitácora |
| Name | `nvarchar(200)` | obligatorio |
| Email | `nvarchar(254)` | guardado normalizado (`Trim().ToLowerInvariant()`); único |
| AreaId | `uniqueidentifier` FK → Area | obligatorio |
| IsActive | `bit` | |
| PasswordHash | `varchar(60)` null | BCrypt; `null` hasta que el usuario establece su contraseña |
| FailedLoginCount | `int` | default `0` |
| LockoutEndUtc | `datetime2` null | |
| LastLoginAtUtc | `datetime2` null | |
| CreatedAtUtc / UpdatedAtUtc | `datetime2` | `ITimestamped` |

- **Roles**: colección propia `UserRoles` (abajo). Un usuario activo tiene ≥ 1 rol (FR-009): lo
  valida el handler al crear/editar; desactivar no toca los roles, así que reactivar los recupera.
- `PasswordHash` nunca se expone en DTOs, logs ni bitácora (FR-003).

### Estados

```text
           alta (admin, con ≥1 rol)
                  │
                  ▼
   ┌──────── Activo ◀──────── reactivar (admin)
   │   (sin contraseña hasta usar el enlace)
   │              │
   │   desactivar (admin; no si es el último administrador activo)
   │              ▼
   └────────▶ Desactivado ── revoca todas sus sesiones abiertas
```

Bloqueo temporal (`LockoutEndUtc > ahora`) es ortogonal al estado y vence solo.

## UserRole

| Campo | Tipo | Reglas |
|---|---|---|
| UserId | `uniqueidentifier` FK → User | PK compuesta (UserId, Role) |
| Role | `varchar(32)` | uno de `BackofficeRoles` (Solicitante, Analista, Aprobador, Administrador, Auditor); `CHECK` en base |

## Session

| Campo | Tipo | Reglas |
|---|---|---|
| Id | `uniqueidentifier` PK | claim `sid` del JWT |
| UserId | `uniqueidentifier` FK → User | índice `(UserId)` para revocar todas |
| CreatedAtUtc | `datetime2` | ingreso |
| ExpiresAtUtc | `datetime2` | ingreso + `SessionAbsoluteHours` (= `exp` del JWT) |
| LastActivityUtc | `datetime2` | se actualiza como mucho una vez por minuto |
| RevokedAtUtc | `datetime2` null | logout, desactivación o restablecimiento de contraseña |

**Válida** ⇔ `RevokedAtUtc is null` ∧ `ahora < ExpiresAtUtc` ∧ `ahora − LastActivityUtc <
SessionIdleMinutes` ∧ usuario activo.

## PasswordToken

| Campo | Tipo | Reglas |
|---|---|---|
| Id | `uniqueidentifier` PK | |
| UserId | `uniqueidentifier` FK → User | |
| Purpose | `varchar(16)` | `Invitation` \| `Reset` |
| TokenHash | `binary(32)` | SHA-256 del token; índice único |
| ExpiresAtUtc | `datetime2` | invitación + 72 h; recuperación + 60 min |
| UsedAtUtc | `datetime2` null | un solo uso |
| CreatedAtUtc | `datetime2` | |

Consumir un token invalida (marca `UsedAtUtc`) todos los demás pendientes del mismo usuario.

> shortcut: no hay limpieza de `Sessions` ni `PasswordTokens` vencidos; crecen con cada ingreso.
> Agregar un job recurrente en el worker cuando el volumen lo justifique.

## Relaciones

```text
Area 1 ──── * User 1 ──── * UserRole
                   1 ──── * Session
                   1 ──── * PasswordToken
```

## Eventos de bitácora (schema `auditoria`, vía `IAuditLog`)

| Acción | EntityType / EntityId | Changes |
|---|---|---|
| `Created` | `Usuario` / id | Nombre, Correo, Area, Roles, Estado (old = null) |
| `Updated` | `Usuario` / id | solo los campos que cambiaron, old → new (incluye `Estado` Activo↔Desactivado) |
| `Created` / `Updated` | `Area` / id | Nombre, Estado |
| `LoginSucceeded` | `Usuario` / id | — |
| `LoginFailed` | `Usuario` / id o `desconocido` | `Motivo`: CredencialInvalida \| Desactivado \| Bloqueado \| SinContraseña |
| `AccountLocked` | `Usuario` / id | `BloqueadoHasta` |
| `LoggedOut` | `Usuario` / id | — |
| `PasswordResetRequested` | `Usuario` / id | — (solo si el correo existe y está activo) |
| `PasswordSet` | `Usuario` / id | `Origen`: Invitation \| Reset |
| `AccessDenied` | `Endpoint` / ruta | — |

Nunca se registran contraseñas, hashes ni tokens.
