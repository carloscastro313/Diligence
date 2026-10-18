# Research: Setup base del proyecto

Fase 0 de [plan.md](plan.md). Cada entrada: **Decisión**, **Rationale**, **Alternativas**.

## R1. Formato de solución y gestión de paquetes

- **Decisión**: `Diligencia.slnx` (formato por defecto del SDK .NET 10), `global.json` con el SDK
  10.0 fijado (`rollForward: latestFeature`), `Directory.Packages.props` (Central Package
  Management) y `Directory.Build.props` con `Nullable=enable`, `ImplicitUsings=enable`,
  `TreatWarningsAsErrors=true` e `<InternalsVisibleTo Include="Diligencia.UnitTests" />` /
  `Diligencia.IntegrationTests` para los proyectos de `src/`.
- **Rationale**: un solo lugar para versiones y reglas de compilación; `InternalsVisibleTo` central
  permite testear código `internal` sin abrir los módulos entre sí.
- **Alternativas**: `.sln` clásico (más ruido en merges); versiones por proyecto (deriva entre
  módulos).

## R2. Orquestación local: Aspire sobre Docker Engine en WSL

- **Decisión**: `Diligencia.AppHost` (Aspire 13.x) declara: `sql` (`AddSqlServer` con
  `WithDataVolume()` y `ContainerLifetime.Persistent`), base `diligencia`, `migrations`
  (MigrationService), `api`, `worker` (ambos `WaitForCompletion(migrations)`), y `backoffice` /
  `portal` con `AddViteApp` (`Aspire.Hosting.JavaScript`, instalación npm habilitada) y
  `WithReference(api)` para que el proxy de Vite resuelva la API por service discovery. Comando
  único: `dotnet run --project src/Aspire/Diligencia.AppHost`.
  Entorno recomendado: clonar y trabajar **dentro de WSL2** (SDK .NET, Node y Docker Engine
  instalados en la distro; VS Code Remote-WSL). Alternativa soportada: Windows con el CLI `docker`
  apuntando al daemon de WSL vía `DOCKER_HOST`/contexto.
- **Rationale**: decisión de la spec (Clarifications); Aspire solo necesita un CLI `docker`
  compatible, no Docker Desktop. Trabajar dentro de WSL evita el puente entre Windows y el daemon y
  da I/O de disco rápido para `npm` y `dotnet`.
- **Alternativas**: Docker Compose como comando local (descartado en Clarifications); Podman
  (`ASPIRE_CONTAINER_RUNTIME=podman`) queda posible sin cambios.

## R3. Dos identidades de base de datos y servicio de migraciones

- **Decisión**: proyecto `Diligencia.MigrationService` (patrón "migration worker" de Aspire) que, en
  orden:
  1. **Bootstrap** (solo si existe `ConnectionStrings:diligencia-admin`, es decir local/CI): con la
     conexión administrativa crea la base si falta, los logins/usuarios `diligencia_migrator`
     (miembro de `db_owner`) y `diligencia_app` (miembro del rol `diligencia_runtime`). Las
     contraseñas salen de las cadenas `diligencia` y `diligencia-runtime` que recibe (no hay claves
     extra). Idempotente.
  2. Con el login **migrador** (`ConnectionStrings:diligencia`): asegura el rol
     `diligencia_runtime`, aplica `MigrateAsync()` de cada módulo (resueltos como
     `IEnumerable<IModuleDatabase>`, en secuencia), instala el schema de Hangfire
     (`SqlServerObjectsInstaller.Install`, schema `hangfire`) y concede
     `SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<schema>` al rol para cada schema, más
     `DENY UPDATE, DELETE` sobre cada `<schema>.__EFMigrationsHistory`.
  3. Termina con código 0. API y worker arrancan después (`WaitForCompletion`).
  En otros entornos se ejecuta el mismo proyecto como paso explícito sin la conexión `-admin`
  (logins provistos por el DBA).
  El `DENY UPDATE, DELETE ON auditoria.AuditEvents TO diligencia_runtime` vive en la migración del
  módulo Auditoría: la inmutabilidad queda versionada junto a la tabla.
