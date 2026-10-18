# Implementation Plan: Autenticación y control de acceso por roles (RBAC) del backoffice

**Branch**: `002-backoffice-auth-rbac` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-backoffice-auth-rbac/spec.md`

## Summary

Reemplazar los tokens de desarrollo (`dotnet user-jwts`) del backoffice por un ingreso real con
correo y contraseña (BCrypt) y dar al administrador la gestión de usuarios, roles y áreas (RF-28).
Todo vive en el módulo **Identidad**, que hoy solo configura los esquemas JWT.

Enfoque técnico: la API emite el mismo JWT del esquema `Backoffice` que ya valida, ahora con un
claim `sid` que apunta a una fila de `identidad.Sessions`, y lo entrega en una cookie HttpOnly. En
cada request, `OnTokenValidated` verifica la sesión (revocada, 30 min de inactividad, 8 h absolutas,
usuario activo) y reemplaza los roles del token por los actuales de la base. Así el logout y la
desactivación cortan el acceso en la siguiente request y los cambios de rol se aplican de inmediato
(FR-012 pide ≤ 5 min), sin refresh tokens. Bloqueo tras 5 fallos, enlaces de un solo uso
(invitación y recuperación) por correo SMTP (Mailpit en local), primer administrador creado por el
MigrationService, y bitácora de cada cambio e ingreso vía `IAuditLog` en la misma transacción.
En el frontend: login, recuperación, guarda de rutas, y pantallas de usuarios y áreas.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 5.x `strict` / Node 24 LTS (sin cambios).

**Primary Dependencies**: existentes (ASP.NET Core minimal APIs, EF Core 10, FluentValidation,
JwtBearer, Aspire 13.6). Nuevas: `BCrypt.Net-Next` (backend, exigido por la constitución);
`react-hook-form`, `zod`, `@hookform/resolvers` (backoffice, en el stack). Sin paquete para correo
(`System.Net.Mail`) ni para Mailpit (`AddContainer`). Limitador de tasa nativo de ASP.NET Core.

**Storage**: SQL Server, schema `identidad` (tablas `Areas`, `Users`, `UserRoles`, `Sessions`,
`PasswordTokens`; ver [data-model.md](data-model.md)). Bitácora en `auditoria` vía contrato.

**Testing**: xUnit v3 + Testcontainers.MsSql + `WebApplicationFactory`, `FakeTimeProvider` para
plazos; NetArchTest; Vitest + Testing Library. Sin Playwright (el login no es flujo crítico según
la constitución).

**Target Platform**: contenedores Linux; local con Aspire sobre Docker Engine en WSL.

**Project Type**: web-service (monolito modular) + SPA de backoffice.

**Performance Goals**: ingreso < 30 s de punta a punta (SC-001; el hash BCrypt factor 12 cuesta
~250 ms); validación de sesión = una lectura por PK por request.

**Constraints**: mensaje de error único en el login (SC-006); credenciales nunca en claro ni en
logs/bitácora (FR-003); cambios de acceso efectivos ≤ 5 min (FR-012); token no legible por JS.

**Scale/Scope**: decenas a pocos cientos de usuarios internos; 5 roles fijos; 16 endpoints; 5
pantallas de frontend.

No quedan NEEDS CLARIFICATION: todas las decisiones están en [research.md](research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Datos exigidos por la constitución**

| Pregunta | Respuesta |
|---|---|
| Módulo dueño | **Identidad** (usuarios, roles, áreas, sesiones, credenciales). |
| Grupo de API | **backoffice** únicamente: `/api/backoffice/auth/*`, `/users*`, `/areas*`. Portal sin cambios. |
| Contratos públicos tocados | `Identidad.Contracts`: + `AuthPolicies.UserDirectoryReader`. `Auditoria.Contracts`: + 7 valores de `AuditAction`. Ambos aditivos ([contracts/module-contracts.md](contracts/module-contracts.md)). |
| Trabajo en worker | No. El correo se envía inline (no es pesado y encolarlo guardaría el token en claro en Hangfire, research R7). |

**Gates por principio**

| Principio | Cómo se cumple | Pre | Post |
|---|---|---|---|
| I. Monolito modular / vertical slices | Todo en `Modules/Identidad/Features/<Feature>/` (endpoint + request + validator + handler juntos). Sin capas globales. | ✅ | ✅ |
| II. Dueño de sus datos | Tablas nuevas solo en `identidad`, migración propia. La bitácora se escribe por `IAuditLog`, nunca contra `auditoria` directo. | ✅ | ✅ |
| III. Solo contratos públicos | Lo nuevo es `internal`; cambios de contrato aditivos. `BootstrapAdministratorAsync` se expone en `IdentidadModule` (la clase de entrada ya justificada en la 001). | ✅ | ✅ |
| IV. Fronteras verificadas | Tests de arquitectura sin cambios de reglas; ESLint de boundaries: `users` y `areas` comparten `useSession` vía `shared/`, no entre sí. | ✅ | ✅ |
| V. Dos superficies | El JWT del backoffice conserva emisor/audiencia/clave propios; la cookie va con `Path=/api/backoffice`; test de token de portal → 401. Tres endpoints `AllowAnonymous` (login, forgot, reset) con límite de tasa y en lista cerrada del test de superficie, según la excepción de la constitución 1.0.1. | ✅ | ✅ |
| VI. Worker aparte | No aplica (sin procesamiento pesado). | ✅ | ✅ |
| VII. Frontend sin lógica de negocio | La UI oculta opciones según `/auth/me` solo por comodidad; toda regla (último admin, área inactiva, unicidad) se decide en la API. Zod solo para UX. | ✅ | ✅ |
| VIII. API autoridad de seguridad y auditoría | JWT + BCrypt + policies RBAC; FluentValidation en el borde; cada cambio de usuario/área y cada ingreso/rechazo en la bitácora, atómico con el cambio; sin secretos en logs. La segregación de funciones por caso queda para la feature de aprobaciones (spec). | ✅ | ✅ |
| Stack | Única dependencia nueva fuera de lo listado: ninguna (BCrypt está nombrado; RHF + Zod están en el stack). | ✅ | ✅ |
| Quality gates | Cada regla nueva (bloqueo, último admin, área inactiva, sesión, permisos) con test de backend; matriz SC-003 parametrizada. | ✅ | ✅ |

**Resultado**: PASS (pre y post diseño). Los endpoints anónimos de autenticación usan la excepción
explícita del Principio V (constitución 1.0.1, enmendada para esta feature).

## Project Structure

### Documentation (this feature)

```text
specs/002-backoffice-auth-rbac/
├── plan.md              # Este archivo
├── research.md          # Fase 0: decisiones técnicas (R1–R15)
├── data-model.md        # Fase 1: tablas de identidad, estados, eventos de bitácora
├── quickstart.md        # Fase 1: validación end-to-end
├── contracts/
│   ├── backoffice-identity-api.openapi.yaml   # 16 endpoints HTTP
│   └── module-contracts.md                    # cambios en Identidad.Contracts y Auditoria.Contracts
└── tasks.md             # Fase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── Aspire/Diligencia.AppHost/Program.cs        # + mailpit, backoffice-signing-key, BootstrapAdmin y Email para api/migrations
├── Hosts/
│   ├── Diligencia.Api/Program.cs               # + UseRateLimiter; appsettings: sección Identidad
│   └── Diligencia.MigrationService/            # llama IdentidadModule.BootstrapAdministratorAsync tras migrar
└── Modules/
    ├── Auditoria/Diligencia.Modules.Auditoria.Contracts/AuditContracts.cs   # + valores de AuditAction
    └── Identidad/
        ├── Diligencia.Modules.Identidad.Contracts/IdentityContracts.cs      # + UserDirectoryReader
        └── Diligencia.Modules.Identidad/
            ├── IdentidadModule.cs              # registra options, email, rate limiter; mapea endpoints
            ├── IdentidadOptions.cs
            ├── Authentication/
            │   ├── AuthenticationSetup.cs      # + OnMessageReceived (cookie) + OnTokenValidated (sesión y roles)
            │   ├── TokenIssuer.cs              # firma el JWT con la clave del esquema Backoffice
            │   └── PasswordHashing.cs          # BCrypt + hash fijo para tiempo constante
            ├── Authorization/
            │   ├── PolicySetup.cs              # + UserDirectoryReader
            │   └── AuditingAuthorizationResultHandler.cs   # registra AccessDenied
            ├── Email/SmtpEmailSender.cs        # IEmailSender interno
            ├── Persistence/                    # IdentidadDbContext + Area, User, UserRole, Session, PasswordToken + migración
            ├── Bootstrap/BootstrapAdministrator.cs
            └── Features/
                ├── Login/  Logout/  Me/
                ├── ForgotPassword/  ResetPassword/
                ├── ListUsers/  GetUser/  CreateUser/  UpdateUser/  SetUserStatus/
                └── ListAreas/  CreateArea/  RenameArea/  SetAreaStatus/

tests/
├── Diligencia.UnitTests/Identidad/             # validadores, hashing de tokens
└── Diligencia.IntegrationTests/
    ├── Identidad/                              # login, bloqueo, sesión, enlaces, usuarios, áreas, último admin, bitácora
    ├── Identidad/PermissionMatrixTests.cs      # SC-003: endpoint × rol
    ├── Api/EndpointSurfaceTests.cs             # lista cerrada de AllowAnonymous
    └── Support/TestTokens.cs → TestUsers.cs    # crea usuario + sesión y devuelve el token

apps/backoffice/src/
├── app/router.tsx                              # rutas + beforeLoad (guarda de sesión)
├── shared/api.ts  shared/session.ts            # cliente fetch + useSession()
└── features/
    ├── auth/      # LoginPage, ForgotPasswordPage, ResetPasswordPage (+ tests)
    ├── users/     # UsersPage (lista + filtros), UserDialog (alta/edición) (+ tests)
    └── areas/     # AreasPage (+ tests)
```

**Structure Decision**: se extiende la estructura existente de la 001 sin proyectos nuevos. Cada
operación es un slice en `Modules/Identidad/Features/`, siguiendo el patrón de
`Modules/Auditoria/Features/QueryEvents`. Las piezas que cruzan slices (emisión/validación del
token, hashing, correo) viven en carpetas técnicas del propio módulo, no en `BuildingBlocks`,
porque hoy solo Identidad las usa.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | Sin violaciones. Los endpoints anónimos de `/auth` quedaron cubiertos por la enmienda 1.0.1 del Principio V. | Alternativa descartada: un host o grupo aparte para la autenticación duplicaría configuración y sacaría rutas del modelo de dos grupos. |

## Riesgos y notas

- `TestTokens` actual firma tokens sin `sid`: los tests de bitácora y diagnóstico existentes se
  migran al helper que crea usuario + sesión (parte de esta feature).
- Sin limpieza de sesiones y tokens vencidos (shortcut anotado en el data model).
- El ingreso no cumple RNF-01 completo (sin MFA ni SSO), según los supuestos del spec.
- La protección CSRF depende de `SameSite=Strict` con SPA y API en el mismo sitio (research R2).
