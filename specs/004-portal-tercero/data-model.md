# Data model: Portal del tercero

Tres schemas tocados, cada uno con su migración y sin joins entre ellos (Principio II). Enums como
texto (`nvarchar(32)`), fechas UTC (`datetime2`), `CreatedAtUtc`/`UpdatedAtUtc` vía `ITimestamped`,
igual que en la feature 003.

## Schema `casos` (módulo Casos)

### `EnlacesPortal` (existe; columnas nuevas)

| Columna | Tipo | Reglas |
|---|---|---|
| `FallidosConsecutivos` | `int` default 0 | suma de intentos fallidos de todos los códigos; 0 al ingresar bien |
| `BloqueadoHastaUtc` | `datetime2` null | al llegar a 10 fallidos: ahora + 30 min (research R4) |

Enlace **válido** = `TokenHash` coincide ∧ `VenceUtc > ahora` ∧ caso en `EsperaTercero` ∧
`BloqueadoHastaUtc` nulo o pasado (research R5).

### `CodigosIngreso` (nueva)

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `EnlaceId` | `uniqueidentifier` FK → `EnlacesPortal` | |
| `CodigoHash` | `binary(32)` | `SHA-256(enlaceId ‖ código)`; nunca el código |
| `VenceUtc` | `datetime2` | emisión + 10 min |
| `IntentosFallidos` | `int` | ≥ 5 ⇒ invalidado |
| `Estado` | `nvarchar(32)` | `Vigente` \| `Usado` \| `Invalidado` |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | |

- Índice `(EnlaceId, CreatedAtUtc)`: cuenta de envíos de la última hora (máx. 5).
- Al emitir uno nuevo, los `Vigente` del mismo enlace pasan a `Invalidado` en la misma transacción.

```text
Vigente ──(código correcto, no vencido)──► Usado
   │
   ├──(5.º intento fallido | se emite otro)──► Invalidado
   └──(vence)  se trata como inválido al leer; no se actualiza
```

### `FormulariosTercero` (nueva)

| Columna | Tipo | Reglas |
|---|---|---|
| `CasoId` | `uniqueidentifier` PK, FK → `Casos` | uno por caso; se crea con el primer guardado |
| `Datos` | `nvarchar(max)` | JSON de `FormularioDatos` (abajo) |
| `Estado` | `nvarchar(32)` | `Borrador` \| `Enviado` |
| `GuardadoUtc` | `datetime2` | último guardado |
| `EnviadoUtc` | `datetime2` null | |
| `EnviadoPor` | `nvarchar(128)` null | `IActorContext.ActorId` (`tercero:{id}`) |
| `Version` | `rowversion` | detecta otra pestaña (research R7) |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | |

```text
(sin fila) ──guardar──► Borrador ──guardar──► Borrador
                           │
                           └──enviar (válido + documentos obligatorios)──► Enviado  (solo lectura)
```

### `Casos` (existe; sin columnas nuevas)

Transición nueva de esta feature, en la misma transacción que el envío del formulario:

```text
EsperaTercero ──(formulario enviado)──► EnVerificacion     EstadoDesdeUtc = ahora
```

## `FormularioDatos` (JSON en `FormulariosTercero.Datos`)

Todos los campos son opcionales al guardar; "Oblig." indica lo que exige el envío. J = persona
jurídica, H = persona humana (del tercero, vía `ITerceroRegistry`). CUIT y razón social/nombre no
están en el JSON: se muestran desde el caso, no editables (US2-7).

| Ruta | Tipo / formato | Persona | Oblig. |
|---|---|---|---|
| `condicionIva` | `ResponsableInscripto` \| `Monotributo` \| `Exento` \| `NoAlcanzado` \| `ConsumidorFinal` | J, H | sí |
| `fechaConstitucion` | fecha ISO, ≤ hoy | J | sí |
| `numeroInscripcion` | texto ≤ 50 | J | sí |
| `fechaNacimiento` | fecha ISO, edad 18–120 | H | sí |
| `nacionalidad` | ISO 3166-1 alfa-2 | H | sí |
| `documento` | `{ tipo: DNI\|Pasaporte, numero ≤ 20 }` (DNI: 7–8 dígitos) | H | sí |
| `domicilioFiscal` | `Domicilio` | J, H | sí |
| `domicilioLegal` | `Domicilio` o `domicilioLegalIgualFiscal: true` | J | sí (uno de los dos) |
| `actividad` | `{ descripcion ≤ 200, pais: alfa-2 }` | J, H | sí |
| `beneficiarios` | lista de `Beneficiario` | J | ≥ 1, salvo `sinBeneficiarios` |
| `sinBeneficiarios` | `{ explicacion: 20–1000 }` | J | excluyente con `beneficiarios` |
| `representantes` | lista de `{ nombre ≤ 200, documento ≤ 20, pep: Pep }` | J | ≥ 1 |
| `pep` | `Pep` (declaración propia) | H | sí |
| `origenFondos` | texto 10–2000 | J, H | sí |