- **Rationale**: cumple FR-009 y FR-019 (las migraciones nunca usan el login de runtime). El
  `DENY` explícito a nivel de objeto prevalece sobre el `GRANT` del schema, y el login de runtime no
  es dueño de ningún schema, así que no puede revocarlo. `TRUNCATE`/`ALTER`/`DROP` requieren permisos
  que runtime no tiene.
- **Alternativas**: `Database.Migrate()` al arrancar la API (requiere DDL en runtime); EF migration
  bundles por módulo (7 artefactos y sin lugar natural para bootstrap y grants); `GRANT ... ON
  DATABASE` (daría acceso al historial de migraciones y a schemas futuros sin revisión).

## R4. Convenciones de DbContext por módulo

- **Decisión**: helper en BuildingBlocks
  `services.AddModuleDbContext<TContext>(configuration, schema)` que:
  `UseSqlServer(GetRequired("ConnectionStrings:diligencia"), sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema).EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: 30s))`,
  agrega `TimestampsInterceptor` y registra `IModuleDatabase` (schema + `MigrateAsync`). Cada
  `DbContext` llama `HasDefaultSchema(schema)`. Los siete módulos tienen una migración `Initial`
  (vacía salvo Auditoría); aplicarla crea el schema y su historial.
  Cada proceso recibe en `ConnectionStrings:diligencia` la cadena de **su propia identidad** (API y
  worker: runtime; MigrationService: migrador), vía `WithReference(..., connectionName: "diligencia")`
  en el AppHost.
- **Rationale**: FR-008/FR-010 con una línea por módulo; el historial en el schema propio cumple
  "las migraciones solo tocan el schema del módulo". Los módulos se migran en secuencia en un solo
  proceso, así que no hay bloqueo cruzado; el lock de migraciones de EF protege contra dos
  instancias del servicio.
- **Alternativas**: clase base `ModuleDbContext` (herencia para algo que es configuración); cadena
  de conexión por módulo (complica la transacción compartida, que requiere la misma base).

## R5. Auditoría atómica con la acción de negocio

- **Decisión**: `IAuditLog.RecordAsync(AuditEntry, DbTransaction, CancellationToken)`. La
  implementación crea un `AuditDbContext` con `UseSqlServer(transaction.Connection)` (sin
  estrategia de reintento propia, sin ser dueño de la conexión), llama
  `Database.UseTransaction(transaction)`, agrega el evento y `SaveChangesAsync`. No confirma ni
  revierte: el llamador lo hace. Si falla, la excepción llega al llamador y su transacción no se
  confirma (FR-018).
  Como `EnableRetryOnFailure` prohíbe transacciones iniciadas por el usuario fuera de la estrategia,
  BuildingBlocks provee
  `db.ExecuteInTransactionAsync((tx, ct) => …)`: `CreateExecutionStrategy().ExecuteAsync` → begin →
  trabajo (`SaveChangesAsync(acceptAllChangesOnSuccess: false)` + `RecordAsync(..., tx.GetDbTransaction())`)
  → commit → `AcceptAllChanges()`.
  Segunda operación `RecordStandaloneAsync(AuditEntry, ct)` para acciones sin escritura de negocio
  (consultas de datos sensibles, descargas): usa su propia conexión.
- **Rationale**: decisión de Clarifications. El llamador nunca toca tablas de Auditoría; Auditoría
  escribe con su propio `DbContext` en su schema. Ambos comparten base (requisito de la
  transacción local; no hace falta MSDTC).
- **Alternativas**: outbox + proceso asíncrono (no atómico con la respuesta y más piezas);
  `TransactionScope` (enlistamiento ambiental frágil con async y retries).

## R6. Inmutabilidad y reloj de la bitácora

- **Decisión**: tres capas. (a) Base: `DENY UPDATE, DELETE` al rol de runtime (R3). (b) Aplicación:
  `AuditDbContext.SaveChanges*` lanza `InvalidOperationException` si hay un `AuditEvent` en estado
  `Modified` o `Deleted`. (c) API: no existe ningún endpoint que modifique o borre.
  `OccurredAtUtc` lo asigna la base con `DEFAULT SYSUTCDATETIME()` (EF `HasDefaultValueSql`), así
  todos los eventos usan el reloj del servidor que persiste, en UTC, sin importar qué proceso los
  generó.
