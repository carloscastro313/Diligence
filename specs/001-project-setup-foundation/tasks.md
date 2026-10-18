---

description: "Tasks: Setup base del proyecto (estructura, base de datos, logs y auditoría)"
---

# Tasks: Setup base del proyecto (estructura, base de datos, logs y auditoría)

**Input**: `specs/001-project-setup-foundation/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: INCLUIDOS. La spec los exige (SC-003 a SC-007, FR-007, constitución: quality gates).

**Pendiente fuera de este archivo**: FR-011 de la spec debe exceptuar a `AuditEvent` (su `OccurredAtUtc` hace de fecha de creación; entidad inmutable). Se enmienda con `/speckit-specify` antes de implementar T055.

**Organization**: por historia de usuario. Prioridad: US1 (P1), US2 (P1), US4 (P1), US3 (P2); por eso la fase de US4 va antes que la de US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede ejecutar en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: US1–US4 según la spec
- Rutas relativas a la raíz del repo. `M` = nombre de módulo. Módulos: Terceros, Casos, Riesgo, Aprobaciones, Documentos, Auditoria, Identidad. Proyectos: `src/Modules/M/Diligencia.Modules.M.Contracts` (público) y `src/Modules/M/Diligencia.Modules.M` (interno).

---

## Phase 1: Setup

**Purpose**: raíz del repositorio y herramientas compartidas

- [X] T001 Crear `Diligencia.slnx` vacía, `global.json` (SDK 10.0, `rollForward: latestFeature`) y `.config/dotnet-tools.json` con `dotnet-ef` en la raíz
- [X] T002 [P] Crear `Directory.Build.props` (Nullable, ImplicitUsings, TreatWarningsAsErrors, `InternalsVisibleTo` para `Diligencia.UnitTests` e `Diligencia.IntegrationTests`), `Directory.Packages.props` (Central Package Management con las versiones de los paquetes del plan) y `.editorconfig`
- [X] T003 [P] Crear `.gitignore` (bin/obj, node_modules, dist, `.vs`, `*.user`, secretos) en la raíz
- [X] T004 [P] Crear `package.json` raíz (npm workspaces `apps/*`, `packages/*`; scripts `lint`, `typecheck`, `test`, `build`) y `tsconfig.base.json` (strict) en la raíz

---

## Phase 2: Foundational (bloquea todas las historias)

**Purpose**: BuildingBlocks, contratos de Identidad/Auditoría, esqueleto de los 7 módulos y proyectos de test

**⚠️ CRITICAL**: ninguna historia empieza antes de terminar esta fase

- [X] T005 Crear proyecto `src/BuildingBlocks/Diligencia.BuildingBlocks/Diligencia.BuildingBlocks.csproj` y agregarlo a la solución
- [X] T006 [P] Implementar `GetRequired(this IConfiguration, string key)` que lanza `InvalidOperationException("Falta la configuración obligatoria '<clave>'.")` sin mostrar valores, en `src/BuildingBlocks/Diligencia.BuildingBlocks/Configuration/ConfigurationExtensions.cs` (FR-006)
- [X] T007 [P] Implementar `CorrelationContext` (`AsyncLocal`, `Current` con fallback a `Activity.Current.TraceId` o GUID `N`, `Set`, validación `^[A-Za-z0-9._:-]{1,64}$`) en `src/BuildingBlocks/Diligencia.BuildingBlocks/Correlation/CorrelationContext.cs` (R11)
- [X] T008 [P] Implementar `ITimestamped` y `TimestampsInterceptor` (`SaveChangesInterceptor` con `TimeProvider`, fija `CreatedAtUtc`/`UpdatedAtUtc`) en `src/BuildingBlocks/Diligencia.BuildingBlocks/Persistence/ITimestamped.cs` y `TimestampsInterceptor.cs` (FR-011, R20)
- [X] T009 [P] Implementar `IModuleDatabase` (schema + `MigrateAsync`) y `AddModuleDbContext<TContext>(configuration, schema)` con `UseSqlServer(GetRequired("ConnectionStrings:diligencia"))`, `MigrationsHistoryTable("__EFMigrationsHistory", schema)`, `EnableRetryOnFailure(6, 30s)` y el interceptor de T008, en `src/BuildingBlocks/Diligencia.BuildingBlocks/Persistence/ModuleDbContextExtensions.cs` (FR-008, FR-010, R4)
- [X] T010 [P] Implementar `ExecuteInTransactionAsync(this DbContext, Func<DbTransaction, CancellationToken, Task>, CancellationToken)` (estrategia de ejecución → begin → trabajo → commit → `AcceptAllChanges`) en `src/BuildingBlocks/Diligencia.BuildingBlocks/Persistence/TransactionExtensions.cs` (R5)
- [X] T011 [P] Implementar `record ApiSurfaces(RouteGroupBuilder Backoffice, RouteGroupBuilder Portal)` en `src/BuildingBlocks/Diligencia.BuildingBlocks/Http/ApiSurfaces.cs`
- [X] T012 [P] Crear `src/Modules/Identidad/Diligencia.Modules.Identidad.Contracts/` con `IActorContext`, `ActorSurface`, `AuthSchemes`, `BackofficeRoles`, `AuthPolicies` exactamente como en [contracts/audit-module-contract.md](contracts/audit-module-contract.md)
- [X] T013 [P] Crear `src/Modules/Auditoria/Diligencia.Modules.Auditoria.Contracts/` con `IAuditLog`, `AuditEntry`, `AuditChange`, `AuditAction` exactamente como en [contracts/audit-module-contract.md](contracts/audit-module-contract.md) (sin dependencia de EF Core)
- [X] T014 [P] Crear proyecto `Contracts` vacío (solo el csproj) para Terceros, Casos, Riesgo, Aprobaciones y Documentos en `src/Modules/M/Diligencia.Modules.M.Contracts/`
- [X] T015 [P] Crear proyecto interno `src/Modules/Terceros/Diligencia.Modules.Terceros/` con la clase pública estática `TercerosModule` (`AddTercerosModule`, `MapTercerosEndpoints` no-op; ver contrato) referenciando su Contracts y BuildingBlocks
- [X] T016 [P] Ídem T015 para `src/Modules/Casos/Diligencia.Modules.Casos/` (`CasosModule`)
- [X] T017 [P] Ídem T015 para `src/Modules/Riesgo/Diligencia.Modules.Riesgo/` (`RiesgoModule`)
- [X] T018 [P] Ídem T015 para `src/Modules/Aprobaciones/Diligencia.Modules.Aprobaciones/` (`AprobacionesModule`)
- [X] T019 [P] Ídem T015 para `src/Modules/Documentos/Diligencia.Modules.Documentos/` (`DocumentosModule`)
- [X] T020 [P] Crear proyecto interno `src/Modules/Auditoria/Diligencia.Modules.Auditoria/` con `AuditoriaModule` (`AddAuditoriaModule`, `MapAuditoriaEndpoints` no-op por ahora), referenciando `Auditoria.Contracts`, `Identidad.Contracts` y BuildingBlocks
- [X] T021 [P] Crear proyecto interno `src/Modules/Identidad/Diligencia.Modules.Identidad/` con `IdentidadModule` (`AddIdentidadModule`, `MapIdentidadEndpoints` no-op por ahora), referenciando `Identidad.Contracts` y BuildingBlocks
- [X] T022 Agregar los 14 proyectos de módulos y BuildingBlocks a `Diligencia.slnx`
- [X] T023 [P] Crear `tests/Diligencia.UnitTests/Diligencia.UnitTests.csproj` (xUnit v3, `Microsoft.Extensions.Diagnostics.Testing`) y agregarlo a la solución
- [X] T024 [P] Crear `tests/Diligencia.ArchitectureTests/Diligencia.ArchitectureTests.csproj` (xUnit v3, NetArchTest.Rules, referencia a todos los proyectos de módulo) y agregarlo a la solución
- [X] T025 [P] Crear `tests/Diligencia.IntegrationTests/Diligencia.IntegrationTests.csproj` (xUnit v3, Testcontainers.MsSql, `Microsoft.AspNetCore.Mvc.Testing`, `Aspire.Hosting.Testing`, FakeLogger) y agregarlo a la solución
- [X] T026 [P] Test unitario de `GetRequired` (falta clave → mensaje con el nombre y sin el valor) en `tests/Diligencia.UnitTests/Configuration/ConfigurationExtensionsTests.cs`
- [X] T027 [P] Test unitario de `TimestampsInterceptor` (Added fija ambas fechas UTC, Modified solo `UpdatedAtUtc`) con `TimeProvider` falso en `tests/Diligencia.UnitTests/Persistence/TimestampsInterceptorTests.cs`
- [X] T028 [P] Test unitario de `CorrelationContext` (validación del formato, fallback generado) en `tests/Diligencia.UnitTests/Correlation/CorrelationContextTests.cs`

**Checkpoint**: `dotnet build Diligencia.slnx` y `dotnet test tests/Diligencia.UnitTests` en verde

---

## Phase 3: User Story 1 — Levantar la solución completa desde cero (P1) 🎯 MVP

**Goal**: un comando levanta API, worker, SQL Server y las dos SPAs; todos sanos; fronteras verificadas.

**Independent Test**: [quickstart.md](quickstart.md) pasos 1, 4 y 7 (arranque, tokens/superficies, build+tests+lint).

> El chequeo de salud de la **base de datos** (US1-2) lo completa US2 (T070); en esta fase `/health` informa solo `self`.

### Tests de US1

- [X] T029 [P] [US1] Escribir las reglas de arquitectura 1, 2, 3 y 5 de R16 como funciones reutilizables sobre un conjunto de ensamblados en `tests/Diligencia.ArchitectureTests/ArchitectureRules.cs`
- [X] T030 [P] [US1] Crear el ensamblado de violaciones deliberadas (una por regla 1, 2, 3 y 5) en `tests/Fixtures/Diligencia.ArchitectureTests.Violations/Diligencia.ArchitectureTests.Violations.csproj` y sus fuentes; agregarlo a la solución
- [X] T031 [US1] Tests que aplican las reglas a los módulos reales (deben pasar) y al ensamblado de violaciones (deben detectar **todas**, SC-003) en `tests/Diligencia.ArchitectureTests/BoundaryTests.cs`
- [X] T032 [P] [US1] Test de integración que recorre `EndpointDataSource` y exige que todo endpoint salvo `/health` y `/alive` esté bajo `/api/backoffice` o `/api/portal` y tenga metadatos de autorización, en `tests/Diligencia.IntegrationTests/Api/EndpointSurfaceTests.cs`
- [X] T033 [P] [US1] Helper `TestTokens.Create(ActorSurface, sub, roles)` que firma con claves de test por superficie, en `tests/Diligencia.IntegrationTests/Support/TestTokens.cs`
- [X] T034 [US1] Tests: token de portal → 401 en backoffice y viceversa; sin token → 401; rol insuficiente → 403 (usa una ruta de prueba registrada por la factory) en `tests/Diligencia.IntegrationTests/Api/AuthenticationSurfaceTests.cs` (FR-004)
- [X] T035 [P] [US1] Test del frontend: ejecuta ESLint por API de Node sobre `eslint-fixtures/` y exige un error por cada violación sembrada (feature→feature, portal→backoffice, packages→apps) en `eslint-fixtures/boundaries.test.ts` con Vitest configurado en la raíz (`vitest.config.ts`), para no acoplar `packages/ui` a ESLint (SC-003)

### Implementación de US1

- [X] T036 [US1] Crear `src/Aspire/Diligencia.ServiceDefaults/` (OpenTelemetry con logs/scopes por OTLP, resiliencia HTTP, service discovery, `MapDefaultEndpoints` con `/alive` (tag `live`) y `/health` en JSON `{status, checks}` sin descripciones ni excepciones, activo en todos los entornos) (R14)
- [X] T037 [US1] Implementar en Identidad, como método aparte `AddIdentidadAuthentication` (lo llama solo la API; `AddIdentidadModule` no exige configuración JWT para que worker y MigrationService arranquen), los esquemas JwtBearer `Backoffice` y `Portal` desde `Authentication:Schemes:<Esquema>` con `GetRequired` para `ValidIssuer`/`ValidAudiences`/`SigningKeys`, `MapInboundClaims=false`, `NameClaimType=sub`, `RoleClaimType=role`, en `src/Modules/Identidad/Diligencia.Modules.Identidad/Authentication/AuthenticationSetup.cs` (R10)
- [X] T038 [US1] Implementar las policies `Backoffice`, `Portal`, `AuditReader` (rol Auditor o Administrador) y `Administrator` y `FallbackPolicy` autenticada en `src/Modules/Identidad/Diligencia.Modules.Identidad/Authorization/PolicySetup.cs`
- [X] T039 [US1] Implementar `HttpActorContext` (claim `sub` + esquema; fuera de request → `system:<ApplicationName>`/`System`) y registrarlo en `IdentidadModule.AddIdentidadModule` en `src/Modules/Identidad/Diligencia.Modules.Identidad/HttpActorContext.cs` (R9)
- [X] T040 [US1] Crear `src/Hosts/Diligencia.Api/` (`Program.cs`): ServiceDefaults, registro de los 7 módulos, llamada a `AddIdentidadAuthentication`, grupos `/api/backoffice` y `/api/portal` con su policy, `UseAuthentication/UseAuthorization`; `appsettings.Development.json` trae emisor y audiencia por esquema (no secretos) y solo `SigningKeys` viene de `dotnet user-jwts`, `MapDefaultEndpoints`, llamada a cada `Map…Endpoints(ApiSurfaces)`; `appsettings.json` y `appsettings.Development.json` sin secretos; `public partial class Program`
- [X] T041 [US1] Crear `src/Hosts/Diligencia.Worker/` (`Microsoft.NET.Sdk.Web` solo para `/health` y `/alive`): `Program.cs` con ServiceDefaults y registro de los 7 módulos con `Add…Module`, sin Hangfire todavía
- [X] T042 [US1] Crear `src/Aspire/Diligencia.AppHost/` con `api`, `worker` (`WithHttpHealthCheck("/health")`), `backoffice` y `portal` (`AddViteApp`, `WithReference(api)`), parámetros secretos de Aspire; las SPAs se enlazan en T046–T047 (R2)
- [X] T043 [P] [US1] Crear `packages/ui/` (`@diligencia/ui`: tema MUI exportado, `package.json`, `tsconfig.json`)
- [X] T044 [P] [US1] Crear `eslint.config.js` raíz (flat config, TypeScript, `eslint-plugin-boundaries` con elementos `feature`, `shared`, `app-shell`, `package` y las reglas de [contracts/cross-cutting.md](contracts/cross-cutting.md) §7)
- [X] T045 [P] [US1] Crear `eslint-fixtures/` con un archivo por violación sembrada (feature→feature, portal→backoffice, packages→apps) fuera de los workspaces lintados normalmente
- [X] T046 [US1] Crear `apps/backoffice/` (Vite + React + TS strict + MUI + TanStack Query/Router; `src/app/`, `src/features/home/` con página inicial, `src/shared/`; proxy `/api` hacia la API inyectada por Aspire; `vitest` con un test de render de la página inicial)
- [X] T047 [US1] Crear `apps/portal/` con la misma estructura que T046, importando solo de `@diligencia/ui` (nunca de `apps/backoffice`)
- [X] T048 [P] [US1] `src/Hosts/Diligencia.Api/Dockerfile` multi-stage (SDK → aspnet, usuario no root, contexto raíz)
- [X] T049 [P] [US1] `src/Hosts/Diligencia.Worker/Dockerfile` multi-stage (ídem)
- [X] T050 [P] [US1] `apps/backoffice/Dockerfile` (node build → nginx sin privilegios) y `apps/portal/Dockerfile` (ídem)
- [X] T051 [US1] Crear `.github/workflows/ci.yml` con jobs backend (`dotnet build`, `dotnet test`), frontend (`npm ci`, lint, typecheck, test, build), performance (`--filter Category=Performance`) e imágenes (`docker build` de las cuatro), según R19
- [X] T052 [US1] Crear `README.md` raíz con prerequisitos (WSL2, Docker Engine, .NET 10, Node 24), el comando único `dotnet run --project src/Aspire/Diligencia.AppHost` el paso previo único `dotnet user-jwts create` para los esquemas `Backoffice` y `Portal` (genera las `SigningKeys` en los user-secrets de la API; sin él la API no arranca) y el enlace a [quickstart.md](quickstart.md); actualizar también el paso 1 del quickstart con ese prerrequisito y la sección de "otros entornos" (migraciones como paso explícito sin `-admin`, FR-009)
- [X] T053 [US1] Smoke test con `Aspire.Hosting.Testing`: arranca el AppHost y espera que `api`, `worker`, `backoffice` y `portal` queden `Healthy` (SC-002) en `tests/Diligencia.IntegrationTests/Smoke/AppHostSmokeTests.cs`

**Checkpoint US1**: `dotnet run` del AppHost deja todo en Running/Healthy; build, tests de arquitectura, ESLint y Vitest en verde

---

## Phase 4: User Story 2 — Persistencia separada por módulo (P1)

**Goal**: un `DbContext` y un schema por módulo, migraciones automáticas en local con dos identidades de base de datos.

**Independent Test**: [quickstart.md](quickstart.md) pasos 2, 3 y 8.

**Depende de**: Phase 3 (hosts y AppHost) y Phase 2.

### Tests de US2

- [X] T054 [US2] Fixture de assembly con `MsSqlContainer` que ejecuta bootstrap + migraciones + grants (mismo código que el MigrationService, T068) y expone las cadenas migrador/runtime, en `tests/Diligencia.IntegrationTests/Support/SqlServerFixture.cs` (R18)
- [X] T055 [P] [US2] Regla de arquitectura 4 (un `DbContext` solo se usa dentro de su módulo) y 6 (toda entidad mapeada implementa `ITimestamped`, salvo `AuditEvent`), y su violación en el ensamblado de fixtures, en `tests/Diligencia.ArchitectureTests/ArchitectureRules.cs`, `tests/Fixtures/Diligencia.ArchitectureTests.Violations/` y `BoundaryTests.cs`
- [X] T056 [P] [US2] Test: tras migrar existen los 8 schemas y 7 `__EFMigrationsHistory` (uno por módulo); una migración nueva de un módulo no altera otros schemas, en `tests/Diligencia.IntegrationTests/Persistence/SchemaTests.cs`
- [X] T057 [P] [US2] Test: con el login de runtime, `CREATE TABLE`, `DROP`, `TRUNCATE` y `UPDATE/DELETE` sobre cualquier `__EFMigrationsHistory` fallan (error 229) y `INSERT/SELECT` en un schema de módulo funcionan, en `tests/Diligencia.IntegrationTests/Persistence/RuntimePermissionsTests.cs` (FR-019)
- [X] T058 [US2] Test de salud con base caída: detener el contenedor → `/health` 503 con `database: Unhealthy` y `/alive` 200, sin credenciales en el cuerpo ni en los logs; al reiniciar vuelve a 200; repetir para el worker una vez disponible Hangfire (T091), en `tests/Diligencia.IntegrationTests/Api/HealthTests.cs`
- [X] T100 [P] [US2] Test: el MigrationService sin `ConnectionStrings:diligencia-admin` (logins ya creados por un DBA) migra los 7 módulos y aplica los permisos con el login migrador, en `tests/Diligencia.IntegrationTests/Persistence/MigrationWithoutBootstrapTests.cs` (FR-009, otros entornos)
- [X] T059 [P] [US2] Test: la API y el worker no arrancan sin `ConnectionStrings:diligencia` y el mensaje nombra la clave sin valor, en `tests/Diligencia.IntegrationTests/Api/MissingConfigurationTests.cs` (FR-006)

### Implementación de US2

- [X] T060 [P] [US2] `DbContext` interno vacío con `HasDefaultSchema` y registro con `AddModuleDbContext` en `TercerosModule`: `src/Modules/Terceros/Diligencia.Modules.Terceros/Persistence/TercerosDbContext.cs` (schema `terceros`)
- [X] T061 [P] [US2] Ídem para Casos: `src/Modules/Casos/Diligencia.Modules.Casos/Persistence/CasosDbContext.cs` (schema `casos`)
- [X] T062 [P] [US2] Ídem para Riesgo: `src/Modules/Riesgo/Diligencia.Modules.Riesgo/Persistence/RiesgoDbContext.cs` (schema `riesgo`)
- [X] T063 [P] [US2] Ídem para Aprobaciones: `src/Modules/Aprobaciones/Diligencia.Modules.Aprobaciones/Persistence/AprobacionesDbContext.cs` (schema `aprobaciones`)
- [X] T064 [P] [US2] Ídem para Documentos: `src/Modules/Documentos/Diligencia.Modules.Documentos/Persistence/DocumentosDbContext.cs` (schema `documentos`)
- [X] T065 [P] [US2] Ídem para Identidad: `src/Modules/Identidad/Diligencia.Modules.Identidad/Persistence/IdentidadDbContext.cs` (schema `identidad`)
- [X] T066 [P] [US2] Ídem para Auditoría (modelo vacío por ahora; la entidad llega en T074): `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Persistence/AuditDbContext.cs` (schema `auditoria`)
- [X] T067 [US2] Generar la migración `Initial` de cada uno de los 7 módulos con `dotnet ef migrations add Initial` en `…/Persistence/Migrations/` de cada proyecto (crea el schema y su historial); cambiar las clases de migración y snapshot generadas a `internal` (Principio III, regla de arquitectura 3); agregar fábrica de diseño `IDesignTimeDbContextFactory` si hace falta
- [X] T068 [US2] Crear `src/Hosts/Diligencia.MigrationService/` con: (1) bootstrap idempotente (solo si existe `ConnectionStrings:diligencia-admin`): crea base, logins `diligencia_migrator` (db_owner) y `diligencia_app` y el rol `diligencia_runtime`; (2) con el login migrador, `MigrateAsync` de cada `IModuleDatabase` en secuencia e instalación del schema `hangfire` con `SqlServerObjectsInstaller` (antes de los permisos); (3) `GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<schema>` al rol para cada módulo y `hangfire`, y `DENY UPDATE, DELETE` sobre cada `<schema>.__EFMigrationsHistory`; termina con código 0 (R3)
- [X] T069 [US2] Actualizar el AppHost: `AddSqlServer` con volumen persistente, base `diligencia`, parámetros secretos para `sa`, migrador y runtime, proyecto `migrations`, y `WaitForCompletion(migrations)` en `api` y `worker` con las cadenas de su identidad (runtime) en `src/Aspire/Diligencia.AppHost/Program.cs` (R2, R3)
- [X] T070 [US2] Registrar el chequeo `database` (`AddDbContextCheck<AuditDbContext>("database")`) en la API y el worker y actualizar `/health` para informarlo por separado; reintentos acotados (6 reintentos, espera máxima 30 s, ya en T009) (FR-005, FR-010, R14)

**Checkpoint US2**: base creada y migrada sola al arrancar; `/health` informa `database`; tests de permisos y arquitectura en verde

---

## Phase 5: User Story 4 — Bitácora de auditoría inalterable (P1)

**Goal**: `IAuditLog` atómico con la transacción del llamador, tabla de solo agregado y consulta para auditor/administrador.

**Independent Test**: [quickstart.md](quickstart.md) pasos 3 y 5, más los tests de integración de esta fase.

**Depende de**: Phase 4 (DbContext, migraciones, identidades de base).

### Tests de US4

- [X] T071 [P] [US4] Test unitario de `AuditValueMasker`/construcción de JSON: descarta cambios iguales, enmascara `IsSensitive` y nombres con password/secret/token, solo campos cambiados, en `tests/Diligencia.UnitTests/Auditoria/AuditValueMaskerTests.cs` (FR-020)
- [X] T072 [P] [US4] Módulo de prueba para integración: `TestModuleDbContext` (entidad ficticia con `ITimestamped`, schema propio creado en el fixture) en `tests/Diligencia.IntegrationTests/Support/TestModule.cs`
- [X] T073 [US4] Tests de integración de `RecordAsync`: evento con todos los campos (US4-1); rollback de la acción → sin evento; fallo al registrar → la acción no se confirma (US4-2); actor `system:` en contexto sin usuario, en `tests/Diligencia.IntegrationTests/Auditoria/RecordEventTests.cs` (FR-018)
- [X] T074 [P] [US4] Tests de inmutabilidad: con el login de runtime, `UPDATE`/`DELETE`/`TRUNCATE` sobre `auditoria.AuditEvents` fallan; el `SaveChanges` con evento `Modified`/`Deleted` lanza, en `tests/Diligencia.IntegrationTests/Auditoria/ImmutabilityTests.cs` (SC-006)
- [X] T075 [US4] Tests de la consulta: filtros por entidad/actor/rango, orden cronológico, paginación (`hasNextPage`), 400 de validación, 403 para rol no autorizado, 401 para token de portal (otro esquema) y sin token, y que cada consulta genera un evento `SensitiveDataViewed` (US4-4, -5, FR-021, FR-022), en `tests/Diligencia.IntegrationTests/Auditoria/QueryEventsTests.cs`
- [X] T076 [P] [US4] Test de rendimiento `Category=Performance`: cargar 1.000.000 de eventos con `SqlBulkCopy` en una base propia (no la compartida con T075) y verificar que la consulta por entidad responde en < 2 s (SC-007), en `tests/Diligencia.IntegrationTests/Auditoria/QueryPerformanceTests.cs`

### Implementación de US4

- [X] T077 [US4] Entidad `AuditEvent` y su configuración EF (columnas y tipos de [data-model.md](data-model.md) §2, `OccurredAtUtc` con `HasDefaultValueSql("SYSUTCDATETIME()")`, 3 índices) en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Persistence/AuditEvent.cs` y `AuditEventConfiguration.cs`
- [X] T078 [US4] Guarda en `AuditDbContext.SaveChanges*`: lanzar `InvalidOperationException` si hay un `AuditEvent` en estado `Modified` o `Deleted`, en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Persistence/AuditDbContext.cs` (R6)
- [X] T079 [US4] Migración `AddAuditEvents` (tabla e índices) que además ejecuta `DENY UPDATE, DELETE ON auditoria.AuditEvents TO diligencia_runtime` con `migrationBuilder.Sql` en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Persistence/Migrations/` (clases `internal`, como en T067)
- [X] T080 [US4] Implementar `AuditValueMasker` (descartar iguales, enmascarar `"***"`, serializar `OldValues`/`NewValues` a JSON; truncar cada valor a 4.000 caracteres con sufijo `…[truncado]`) en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Features/RecordEvent/AuditValueMasker.cs` (R8)
- [X] T081 [US4] Implementar `AuditLog : IAuditLog` (`RecordAsync` con `UseSqlServer(transaction.Connection)` + `Database.UseTransaction`, sin reintentos propios; `RecordStandaloneAsync`; validación de entrada; actor de `IActorContext`, correlación de `CorrelationContext`) en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Features/RecordEvent/AuditLog.cs` y registrarlo en `AuditoriaModule` (R5, R9)
- [X] T082 [US4] Feature `QueryEvents`: `QueryEventsRequest`, `QueryEventsValidator` (FluentValidation: `entityId` requiere `entityType`, `toUtc` > `fromUtc`, `page ≥ 1`, `pageSize 1–100`), `QueryEventsHandler` (índices de R7, `pageSize + 1`, `AsNoTracking`, sin `COUNT`) y respuesta paginada, en `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Features/QueryEvents/`
- [X] T083 [US4] Endpoint `GET /api/backoffice/audit/events` con policy `AuditReader`, validación en el borde y registro de `SensitiveDataViewed` (`EntityType="AuditLog"`, `EntityId="query"`, filtros en `NewValues`) con `RecordStandaloneAsync` antes de devolver datos; mapearlo en `AuditoriaModule.MapAuditoriaEndpoints` (FR-021, FR-022)

**Checkpoint US4**: tests de la fase en verde; `curl` del quickstart paso 5 funciona

---

## Phase 6: User Story 3 — Registros técnicos para operar y diagnosticar (P2)

**Goal**: logs JSON estructurados con correlación de punta a punta (API → Hangfire → worker), sin datos sensibles, configurables por módulo.

**Independent Test**: [quickstart.md](quickstart.md) paso 6.

**Depende de**: Phase 3 y Phase 4 (Hangfire usa la base y su schema lo instala el MigrationService).

### Tests de US3

- [X] T084 [P] [US3] Test de `CorrelationMiddleware`: reutiliza un header válido, genera uno si falta o es inválido (200 caracteres, caracteres no permitidos), lo devuelve en la respuesta, en `tests/Diligencia.IntegrationTests/Logging/CorrelationMiddlewareTests.cs`
- [X] T085 [US3] Test de punta a punta: `POST /api/backoffice/diagnostics/jobs` con `X-Correlation-Id` propio; los logs (FakeLogger) de API y worker llevan ese id en sus scopes, en `tests/Diligencia.IntegrationTests/Logging/CorrelationEndToEndTests.cs` (US3-3, SC-004)
- [X] T086 [P] [US3] Test: un 500 forzado devuelve ProblemDetails genérico con `correlationId`, sin traza, y el log registra la excepción (US3-5, FR-016), en `tests/Diligencia.IntegrationTests/Logging/UnhandledErrorTests.cs`
- [X] T087 [US3] Test de secretos (SC-005): envía solicitudes con contraseña en query, token en `Authorization` y cuerpo multipart con archivo (a una ruta de prueba protegida registrada por la factory, pues la feature no tiene endpoints de carga); ningún log capturado ni evento de bitácora contiene esos valores ni la cadena de conexión, en `tests/Diligencia.IntegrationTests/Logging/NoSecretsInLogsTests.cs`
- [X] T088 [P] [US3] Test: el log de solicitud incluye método, ruta, status y duración; cada línea JSON trae nivel, timestamp UTC, `Category` (módulo), `Service` y `CorrelationId` (FR-012); y cambiar `Logging:LogLevel:Diligencia.Modules.Auditoria` cambia el volumen sin tocar código (US3-1, -6), en `tests/Diligencia.IntegrationTests/Logging/RequestLoggingTests.cs`

### Implementación de US3

- [X] T089 [US3] `CorrelationMiddleware` (toma o genera el id, lo agrega a la respuesta, abre scope `{ CorrelationId, Service }`) en `src/BuildingBlocks/Diligencia.BuildingBlocks/Correlation/CorrelationMiddleware.cs` y usarlo en `src/Hosts/Diligencia.Api/Program.cs` como PRIMER middleware, antes de `UseExceptionHandler` y `UseHttpLogging`, para que hasta los logs de errores lleven el id (FR-013, SC-004)
- [X] T090 [US3] `CorrelationJobFilter` (`IClientFilter`/`IServerFilter`: guarda el parámetro `CorrelationId` al encolar, lo restaura y abre el scope al ejecutar) en `src/BuildingBlocks/Diligencia.BuildingBlocks/Correlation/CorrelationJobFilter.cs` (R11)
- [X] T091 [US3] Hangfire en la API como cliente (`AddHangfire` con SQL Server en schema `hangfire`, `PrepareSchemaIfNecessary=false`, filtro de T090) en `src/Hosts/Diligencia.Api/Program.cs`, y en el worker como servidor (`AddHangfireServer`) en `src/Hosts/Diligencia.Worker/Program.cs` (el schema `hangfire` ya lo instala T068) (R3)
- [X] T092 [P] [US3] `IDiagnosticJob` en `src/BuildingBlocks/Diligencia.BuildingBlocks/Diagnostics/IDiagnosticJob.cs` e implementación que registra "Diagnostic job executed" en `src/Hosts/Diligencia.Worker/DiagnosticJob.cs` (idempotente) (R21)
- [X] T093 [US3] Endpoint `POST /api/backoffice/diagnostics/jobs` (policy `Administrator`, 202 con `jobId`, solo si `Diagnostics:Enabled=true`) en `src/Hosts/Diligencia.Api/Diagnostics/DiagnosticsEndpoints.cs`; habilitarlo en `appsettings.Development.json`
- [X] T094 [P] [US3] Configuración de logs: formateador JSON con scopes y UTC (`Logging:Console:*`) y niveles por módulo en `appsettings.json` de API y worker; `AddHttpLogging` limitado a método, ruta, status y duración con `CombineLogs=true`; sin `EnableSensitiveDataLogging`; EF `Database.Command` en `Warning` (R12)
- [X] T095 [US3] `AddProblemDetails` con extensión `correlationId` + `UseExceptionHandler` + `UseStatusCodePages` en `src/Hosts/Diligencia.Api/Program.cs` (R13, FR-016)

**Checkpoint US3**: quickstart paso 6 verificado; tests de logging en verde

---

## Phase 7: Polish y verificación final

- [X] T096 [P] Ejecutar el [quickstart.md](quickstart.md) completo (pasos 1–9) en una máquina/WSL limpia y corregir las diferencias en los documentos o el código
- [X] T097 [P] Verificar manualmente las fronteras (quickstart paso 7): una violación en `apps/portal` rompe `npm run lint` y una referencia entre módulos rompe `dotnet test` de arquitectura
- [X] T098 [P] Medir el tiempo de arranque desde el clon (SC-001 < 15 min sin descarga de imágenes) y anotarlo en `README.md`
- [X] T099 Revisar que ningún secreto, contraseña ni cadena de conexión esté versionado (`git grep`) y que el pipeline `.github/workflows/ci.yml` pase completo, incluidas las cuatro imágenes Docker y el job de rendimiento

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2 → historias**. Phase 2 bloquea todo.
- **US1** (Phase 3) depende solo de Phase 2.
- **US2** (Phase 4) depende de US1 (hosts y AppHost).
- **US4** (Phase 5) depende de US2 (DbContext de Auditoría, migraciones, identidades de base).
- **US3** (Phase 6) depende de US1 y US2 (Hangfire usa la base); no depende de US4.
- US3 y US4 pueden desarrollarse en paralelo una vez terminada US2. El único cruce es que `AuditLog` (T081) lee `CorrelationContext` de Phase 2; sin el middleware de T089 usa el fallback generado, por lo que los tests de US4 no dependen de US3.
- Dentro de cada fase: tests → modelo → servicios → endpoints. Las tareas de test pueden fallar hasta que exista la implementación correspondiente.

### Orden de dependencia dentro de fases

- T005 antes de T006–T011; T022 después de T012–T021; T029/T030 antes de T031.
- T036 antes de T040–T042; T037–T039 antes de T040; T046–T047 antes de enlazarlas en el AppHost (T042) y de T050.
- T060–T066 antes de T067; T067 antes de T068–T070; T054 usa el mismo código de bootstrap de T068 (extraerlo a una librería interna o reusar el proyecto).
- T077 antes de T079; T080 antes de T081; T081–T082 antes de T083.
- T089, T090 antes de T091; T091–T092 antes de T093.

## Parallel Examples

```text
# Phase 2 (tras T005): T006–T011 en paralelo (archivos distintos de BuildingBlocks)
# Phase 2: T015–T021 (siete proyectos de módulo) en paralelo
# US1: T043 (packages/ui), T044 (eslint), T045 (fixtures), T048–T050 (Dockerfiles) en paralelo
# US2: T060–T066 (siete DbContext) en paralelo; T056, T057, T059 en paralelo
# US3 y US4 completas en paralelo entre sí, con dos desarrolladores
```

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + US1** (Phase 3): solución que compila, arranca con un comando, dos superficies autenticadas, fronteras verificadas y las dos SPAs. Parar y validar con el quickstart pasos 1, 4 y 7.
- **Incremento 2**: US2 (base y permisos). **Incremento 3**: US4 (valor central: bitácora). **Incremento 4**: US3 (logs y correlación). Cada incremento se valida con su Independent Test y deja CI en verde.
- Los tests de arquitectura y ESLint se crean en la primera historia en que aplican y se amplían después: nunca se desactivan para destrabar un cambio (constitución, Principio IV).
