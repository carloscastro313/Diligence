---

description: "Tasks: Solicitudes de alta de terceros"
---

# Tasks: Solicitudes de alta de terceros

**Input**: `specs/003-solicitudes/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: INCLUIDOS. La constitución exige un test de backend por cada regla, permiso o transición nueva, y Playwright para el flujo crítico de alta de tercero.

**Organization**: por historia de usuario. US1 (P1) crear la solicitud y enviar el enlace, solo para CUIT nuevo; US2 (P1) aviso de tercero existente y todas las ramas de CUIT existente (sumarse, bloqueado/activo, reabrir, concurrencia); US3 (P2) mis solicitudes con fecha estimada.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede ejecutar en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: US1–US3 según la spec
- Rutas relativas a la raíz del repo. `CA` = `src/Modules/Casos/Diligencia.Modules.Casos`; `TE` = `src/Modules/Terceros/Diligencia.Modules.Terceros`; `BB` = `src/BuildingBlocks/Diligencia.BuildingBlocks`; `ID` = `src/Modules/Identidad/Diligencia.Modules.Identidad`; `IT` = `tests/Diligencia.IntegrationTests`; `UT` = `tests/Diligencia.UnitTests`; `BO` = `apps/backoffice/src`. Todo tipo nuevo en un módulo es `internal` salvo lo listado en [contracts/module-contracts.md](contracts/module-contracts.md).
- Slices con la forma de `ID/Features/CreateUser/CreateUser.cs`: request + validador + handler + `XxxEndpoint.Map` en un archivo; transacción con `ExecuteInTransactionAsync`/`SaveChangesAsync(false, ct)` y bitácora con `IAuditLog.RecordAsync(entry, tx, ct)`.

---

## Phase 1: Setup

**Purpose**: referencias, contratos y configuración compartida

- [X] T001 [P] En `CA/Diligencia.Modules.Casos.csproj` agregar `ProjectReference` a `Diligencia.Modules.Terceros.Contracts`, `Diligencia.Modules.Auditoria.Contracts` y `Diligencia.Modules.Identidad.Contracts`, `PackageReference` a `FluentValidation` y `Hangfire.Core` (versiones centrales de `Directory.Packages.props`), `FrameworkReference Microsoft.AspNetCore.App` e `InternalsVisibleTo` para `Diligencia.IntegrationTests` y `Diligencia.UnitTests`, copiando la forma de `ID/Diligencia.Modules.Identidad.csproj`
- [X] T002 [P] En `TE/Diligencia.Modules.Terceros.csproj` agregar `ProjectReference` a `Diligencia.Modules.Auditoria.Contracts` e `InternalsVisibleTo` para `Diligencia.IntegrationTests`
- [X] T003 [P] Agregar en `tests/Diligencia.UnitTests/Diligencia.UnitTests.csproj` un `ProjectReference` a `src/Modules/Casos/Diligencia.Modules.Casos/Diligencia.Modules.Casos.csproj` (los tests unitarios de `Cuit`, `BusinessCalendar`, validador y fecha estimada lo necesitan)
- [X] T004 [P] Crear `src/Modules/Terceros/Diligencia.Modules.Terceros.Contracts/TercerosContracts.cs` con `TipoTercero`, `TipoPersona`, `EstadoTercero`, `TerceroInfo`, `NuevoTercero` e `ITerceroRegistry` exactamente como en [contracts/module-contracts.md](contracts/module-contracts.md)
- [X] T005 [P] Crear `BB/Email/EmailTransport.cs`: `public interface IEmailTransport`, `internal sealed class SmtpEmailTransport` (lee `Email:Smtp:Host`, `Port`, `From` con los mismos valores por defecto que `ID/Email/SmtpEmailSender.cs`, sin `try/catch`: lanza si falla) y `public static class EmailSetup { AddEmailTransport(this IServiceCollection) }` (R7). **No modificar** nada dentro de `ID/` (Principio I)
- [X] T006 Llamar `.AddEmailTransport()` en `src/Hosts/Diligencia.Api/Program.cs` y `src/Hosts/Diligencia.Worker/Program.cs`, y `.AddEmailTransport().AddDiligenciaHangfire(config)` en `src/Hosts/Diligencia.MigrationService/MigrationEntryPoint.cs` (también llama `AddCasosModule`; sin esos registros `ValidateOnBuild` en Development rompe `host.Build()` por las dependencias de `CreateSolicitudHandler` y `PortalLinkDelivery`). Depende de T005
- [X] T007 [P] En `src/Aspire/Diligencia.AppHost/Program.cs` pasar al proyecto `worker` `Email__Smtp__Host`, `Email__Smtp__Port` (endpoint `smtp` de Mailpit) y `Casos__PortalBaseUrl` = endpoint http de `portal` (después de declarar `portal`, como se hace con `Identidad__BackofficeBaseUrl`)

---

## Phase 2: Foundational (bloquea todas las historias)

**Purpose**: tablas de Terceros y Casos, registro de terceros, reglas compartidas y soporte de tests

**⚠️ CRITICAL**: ninguna historia empieza antes de terminar esta fase

- [X] T008 [P] Crear la entidad `Tercero` (`ITimestamped`) y su configuración (`Cuit char(11)` único, enums como `nvarchar(32)` con `HasConversion<string>()`, `RazonSocial nvarchar(200)`) en `TE/Persistence/Tercero.cs`; agregar `DbSet<Tercero> Terceros` y `ApplyConfigurationsFromAssembly` en `TE/Persistence/TercerosDbContext.cs`; generar la migración `AddTerceros` (`dotnet ef migrations add AddTerceros --project TE --context TercerosDbContext --output-dir Persistence/Migrations`) según [data-model.md](data-model.md)
- [X] T009 Implementar `TerceroRegistry : ITerceroRegistry` en `TE/Registry/TerceroRegistry.cs`: `FindByCuitAsync` con el `TercerosDbContext` inyectado (`AsNoTracking`); `GetOrCreateForOnboardingAsync` con un `TercerosDbContext` efímero sobre `transaction.Connection` + `UseTransactionAsync(transaction)` (copiar el patrón de `src/Modules/Auditoria/Diligencia.Modules.Auditoria/Features/RecordEvent/AuditLog.cs`) **agregando `.AddInterceptors(<TimestampsInterceptor inyectado>)` a las opciones** para que `CreatedAtUtc`/`UpdatedAtUtc` se completen (R2), lectura con `FromSql($"SELECT * FROM terceros.Terceros WITH (UPDLOCK, HOLDLOCK) WHERE Cuit = {cuit}")`; si no existe crea en `EnAlta` y registra `AuditAction.Created`/`"Tercero"` (Cuit, TipoTercero, TipoPersona, RazonSocial, Estado) con `IAuditLog.RecordAsync(entry, transaction)`; no confirma. Registrar `ITerceroRegistry` como scoped en `TE/TercerosModule.cs` (R2, R3). Depende de T004, T008
- [X] T010 [P] Crear entidades `Caso` (`ITimestamped`), `Solicitud`, `EnlacePortal` y enums internos `TipoCaso`, `EstadoCaso`, `EntregaEnlace` con sus configuraciones (índice único filtrado `Casos(TerceroId) WHERE [Estado] IN ('EsperaTercero','EnVerificacion','EnAprobacion')`; único `Solicitudes(CasoId, SolicitanteId)`; índice `Solicitudes(SolicitanteId)`; único `EnlacesPortal(CasoId)`; único filtrado `EnlacesPortal(TokenHash) WHERE TokenHash IS NOT NULL`; `MontoAnualEstimado decimal(18,2)`) en `CA/Persistence/Entities.cs` y `CA/Persistence/EntityConfigurations.cs`; registrar los `DbSet` en `CA/Persistence/CasosDbContext.cs` y generar la migración `AddSolicitudes` (`--project CA --context CasosDbContext --output-dir Persistence/Migrations`) según [data-model.md](data-model.md)
- [X] T011 [P] Implementar `Cuit` (`Normalize`: solo dígitos; `IsValid(cuit)`: 11 dígitos + dígito verificador con pesos `5,4,3,2,7,6,5,4,3,2`, `11→0`, `10→inválido`; `MatchesPersona(cuit, TipoPersona)`: `20,23,24,27` humana / `30,33,34` jurídica) en `CA/Features/Shared/Cuit.cs` con tests en `UT/Casos/CuitTests.cs` (`30-71234567-1` válido, `30-71234567-2` inválido, con espacios/guiones, prefijo vs persona, longitud ≠ 11) (R4)
- [X] T012 [P] Implementar `BusinessCalendar.AddBusinessDays(DateOnly, int)` (lunes a viernes) y `BusinessCalendar.Today(TimeProvider)` / `ToLocalDate(DateTime utc)` con UTC−3 fijo y comentario `// shortcut: UTC-3 fijo sin horario de verano; pasar a TimeZoneInfo si Argentina vuelve a tenerlo`, más `StagePlazos` (`EsperaTercero=15`, `EnVerificacion=5`, `EnAprobacion=3`) en `CA/Features/Shared/BusinessCalendar.cs`, con tests en `UT/Casos/BusinessCalendarTests.cs` (cruce de fin de semana, empezar en sábado, 0 días) (R8)
- [X] T013 [P] Crear `CA/CasosOptions.cs` (sección `Casos`, `PortalBaseUrl`) y `CA/Features/Shared/PortalToken.cs` (`Generate()` 32 bytes `RandomNumberGenerator` en Base64Url, `Hash()` SHA-256; misma forma que `ID/Authentication/OpaqueToken.cs`) (R5)
- [X] T014 Extender `CA/CasosModule.cs`: bind de `CasosOptions`, registro por convención de validadores (singleton) y `*Handler` en `.Features.` (scoped) copiando `AddFeatureServices` de `ID/IdentidadModule.cs`. Depende de T001, T013
- [X] T015 [P] Agregar `IT/Support/FakeEmailTransport.cs` (guarda `(to, subject, body)` en memoria; propiedad `FailNext(int n)` para que las próximas n llamadas lancen) y `IT/Support/FakeBackgroundJobClient.cs` (`IBackgroundJobClient` que guarda los `Job` creados y devuelve un id); en `IT/Support/ApiFactory.cs` reemplazar `IEmailTransport` e `IBackgroundJobClient` por esos fakes, fijar `Casos:PortalBaseUrl=http://portal.test` y exponerlos (`Factory.EmailTransport`, `Factory.Jobs`); exponerlos también en `IT/Support/IdentidadApiTest.cs`. Depende de T005
- [X] T016 [P] Agregar `IT/Support/TestCuits.cs`: genera CUITs válidos y únicos por test (prefijo dado + 8 dígitos aleatorios + DV calculado) para que los tests no choquen en la base compartida