- **Rationale**: FR-019/SC-006 se verifican conectándose con el login de runtime y ejecutando SQL
  directo (error 229 esperado). El reloj único resuelve el edge case de relojes desfasados.
- **Alternativas**: trigger `INSTEAD OF UPDATE/DELETE` (un `db_owner` lo deshabilita igual y agrega
  lógica oculta); `LEDGER` tables de SQL Server (integridad criptográfica, fuera de alcance según
  Assumptions; se puede adoptar después sin cambiar el contrato).

## R7. Consulta de la bitácora y rendimiento (SC-007)

- **Decisión**: `GET /api/backoffice/audit/events` con filtros `entityType`+`entityId`, `actorId`,
  `fromUtc` (inclusivo), `toUtc` (exclusivo); orden `OccurredAtUtc, Id` ascendente; paginación
  `page`/`pageSize` (1–100, por defecto 50) que trae `pageSize + 1` filas para calcular
  `hasNextPage` sin `COUNT(*)`. Índices no agrupados: `(EntityType, EntityId, OccurredAtUtc, Id)`,
  `(ActorId, OccurredAtUtc, Id)`, `(OccurredAtUtc, Id)`; PK agrupada `Id bigint IDENTITY` (orden de
  inserción). Proyección con `AsNoTracking`. Test de rendimiento con 1.000.000 de filas cargadas
  con `SqlBulkCopy`, marcado `Category=Performance` y ejecutado en un paso propio de CI.
- **Rationale**: una búsqueda por entidad es un seek sobre el primer índice; sin conteo total, el
  costo no crece con la tabla.
- **Alternativas**: paginación keyset (mejor para páginas profundas, pero más complejo para el
  cliente; se puede agregar sin romper el contrato); `COUNT(*)` total (costoso sin filtros).

## R8. Valores anterior/nuevo y enmascarado (FR-020)

- **Decisión**: el llamador envía `AuditChange(Field, OldValue, NewValue, IsSensitive)` con valores
  ya convertidos a `string` invariante. Auditoría: descarta los cambios con `OldValue == NewValue`;
  enmascara como `"***"` los marcados `IsSensitive` **y** los campos cuyo nombre contiene
  `password`, `secret` o `token` (sin distinguir mayúsculas), como red de seguridad; serializa
  `OldValues`/`NewValues` como objetos JSON `{campo: valor}` (`System.Text.Json`).
  `shortcut:` no hay helper que derive los cambios desde el `ChangeTracker` de EF; se agrega con la
  primera feature de negocio que lo necesite.
- **Rationale**: el contrato no depende de EF ni de los tipos de otros módulos; el filtrado y el
  enmascarado viven en un único lugar testeado.
- **Alternativas**: recibir objetos completos y comparar por reflexión (guarda de más y depende de
  tipos ajenos); atributo `[AuditSensitive]` (requiere el helper diferido).

## R9. Actor y superficie

- **Decisión**: `IActorContext` en `Identidad.Contracts`, con una sola implementación
  `HttpActorContext` en Identidad: si hay `HttpContext` con usuario autenticado, actor = claim `sub`
  y superficie = esquema que autenticó (`Backoffice`/`Portal`); si no (jobs del worker, procesos de
  sistema), actor = `system:<ApplicationName>` y superficie `System`. Auditoría lo resuelve por DI;
  el llamador no lo pasa.
- **Rationale**: el edge case "acción del sistema" nunca queda con actor vacío, y los módulos no
  pueden falsificar el actor.
- **Alternativas**: pasar actor en `AuditEntry` (propenso a errores y a suplantación).

## R10. Autenticación: dos esquemas, tokens de desarrollo y de test

