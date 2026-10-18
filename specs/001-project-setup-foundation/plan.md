# Implementation Plan: Setup base del proyecto (estructura, base de datos, logs y auditoría)

**Branch**: `001-project-setup-foundation` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-project-setup-foundation/spec.md`

## Summary

Crear el esqueleto completo de Diligencia Service: una solución .NET 10 (monolito modular) con host
de API (superficies `/api/backoffice` y `/api/portal` con esquemas JWT separados), proceso worker
Hangfire, un servicio de migraciones y siete módulos vacíos (Contracts + interno), más dos SPAs React
(`apps/backoffice`, `apps/portal`) y `packages/` compartidos. Todo se orquesta en local con un único
`dotnet run` del AppHost de Aspire sobre SQL Server en contenedor (Docker Engine en WSL).

El único módulo con funcionalidad es **Auditoría**: un contrato público `IAuditLog` que escribe el
evento dentro de la conexión/transacción del módulo llamador (atómico), una tabla de solo agregado
protegida por `DENY UPDATE, DELETE` al login de runtime, y un endpoint de consulta para auditor y
administrador que a su vez queda auditado. Transversalmente: identificador de correlación de punta a
punta (API → Hangfire → worker → bitácora), logs JSON estructurados configurables por categoría,
ProblemDetails genérico ante errores, chequeos de salud con estado de base de datos separado,
configuración fail-fast y tests de arquitectura + ESLint de fronteras que bloquean CI.

## Technical Context

**Language/Version**: C# 14 sobre .NET 10 (SDK fijado en `global.json`); TypeScript 5.x `strict` sobre
Node.js 24 LTS.

**Primary Dependencies**: ASP.NET Core minimal APIs, Aspire 13.x (AppHost, ServiceDefaults,
`Aspire.Hosting.SqlServer`, `Aspire.Hosting.JavaScript`), EF Core 10 (SqlServer), FluentValidation,
Hangfire (Core, SqlServer, AspNetCore), `Microsoft.AspNetCore.Authentication.JwtBearer`,
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`. Frontend: React, Vite, MUI,
TanStack Query/Router, ESLint (flat config) + `eslint-plugin-boundaries`.

**Storage**: SQL Server 2022 (contenedor en local y CI). Una base `diligencia`; un schema por módulo
(`terceros`, `casos`, `riesgo`, `aprobaciones`, `documentos`, `auditoria`, `identidad`) más
`hangfire`. Historial de migraciones por módulo en `<schema>.__EFMigrationsHistory`.

**Testing**: xUnit v3 (unit, integración, arquitectura); NetArchTest.Rules; Testcontainers.MsSql +
`WebApplicationFactory` para integración; `Aspire.Hosting.Testing` para el smoke de arranque;
`Microsoft.Extensions.Diagnostics.Testing` (FakeLogger) para verificar logs; Vitest + Testing Library
en frontend. Playwright se difiere (no hay flujos críticos en esta feature).

**Target Platform**: contenedores Linux. Desarrollo en WSL2 con Docker Engine (sin Docker Desktop);
CI en runners Linux con Docker (GitHub Actions, ver research R19).

**Project Type**: web-service (monolito modular) + worker + 2 aplicaciones web SPA.

**Performance Goals**: consulta de bitácora por entidad < 2 s con 1.000.000 de eventos (SC-007);
entorno local completo en < 15 min desde el clon (SC-001).

**Constraints**: sin secretos versionados; API y worker solo usan el login de runtime; la base
rechaza UPDATE/DELETE sobre la bitácora con esa identidad; ningún log contiene contraseñas, tokens,
secretos, cadenas de conexión ni contenido de documentos; ningún endpoint de emisión de tokens.

**Scale/Scope**: 7 módulos (6 vacíos), 2 superficies de API, 1 worker, 1 servicio de migraciones,
2 SPAs, 1 paquete compartido (`packages/ui`).

Todas las incógnitas técnicas quedaron resueltas en [research.md](research.md); no quedan
NEEDS CLARIFICATION.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Datos exigidos por la constitución (Flujo de desarrollo)**

| Pregunta | Respuesta |
|---|---|
| Módulo dueño | **Auditoría** (única funcionalidad). Infraestructura transversal en hosts y `BuildingBlocks`. Los otros seis módulos se crean vacíos. |
| Grupo de API | **backoffice**: `GET /api/backoffice/audit/events` (auditor/administrador) y `POST /api/backoffice/diagnostics/jobs` (administrador, solo con `Diagnostics:Enabled`). **portal**: grupo y esquema configurados, sin endpoints. |
| Contratos públicos tocados | `Auditoria.Contracts` (nuevo: `IAuditLog`, `AuditEntry`, `AuditChange`, `AuditAction`). `Identidad.Contracts` (nuevo: `IActorContext`, `ActorSurface`, `BackofficeRoles`, `AuthPolicies`). Resto: proyectos Contracts vacíos. |
| Trabajo en worker | Sí: host del worker, filtro de correlación de Hangfire y un job de diagnóstico. Sin procesamiento pesado. |