**Checkpoint**: la solución compila; `dotnet test` sigue verde (incluidos los tests de arquitectura con Casos → Terceros.Contracts).

---

## Phase 3: User Story 1 — Crear una solicitud y enviar el enlace al tercero (Priority: P1) 🎯 MVP

**Goal**: un solicitante crea una solicitud para un CUIT nuevo; se crean tercero y caso en "Espera del tercero" y el contacto recibe el enlace por correo, con reintentos y fallo visible.

**Independent Test**: con rol Solicitante, `POST /solicitudes` válido → 201 `CasoAbierto`; tercero `EnAlta`, caso `EsperaTercero`, un job encolado; al ejecutar el job, `FakeEmailTransport` tiene un correo con `http://portal.test/acceso#t=<token>` y `SHA256(token)` = `EnlacesPortal.TokenHash`.

### Tests (escribir primero, deben fallar)

- [X] T017 [P] [US1] Tests del validador en `UT/Casos/CrearSolicitudValidatorTests.cs`: cada campo obligatorio vacío, correo mal formado, monto 0 y negativo, CUIT con DV inválido, prefijo inconsistente con `tipoPersona`, enums inválidos; error por campo con la clave del request (US1-2/3, FR-002)
- [X] T018 [P] [US1] Tests de integración en `IT/Casos/CreateSolicitudTests.cs` (heredan `IdentidadApiTest`): alta válida → 201 `CasoAbierto` + `Location`; filas en `terceros.Terceros` (`EnAlta`, `CreatedAtUtc` = hora del `FakeTimeProvider`), `casos.Casos` (`EsperaTercero`, `SolicitanteId` = usuario), `casos.Solicitudes` (`EsOriginal`), `casos.EnlacesPortal` (`Pendiente`, `TokenHash` null, `VenceUtc` = apertura + 15 días hábiles) y un job en `Factory.Jobs`; CUIT con guiones se guarda normalizado; 400 con errores por campo (US1-1/2/3, FR-001..003, FR-008)
- [X] T019 [P] [US1] Tests del job en `IT/Casos/SendPortalLinkTests.cs` (resolver `IPortalLinkDelivery` del scope de la factory y llamarlo directo): éxito → `Enviado`, `Intentos=1`, correo a `ContactoEmail` con `/acceso#t=`, hash del token del correo = `TokenHash`; dos casos distintos → tokens distintos; segunda ejecución sobre `Enviado` no envía nada; fallo con `Intentos<5` → `Reintentando` y la llamada lanza; quinto fallo → `Fallido` y no lanza (US1-4, FR-009, FR-011, R6)
- [X] T020 [P] [US1] Test de bitácora en `IT/Casos/SolicitudAuditTests.cs`: tras el alta y el job hay eventos `Created` de `Tercero`, `Caso`, `Solicitud` (con autor, datos cargados y estado null→EsperaTercero) y `Updated` de `EnlacePortal` (Pendiente→Enviado, actor `system:`); ninguna fila de `auditoria` contiene el token ni la URL del enlace (US1-6, FR-010, FR-017; los logs los cubre T044)
- [X] T021 [P] [US1] Agregar a `IT/Identidad/PermissionMatrixTests.cs` un arreglo `RequesterOnly = [BackofficeRoles.Solicitante]` y la fila `["POST /solicitudes"] = RequesterOnly` (US1-5, FR-016). Las filas de los GET se agregan con sus endpoints (T032, T038) para no dejar la matriz en rojo entre fases