- **Decisión**: esquemas JwtBearer `Backoffice` y `Portal`, configurados desde
  `Authentication:Schemes:<Esquema>` (`ValidIssuer`, `ValidAudiences`, `SigningKeys`), sin
  `DefaultScheme`. Grupo `/api/backoffice` exige policy con esquema `Backoffice` + usuario
  autenticado; `/api/portal`, ídem con `Portal`. `FallbackPolicy` = autenticado. Policies en
  `Identidad.Contracts.AuthPolicies` (p. ej. `AuditReader` = `Backoffice` + rol `Auditor` o
  `Administrador`). `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`.
  Local: `dotnet user-jwts create --project src/Hosts/Diligencia.Api --scheme Backoffice --issuer
  diligencia-backoffice-dev --audience diligencia-backoffice --role Auditor` (escribe clave y
  emisor en los user-secrets de la API). Tests: helper `TestTokens.Create(surface, roles)` que firma
  con una clave simétrica de test por superficie inyectada en la configuración de la
  `WebApplicationFactory`.
  Test de integración que recorre `EndpointDataSource` y exige que todo endpoint (salvo `/health` y
  `/alive`) empiece por `/api/backoffice` o `/api/portal` y tenga metadatos de autorización.
- **Rationale**: decisión de Clarifications (sin endpoint de emisión). Claves y emisores distintos
  por esquema garantizan que un token de un grupo falla en el otro (FR-004).
- **Alternativas**: un solo esquema con claim de superficie (un error de policy expondría el
  backoffice, contra el Principio V).

## R11. Identificador de correlación de punta a punta

- **Decisión**: `CorrelationContext` (valor en `AsyncLocal<string?>`) en BuildingBlocks.
  - API: middleware que toma `X-Correlation-Id` si cumple `^[A-Za-z0-9._:-]{1,64}$`, si no usa
    `Activity.Current.TraceId` (o un GUID); lo fija en el contexto, lo agrega a la respuesta y abre
    un scope de log `{ CorrelationId, Service }`.
  - Hangfire: `CorrelationJobFilter : IClientFilter, IServerFilter` registrado en API y worker. Al
    crear el job guarda el parámetro `CorrelationId`; al ejecutarlo lo restaura en el contexto y
    abre el mismo scope (se cierra en `OnPerformed`).
  - Auditoría lo lee del contexto al registrar el evento.
- **Rationale**: FR-013 sin pasar el id a mano; validar el header es validación en el borde de
  confianza (evita inyección en logs). Usar el TraceId enlaza logs y trazas en el dashboard de Aspire.
- **Alternativas**: solo W3C `traceparent` (no permite el header propio que pide la spec); pasar el
  id como argumento de cada job (fácil de olvidar).

## R12. Logs estructurados

- **Decisión**: logging nativo de .NET. Formato JSON por configuración, sin código:
  `Logging:Console:FormatterName=json` con `IncludeScopes`, `UseUtcTimestamp` y
  `TimestampFormat="O"`. ServiceDefaults exporta además por OTLP al dashboard de Aspire (con scopes).
  Campos: nivel, timestamp UTC, `Category` (el namespace `Diligencia.Modules.<Módulo>.*` identifica
  el módulo), `CorrelationId` y `Service` (scope) y `service.name` (recurso OTel).
  Nivel por entorno y módulo con `Logging:LogLevel:Diligencia.Modules.<Módulo>` en appsettings o
  variables de entorno (FR-015).
  Log de solicitudes con `AddHttpLogging` limitado a
  `RequestMethod | RequestPath | ResponseStatusCode | Duration` y `CombineLogs = true`: no registra
  headers, query string ni cuerpos, por lo que `Authorization`, tokens en query y archivos nunca
  llegan al log (FR-014). EF: sin `EnableSensitiveDataLogging`; `Microsoft.EntityFrameworkCore.Database.Command`
  en `Warning`.
- **Rationale**: cumple FR-012/14/15 con configuración y una política de "no registrar lo que no
  hace falta" en vez de redacción a posteriori.
- **Alternativas**: Serilog (fuera del stack); `Microsoft.Extensions.Compliance.Redaction` (útil
  cuando haya que loguear datos clasificados; hoy no hay ninguno).

## R13. Errores no controlados

- **Decisión**: `AddProblemDetails(o => o.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["correlationId"] = CorrelationContext.Current)`
  + `UseExceptionHandler()` + `UseStatusCodePages()`. El middleware registra la excepción con su
  detalle (`ExceptionHandlerMiddleware`, nivel Error) y el cliente recibe un 500 genérico sin traza.