- `Domicilio` = `{ calle ≤ 200, numero ≤ 20, piso? ≤ 10, depto? ≤ 10, localidad ≤ 100,
  provincia ≤ 100, codigoPostal ≤ 10, pais: alfa-2 }`, todos obligatorios salvo piso y depto.
- `Beneficiario` = `{ nombre ≤ 200, documento ≤ 20, nacionalidad: alfa-2, porcentaje: 0 < p ≤ 100
  (2 decimales), pep: Pep }`. **Suma de `porcentaje` ≤ 100** (se valida también al guardar: es un
  error de forma). Máx. 20 beneficiarios y 10 representantes.
- `Pep` = `{ esPep: bool, cargo?: ≤ 200, organismo?: ≤ 200 }`; si `esPep`, cargo y organismo son
  obligatorios al enviar (FR-009).
- Campos de la otra persona (p. ej. `beneficiarios` en H) se rechazan al enviar ("no corresponde al
  tipo de persona") y se ignoran al guardar.

## Documentos requeridos (FR-016; constante en Casos, `DocumentosRequeridos`)

| Código (`Tipo`) | Nombre en pantalla | J | H |
|---|---|---|---|
| `Estatuto` | Estatuto o contrato social | oblig. | — |
| `ConstanciaArca` | Constancia de inscripción en ARCA | oblig. | oblig. |
| `ActaAutoridades` | Acta de designación de autoridades | oblig. | — |
| `DocumentoRepresentante` | Documento del representante legal | oblig. | — |
| `UltimoBalance` | Último balance | opc. | — |
| `DniFrente` | Documento de identidad (frente) | — | oblig. |
| `DniDorso` | Documento de identidad (dorso) | — | oblig. |
| `ConstanciaDomicilio` | Constancia de domicilio | — | opc. |

Un archivo vigente por tipo; subir otro lo reemplaza. Un tipo que no corresponde a la persona del
caso se rechaza (400).

## Schema `documentos` (módulo Documentos)

### `Documentos` (nueva)

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `CasoId`, `TerceroId` | `uniqueidentifier` | sin FK: otros schemas |
| `Tipo` | `nvarchar(64)` | código de la tabla de arriba (Documentos no lo interpreta) |
| `NombreOriginal` | `nvarchar(255)` | sin ruta; se recorta a 255 |
| `ContentType` | `nvarchar(64)` | **detectado**: `application/pdf` \| `image/jpeg` \| `image/png` |
| `TamanoBytes` | `bigint` | |
| `Sha256` | `binary(32)` | del contenido en claro (evidencia de integridad) |
| `CargadoPor` | `nvarchar(128)` | `IActorContext.ActorId` |
| `CargadoUtc` | `datetime2` | |
| `BajaUtc` | `datetime2` null | |
| `Baja` | `nvarchar(32)` null | `Reemplazado` \| `Eliminado` |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` | |

- Índice único **filtrado** `(CasoId, Tipo) WHERE BajaUtc IS NULL`: un vigente por tipo y caso.
- Las bajas no borran filas ni contenido (evidencia; FR-017 prohíbe eliminar tras el envío, y antes
  del envío la versión anterior queda en la bitácora).

### `DocumentoContenidos` (nueva)

| Columna | Tipo | Reglas |
|---|---|---|
| `DocumentoId` | `uniqueidentifier` PK, FK → `Documentos` | |
| `Nonce` | `binary(12)` | aleatorio por archivo |
| `Tag` | `binary(16)` | tag de AES-GCM |
| `Cifrado` | `varbinary(max)` | AES-256-GCM (research R8) |

Tabla aparte para que listar documentos nunca lea el contenido.

### `Configuracion` (nueva, una fila)

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `int` PK | siempre 1 (`CHECK (Id = 1)`) |
| `TamanoMaximoBytes` | `bigint` | 1 MB – 50 MB; sembrado en 10 MB (`HasData`) |
| `UpdatedAtUtc` | `datetime2` | |

## Schema `identidad` (módulo Identidad)

### `PortalSessions` (nueva)

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uniqueidentifier` PK | claim `sid` |
| `CasoId`, `TerceroId` | `uniqueidentifier` | sin FK: otros schemas; origen de los claims `caso`/`tercero` |
| `CreatedAtUtc` | `datetime2` | |
| `LastActivityUtc` | `datetime2` | como mucho una escritura por minuto (igual que `Sessions`) |
| `ExpiresAtUtc` | `datetime2` | creación + 8 h |
| `RevokedAtUtc` | `datetime2` null | sin uso en esta feature (no hay "Salir") |

Sesión válida = no revocada ∧ `ahora < ExpiresAtUtc` ∧ `ahora − LastActivityUtc < 30 min`.
Varias sesiones por caso permitidas (otro dispositivo; edge case).

## Bitácora

Ver research R11. Toda escritura de negocio y su evento van en la misma transacción
(`IAuditLog.RecordAsync(entry, tx)`). Valores nunca registrados: token del enlace, código, contenido
de archivos.