### Implementación

- [X] T022 [US1] Crear el slice `CA/Features/CreateSolicitud/CreateSolicitud.cs`: `CrearSolicitudRequest` (campos del contrato, enums como `string?` validados), `CrearSolicitudValidator` (FluentValidation con `Cuit`, longitudes de [data-model.md](data-model.md), `EmailAddress`, monto > 0), `CreateSolicitudHandler` y `CreateSolicitudEndpoint.Map(RouteGroupBuilder solicitudes)` (`MapPost("")` sobre el subgrupo `/solicitudes`, que ya exige el rol; ver T024). Handler, en una transacción de `CasosDbContext`: `ITerceroRegistry.GetOrCreateForOnboardingAsync(..., tx)`; si `Created`, crea `Caso` (copia Cuit/RazonSocial/TipoTercero, `EstadoDesdeUtc` = ahora), `Solicitud` original (correo en minúsculas), `EnlacePortal` (`Pendiente`, `Destinatario`, `VenceUtc` = fin del día hábil 15 en hora Argentina) y registra `Created` de `Caso` y `Solicitud` con `IAuditLog`; si no fue creado, responder por ahora `409` "ya existe un tercero con ese CUIT" (lo reemplaza US2). Después de confirmar: `IBackgroundJobClient.Enqueue<IPortalLinkDelivery>(d => d.SendAsync(enlaceId, CancellationToken.None))` con comentario `// shortcut: si encolar falla tras confirmar, el enlace queda Pendiente; agregar barrido recurrente con la feature de plazos`; responder 201 `{ casoId, resultado: "CasoAbierto" }` con `Location: /api/backoffice/solicitudes/{casoId}`. Depende de T009–T014
- [X] T023 [US1] Crear `CA/Features/SendPortalLink/SendPortalLink.cs`: `internal interface IPortalLinkDelivery { [AutomaticRetry(Attempts = 5)] Task SendAsync(Guid enlaceId, CancellationToken ct); }` (el atributo va en la **interfaz**: Hangfire lee los filtros del tipo encolado, R6) e implementación `PortalLinkDelivery` (usa `CasosDbContext`, `IEmailTransport`, `IAuditLog`, `TimeProvider`, `IOptions<CasosOptions>`, `ILogger`): si `Enviado`/`Fallido` termina; genera token con `PortalToken`, guarda hash, `Intentos++`, `UltimoIntentoUtc`, confirma; envía `{PortalBaseUrl}/acceso#t={token}` con la plantilla `PortalLinkEmail` (asunto y cuerpo en español con el vencimiento, sin datos de riesgo); éxito → `Enviado` + bitácora `Updated` `EnlacePortal`; excepción → si `Intentos < 5` `Reintentando` y relanza, si no `Fallido` + bitácora y no relanza; nunca loguea el cuerpo ni la URL. Registrar `IPortalLinkDelivery` scoped en `CA/CasosModule.cs` (R6). Depende de T014
- [X] T024 [US1] En `CasosModule.MapCasosEndpoints` (`CA/CasosModule.cs`) crear `var solicitudes = surfaces.Backoffice.MapGroup("/solicitudes").RequireAuthorization(p => p.RequireRole(BackofficeRoles.Solicitante));` y mapear `CreateSolicitudEndpoint.Map(solicitudes)` (R9: no se toca `PolicySetup` de Identidad). Depende de T022
- [X] T025 [P] [US1] Crear `BO/features/solicitudes/cuit.ts` (normalizar + DV + prefijo, misma regla que `Cuit.cs`, solo UX) con test `BO/features/solicitudes/cuit.test.ts`
- [X] T026 [US1] Crear `BO/features/solicitudes/NuevaSolicitudPage.tsx` (React Hook Form + Zod con `cuit.ts`; campos del contrato; tipo de persona cambia la etiqueta "Razón social" ↔ "Nombre y apellido"; envía `POST /solicitudes` con `api` de `BO/shared/api.ts`; muestra errores por campo del ProblemDetails 400 y el `title` de un 409; al 201 llama `onCreated(casoId)`) con test `BO/features/solicitudes/NuevaSolicitudPage.test.tsx` (CUIT inválido bloquea el envío, envío correcto, errores del servidor por campo). Depende de T025
- [X] T027 [US1] Agregar la ruta `/solicitudes/nueva` en `BO/app/router.tsx` (al 201 navega a `/`, US3 lo cambia a la lista) y un enlace "Nueva solicitud" en `BO/app/Layout.tsx` visible con `hasRole(session, Roles.Solicitante)`; actualizar `BO/app/router.test.tsx` si enumera rutas. Depende de T026

