# Contratos entre módulos: Portal del tercero

Todos los cambios son aditivos. Lo no listado acá es `internal`.

## Identidad.Contracts (nuevo contenido)

```csharp
public interface IPortalSignIn
{
    // Crea la sesión de portal dentro de la conexión y transacción del llamador, escribe la cookie
    // dlg_pt en la respuesta actual y hace que el resto de la request actúe como "tercero:{terceroId}"
    // (IActorContext.ActorId), para que la bitácora del ingreso tenga autor. No confirma ni revierte.
    Task SignInAsync(Guid casoId, Guid terceroId, DbTransaction transaction, CancellationToken cancellationToken = default);
}

public interface IPortalActor
{
    // Caso y tercero de la sesión de portal de la request actual (claims cargados desde la base al
    // validar la sesión). Lanza InvalidOperationException fuera de una request de portal autenticada.
    Guid CasoId { get; }
    Guid TerceroId { get; }
}
```

Cambios internos de Identidad (no son contrato): `PortalSession` + migración, eventos del esquema
Portal (cookie, validación de sesión, inactividad de 30 min, claims `caso`/`tercero`), `TokenIssuer`
recibe el esquema, `IdentidadOptions.PortalSessionIdleMinutes = 30` y
`PortalSessionAbsoluteHours = 8`.

## Documentos.Contracts (nuevo contenido; el proyecto ya existe vacío)

```csharp
public sealed record NuevoDocumento(Guid CasoId, Guid TerceroId, string Tipo, string NombreOriginal);

public sealed record DocumentoInfo(
    Guid Id, Guid CasoId, string Tipo, string NombreOriginal, string ContentType, long TamanoBytes, DateTime CargadoUtc);

public enum MotivoRechazo { FormatoNoPermitido, Ilegible, DemasiadoGrande }

public sealed record DocumentoRechazado(MotivoRechazo Motivo, long TamanoMaximoBytes);

public interface IDocumentStore
{
    // Tamaño máximo vigente (lectura sin transacción; lo usa el portal para mostrarlo y cortar temprano).
    Task<long> GetTamanoMaximoBytesAsync(CancellationToken cancellationToken = default);

    // Dentro de la transacción del llamador: verifica tamaño y contenido real, cifra, guarda, da de baja
    // como Reemplazado el vigente del mismo (CasoId, Tipo) y registra la bitácora. No confirma ni revierte.
    // Devuelve el rechazo en vez de lanzar: es un resultado esperado, no un error.
    Task<(DocumentoInfo? Documento, DocumentoRechazado? Rechazo)> PutAsync(
        NuevoDocumento documento, Stream contenido, DbTransaction transaction, CancellationToken cancellationToken = default);

    // Baja lógica como Eliminado del vigente de (casoId, tipo) + bitácora. false si no había vigente.
    Task<bool> RemoveAsync(Guid casoId, string tipo, DbTransaction transaction, CancellationToken cancellationToken = default);

    // Vigentes de un caso (sin contenido). Con transaction, lee dentro de ella (para el envío).
    Task<IReadOnlyList<DocumentoInfo>> ListVigentesAsync(
        Guid casoId, DbTransaction? transaction = null, CancellationToken cancellationToken = default);
}
```

`Documentos` no conoce tipos de persona ni el catálogo de documentos requeridos: `Tipo` es un
código opaco que define Casos. Descargar o descifrar no forma parte del contrato todavía (lo agrega
la feature de verificación, con su bitácora `Downloaded`).

Superficie del módulo: `AddDocumentosModule` registra `IDocumentStore` y lee
`Documentos:EncryptionKey` (obligatoria en la API); `MapDocumentosEndpoints` mapea
`/api/backoffice/documentos/configuracion` con `AuthPolicies.Administrator`.

## Casos (superficie pública del módulo)

`Casos.Contracts` sigue sin contenido: ningún módulo consume casos todavía. `MapCasosEndpoints`
agrega el subgrupo `/api/portal` (`/acceso/codigo`, `/acceso` anónimos con
`RequireRateLimiting("portal-acceso")`; `/caso/**` con la policy Portal del grupo).
`AddCasosModule` registra la política de rate limiting `portal-acceso` vía
`Configure<RateLimiterOptions>`.

Casos referencia: `Identidad.Contracts` (`IPortalSignIn`, `IPortalActor`, `IActorContext`),
`Documentos.Contracts` (`IDocumentStore`), `Terceros.Contracts` (`FindByCuitAsync` para el tipo de
persona), `Auditoria.Contracts`. `BuildingBlocks.Email` (`IEmailTransport`) para el código.

## Terceros.Contracts y Auditoria.Contracts

Sin cambios.

## Configuración nueva

| Clave | Quién la lee | Valor local (AppHost) |
|---|---|---|
| `Authentication:Schemes:Portal:SigningKeys:0:Value` | API | parámetro persistido `portal-signing-key` (nuevo) |
| `Documentos:EncryptionKey` | API | parámetro persistido `documentos-encryption-key` (nuevo) |
| `Casos:PortalBaseUrl` | worker (ya existía) | endpoint http de `portal` |
| `Casos:AccesoRateLimitPerMinute` | API | default 10 |
| `Identidad:PortalSessionIdleMinutes`, `Identidad:PortalSessionAbsoluteHours` | API | default 30 / 8 |