**Gates por principio (pre-diseño y post-diseño)**

| Principio | Cómo se cumple | Pre | Post |
|---|---|---|---|
| I. Monolito modular / vertical slices | Un host de API; módulos en `src/Modules/<Módulo>`; dentro de Auditoría, carpetas `Features/RecordEvent` y `Features/QueryEvents`. Sin `Services/` ni `Repositories/` globales. | ✅ | ✅ |
| II. Cada módulo dueño de sus datos | Un `DbContext` y schema por módulo; historial de migraciones en el schema propio; Auditoría escribe en su schema con su propio `DbContext` aun cuando se enlista en la transacción del llamador (research R5). | ✅ | ✅ |
| III. Solo contratos públicos | Todo `internal` salvo `Contracts` y la clase de entrada `<Módulo>Module` (registro DI + mapeo de endpoints), justificada en Complexity Tracking. | ✅ | ✅ |
| IV. Fronteras verificadas (NON-NEGOTIABLE) | `Diligencia.ArchitectureTests` (NetArchTest + reflexión) y ESLint `eslint-plugin-boundaries`, ambos con fixtures de violación deliberada (SC-003); corren en CI. | ✅ | ✅ |
| V. Dos superficies de API | Esquemas JWT `Backoffice` y `Portal` (emisor, audiencia y clave distintos); grupos con policy obligatoria; `FallbackPolicy` autenticada; test que recorre todos los endpoints y exige grupo + policy. | ✅ | ✅ |
| VI. Worker en proceso aparte | `Diligencia.Worker` con Hangfire Server; la API solo tiene el cliente Hangfire y encola. Jobs idempotentes. Test de arquitectura API ⟂ Worker. | ✅ | ✅ |
| VII. Frontends por features | `apps/backoffice` y `apps/portal` con `src/features/*` y `src/shared`; `packages/ui`; reglas de boundaries feature→feature y portal→backoffice. Sin lógica de negocio. | ✅ | ✅ |
| VIII. API autoridad de seguridad y auditoría | RBAC por policies; bitácora de solo agregado (DENY en base + guarda en `SaveChanges`); FluentValidation en el borde; logs sin secretos. BCrypt no aplica aún (no se almacenan contraseñas). | ✅ | ✅ |
| Stack | Todo dentro del stack salvo las dependencias justificadas abajo. | ✅ | ✅ |
| Quality gates | CI: build, xUnit (unit + integración + arquitectura), ESLint, typecheck, Vitest, build de imágenes Docker. | ✅ | ✅ |

**Resultado**: PASS (pre y post diseño). Las excepciones están justificadas en Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-project-setup-foundation/
├── plan.md              # Este archivo
├── research.md          # Fase 0: decisiones técnicas
├── data-model.md        # Fase 1: entidades, schemas, identidades y permisos
├── quickstart.md        # Fase 1: guía de validación end-to-end
├── contracts/
│   ├── audit-api.openapi.yaml    # Endpoints HTTP (bitácora, diagnóstico, salud)
│   ├── audit-module-contract.md  # Contrato público C# de Auditoría e Identidad
│   └── cross-cutting.md          # Correlación, logs, errores, configuración, tokens
└── tasks.md             # Fase 2 (/speckit-tasks; no lo crea este comando)
```

### Source Code (repository root)

```text
Diligencia.slnx
global.json                      # SDK .NET 10 fijado
Directory.Build.props            # Nullable, TreatWarningsAsErrors, InternalsVisibleTo → tests
Directory.Packages.props         # Central Package Management
.editorconfig  .gitignore
package.json                     # npm workspaces: apps/*, packages/*
eslint.config.js                 # flat config compartida + reglas de boundaries
tsconfig.base.json
.github/workflows/ci.yml