**Checkpoint**: US1 se puede demostrar: crear una solicitud y ver el correo en Mailpit.

---

## Phase 4: User Story 2 — Aviso de tercero existente por CUIT (Priority: P1)

**Goal**: antes de guardar, el solicitante ve si el CUIT ya existe y su estado; al guardar con CUIT existente se suma al caso abierto, se rechaza si está activo/bloqueado o se abre un caso nuevo si el último terminó abandonado o rechazado.

**Independent Test**: con un tercero y caso existentes, `GET /solicitudes/tercero-existente?cuit=` devuelve razón social, estado y etapa sin datos de riesgo; un segundo solicitante que guarda ese CUIT obtiene `SumadoACasoAbierto` y no se encola otro job.

### Tests (escribir primero, deben fallar)

- [X] T028 [P] [US2] Tests en `IT/Casos/CheckCuitTests.cs`: CUIT inexistente → `existe=false`; existente con caso abierto → razón social, `estadoTercero`, `etapaCasoAbierto`, `yaLoSigue` (true para el creador, false para otro), `admiteSolicitud`; tercero `Activo`/`Bloqueado` (actualizar el estado por SQL directo en el test) → `admiteSolicitud=false`; CUIT inválido → 400; el JSON solo tiene las claves de `AvisoTercero` del contrato (US2-1/4, FR-004/005)
- [X] T029 [P] [US2] Tests en `IT/Casos/CreateSolicitudExistingTests.cs`: segundo solicitante con caso abierto → 201 `SumadoACasoAbierto`, mismo `casoId`, una `Solicitud` nueva con `EsOriginal=false` y su motivo/monto, bitácora `Created` de `Solicitud`, ningún job nuevo; el mismo usuario otra vez → 409 "ya lo sigue"; tercero `Activo` → 409; `Bloqueado` → 409; último caso `Abandonado` y `Rechazado` (por SQL) → 201 `CasoAbierto` con nuevo caso sobre el mismo `TerceroId` y nuevo job (US2-2/3, FR-006, FR-007, edge cases)
- [X] T030 [P] [US2] Test de concurrencia en `IT/Casos/CreateSolicitudConcurrencyTests.cs`: dos solicitantes hacen `POST /solicitudes` con el mismo CUIT nuevo en paralelo (`Task.WhenAll`, repetir 5 veces con CUITs distintos) → siempre un tercero, un caso, dos solicitudes, un `CasoAbierto` y un `SumadoACasoAbierto` (edge case, SC-003, R3)

