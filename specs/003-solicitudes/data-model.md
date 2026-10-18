# Data model: Solicitudes de alta de terceros

Dos schemas tocados, cada uno con su migración. Ningún join entre ellos (Principio II). Enums se
guardan como texto (`nvarchar(32)`), como `AuditEvent.Action`. Fechas en UTC (`datetime2`).
`CreatedAtUtc`/`UpdatedAtUtc` vía `ITimestamped`.

## Schema `terceros` (módulo Terceros)

### `Terceros`

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `Cuit` | `char(11)` | **único**; solo dígitos, ya validado por el llamador |
| `TipoTercero` | `nvarchar(32)` | `Cliente` \| `Proveedor` |
| `TipoPersona` | `nvarchar(32)` | `Humana` \| `Juridica` |
| `RazonSocial` | `nvarchar(200)` | no vacío |
| `Estado` | `nvarchar(32)` | `EnAlta` \| `Activo` \| `Bloqueado` \| `Inactivo` |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | |

- Se crea en `EnAlta` al abrir el primer caso. Esta feature no cambia el estado de un tercero que ya
  existe (pasar a `Activo`, `Bloqueado` o `Inactivo` es de otras features).
- País, actividad, nivel de riesgo y próxima revisión (sección 7) llegan con el formulario y la
  evaluación (RF-06, RF-14).

## Schema `casos` (módulo Casos)

### `Casos`

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `TerceroId` | `uniqueidentifier` | id del tercero (sin FK: otro schema) |
| `Cuit`, `RazonSocial`, `TipoTercero` | copia del tercero al abrir (research R10) | |
| `Tipo` | `nvarchar(32)` | `Alta` (única en esta feature) |
| `Estado` | `nvarchar(32)` | ver transiciones |
| `EstadoDesdeUtc` | `datetime2` | inicio de la etapa actual (para la fecha estimada) |
| `CerradoUtc` | `datetime2` null | fecha del estado final |
| `SolicitanteId` | `nvarchar(128)` | `IActorContext.ActorId` del creador |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | |

- Índice único **filtrado** `(TerceroId) WHERE Estado IN ('EsperaTercero','EnVerificacion','EnAprobacion')`:
  a lo sumo un caso abierto por tercero (FR-007, research R3).

**Estados** (sección 4; `Borrador` no se usa):

```text
(alta) ──► EsperaTercero ──► EnVerificacion ──► EnAprobacion ──► Aprobado | Rechazado
                │
                └──► Abandonado   (vence el plazo; otra feature)
```

Esta feature solo crea casos en `EsperaTercero`. Las demás transiciones son de otras features; el
listado ya sabe mostrarlas. Abiertos: `EsperaTercero`, `EnVerificacion`, `EnAprobacion`. Finales:
`Aprobado`, `Rechazado`, `Abandonado`.

### `Solicitudes`

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `CasoId` | `uniqueidentifier` FK → `Casos` | |
| `SolicitanteId` | `nvarchar(128)` | actor que la creó |
| `EsOriginal` | `bit` | `true` = la que abrió el caso; `false` = interesado sumado |
| `ContactoNombre` | `nvarchar(200)` | no vacío |
| `ContactoEmail` | `nvarchar(254)` | formato válido, normalizado a minúsculas |
| `ContactoTelefono` | `nvarchar(50)` null | |
| `MontoAnualEstimado` | `decimal(18,2)` | > 0, pesos |
| `Motivo` | `nvarchar(1000)` | no vacío |
| `CreatedAtUtc` | `datetime2` | |

- Único `(CasoId, SolicitanteId)`: un usuario no se suma dos veces al mismo caso.
- Índice `(SolicitanteId)` para "mis solicitudes".
- El contacto de una solicitud de interesado se guarda pero no se usa (el enlace ya salió con el
  contacto original).

### `EnlacesPortal`

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `CasoId` | `uniqueidentifier` FK → `Casos`, **único** | uno por caso (FR-009) |
| `TokenHash` | `binary(32)` null | SHA-256 del token; null hasta el primer intento. Único filtrado `WHERE TokenHash IS NOT NULL` |
| `Destinatario` | `nvarchar(254)` | correo del contacto original |
| `VenceUtc` | `datetime2` | apertura + 15 días hábiles, 23:59:59 hora Argentina |
| `Entrega` | `nvarchar(32)` | `Pendiente` \| `Reintentando` \| `Enviado` \| `Fallido` |
| `Intentos` | `int` | 0..5 |
| `UltimoIntentoUtc` | `datetime2` null | |

**Entrega**:

```text
Pendiente ──► Enviado
    │  ▲
    ▼  │ (fallo, Intentos < 5)
Reintentando ──► Enviado
    │
    └──► Fallido   (Intentos = 5)
```

- El token en claro solo existe en memoria del job y en el cuerpo del correo (FR-010).
- Validez para el portal (lo verifica RF-05): `now < VenceUtc` y caso en `EsperaTercero`.

## Bitácora (FR-017)

Todo con `IAuditLog.RecordAsync` en la misma transacción que el cambio. Sin valores nuevos de
`AuditAction`.

| Acción | EntityType | Cambios | Quién escribe |
|---|---|---|---|
| `Created` | `Tercero` | Cuit, TipoTercero, TipoPersona, RazonSocial, Estado=EnAlta | Terceros (dentro de la tx de Casos) |
| `Created` | `Caso` | Tercero, Tipo=Alta, Estado: null→EsperaTercero, Solicitante | Casos |
| `Created` | `Solicitud` | Caso, EsOriginal, contacto (nombre, correo, teléfono), monto, motivo | Casos (original e interesado) |
| `Updated` | `EnlacePortal` | Entrega: anterior→nuevo, Intentos, VenceUtc (al enviar) | job en el worker, actor `system:` |

El token y la URL nunca entran en la bitácora ni en los logs.

## Fecha estimada (calculada al leer, no se guarda)

Ver research R8. Plazos: `EsperaTercero` 15, `EnVerificacion` 5, `EnAprobacion` 3 días hábiles.