- **Rationale**: FR-016 con piezas del framework; el mismo formato sirve para 400/401/403/404.
- **Alternativas**: middleware propio de excepciones (código duplicado del framework).

## R14. Chequeos de salud

- **Decisión**: en ServiceDefaults, `self` (tag `live`) y `database`
  (`AddDbContextCheck<AuditDbContext>("database")`, que hace `CanConnectAsync`). `/alive` = solo
  `live`; `/health` = todos. Writer JSON `{ status, checks: { self, database } }` sin descripciones
  ni excepciones (no expone credenciales). 200 si sano, 503 si no. Mapeados en todos los entornos
  (el template de Aspire solo lo hace en Development). Worker es `Microsoft.NET.Sdk.Web` solo para
  exponer estos dos endpoints. En el AppHost, `WithHttpHealthCheck("/health")` para api/worker y
  `"/"` para las SPAs.
- **Rationale**: FR-005 y SC-002. Si la base cae, el proceso sigue vivo (`/alive` 200) y `/health`
  la reporta caída; el reintento de EF y la reconexión de Hangfire recuperan sin reinicio.
- **Alternativas**: `AspNetCore.HealthChecks.SqlServer` (dependencia externa para lo mismo).

## R15. Configuración obligatoria (fail-fast)

- **Decisión**: extensión `configuration.GetRequired(key)` en BuildingBlocks que lanza
  `InvalidOperationException("Falta la configuración obligatoria '<clave>'.")` sin el valor. Se
  usa en el arranque para `ConnectionStrings:diligencia` y para `ValidIssuer`/`ValidAudiences`/
  `SigningKeys` de cada esquema. Secretos: user-secrets en local, parámetros secretos de Aspire
  (contraseñas de `sa`, migrador y runtime generadas y persistidas en los user-secrets del AppHost),
  variables de entorno en CI.
- **Rationale**: FR-006 y su edge case, con un test por clave obligatoria.
- **Alternativas**: `IOptions` + `ValidateOnStart` para cada sección (más tipos para tres claves).

## R16. Tests de arquitectura

- **Decisión**: `Diligencia.ArchitectureTests` (xUnit v3 + NetArchTest.Rules + reflexión). Reglas,
  escritas como funciones sobre un conjunto de ensamblados:
  1. Un módulo interno no referencia el ensamblado interno de otro módulo (solo `*.Contracts`).
  2. Un `*.Contracts` no referencia ensamblados internos ni EF Core.
  3. Los únicos tipos públicos de un módulo interno son `<Módulo>Module`.
  4. Ningún tipo que herede `DbContext` se usa fuera de su módulo.
  5. `Diligencia.Api` y `Diligencia.Worker` no se referencian entre sí; `BuildingBlocks` no
     referencia módulos.
  6. Toda entidad de cada `DbContext` implementa `ITimestamped`, salvo `AuditEvent` (FR-011).
  Las mismas funciones se aplican a `tests/Fixtures/Diligencia.ArchitectureTests.Violations`
  (un ensamblado con una violación de cada tipo) y el test exige que **todas** se detecten (SC-003).
- **Rationale**: NetArchTest tiene la API más simple de las dos que admite la constitución; las
  reglas de referencias entre ensamblados son reflexión pura y no necesitan más.
- **Alternativas**: ArchUnitNET (más potente, más ceremonia); si NetArchTest diera problemas con
  .NET 10 se migra a ArchUnitNET sin cambiar las reglas.

## R17. Frontend: workspaces, estructura y fronteras