### Implementación

- [X] T031 [US2] Crear `CA/Features/Shared/CasoQueries.cs` con `EstadosAbiertos`, `FindOpenCasoAsync(CasosDbContext, Guid terceroId, ct)` y `LastCasoAsync`, compartidos por `CheckCuit` y `CreateSolicitud`. Depende de T010
- [X] T032 [US2] Crear el slice `CA/Features/CheckCuit/CheckCuit.cs`: `MapGet("/tercero-existente")` sobre el subgrupo `/solicitudes`, con `[AsParameters] CheckCuitQuery(string? Cuit)` y `CheckCuitQueryValidator` (FluentValidation: obligatorio, máx. 20, `Cuit.IsValid` tras normalizar → 400 por campo con `ApiProblems.Validation`); `ITerceroRegistry.FindByCuitAsync`; caso abierto con `CasoQueries`; `yaLoSigue` = existe `Solicitud` del actor en ese caso; `admiteSolicitud` = estado no `Activo`/`Bloqueado` y no `yaLoSigue`; responde `AvisoTercero` del contrato; mapear en el subgrupo de `CA/CasosModule.cs` y agregar la fila `["GET /solicitudes/tercero-existente"] = RequesterOnly` en `IT/Identidad/PermissionMatrixTests.cs`. Depende de T031
- [X] T033 [US2] Reemplazar el 409 provisional de `CreateSolicitudHandler` en `CA/Features/CreateSolicitud/CreateSolicitud.cs` por las ramas de FR-007 dentro de la misma transacción (el registro ya tiene el CUIT bloqueado): `Activo`/`Bloqueado` → 409 con motivo; caso abierto → si el actor ya tiene `Solicitud` 409 "Ya seguís este caso.", si no agrega `Solicitud` `EsOriginal=false` + bitácora y responde 201 `SumadoACasoAbierto` sin encolar; sin caso abierto → mismo camino que un tercero nuevo (caso + solicitud original + enlace + job). Extraer el "abrir caso" a un método privado para no duplicarlo. Depende de T031
- [X] T034 [US2] En `BO/features/solicitudes/NuevaSolicitudPage.tsx` agregar el aviso: `useQuery(['tercero-existente', cuit])` habilitada cuando `cuit.ts` da válido, con debounce de 400 ms; muestra `Alert` con razón social, estado y etapa; si `admiteSolicitud=false` deshabilita "Guardar" y explica el motivo (comodidad: la API decide); al 201 `SumadoACasoAbierto` informa "Te sumaste al caso existente". Tests en `BO/features/solicitudes/NuevaSolicitudPage.test.tsx` (aviso visible, guardar deshabilitado para bloqueado, mensaje de sumado). Depende de T032 (contrato), T026