src/
├── Aspire/
│   ├── Diligencia.AppHost/              # único comando de arranque local
│   └── Diligencia.ServiceDefaults/      # OTel, health endpoints (JSON), resiliencia
├── Hosts/
│   ├── Diligencia.Api/                  # Program.cs, grupos backoffice/portal, Dockerfile
│   ├── Diligencia.Worker/               # Hangfire Server + /health, Dockerfile
│   └── Diligencia.MigrationService/     # bootstrap de logins (local/CI), migraciones, grants
├── BuildingBlocks/
│   └── Diligencia.BuildingBlocks/       # técnico, sin negocio:
│       ├── Correlation/                 #   CorrelationContext, middleware, filtro Hangfire
│       ├── Persistence/                 #   AddModuleDbContext, IModuleDatabase, ITimestamped,
│       │                                #   TimestampsInterceptor, ExecuteInTransactionAsync
│       ├── Http/                        #   ApiSurfaces (grupos backoffice/portal)
│       └── Configuration/               #   GetRequired (fail-fast)
└── Modules/
    ├── Auditoria/
    │   ├── Diligencia.Modules.Auditoria.Contracts/   # IAuditLog, AuditEntry, AuditChange, AuditAction
    │   └── Diligencia.Modules.Auditoria/
    │       ├── AuditoriaModule.cs                     # entrada pública: Add…/Map…
    │       ├── Persistence/  (AuditDbContext, AuditEvent, Migrations/)
    │       └── Features/
    │           ├── RecordEvent/   (AuditLog, AuditValueMasker)
    │           └── QueryEvents/   (endpoint, request, validator, handler)
    ├── Identidad/
    │   ├── Diligencia.Modules.Identidad.Contracts/   # IActorContext, ActorSurface, roles, policies
    │   └── Diligencia.Modules.Identidad/             # esquemas JWT, policies, HttpActorContext, DbContext vacío
    ├── Terceros/      (Contracts + interno: TercerosModule, DbContext vacío, migración Initial)
    ├── Casos/         (ídem)
    ├── Riesgo/        (ídem)
    ├── Aprobaciones/  (ídem)
    └── Documentos/    (ídem)

tests/
├── Diligencia.UnitTests/                # masking, correlación, timestamps, validadores
├── Diligencia.IntegrationTests/         # Testcontainers + WebApplicationFactory; smoke Aspire
├── Diligencia.ArchitectureTests/        # reglas de fronteras (NetArchTest + reflexión)
└── Fixtures/
    └── Diligencia.ArchitectureTests.Violations/   # violaciones deliberadas (SC-003)

apps/
├── backoffice/                          # Vite + React + MUI; Dockerfile (nginx)
│   └── src/ app/  features/home/  shared/
└── portal/                              # ídem; solo importa de packages/
    └── src/ app/  features/home/  shared/
packages/
└── ui/                                  # tema MUI compartido
eslint-fixtures/                         # imports prohibidos a propósito (SC-003 frontend)
```

**Structure Decision**: monolito modular con backend en `src/` (Aspire, hosts, building blocks y
módulos), tests en `tests/`, y frontends en `apps/` + `packages/` en la raíz como fija la
constitución (Principio VII), con npm workspaces en la raíz. Cada módulo es un par de proyectos
`Diligencia.Modules.<M>.Contracts` (público) y `Diligencia.Modules.<M>` (interno).

## Complexity Tracking

| Violación / excepción | Por qué hace falta | Alternativa más simple descartada porque |
|---|---|---|
| Clase pública `<Módulo>Module` fuera de `Contracts` | El host necesita registrar servicios y mapear endpoints de cada módulo. Es el único tipo público permitido; el test de arquitectura lo admite por nombre exacto. | Hacer `public` el `DbContext` o los endpoints rompería el Principio III; un escaneo por reflexión de tipos internos esconde el acoplamiento. |
| Proyecto `Diligencia.BuildingBlocks` compartido | Correlación, convenciones de `DbContext` y fail-fast de configuración se necesitan idénticas en todos los módulos y hosts. Solo código técnico; un test prohíbe que referencie módulos. | Duplicarlo en 7 módulos y 3 hosts divergiría; meterlo en un módulo de negocio crearía dependencias entre módulos. |
| Endpoints `/health` y `/alive` fuera de los grupos y anónimos | Aspire, Docker y CI los consultan sin token (FR-005). No exponen datos ni mensajes de excepción. Excluidos por nombre en el test de "todo endpoint en un grupo con policy". | Ponerlos tras JWT impediría el chequeo del orquestador. |
| Host extra `Diligencia.MigrationService` | FR-009/FR-019: las migraciones corren con el login migrador, nunca con el de runtime; en local se aplica solo al arrancar y en otros entornos es un paso explícito. | Migrar desde la API exigiría darle a la API permisos de DDL, lo que anula el `DENY` sobre la bitácora. |
| Dependencia `Testcontainers.MsSql` (solo tests) | Los tests de integración necesitan un SQL Server real para verificar `DENY`, transacciones compartidas y el rendimiento de SC-007. | SQLite/InMemory no soportan schemas, permisos ni `SYSUTCDATETIME()`; usar el AppHost completo en cada test es mucho más lento. |
| Dependencia `Microsoft.Extensions.Diagnostics.Testing` (solo tests) | `FakeLogger` captura logs con scopes para probar SC-004/SC-005. | Un `ILoggerProvider` propio es código extra para lo mismo. |
| `eslint-plugin-boundaries` en vez de `import/no-restricted-paths` | La constitución admite "o equivalente". Expresa "una feature solo importa de sí misma" de forma genérica, sin una zona por feature. | `no-restricted-paths` exige editar la config en cada feature nueva. |
