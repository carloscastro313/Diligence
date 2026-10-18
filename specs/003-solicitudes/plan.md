# Implementation Plan: Solicitudes de alta de terceros

**Branch**: `003-solicitudes` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-solicitudes/spec.md`

## Summary

El solicitante crea una solicitud de alta (RF-01), ve antes de guardar si el CUIT ya existe
(RF-02), el sistema envía al tercero un enlace seguro al portal (RF-03) y el solicitante consulta
sus solicitudes con estado y fecha estimada (RF-04).

Enfoque técnico: los primeros datos reales de **Terceros** (tabla `Terceros`, CUIT único) y
**Casos** (`Casos`, `Solicitudes`, `EnlacesPortal`). Los tres endpoints son slices de Casos, que
crea el tercero por `ITerceroRegistry` dentro de su propia transacción (mismo patrón que
`IAuditLog`), con un lock por CUIT para que dos solicitudes simultáneas terminen en un solo
tercero y un solo caso. El correo con el enlace lo envía un job de Hangfire en el worker: genera el
token ahí (nunca viaja en los argumentos del job ni se guarda en claro), reintenta hasta 5 veces y
deja el fallo visible. El envío usa un transporte SMTP nuevo en `BuildingBlocks` que lanza si
falla; Identidad no se modifica.
La fecha estimada se calcula al leer con días hábiles. En el backoffice: lista "Mis solicitudes" y
formulario de nueva solicitud con aviso de duplicado; primer test de Playwright.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 5.x `strict` / Node 24 LTS (sin cambios).

**Primary Dependencies**: existentes (ASP.NET Core minimal APIs, EF Core 10, FluentValidation,
Hangfire, React, MUI, TanStack Query/Router, React Hook Form + Zod). Nueva: `@playwright/test`
(dev, en el stack). Sin paquetes nuevos en el backend.

**Storage**: SQL Server, schemas `terceros` (`Terceros`) y `casos` (`Casos`, `Solicitudes`,
`EnlacesPortal`); una migración por módulo ([data-model.md](data-model.md)). Bitácora vía
`IAuditLog`; cola de correo en `hangfire`.

**Testing**: xUnit v3 + Testcontainers.MsSql + `WebApplicationFactory`, `FakeTimeProvider`,
`IBackgroundJobClient` y `IEmailTransport` falsos; NetArchTest; Vitest + Testing Library;
Playwright contra el AppHost.

**Target Platform**: contenedores Linux; local con Aspire sobre Docker Engine en WSL.

**Project Type**: web-service (monolito modular) + worker + SPA de backoffice.

**Performance Goals**: aviso de duplicado < 2 s (SC-002: una lectura por índice único + debounce de
400 ms); correo entregado < 5 min para el 95 % (SC-004: poll de Hangfire 5 s). Ambos se dan por
cumplidos por diseño y se observan en el recorrido del quickstart; no hay test de carga.

**Constraints**: token nunca en claro, en logs, en bitácora ni en Hangfire (FR-010); un solo
tercero por CUIT y un solo caso abierto por tercero también con concurrencia (SC-003); el
solicitante nunca ve datos de otro ni de riesgo (SC-005).

**Scale/Scope**: cientos de solicitudes al mes; 3 endpoints; 1 job; 2 pantallas.

No quedan NEEDS CLARIFICATION: decisiones en [research.md](research.md) (R1–R12).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Datos exigidos por la constitución**

| Pregunta | Respuesta |
|---|---|
| Módulo dueño | **Casos** (caso, solicitud, enlace; endpoints y job). **Terceros** (tercero y unicidad del CUIT). |
| Grupo de API | **backoffice** únicamente: `/api/backoffice/solicitudes*`. Portal sin endpoints nuevos. |
| Contratos públicos tocados | `Terceros.Contracts`: + `ITerceroRegistry` y tipos. `BuildingBlocks`: + `IEmailTransport`. `Identidad.Contracts` sin cambios. Todos aditivos ([contracts/module-contracts.md](contracts/module-contracts.md)). |
| Trabajo en worker | **Sí**: job `IPortalLinkDelivery` (envío del enlace con reintentos). Idempotente por estado de entrega. |

**Gates por principio**

| Principio | Cómo se cumple | Pre | Post |
|---|---|---|---|
| I. Monolito modular / vertical slices | `Casos/Features/{CheckCuit,CreateSolicitud,ListMisSolicitudes,SendPortalLink}`; reglas compartidas (CUIT, calendario) en `Casos/Features/Shared`. **No se modifica el interior de otro módulo**: el rol se exige en el subgrupo de Casos (R9) e Identidad conserva su sender de correo (R7); solo se agregan contenidos a Terceros, cuyo modelo nace en esta feature. | ✅ | ✅ |
| II. Dueño de sus datos | Tablas nuevas solo en `terceros` y `casos`. Casos no lee `terceros`: usa el contrato y guarda su copia de CUIT/razón social (R10). La transacción compartida pasa por contrato, como `IAuditLog`. | ✅ | ✅ |
| III. Solo contratos públicos | Casos referencia `Terceros.Contracts`, `Auditoria.Contracts`, `Identidad.Contracts`. Todo lo nuevo en los módulos es `internal`. | ✅ | ✅ |
| IV. Fronteras verificadas | Reglas de arquitectura sin cambios (cubren el nuevo uso). ESLint: `features/solicitudes` solo importa `shared/` y `packages/`. | ✅ | ✅ |
| V. Dos superficies | Tres endpoints en el subgrupo Backoffice `/solicitudes` con `RequireRole(Solicitante)`; ninguno anónimo; filas nuevas en `PermissionMatrixTests`. | ✅ | ✅ |
| VI. Worker aparte | El envío de correo con reintentos va a Hangfire en el worker; la API solo encola. Job idempotente y reintentable (R6). | ✅ | ✅ |
| VII. Frontend sin lógica de negocio | Zod repite la validación de CUIT solo para UX; el aviso de duplicado y toda regla (bloqueado, activo, caso abierto, ya lo sigue) los decide la API. | ✅ | ✅ |
| VIII. API autoridad de seguridad y auditoría | FluentValidation en el borde para los tres endpoints (cuerpo y query params); filtro por `ActorId` en SQL; Tercero/Caso/Solicitud/Enlace en la bitácora en la misma tx que el cambio; token solo como hash. | ✅ | ✅ |
| Stack | Única dependencia nueva: `@playwright/test`, nombrada en el stack. | ✅ | ✅ |
| Quality gates | Test de backend por cada regla (CUIT, duplicados, concurrencia, reintentos, filtro por actor, fecha estimada). Playwright para el tramo de alta que existe (R11). | ✅ | ✅ |

**Resultado**: PASS (pre y post diseño). Sin excepciones.

## Project Structure

### Documentation (this feature)

```text
specs/003-solicitudes/
├── plan.md              # Este archivo
├── research.md          # Fase 0: decisiones R1–R12
├── data-model.md        # Fase 1: tablas, estados, bitácora
├── quickstart.md        # Fase 1: validación end-to-end
├── contracts/
│   ├── backoffice-solicitudes-api.openapi.yaml  # 3 endpoints HTTP
│   └── module-contracts.md                      # Identidad, Terceros, BuildingBlocks, Casos
└── tasks.md             # Fase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── Aspire/Diligencia.AppHost/Program.cs        # worker: + Email__Smtp__*, Casos__PortalBaseUrl (endpoint de portal)
├── BuildingBlocks/Diligencia.BuildingBlocks/Email/
│   └── EmailTransport.cs                       # IEmailTransport + SmtpEmailTransport + AddEmailTransport
├── Hosts/
│   ├── Diligencia.Api/Program.cs               # + AddEmailTransport
│   └── Diligencia.Worker/Program.cs            # + AddEmailTransport
└── Modules/
    ├── Terceros/
    │   ├── Diligencia.Modules.Terceros.Contracts/TercerosContracts.cs     # ITerceroRegistry, TerceroInfo, enums
    │   └── Diligencia.Modules.Terceros/
    │       ├── TercerosModule.cs                # registra ITerceroRegistry
    │       ├── Registry/TerceroRegistry.cs      # FindByCuit + GetOrCreate (UPDLOCK, HOLDLOCK) + bitácora
    │       └── Persistence/                     # Tercero + configuración + migración
    └── Casos/Diligencia.Modules.Casos/
        ├── CasosModule.cs                       # handlers, validadores, opciones, job; subgrupo /solicitudes con RequireRole(Solicitante)
        ├── CasosOptions.cs                      # PortalBaseUrl
        ├── Persistence/                         # Caso, Solicitud, EnlacePortal + configuración + migración
        └── Features/
            ├── Shared/        # Cuit, BusinessCalendar, StagePlazos/fecha estimada, PortalToken
            ├── CheckCuit/     # GET /solicitudes/tercero-existente
            ├── CreateSolicitud/  # POST /solicitudes (+ encola el job)
            ├── ListMisSolicitudes/  # GET /solicitudes
            └── SendPortalLink/   # IPortalLinkDelivery (job Hangfire) + plantilla de correo