**Checkpoint**: US1 + US2 cubren RF-01..RF-03 completos.

---

## Phase 5: User Story 3 — Consultar mis solicitudes (Priority: P2)

**Goal**: el solicitante lista los casos que creó o sigue, con estado, fecha estimada o resultado y aviso de enlace no entregado, con filtro y búsqueda.

**Independent Test**: dos solicitantes con solicitudes en distintos estados; cada uno ve solo las suyas, con fecha estimada correcta según `FakeTimeProvider`.

### Tests (escribir primero, deben fallar)

- [X] T035 [P] [US3] Tests unitarios de la fecha estimada en `UT/Casos/FechaEstimadaTests.cs`: caso recién abierto en `EsperaTercero` → hoy + 23 días hábiles; `EsperaTercero` con plazo vencido → hoy + 8; `EnVerificacion` desde hace 2 días hábiles → fin de verificación + 3; `EnAprobacion` vencido → hoy + 0; estado final → null (FR-013, R8)
- [X] T036 [P] [US3] Tests en `IT/Casos/ListMisSolicitudesTests.cs`: solicitante A ve las que creó y a las que se sumó (`esOriginal` correcto), nunca las de B; un usuario Solicitante+Analista ve solo las suyas; filtro `estado`; `q` por razón social (contiene, sin distinguir mayúsculas) y por CUIT (con o sin guiones); caso `Rechazado`/`Aprobado`/`Abandonado` (por SQL, con `CerradoUtc`) → `cerradoUtc` y `fechaEstimada=null`; enlace `Fallido` → `enlaceNoEntregado=true`; `estado` fuera del enum y `q` de más de 200 caracteres → 400; el JSON solo tiene las claves de `MiSolicitud`; `creadaUtc` de un interesado es la fecha en que se sumó (US3-1..5, FR-012..015, SC-005)

