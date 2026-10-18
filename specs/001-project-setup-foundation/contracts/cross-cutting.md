# Contratos transversales

Comportamientos que cualquier cliente, operador o feature futura puede asumir. Detalle de las
decisiones en [../research.md](../research.md).

## 1. Correlación (FR-013)

| Elemento | Contrato |
|---|---|
| Header de entrada | `X-Correlation-Id`, opcional. Se reutiliza si cumple `^[A-Za-z0-9._:-]{1,64}$`. |
| Header de salida | `X-Correlation-Id` en **toda** respuesta de la API, incluidos errores. |
| Errores | `correlationId` en el cuerpo ProblemDetails. |
| Jobs | Parámetro Hangfire `CorrelationId`; el worker lo restaura antes de ejecutar. |
| Bitácora | Columna `CorrelationId` de cada evento. |

## 2. Registros técnicos (FR-012, FR-014, FR-015)

Formato: una línea JSON por evento en stdout (`Logging:Console:FormatterName=json`) y OTLP al
dashboard de Aspire en local.

| Campo | Origen |
|---|---|
| `Timestamp` | UTC, ISO 8601 (`O`) |
| `LogLevel` | nivel |
| `Category` | logger; `Diligencia.Modules.<Módulo>.*` identifica el módulo |
| `Scopes[].CorrelationId` | middleware (API) o filtro de job (worker) |
| `Scopes[].Service` | `Diligencia.Api` o `Diligencia.Worker` |
| `Message` / `State` | mensaje y propiedades con nombre |

Log de solicitud (HttpLogging): método, ruta (sin query string), status, duración. Nunca headers ni
cuerpos.

**Prohibido en logs y bitácora**: contraseñas, tokens (incluido `Authorization`), secretos, cadenas
de conexión, contenido de documentos. Verificado por test (SC-005).

**Nivel por entorno/módulo**, sin cambiar código:

```text
Logging__LogLevel__Default=Information
Logging__LogLevel__Diligencia.Modules.Auditoria=Debug
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning
```

## 3. Errores (FR-016)

`application/problem+json` (RFC 9457). Un 500 no incluye mensaje de excepción, tipo ni stack trace:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1", "title": "An error occurred while processing your request.", "status": 500, "correlationId": "4bf92f3577b34da6a3ce929d0e0e4736" }
```

## 4. Salud (FR-005)

| Endpoint | Proceso | Chequeos | Códigos |
|---|---|---|---|
| `/alive` | api, worker | `self` | 200 |
| `/health` | api, worker | `self`, `database` | 200 sano / 503 no sano |

Cuerpo: `{"status":"Unhealthy","checks":{"self":"Healthy","database":"Unhealthy"}}`. Sin mensajes
de excepción ni datos de conexión.

## 5. Configuración (FR-006)

| Clave | Proceso | Obligatoria | Nota |
|---|---|---|---|
| `ConnectionStrings:diligencia` | api, worker, migrations | sí | identidad propia del proceso: runtime (api/worker), migrador (migrations) |
| `ConnectionStrings:diligencia-runtime` | migrations | solo con bootstrap | de ahí salen usuario y contraseña del login de runtime |
| `ConnectionStrings:diligencia-admin` | migrations | no | su presencia activa el bootstrap (local/CI) |
| `Authentication:Schemes:Backoffice:ValidIssuer` / `ValidAudiences` / `SigningKeys` | api | sí | la clave la genera el AppHost (`backoffice-signing-key`); la API emite estos JWT al ingresar (feature 002) |
| `Authentication:Schemes:Portal:ValidIssuer` / `ValidAudiences` / `SigningKeys` | api | sí | ídem |
| `Diagnostics:Enabled` | api | no (`false`) | `true` en Development y tests |
| `Logging:*` | todos | no | ver §2 |

Si falta una obligatoria el proceso termina al arrancar con:
`Falta la configuración obligatoria '<clave>'.` (nunca imprime valores).

Ningún secreto se versiona: user-secrets (API y AppHost) en local, parámetros secretos de Aspire
para las contraseñas de SQL, variables de entorno en CI.

## 6. Tokens de desarrollo y de test (FR-004)

El backoffice emite sus propios tokens al ingresar (`POST /api/backoffice/auth/login`, feature 002); el portal sigue sin
endpoint de emisión hasta RF-05.

| Contexto | Cómo se obtiene |
|---|---|
| Local, backoffice | Ingreso real: ver `specs/002-backoffice-auth-rbac/quickstart.md` (el JWT va en la cookie `dlg_bo` o como Bearer) |
| Local, portal | `dotnet user-jwts create --project src/Hosts/Diligencia.Api --scheme Portal --issuer diligencia-portal-dev --audience diligencia-portal --name tercero-1` |
| Tests de integración, backoffice | `TestUsers.CreateAsync(...)` inserta usuario + roles + sesión y devuelve el JWT (con `sid`) firmado con la clave de test |
| Tests de integración, portal | `TestUsers.CreatePortalToken("tercero-1")` |

Un token firmado para un esquema es rechazado con 401 en el otro grupo.

## 7. Fronteras del frontend (FR-007)

| Desde | Puede importar |
|---|---|
| `apps/<app>/src/features/<f>` | la misma feature `<f>`, `apps/<app>/src/shared`, `packages/*` |
| `apps/<app>/src/shared` | `apps/<app>/src/shared`, `packages/*` |
| `apps/<app>/src/app` | features de la misma app, `shared`, `packages/*` |
| `packages/<p>` | `packages/*` |
| `apps/portal/**` | nunca `apps/backoffice/**` |

Una violación es un error de ESLint y bloquea CI.