tests/
├── Diligencia.UnitTests/Casos/                 # Cuit, BusinessCalendar, fecha estimada, validador
└── Diligencia.IntegrationTests/
    ├── Casos/                                  # alta, duplicados, concurrencia, job, lista, bitácora
    ├── Identidad/PermissionMatrixTests.cs      # + 3 filas Solicitante
    └── Support/                                # FakeEmailTransport, FakeBackgroundJobClient

apps/backoffice/
├── src/app/router.tsx  src/app/Layout.tsx      # rutas /solicitudes y /solicitudes/nueva, ítem de menú
├── src/features/solicitudes/                   # SolicitudesPage, NuevaSolicitudPage, cuit.ts (+ tests)
├── e2e/solicitudes.spec.ts
└── playwright.config.ts
```

**Structure Decision**: se extiende la estructura existente sin proyectos nuevos. Los módulos
Terceros y Casos reciben sus primeras tablas y slices siguiendo el patrón de
`Modules/Identidad/Features/*` (endpoint + request + validador + handler juntos; registro por
convención de nombre como `IdentidadModule.AddFeatureServices`). El transporte SMTP que lanza va a
`BuildingBlocks` (infraestructura sin reglas de negocio); Identidad queda intacta.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | Sin violaciones. | Transacción compartida por contrato en vez de saga: ya es el patrón de `IAuditLog` (R2). |

## Riesgos y notas

- Si encolar el job falla después de confirmar, el enlace queda `Pendiente` sin reintento hasta que
  exista un barrido recurrente (shortcut anotado; R6).
- Un SMTP que entrega pero responde error hace que el reintento invalide el primer enlace (R6).
- El portal todavía no tiene `/acceso`: el enlace lleva a una página inexistente hasta RF-05.
- La copia de razón social en `casos.Casos` no se actualiza si en el futuro se edita el tercero
  (R10); la feature que lo permita debe publicar un evento.
- Días hábiles sin feriados y plazos fijos (supuestos del spec).
- Lectura de `Email:Smtp` duplicada entre `Identidad.SmtpEmailSender` y `SmtpEmailTransport`
  (~5 líneas); unificar en un refactor de Identidad aparte (R7).
- Playwright necesita el AppHost levantado; en CI se suma un paso que lo arranca (como
  `AppHostSmokeTests`).