### Implementación

- [X] T037 [US3] Implementar `FechaEstimada.Calcular(EstadoCaso, DateTime estadoDesdeUtc, DateOnly hoy)` en `CA/Features/Shared/FechaEstimada.cs` con `BusinessCalendar` y `StagePlazos`: `fin = max(hoy, AddBusinessDays(inicio, plazo actual))` + plazos de las etapas siguientes; null para estados finales. Depende de T012
- [X] T038 [US3] Crear el slice `CA/Features/ListMisSolicitudes/ListMisSolicitudes.cs`: `MapGet("")` sobre el subgrupo `/solicitudes` con `[AsParameters] MisSolicitudesQuery(string? Estado, string? Q)` y `MisSolicitudesQueryValidator` (FluentValidation: `Estado` vacío o nombre de `EstadoCaso`, `Q` máx. 200 → 400 con `ApiProblems.Validation`); una consulta sobre `Solicitudes` filtrada por `SolicitanteId == IActorContext.ActorId` con join a `Casos` y `EnlacesPortal` (mismo schema), filtros en SQL (`q` normalizado: si tiene solo dígitos/guiones busca `Cuit.StartsWith`, si no `RazonSocial.Contains`), orden por `Solicitud.CreatedAtUtc` desc; proyecta `MiSolicitud` y calcula `fechaEstimada` en memoria con `FechaEstimada`; comentario `// shortcut: sin paginado (decenas por solicitante); paginar si un usuario supera ~500`. Mapear en el subgrupo de `CA/CasosModule.cs` y agregar la fila `["GET /solicitudes"] = RequesterOnly` en `IT/Identidad/PermissionMatrixTests.cs`. Depende de T037
- [X] T039 [US3] Crear `BO/features/solicitudes/SolicitudesPage.tsx` (tabla con razón social, CUIT, tipo, estado en texto legible, fecha de la solicitud, fecha estimada o "Resultado: X el dd/mm/aaaa", chip "Sumado" si `esOriginal=false`, alerta "Enlace no entregado"; filtro de estado y búsqueda en search params, como `BO/features/users/UsersPage.tsx`; botón "Nueva solicitud") con test `BO/features/solicitudes/SolicitudesPage.test.tsx` (render de estados abiertos y finales, enlace no entregado, cambio de filtro)
- [X] T040 [US3] Agregar la ruta `/solicitudes` en `BO/app/router.tsx` con search params tipados, cambiar la navegación del 201 de `/solicitudes/nueva` a `/solicitudes`, y en `BO/app/Layout.tsx` reemplazar "Nueva solicitud" por "Mis solicitudes" (`hasRole(session, Roles.Solicitante)`). Depende de T039