- **Decisión**: npm workspaces (`apps/*`, `packages/*`) en un `package.json` raíz, sin herramientas
  extra. Cada app: Vite + React + TS strict, MUI con tema de `@diligencia/ui`, TanStack Router (una
  ruta `/` de la feature `home`) y QueryClientProvider; proxy de Vite `/api` → URL de la API que
  inyecta Aspire. ESLint flat config raíz con `eslint-plugin-boundaries`: elementos `feature`
  (`apps/*/src/features/*`, capturando app y nombre), `shared` (`apps/*/src/shared`), `app-shell`
  (`apps/*/src/app`), `package` (`packages/*`). Reglas: una feature importa solo de sí misma, del
  `shared` de su app y de `packages`; `shared` no importa features; `packages` solo importa
  `packages`; nada en `apps/portal` importa de `apps/backoffice`. Un test Vitest corre ESLint (API de
  Node) sobre `eslint-fixtures/` y exige un error por cada violación sembrada (SC-003).
- **Rationale**: Principio VII con la configuración mínima; npm viene con Node.
- **Alternativas**: pnpm/turborepo (más herramientas sin necesidad todavía); `no-restricted-paths`
  (ver Complexity Tracking).

## R18. Estrategia de tests de integración

- **Decisión**: un contenedor `MsSqlContainer` (Testcontainers) por ejecución como assembly fixture
  de xUnit v3; el fixture ejecuta el mismo código del MigrationService (bootstrap + migraciones +
  grants), y la `WebApplicationFactory` de la API y la del worker usan la cadena de **runtime**.
  Logs capturados con `AddFakeLogging`. Correlación API→worker: la factory del worker con Hangfire
  en polling corto; el test llama `POST /api/backoffice/diagnostics/jobs` con un
  `X-Correlation-Id` propio y espera el log del job con ese valor. Un smoke con
  `Aspire.Hosting.Testing` arranca el AppHost y espera `Healthy` en todos los recursos (SC-002).
- **Rationale**: prueba la configuración real de permisos, no un doble.
- **Alternativas**: base compartida de CI (estado entre corridas); InMemory (ver Complexity
  Tracking).

## R19. Dockerfiles y CI

- **Decisión**: Dockerfiles multi-stage para `api`, `worker` (SDK → `aspnet` runtime, usuario no
  root, contexto = raíz del repo por los `Directory.*.props`) y `backoffice`/`portal`
  (`node` build → `nginx` sin privilegios sirviendo estáticos). CI en GitHub Actions (supuesto: el
  repo se aloja en GitHub; ajustar si no) con jobs: backend (`dotnet build`, `dotnet test` con
  Docker para Testcontainers), frontend (`npm ci`, `npm run lint`, `npm run typecheck`,
  `npm test`, `npm run build`), performance (`dotnet test --filter Category=Performance`) e
  imágenes (`docker build` de las cuatro, sin push).
- **Rationale**: FR-003 (imágenes construidas en CI) y quality gates de la constitución.
- **Alternativas**: `dotnet publish /t:PublishContainer` (sin Dockerfile, pero la spec pide
  Dockerfile); push a registry (pertenece a la feature de despliegue).

## R20. Fechas de creación y modificación (FR-011)

- **Decisión**: interfaz `ITimestamped { DateTime CreatedAtUtc; DateTime UpdatedAtUtc }` en
  BuildingBlocks y `TimestampsInterceptor` (`SaveChangesInterceptor` con `TimeProvider`) que las
  fija en `Added`/`Modified`. Columnas `datetime2`. La regla 6 de R16 obliga a implementarla.
  `AuditEvent` se exceptúa: es inmutable y su única fecha es `OccurredAtUtc`.
- **Rationale**: los módulos no pueden olvidarlo, y el test lo detecta en la primera entidad nueva.
- **Alternativas**: propiedades shadow por convención (invisibles en el dominio).

## R21. Job de diagnóstico

- **Decisión**: `IDiagnosticJob` en BuildingBlocks, implementado en el worker (registra
  "Diagnostic job executed" en Information). `POST /api/backoffice/diagnostics/jobs` (policy
  `Administrador`) encola `IDiagnosticJob.RunAsync` y responde 202 con el `jobId`. Solo se mapea si
  `Diagnostics:Enabled=true` (activo en `appsettings.Development.json` y en tests).
- **Rationale**: hace verificable US3 (correlación API → worker) de forma manual y automatizada,
  sin abrir la superficie en producción.
- **Alternativas**: verificar solo con tests que encolan directo (no sirve para el quickstart
  manual); dashboard de Hangfire (otra superficie a proteger).
