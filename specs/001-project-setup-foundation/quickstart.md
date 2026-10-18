# Quickstart: validar el setup base

Guía para comprobar de punta a punta que la feature funciona. Contratos en [contracts/](contracts/),
modelo en [data-model.md](data-model.md).

## Prerequisitos

Recomendado: trabajar **dentro de WSL2** (Ubuntu), con el repo clonado en el filesystem de Linux.

| Herramienta | Versión | Comprobar |
|---|---|---|
| WSL2 + distro Linux | — | `wsl --status` (desde Windows) |
| Docker Engine (en WSL, no Docker Desktop) | 24+ | `docker info` |
| .NET SDK | 10.0 (ver `global.json`) | `dotnet --version` |
| Node.js | 24 LTS | `node --version` |
| `sqlcmd` (go-sqlcmd), opcional | — | `sqlcmd --version` |

Alternativa sin trabajar en WSL: CLI `docker` en Windows apuntando al daemon de WSL
(`DOCKER_HOST`); el resto igual.

## 1. Arranque con un comando (US1, SC-001, SC-002)

```bash
git clone <repo> diligencia && cd diligencia
# Una sola vez: genera las claves de firma en los user-secrets de la API (sin ellas la API no arranca)
(cd src/Hosts/Diligencia.Api &&
  dotnet user-jwts create --scheme Backoffice --issuer diligencia-backoffice-dev --audience diligencia-backoffice --name ana --role Auditor &&
  dotnet user-jwts create --scheme Portal --issuer diligencia-portal-dev --audience diligencia-portal --name tercero-1)
dotnet run --project src/Aspire/Diligencia.AppHost
```

Otros entornos: las migraciones son un paso explícito (`Diligencia.MigrationService` sin
`ConnectionStrings:diligencia-admin`; los logins los crea el DBA).

Esperado:

- La consola imprime la URL del dashboard de Aspire.
- En el dashboard, `sql`, `migrations` (estado *Finished*, código 0), `api`, `worker`,
  `backoffice` y `portal`; los cinco de larga vida en *Running* y *Healthy*.
- Las URLs de `backoffice` y `portal` cargan su página inicial.
- Tiempo desde el clon < 15 min sin contar la descarga de imágenes.

## 2. Salud y base de datos (US1-2, US2-4)

```bash
curl -s http://localhost:<puerto-api>/health    # {"status":"Healthy","checks":{"self":"Healthy","database":"Healthy"}}
curl -s http://localhost:<puerto-worker>/health
docker stop <contenedor-sql>
curl -si http://localhost:<puerto-api>/health   # 503, "database":"Unhealthy"; el log no muestra credenciales
docker start <contenedor-sql>                   # vuelve a 200 sin reiniciar la API
```

## 3. Schemas, identidades y permisos (US2, US4-3, SC-006)

Conectarse con el login de runtime (contraseña en los user-secrets del AppHost, parámetro
`sql-runtime-password`):

```sql
SELECT name FROM sys.schemas WHERE name IN
 ('terceros','casos','riesgo','aprobaciones','documentos','auditoria','identidad','hangfire');  -- 8 filas
SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='__EFMigrationsHistory';      -- 7 filas
UPDATE auditoria.AuditEvents SET ActorId = 'x';   -- error 229: permiso denegado
DELETE FROM auditoria.AuditEvents;                -- error 229
```

## 4. Tokens y superficies (FR-004)

```bash
cd src/Hosts/Diligencia.Api
BO=$(dotnet user-jwts create --scheme Backoffice --issuer diligencia-backoffice-dev --audience diligencia-backoffice --name ana --role Auditor -o token)
PO=$(dotnet user-jwts create --scheme Portal --issuer diligencia-portal-dev --audience diligencia-portal --name tercero-1 -o token)
API=http://localhost:<puerto-api>
curl -si -H "Authorization: Bearer $BO" "$API/api/backoffice/audit/events"   # 200
curl -si -H "Authorization: Bearer $PO" "$API/api/backoffice/audit/events"   # 401 (token de portal)
```

Un token de backoffice sin rol `Auditor`/`Administrador` → 403 (US4-5).

## 5. Bitácora (US4)

```bash
curl -s -H "Authorization: Bearer $BO" "$API/api/backoffice/audit/events?entityType=AuditLog"
```

Esperado: aparece un evento `SensitiveDataViewed` por cada consulta anterior (FR-022), con
`actorId: "ana"`, `surface: "Backoffice"` y el `correlationId` de esa solicitud. El registro de
cambios de negocio (US4-1, -2, -6) se valida con los tests de integración del paso 7, que usan un
módulo de prueba.

## 6. Correlación API → worker (US3, SC-004)

```bash
ADM=$(dotnet user-jwts create --scheme Backoffice --issuer diligencia-backoffice-dev --audience diligencia-backoffice --name admin --role Administrador -o token)
curl -si -X POST -H "Authorization: Bearer $ADM" -H "X-Correlation-Id: demo-123" "$API/api/backoffice/diagnostics/jobs"
```

Esperado: 202 con `X-Correlation-Id: demo-123`. En el dashboard (Structured logs), filtrar
`CorrelationId = demo-123` muestra el log de la solicitud en `api` y "Diagnostic job executed" en
`worker`. Ningún log contiene el token.

Repetir sin el header: la respuesta trae un id generado. Con un header inválido (p. ej. 200
caracteres) se genera uno nuevo.

## 7. Build, tests y lint (US1-3, SC-003, SC-005, SC-007)

```bash
dotnet build Diligencia.slnx                             # sin warnings (TreatWarningsAsErrors)
dotnet test --solution Diligencia.slnx --filter-not-trait "Category=Performance"   # unit + integración (Docker) + arquitectura
dotnet test --project tests/Diligencia.IntegrationTests --filter-trait "Category=Performance"  # 1.000.000 eventos, consulta < 2 s
npm ci
npm run lint && npm run typecheck && npm test && npm run build
```

Esperado: todo en verde. Los tests de arquitectura y el test de ESLint sobre `eslint-fixtures/`
confirman que cada violación sembrada se detecta.

Prueba manual de fronteras: agregar en `apps/portal/src/features/home` un
`import x from '../../../../backoffice/src/app'` → `npm run lint` falla. Agregar en un módulo
interno una referencia al proyecto interno de otro módulo → `dotnet test` (arquitectura) falla.

## 8. Configuración faltante (FR-006)

```bash
ConnectionStrings__diligencia= dotnet run --project src/Hosts/Diligencia.Api
```

Esperado: termina al arrancar con `Falta la configuración obligatoria 'ConnectionStrings:diligencia'.`

## 9. Imágenes (FR-003)

```bash
docker build -f src/Hosts/Diligencia.Api/Dockerfile .
docker build -f src/Hosts/Diligencia.Worker/Dockerfile .
docker build -f apps/backoffice/Dockerfile .
docker build -f apps/portal/Dockerfile .
```

Esperado: las cuatro imágenes se construyen; CI hace lo mismo en cada PR.