**Checkpoint**: las tres historias funcionan de punta a punta.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T041 [P] Agregar `@playwright/test` como devDependency de `apps/backoffice/package.json`, script `"e2e": "playwright test"`, `apps/backoffice/playwright.config.ts` (`baseURL` de `BASE_URL`, solo Chromium, `testDir: 'e2e'`) y excluir `e2e/` de Vitest en la config de Vite/Vitest del backoffice (R11)
- [X] T042 Escribir `apps/backoffice/e2e/solicitudes.spec.ts`: ingresar con un solicitante (credenciales por `E2E_REQUESTER_EMAIL`/`E2E_REQUESTER_PASSWORD`), crear una solicitud con CUIT generado, verla en "Mis solicitudes" en "Espera del tercero" y comprobar en `MAILPIT_URL/api/v1/messages` un correo al contacto con `/acceso#t=`. Depende de T041, T040
- [X] T043 Agregar a `.github/workflows/ci.yml` un job `e2e` que arranque el AppHost como lo hace `IT/Smoke/AppHostSmokeTests.cs` (o un script en `scripts/`), cree el usuario solicitante vía API como administrador inicial y ejecute `npm run e2e -w apps/backoffice`. Depende de T042
- [X] T044 [P] Extender `IT/Logging/NoSecretsInLogsTests.cs` para que, tras crear una solicitud y ejecutar el job, ningún log capturado contenga el token ni `/acceso#t=` (FR-010)
- [X] T045 Ejecutar todas las secciones de [quickstart.md](quickstart.md) con el AppHost (incluido detener Mailpit para ver `Fallido`) y corregir lo que falle
- [X] T046 [P] Agregar a la tabla de FR-016 en `specs/002-backoffice-auth-rbac/spec.md` las filas "Crear solicitudes (RF-01..03)" y "Consultar mis solicitudes (RF-04)" con "Sí" solo para Solicitante (FR-016 de esta feature)
- [X] T047 [P] Verificar `npm run lint` (fronteras de `features/solicitudes`), `npm run typecheck` y `dotnet test --filter-not-trait "Category=Performance"` en verde

---

## Dependencies & Execution Order

- **Setup (Ph.1)** → sin dependencias; T001–T005 y T007 en paralelo; T006 tras T005.
- **Foundational (Ph.2)** → depende de Setup; **bloquea** todas las historias. T008→T009; T013→T014; T015 tras T005.
- **US1 (Ph.3)** → depende de Foundational. Es el MVP.
- **US2 (Ph.4)** → depende de Foundational y de T022 (modifica el mismo handler) y T026 (misma página).
- **US3 (Ph.5)** → depende de Foundational; usa los datos que crea US1 (sus tests pueden sembrar casos por la API de US1). T039/T040 tocan el router después de T027.
- **Polish (Ph.6)** → al final; T042 necesita US1 y US3.

### Dentro de cada historia

Tests primero (deben fallar), luego slices, luego mapeo de endpoints, luego frontend.

## Parallel Examples

```text
Setup:       T001 T002 T003 T004 T005 T007   (→ T006)
Foundation:  T008 T010 T011 T012 T013 T015 T016   (→ T009, T014)
US1 tests:   T017 T018 T019 T020 T021
US1 impl:    T022 | T023 | T025   (→ T024, T026 → T027)
US2 tests:   T028 T029 T030
US3 tests:   T035 T036
US3 impl:    T037 → T038   |   T039 → T040
Polish:      T041 T044 T046 T047
```

## Implementation Strategy

1. **MVP = Setup + Foundational + US1**: solicitudes para CUIT nuevos con el enlace enviado y reintentado. Ya habilita a la feature del portal (RF-05).
2. **Incremento 2 = US2**: evita duplicados (prioridad alta en el documento funcional); sin ella, un CUIT existente devuelve 409.
3. **Incremento 3 = US3**: visibilidad para el solicitante; reduce consultas a compliance.
4. Validar con [quickstart.md](quickstart.md) al cerrar cada fase.
