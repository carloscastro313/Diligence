---

description: "Tasks: Portal del tercero"
---

# Tasks: Portal del tercero

**Input**: `specs/004-portal-tercero/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: INCLUIDOS. La constitución exige un test de backend por cada regla, permiso o transición nueva, y Playwright para el flujo crítico "carga de documentos en portal".

**Organization**: por historia de usuario. US1 (P1) ingreso con enlace y código; US3 (P1) documentos; US2 (P1) formulario y envío; US4 (P2) guardar a medias y retomar. **US3 va antes que US2** aunque tenga número mayor: el envío exige los documentos obligatorios (FR-011) y el formulario se muestra junto a la sección de documentos.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede ejecutar en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: US1–US4 según la spec
- Rutas relativas a la raíz del repo. Abreviaturas: `CA` = `src/Modules/Casos/Diligencia.Modules.Casos`; `ID` = `src/Modules/Identidad/Diligencia.Modules.Identidad`; `IDC` = `src/Modules/Identidad/Diligencia.Modules.Identidad.Contracts`; `DO` = `src/Modules/Documentos/Diligencia.Modules.Documentos`; `DOC` = `src/Modules/Documentos/Diligencia.Modules.Documentos.Contracts`; `IT` = `tests/Diligencia.IntegrationTests`; `UT` = `tests/Diligencia.UnitTests`; `PO` = `apps/portal/src`. Todo tipo nuevo en un módulo es `internal` salvo lo listado en [contracts/module-contracts.md](contracts/module-contracts.md).
- Slices con la forma de `CA/Features/CreateSolicitud/CreateSolicitud.cs`: request + validador + handler + `XxxEndpoint.Map` en un archivo; transacción con `ExecuteInTransactionAsync` / `SaveChangesAsync(false, ct)` y bitácora con `IAuditLog.RecordAsync(entry, tx, ct)`. Los endpoints de portal se mapean en el grupo `surfaces.Portal`, nunca reciben un id de caso: el caso sale de `IPortalActor` (FR-006).
- Pasar un tipo de la fila `Oblig.` de [data-model.md](data-model.md) a una regla significa leer esa tabla; no se repite acá.

---

## Phase 1: Setup

**Purpose**: referencias, dependencias y configuración compartida

- [X] T001 [P] En `CA/Diligencia.Modules.Casos.csproj` agregar `ProjectReference` a `Diligencia.Modules.Documentos.Contracts` (las referencias a Identidad, Terceros y Auditoría Contracts ya existen)
- [X] T002 [P] En `DO/Diligencia.Modules.Documentos.csproj` agregar `ProjectReference` a `Diligencia.Modules.Auditoria.Contracts` y `Diligencia.Modules.Identidad.Contracts`, `PackageReference` a `FluentValidation`, `FrameworkReference Microsoft.AspNetCore.App` e `InternalsVisibleTo` para `Diligencia.IntegrationTests` y `Diligencia.UnitTests`, copiando la forma de `CA/Diligencia.Modules.Casos.csproj`
- [X] T003 [P] En `tests/Diligencia.UnitTests/Diligencia.UnitTests.csproj` agregar `ProjectReference` a `DO/Diligencia.Modules.Documentos.csproj` (inspector de contenido y cifrado se prueban unitariamente)
- [X] T004 [P] En `apps/portal/package.json` agregar dependencias `react-hook-form`, `@hookform/resolvers`, `zod`, `react-dropzone` (mismas versiones que `apps/backoffice/package.json` donde existan) y devDependency `@playwright/test`, script `"e2e": "playwright test"`; crear `apps/portal/playwright.config.ts` (`baseURL` de `BASE_URL`, `testDir: 'e2e'`, proyectos `chromium` y `mobile` con `devices['Pixel 7']`); excluir `e2e/` de Vitest en `apps/portal/vite.config.ts`; correr `npm install` en la raíz (R12)
- [X] T005 [P] En `src/Aspire/Diligencia.AppHost/Program.cs` verificar cómo recibe hoy la API `Authentication__Schemes__Portal__SigningKeys__0__Value` (solo se pasa la clave de backoffice) y agregar los parámetros persistidos `portal-signing-key` (64 alfanuméricos, como `backoffice-signing-key`) y `documentos-encryption-key` (secreto de 32+ caracteres) como `Authentication__Schemes__Portal__SigningKeys__0__Value` y `Documentos__EncryptionKey` del proyecto `api`
- [X] T006 [P] En `src/Hosts/Diligencia.Api/appsettings.json` agregar `Identidad:PortalSessionIdleMinutes=30`, `Identidad:PortalSessionAbsoluteHours=8` y `Casos:AccesoRateLimitPerMinute=10` (valores por defecto también en `ID/IdentidadOptions.cs` y `CA/CasosOptions.cs`, T015/T023)
- [X] T007 [P] En `IT/Support/ApiFactory.cs` (y `DbApiFactory.cs` si arma su propia configuración) agregar `Documentos:EncryptionKey` de test (32 bytes en base64) para que la API arranque con la clave obligatoria, y `Casos:AccesoRateLimitPerMinute=10000` (en TestServer todas las requests comparten la IP `unknown`; revisar primero cómo `IT/Identidad/RateLimitTests.cs` resuelve lo mismo para `Identidad:AuthRateLimitPerMinute` y copiar ese patrón) (research R13)

---

## Phase 2: Foundational (bloquea todas las historias)

**Purpose**: contratos, tablas de Casos, ayudantes compartidos y soporte de tests

**⚠️ CRITICAL**: ninguna historia empieza antes de terminar esta fase

- [X] T008 [P] Agregar a `IDC/IdentityContracts.cs` `IPortalSignIn` e `IPortalActor` exactamente como en [contracts/module-contracts.md](contracts/module-contracts.md) (sin implementación todavía)
- [X] T009 [P] Crear `DOC/DocumentosContracts.cs` con `NuevoDocumento`, `DocumentoInfo`, `MotivoRechazo`, `DocumentoRechazado` e `IDocumentStore` exactamente como en [contracts/module-contracts.md](contracts/module-contracts.md) (sin implementación todavía)
- [X] T010 Extender `CA/Persistence/Entities.cs` y `CA/Persistence/EntityConfigurations.cs` según [data-model.md](data-model.md): columnas `FallidosConsecutivos int default 0` y `BloqueadoHastaUtc datetime2 null` en `EnlacePortal`; entidades `CodigoIngreso` (índice `(EnlaceId, CreatedAtUtc)`, FK a `EnlacesPortal`, enum `EstadoCodigo { Vigente, Usado, Invalidado }`) y `FormularioTercero` (PK = `CasoId`, FK a `Casos`, `Datos nvarchar(max)`, enum `EstadoFormulario { Borrador, Enviado }`, `Version` con `IsRowVersion()`), con `DbSet` en `CA/Persistence/CasosDbContext.cs`; generar la migración `AddPortalAcceso` (`dotnet ef migrations add AddPortalAcceso --project CA --context CasosDbContext --output-dir Persistence/Migrations`)
- [X] T011 [P] Extender `CA/Features/Shared/ApiProblems.cs`: `Validation` convierte **cada** segmento de la ruta a camelCase (`Domicilio.Calle` → `domicilio.calle`, `Beneficiarios[0].Porcentaje` → `beneficiarios[0].porcentaje`); agregar `NotFound(title)`, `Unauthorized(title, extra?)` (para `codigoInvalidado`), `TooManyRequests(title, retryAfterSeconds?)` con header `Retry-After` y `ServiceUnavailable(title). Test en `UT/Casos/ApiProblemsTests.cs` (ruta anidada y con índice)
- [X] T012 [P] Crear `CA/Features/Shared/DocumentosRequeridos.cs` con el catálogo de [data-model.md](data-model.md) (código `Tipo`, nombre en español, obligatorio, persona J/H) y `For(TipoPersona)`; test en `UT/Casos/DocumentosRequeridosTests.cs` (jurídica: 4 obligatorios + balance opcional; humana: DNI frente, DNI dorso y ARCA obligatorios + domicilio opcional; un tipo ajeno a la persona no está en la lista)
- [X] T013 Crear (depende de T008 y T010) `CA/Features/Shared/CasoPortalQueries.cs`: `LoadSessionCasoAsync(db, IPortalActor)` (caso por `actor.CasoId`, `AsNoTracking`) y `LockCasoAsync(db, casoId)` (`FromSql("SELECT * FROM casos.Casos WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}")` dentro de la transacción del llamador); `IsEditable(caso, formulario)` = `EsperaTercero` y formulario no `Enviado`. Es lo único que lee el caso de la sesión en las historias siguientes (FR-006)
- [X] T014 [P] Crear `PO/shared/api.ts` copiando `apps/backoffice/src/shared/api.ts` con base `/api/portal`, soporte de `FormData` (sin `Content-Type` manual) y `ApiError` con `status`, `title`, `errors` y `extras` (para `codigoInvalidado`); un 401 invoca `setUnauthorizedHandler`; exportar desde `PO/shared/index.ts`. Comentario `// shortcut: copia de apps/backoffice/src/shared/api.ts (~35 líneas); mover a packages/api-client al tercer uso`. Test `PO/shared/api.test.ts` (JSON, FormData, 401 llama al handler)
- [X] T015 Extender `CA/CasosOptions.cs` con `AccesoRateLimitPerMinute = 10` y `CA/CasosModule.cs`: registrar la política `portal-acceso` con `services.Configure<RateLimiterOptions>(o => o.AddPolicy("portal-acceso", ...))` (ventana fija de 1 minuto por IP, límite de `CasosOptions`, mismo esquema que `RateLimitPolicies.Anonymous` en `ID/IdentidadModule.cs`) y crear el subgrupo `surfaces.Portal.MapGroup("")` en `MapCasosEndpoints` donde cada slice de las historias se mapea. Depende de T006
- [X] T016 [P] Crear `IT/Support/PortalSeed.cs`: `CreateCasoAsync(connectionString, TipoPersona persona, ...)` que crea el tercero (con `ITerceroRegistry.GetOrCreateForOnboardingAsync` en una transacción, como `IT/Casos/CreateSolicitudTests.cs`), el `Caso` en `EsperaTercero`, una `Solicitud` con `ContactoEmail` y un `EnlacePortal` con un token conocido (`PortalToken.Generate()` + `Hash`, `VenceUtc` configurable) y devuelve `(casoId, terceroId, token, email)`. Crear también `IT/Support/SampleFiles.cs` que genera en código `Pdf()`, `Jpeg()`, `Png()`, `PdfCifrado()` (con `/Encrypt` en el trailer, como uno con contraseña de propietario: debe aceptarse), `PdfTruncado()`, `TextoRenombrado()` y `Grande(bytes)` (PDF válido relleno); ver nota de [quickstart.md](quickstart.md)

**Checkpoint**: la solución compila; `dotnet test` sigue verde.

---

## Phase 3: User Story 1 — Ingresar al portal con enlace y código (Priority: P1) 🎯 MVP

**Goal**: el tercero abre su enlace, recibe un código en su correo, ingresa y ve su caso; todo aislado por sesión, con límites de abuso y bitácora.

**Independent Test**: con un caso sembrado en `EsperaTercero`, pedir código con el token, leerlo del `FakeEmailTransport`, ingresar y obtener `GET /api/portal/caso` con la razón social.

### Tests (escribir primero, deben fallar)

- [X] T017 [P] [US1] Tests unitarios en `UT/Casos/AccessCodeTests.cs` (6 dígitos con ceros a la izquierda, hash dependiente del enlace, comparación en tiempo constante) y `UT/Casos/EmailMaskTests.cs` (`contacto@example.com` → `c******@example.com`, local de un carácter, dominio intacto) (R3, US1-1)
- [X] T018 [P] [US1] Tests en `IT/Portal/AccesoTests.cs` con `PortalSeed`: enlace válido → 202 con correo enmascarado y código de 6 dígitos en el transporte falso (solo el hash en `CodigosIngreso`); código correcto → 204 + cookie `dlg_pt` `HttpOnly`/`Secure`/`SameSite=Strict`/`Path=/api/portal`; 5 incorrectos → 401 con `codigoInvalidado = true` y el correcto después ya no sirve; código vencido a los 10 min + 1 s con `FakeTimeProvider` → 401; reenvío invalida el anterior; token inexistente, enlace vencido y caso en `EnVerificacion` → mismo 404 y mismo mensaje; SMTP que falla (`FailNext`) → 503 y el código emitido queda `Invalidado`; cuerpo inválido (token vacío, código no numérico) → 400 (US1-1..5, FR-001/002/004)
- [X] T019 [P] [US1] Tests en `IT/Portal/AccesoLimitesTests.cs`: el 6.º código en una hora → 429 con `Retry-After` y `AccessDenied` en la bitácora; 10 fallidos repartidos en dos códigos → 429 por bloqueo y `AccountLocked`; a los 31 min vuelve a ingresar; un ingreso exitoso pone `FallidosConsecutivos` en 0; dos `POST /acceso` simultáneos con el mismo código incorrecto cuentan 2 fallidos (UPDLOCK); límite por IP de `portal-acceso` → 429, con una factory propia que deja el valor real de 10/min (la compartida lo sube en T007) (US1-5, FR-003, R4)
- [X] T020 [P] [US1] Tests en `IT/Portal/SesionTests.cs`: `GET /caso` devuelve razón social, CUIT, tipo de persona, estado y `pendientes`; sin cookie → 401; sesión sin actividad 30 min → 401 y con actividad a los 29 min sigue (`FakeTimeProvider`); cookie de portal en `/api/backoffice/*` → 401 y token de backoffice en `/api/portal/caso` → 401; **dos terceros**: la sesión del segundo ve su propio caso y nunca el del primero (no existe parámetro que lo permita); el JSON de `GET /caso` solo contiene las claves de `CasoPortal` (sin hallazgos, puntajes ni notas) (US1-2/6, FR-005/006, SC-005)
- [X] T021 [P] [US1] Tests en `IT/Portal/AccesoAuditTests.cs`: ingreso exitoso → `LoginSucceeded` sobre `EnlacePortal` con autor `tercero:{id}`; fallido → `LoginFailed` con `Motivo` (`CodigoIncorrecto`, `CodigoVencido`, `SinCodigo`); código enviado → `Created` sobre `CodigoIngreso`; ninguna fila de `audit.AuditEvents` contiene el token, el código ni la URL (US1-7, FR-021)
- [X] T022 [US1] (depende de T023: usa la entidad `PortalSession`; hasta entonces el proyecto `IT` no compila) Actualizar `IT/Api/EndpointSurfaceTests.cs` sumando `/api/portal/acceso/codigo` y `/api/portal/acceso` a la lista `Anonymous`; reemplazar `TestUsers.CreatePortalToken` en `IT/Support/TestUsers.cs` por una versión que inserta una `PortalSession` real (el esquema Portal ahora exige `sid` en la base) y actualizar sus usos (buscar con `grep -rn CreatePortalToken tests/`), incluido `IT/Api/AuthenticationSurfaceTests.cs`

### Implementación

- [X] T023 [US1] En `ID/Persistence/Entities.cs` y `ID/Persistence/EntityConfigurations.cs` agregar `PortalSession` según [data-model.md](data-model.md) (índice por `CasoId`) con su `DbSet` en `IdentidadDbContext`; generar la migración `AddPortalSessions` (`--project ID --context IdentidadDbContext --output-dir Persistence/Migrations`)
- [X] T024 [US1] En `ID`: `IdentidadOptions` + `PortalSessionIdleMinutes = 30` y `PortalSessionAbsoluteHours = 8`; `Authentication/TokenIssuer.cs` recibe el esquema (`Issue(scheme, sub, sessionId, expiresUtc)`) y se actualiza su único llamador actual, `ID/Features/Login/Login.cs` (`Issue(AuthSchemes.Backoffice, user.Id.ToString(), session.Id, session.ExpiresAtUtc)`; el token del backoffice no cambia); `Authentication/SessionCookie.cs` agrega `PortalName = "dlg_pt"` y `PortalOptions` (`Path=/api/portal`); `Authentication/AuthenticationSetup.cs` agrega `PortalEvents()` al esquema Portal: lee la cookie si no hay `Authorization`, en `OnTokenValidated` carga `PortalSessions` por `sid`, exige que `sub == "tercero:{TerceroId}"`, no revocada, no vencida y con actividad en los últimos 30 min, reemplaza claims por `caso` y `tercero` desde la base y actualiza `LastActivityUtc` como mucho una vez por minuto (copiar `BackofficeEvents`). Crear `Authentication/PortalSignIn.cs` (`IPortalSignIn`: inserta la sesión con `db` sobre la transacción del llamador — contexto efímero como `TerceroRegistry` —, firma el JWT, escribe la cookie y deja la request actuando como `tercero:{id}` con el mismo mecanismo que `ActAs` en `ID/Features/Shared/Shared.cs`) y `HttpPortalActor.cs` (`IPortalActor` desde los claims). Registrar ambos como scoped en `AddIdentidadModule`. Depende de T008, T023
- [X] T025 [P] [US1] Crear `CA/Features/Shared/AccessCode.cs` (`Generate()` con `RandomNumberGenerator.GetInt32(0, 1_000_000)` formateado a 6 dígitos, `Hash(enlaceId, código)` SHA-256, `Matches` con `CryptographicOperations.FixedTimeEquals`) y `CA/Features/Shared/EmailMask.cs` (`Mask(email)`); `CA/Features/Shared/EnlaceAcceso.cs`: `FindValidAsync(db, token, now)` (por `TokenHash`, `VenceUtc > now`, caso en `EsperaTercero`; devuelve el enlace y el caso, o `Invalid`/`Locked` si `BloqueadoHastaUtc > now`) con `UPDLOCK` sobre la fila del enlace. Depende de T010, T017
- [X] T026 [US1] Crear el slice `CA/Features/SolicitarCodigo/SolicitarCodigo.cs`: `POST /acceso/codigo` `.AllowAnonymous().RequireRateLimiting("portal-acceso")`; validador (`Token` no vacío, ≤ 100); en una transacción: enlace válido o 404 con el mensaje único de [portal-api.openapi.yaml](contracts/portal-api.openapi.yaml); bloqueado → 429; ≥ 5 códigos en la última hora → 429 + `AccessDenied` (`Motivo = LimiteCodigos`); invalida los `Vigente`, crea el nuevo (10 min) y registra `Created`/`CodigoIngreso`; **después** de confirmar envía el correo con `IEmailTransport` (asunto y cuerpo sin la URL del enlace) y, si falla, invalida el código recién emitido y responde 503; 202 con `correo` enmascarado y `venceEnMinutos`. Comentario `// shortcut: envío inline como ForgotPassword; pasar a cola si el SMTP deja de ser local`. Depende de T011, T015, T025
- [X] T027 [US1] Crear el slice `CA/Features/Ingresar/Ingresar.cs`: `POST /acceso` anónimo con la misma política; validador (`Token`, `Codigo` = 6 dígitos); en una transacción con el enlace bloqueado (`UPDLOCK`): inválido → 404; bloqueado → 429; sin código `Vigente` o vencido → 401 `codigoInvalidado = true` + `LoginFailed`; código incorrecto → `IntentosFallidos++` (al 5.º `Invalidado`, `codigoInvalidado = true`), `FallidosConsecutivos++` (al 10.º `BloqueadoHastaUtc = ahora + 30 min`, contador a 0, `AccountLocked`), `LoginFailed`; correcto → código `Usado`, `FallidosConsecutivos = 0`, `IPortalSignIn.SignInAsync(casoId, terceroId, tx)`, `LoginSucceeded` y 204. Depende de T024, T026
- [X] T028 [US1] Crear el slice `CA/Features/GetCasoPortal/GetCasoPortal.cs`: `GET /caso` con la policy del grupo Portal; carga el caso con `CasoPortalQueries.LoadSessionCasoAsync` (tipo de persona con `ITerceroRegistry.FindByCuitAsync`), devuelve `CasoPortal` (`editable`, `pendientes` = vacío por ahora; US2/US3 lo completan). Mapear los tres slices en `CA/CasosModule.cs`. Depende de T013, T027
- [X] T029 [P] [US1] Crear `PO/shared/acceso.ts` (guarda el token en `sessionStorage` con try/catch, lo lee y lo borra) y `PO/features/acceso/AccesoPage.tsx`: al montar lee `#t=`, lo guarda y limpia el fragmento con `history.replaceState`; pide el código automáticamente **una sola vez por token y pestaña** (marca `codigoPedido` en `sessionStorage` + `useRef` contra el doble montaje de StrictMode; recargar o volver tras una sesión vencida no reenvía, muestra el campo y "Reenviar código"); sin token en el fragmento ni en `sessionStorage` muestra "Abrí el enlace que recibiste por correo" sin llamar a la API (research R5); muestra "Te enviamos un código a c***@…", campo de 6 dígitos (`inputMode="numeric"`, `autoComplete="one-time-code"`), "Reenviar código" con cuenta regresiva de `Retry-After`, mensajes en español para 404 ("El enlace no es válido o venció…"), 401 ("El código no es válido", y al `codigoInvalidado` pide uno nuevo), 429 y 503; al ingresar navega a `/caso`. Test `PO/features/acceso/AccesoPage.test.tsx` (limpia el fragmento, un solo envío con StrictMode, recarga sin reenvío, sin token, enmascara, reenvío, cada error). Depende de T014
- [X] T030 [US1] En `PO/app/router.tsx` agregar las rutas `/acceso` y `/caso` (`beforeLoad` de `/caso` redirige a `/acceso` ante 401 con `setUnauthorizedHandler`) y crear `PO/features/caso/CasoPage.tsx` mínima (razón social, CUIT, estado en texto legible, lista de `pendientes`) con test; `HomePage` redirige a `/acceso`. Depende de T029

**Checkpoint**: US1 funciona sola: enlace + código → sesión → inicio del caso.

---

## Phase 4: User Story 3 — Subir documentos (Priority: P1)

**Goal**: el tercero ve los documentos requeridos para su tipo de persona y sube, reemplaza o elimina PDF/JPG/PNG verificados, cifrados y dentro del tamaño máximo que fija un administrador.

**Independent Test**: con una sesión de portal, subir un PDF y una imagen válidos y verlos como vigentes; un `.txt` renombrado, un GIF, un PDF cifrado y un archivo grande se rechazan.

### Tests (escribir primero, deben fallar)

- [X] T031 [P] [US3] Tests unitarios en `UT/Documentos/ContentInspectorTests.cs` (PDF/JPEG/PNG válidos con `SampleFiles`; `.txt` con extensión `.pdf`, GIF, PDF sin `%%EOF`, PDF con `/Encrypt` **aceptado** como `application/pdf`, JPEG sin `FF D9`, PNG sin `IEND`, vacío) y `UT/Documentos/DocumentCipherTests.cs` (ida y vuelta, nonce distinto por cifrado, tag alterado lanza) (R8, R9)
- [X] T032 [P] [US3] Tests en `IT/Documentos/ConfiguracionTests.cs`: `GET` devuelve 10 MB por defecto; `PUT` con 2 cambia el valor y deja `Updated` en la bitácora; 0 y 51 → 400; sin rol Administrador → 403; agregar las filas `GET`/`PUT /api/backoffice/documentos/configuracion` (`AdminOnly`) a `IT/Identidad/PermissionMatrixTests.cs` (US3-6, FR-014)
- [X] T033 [P] [US3] Tests en `IT/Portal/DocumentosTests.cs` con sesión de portal: `GET /caso/documentos` lista los requeridos de la persona con obligatoriedad, máximo y formatos; subir PDF, JPG y PNG válidos → 200 con `contentType` detectado, `nombreOriginal` y `cargadoUtc`; `.txt` renombrado y GIF → 400 con "Formatos aceptados: PDF, JPG, PNG"; PDF cifrado con contraseña de propietario → 200; PDF truncado → 400 "no se puede leer"; archivo mayor al máximo → 413 con el máximo en MB; tras bajar el máximo a 2 MB un archivo de 3 MB da 413 y los ya cargados siguen; tipo que no corresponde a la persona → 400; reemplazar deja un solo vigente (el anterior `Baja = Reemplazado`); eliminar → 204 y `Baja = Eliminado`; eliminar inexistente → 404; el segundo tercero no ve ni toca los documentos del primero; `Documentos.CargadoPor` = `tercero:{id}` (US3-1..6, FR-013..017, SC-005/006)
- [X] T034 [P] [US3] Tests en `IT/Documentos/CifradoEnReposoTests.cs`: `documentos.DocumentoContenidos.Cifrado` difiere de los bytes subidos y se descifra a los mismos; `Sha256` del documento coincide con el del archivo; en `IT/Portal/DocumentosAuditTests.cs`: cada carga, reemplazo y eliminación deja `Created`/`Updated` sobre `Documento` con tipo, nombre, tamaño y hash, y ninguna fila de bitácora ni log capturado contiene bytes del archivo (FR-018, FR-021)

### Implementación

- [X] T035 [US3] En `DO/Persistence` crear entidades `Documento`, `DocumentoContenido` y `Configuracion` con sus configuraciones según [data-model.md](data-model.md) (índice único filtrado `(CasoId, Tipo) WHERE BajaUtc IS NULL`, `Configuracion` con `CHECK (Id = 1)` y `HasData` de 10 MB), `DbSet` en `DocumentosDbContext` y migración `AddDocumentos` (`--project DO --context DocumentosDbContext --output-dir Persistence/Migrations`)
- [X] T036 [P] [US3] Crear `DO/Features/Store/ContentInspector.cs` (`Detect(Stream)` → `application/pdf` | `image/jpeg` | `image/png` | `Ilegible` | `NoPermitido`; PDF: `%PDF-` al inicio, `%%EOF` en los últimos 1024 bytes, **sin** rechazar por `/Encrypt` (las constancias con contraseña de propietario son válidas; research R9); JPEG y PNG por firma y cierre; lee solo cabecera y cola, costo fijo) con comentario `// shortcut: no parsea el cuerpo; la extracción con PdfPig del worker detectará PDF dañados por dentro o con contraseña de apertura` (R9). Depende de T031
- [X] T037 [P] [US3] Crear `DO/Features/Store/DocumentCipher.cs` (AES-256-GCM con `AesGcm`, nonce de 12 bytes aleatorio, tag de 16; clave = `SHA-256(Documentos:EncryptionKey)` leída de forma perezosa con `GetRequired` para que worker y MigrationService arranquen sin ella) con comentario `// shortcut: una sola clave sin KeyId; agregar la columna y el anillo de claves al necesitar rotar` (R8). Depende de T031
- [X] T038 [US3] Implementar `DO/Features/Store/DocumentStore.cs` (`IDocumentStore`): `PutAsync` verifica tamaño contra la configuración y el contenido con `ContentInspector`, calcula SHA-256, cifra y guarda con un `DocumentosDbContext` efímero sobre `transaction.Connection` (patrón de `TerceroRegistry`, con `TimestampsInterceptor`), da de baja el vigente previo como `Reemplazado` y registra la bitácora con `IAuditLog.RecordAsync`; `RemoveAsync` y `ListVigentesAsync` según el contrato; `GetTamanoMaximoBytesAsync` sin transacción. Nunca registra el contenido. Depende de T009, T035, T036, T037
- [X] T039 [US3] Crear el slice `DO/Features/Configuracion/Configuracion.cs`: `GET` y `PUT /documentos/configuracion` en `surfaces.Backoffice` con `RequireAuthorization(AuthPolicies.Administrator)`; validador `TamanoMaximoMb` entero 1–50; `PUT` guarda en bytes y registra `Updated` sobre `ConfiguracionDocumentos`. Completar `DO/DocumentosModule.cs`: `AddDocumentosModule` registra `IDocumentStore`, cifrado, validadores y handlers por convención (copiar `AddFeatureServices` de `CA/CasosModule.cs`) y `MapDocumentosEndpoints` mapea la configuración. Depende de T035, T038
- [X] T040 [US3] Crear los slices de Casos `CA/Features/ListDocumentosPortal/ListDocumentosPortal.cs` (`GET /caso/documentos`: catálogo de `DocumentosRequeridos` de la persona + `IDocumentStore.ListVigentesAsync` + máximo vigente), `CA/Features/SubirDocumento/SubirDocumento.cs` (`PUT /caso/documentos/{tipo}` multipart con `[RequestSizeLimit(55 MB)]` y `.DisableAntiforgery()` (comentario: la cookie `SameSite=Strict` cubre CSRF, como en el backoffice); `SubirDocumentoRequest(string Tipo, IFormFile? Archivo)` con validador FluentValidation (archivo presente y no vacío, nombre ≤ 255) y el `tipo` contra el catálogo de la persona; tipo real y tamaño los verifica `IDocumentStore`; lee el archivo a memoria cortando primero por `IFormFile.Length` contra el máximo → 413; abre la transacción, `LockCasoAsync`, exige caso editable (si no 409) y llama `PutAsync`; mapea `MotivoRechazo` a 400/413 con los mensajes en español) y `CA/Features/EliminarDocumento/EliminarDocumento.cs` (`DELETE`, mismo bloqueo y edición; 404 si no hay vigente). Mapearlos en `CA/CasosModule.cs`. Depende de T013, T038
- [X] T041 [US3] Actualizar `CA/Features/GetCasoPortal/GetCasoPortal.cs` para que `pendientes` incluya "Subir: {nombre}" por cada documento obligatorio sin cargar (consulta `IDocumentStore.ListVigentesAsync`). Depende de T040
- [X] T042 [P] [US3] Crear `PO/features/caso/DocumentosSection.tsx` con `react-dropzone` (R12): una zona por documento requerido con `useDropzone({ accept, maxFiles: 1, multiple: false, maxSize })`, estado cargado (nombre, tamaño, fecha, "Reemplazar", "Eliminar"), `fileRejections` con mensajes en español ("Formatos aceptados: PDF, JPG, PNG", "El máximo es N MB"), error de la API mostrado por documento, barra de progreso con `fetch` + `FormData`, y modo solo lectura (sin reemplazar ni eliminar). El `<input>` de `getInputProps()` permite cámara en el celular; objetivos táctiles de al menos 44 px. Test `PO/features/caso/DocumentosSection.test.tsx` (archivo válido sube, GIF rechazado en el cliente, archivo grande rechazado, reemplazo, eliminación, solo lectura). Depende de T014
- [X] T043 [US3] Integrar `DocumentosSection` en `PO/features/caso/CasoPage.tsx` con TanStack Query (`GET /caso/documentos`, invalidar tras subir o eliminar) y actualizar su test. Depende de T030, T042

**Checkpoint**: US3 funciona sola: documentos subidos, verificados, cifrados y administrables.

---

## Phase 5: User Story 2 — Completar el formulario y enviarlo (Priority: P1)

**Goal**: el tercero completa el formulario según su tipo de persona y lo envía; con todo válido y los documentos obligatorios, el caso pasa a "En verificación" y el formulario queda en solo lectura.

**Independent Test**: una persona jurídica y una humana, cada una con sus documentos obligatorios, envían su formulario y sus casos pasan a `EnVerificacion`; con datos faltantes o inválidos el envío devuelve un error por campo.

### Tests (escribir primero, deben fallar)

- [X] T044 [P] [US2] Tests unitarios en `UT/Casos/EnviarFormularioValidatorTests.cs` con la tabla de [data-model.md](data-model.md): jurídica completa pasa; faltan obligatorios por persona; `beneficiarios` en humana → "no corresponde"; suma 100,01 % → error en `beneficiarios`; porcentaje 0 o > 100; sin beneficiarios ni `sinBeneficiarios` → error; ambos a la vez → error; `sinBeneficiarios.explicacion` < 20 caracteres; PEP sin cargo u organismo (propio, de beneficiario y de representante); fecha de constitución futura; edad fuera de 18–120; país fuera de ISO; más de 20 beneficiarios (US2-3/4, FR-008/009/010)
- [X] T045 [P] [US2] Tests en `IT/Portal/EnvioFormularioTests.cs`: `GET /caso/formulario` sin guardados devuelve `Borrador` vacío; envío completo de jurídica y de humana con documentos → 200, caso `EnVerificacion` con `EstadoDesdeUtc`, formulario `Enviado` con `EnviadoPor = tercero:{id}`; envío con un documento obligatorio faltante → 400 `documentos` y el caso no cambia; datos inválidos → 400 con rutas anidadas en camelCase; después del envío `PUT`/`DELETE` de documentos y reenvío → 409, `GET /caso/formulario` y `GET /caso` siguen respondiendo (`editable = false`, `pendientes` vacío) y `POST /acceso/codigo` → 404; envío y carga simultáneos: o entra el documento antes o la carga recibe 409, nunca ambos; bitácora con `Updated` sobre `FormularioTercero` (`Estado`) y `Caso` (`Estado`), autor y fecha; el segundo tercero no puede leer ni enviar el formulario del primero (US2-1..7, FR-006/011/012/021, SC-004/005)

### Implementación

- [X] T046 [P] [US2] Crear `CA/Features/Formulario/FormularioDatos.cs` con los records tipados de [data-model.md](data-model.md) (`FormularioDatos`, `Domicilio`, `Beneficiario`, `Pep`, `Documento`, `Representante`, `SinBeneficiarios`, enums `CondicionIva` y `TipoDocumentoPersonal`), opciones de `System.Text.Json` compartidas (camelCase, enums como texto, `null` omitido) y `CA/Features/Shared/JsonDiff.cs` (`Diff(old, new)` aplana ambos JSON a `ruta → valor` y devuelve solo las rutas distintas como `AuditChange`) con tests en `UT/Casos/JsonDiffTests.cs` (rutas con índice `beneficiarios[0].porcentaje`, agregado, quitado, sin cambios) (R6, R11)
- [X] T047 [US2] Crear `CA/Features/Formulario/FormularioEnvioValidator.cs`: `AbstractValidator<FormularioDatos>` con `TipoPersona` y fecha de hoy por constructor (reglas de envío de [data-model.md](data-model.md)); el handler lo instancia con la persona del caso. Registro por convención no aplica (lleva parámetros): crearlo con `new` en el handler. Depende de T044, T046
- [X] T048 [US2] Crear el slice `CA/Features/GetFormulario/GetFormulario.cs`: `GET /caso/formulario` devuelve `estado`, `datos`, `version` (rowversion en base64), `guardadoUtc` y `enviadoUtc`; sin fila → `Borrador` con `datos` vacío y `version = null`. Depende de T013, T046
- [X] T049 [US2] Crear el slice `CA/Features/EnviarFormulario/EnviarFormulario.cs`: `POST /caso/formulario/envio`; en una transacción: `LockCasoAsync`, 409 si no es editable, validar `datos` con `FormularioEnvioValidator`, `IDocumentStore.ListVigentesAsync(casoId, tx)` y error `documentos` con un mensaje por obligatorio que falta, guardar el JSON (crear o actualizar la fila, `Estado = Enviado`, `EnviadoUtc`, `EnviadoPor = IActorContext.ActorId`), pasar `Caso.Estado` a `EnVerificacion` con `EstadoDesdeUtc`, y registrar `Updated` sobre `FormularioTercero` (diff de `JsonDiff` + `Estado`) y sobre `Caso` (`Estado`) en la misma transacción. El cuerpo es `EnviarFormularioRequest(FormularioDatos Datos)`, sin `version` (contrato). Mapear con `GetFormulario` en `CA/CasosModule.cs`. Depende de T038, T047, T048
- [X] T050 [US2] Actualizar `CA/Features/GetCasoPortal/GetCasoPortal.cs` para que `pendientes` incluya "Completar el formulario" mientras sea editable y `editable` considere el formulario enviado. Depende de T049
- [X] T051 [P] [US2] Crear `PO/shared/paises.ts` (lista de códigos ISO 3166-1 alfa-2 con nombres de `Intl.DisplayNames(['es'], { type: 'region' })`) y `PO/features/caso/schema.ts` con el esquema Zod de `FormularioDatos` solo para UX (mismos nombres de campo que la API; obligatoriedad según tipo de persona). Test `PO/features/caso/schema.test.ts`. Depende de T014
- [X] T052 [US2] Crear `PO/features/caso/FormularioSection.tsx` con React Hook Form + `zodResolver`: secciones datos fiscales (CUIT y razón social precargados y deshabilitados), domicilio fiscal y legal ("igual al fiscal"), actividad, beneficiarios finales con `useFieldArray` (solo jurídica; porcentaje, PEP y "No hay beneficiarios identificables" con explicación), representantes, declaración PEP (cargo y organismo aparecen si es PEP) y origen de fondos; botón "Enviar" con diálogo de confirmación; errores de la API por campo mapeados a `setError` con las rutas del servidor; tras el envío todo en solo lectura con aviso "Recibimos tu información". Diseño de una columna en pantallas chicas. Test `PO/features/caso/FormularioSection.test.tsx` (secciones por persona, beneficiarios agregar/quitar, errores del servidor, solo lectura). Depende de T051
- [X] T053 [US2] Integrar `FormularioSection` en `PO/features/caso/CasoPage.tsx` (`GET /caso/formulario`, `POST /caso/formulario/envio`, invalidar `caso` y `documentos` tras enviar) y actualizar su test. Depende de T043, T052

**Checkpoint**: US1, US3 y US2 funcionan juntas: el caso llega a "En verificación" desde el portal.

---

## Phase 6: User Story 4 — Guardar a medias y retomar (Priority: P2)

**Goal**: el tercero guarda el formulario incompleto, sale y vuelve a ingresar con el mismo enlace sin perder datos ni documentos; el portal avisa antes de perder cambios y si otra pestaña guardó.

**Independent Test**: guardar con campos obligatorios vacíos, ingresar de nuevo con un código nuevo y ver los mismos datos y documentos.

### Tests (escribir primero, deben fallar)

- [X] T054 [P] [US4] Tests unitarios en `UT/Casos/GuardarFormularioValidatorTests.cs`: vacío es válido; solo forma (fecha inválida, porcentaje fuera de 0–100, suma > 100 %, país fuera de ISO, longitudes) (FR-019, data-model)
- [X] T055 [P] [US4] Tests en `IT/Portal/GuardadoParcialTests.cs`: guardar con obligatorios vacíos → 200 con `version` y `guardadoUtc`; ingresar con otra sesión y código nuevo → `GET` devuelve los mismos datos y los documentos siguen; guardar con una `version` vieja → 200 y `sobrescrito = true` (gana el último); `PUT` tras el envío → 409; enlace vencido tras el guardado → no se puede ingresar; bitácora con un `Updated` sobre `FormularioTercero` por campo modificado (`ruta`, anterior, nuevo) y sin entrada si no cambió nada; el segundo tercero no puede guardar sobre el formulario del primero (US4-1/2/4, FR-019/021, SC-007)

### Implementación

- [X] T056 [US4] Crear `CA/Features/Formulario/FormularioGuardadoValidator.cs` (registrado por convención como `IValidator<GuardarFormularioRequest>`: solo forma, nada obligatorio) y el slice `CA/Features/GuardarFormulario/GuardarFormulario.cs`: `PUT /caso/formulario` con `GuardarFormularioRequest(FormularioDatos Datos, string? Version)`; en una transacción: `LockCasoAsync`, 409 si no es editable, upsert del JSON, `sobrescrito` = la `Version` recibida existe y no coincide con la guardada, `GuardadoUtc`, y `Updated` sobre `FormularioTercero` con `JsonDiff` (sin entrada si no cambia nada); responde la nueva `version`. Mapear en `CA/CasosModule.cs`. Depende de T046, T054
- [X] T057 [US4] En `PO/features/caso/FormularioSection.tsx` agregar "Guardar" (envía `PUT` con la última `version`, confirma "Guardado", avisa "Otra sesión guardó cambios y se sobrescribieron" si `sobrescrito`), `useBlocker` de TanStack Router y listener `beforeunload` mientras `formState.isDirty`, y deshabilitar ambos botones durante el guardado; actualizar `FormularioSection.test.tsx` (guardar con campos vacíos, aviso de salida, aviso de sobrescrito). Depende de T052, T056

**Checkpoint**: las cuatro historias funcionan de punta a punta.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T058 [P] Escribir `apps/portal/e2e/portal.spec.ts` (flujo crítico "carga de documentos en portal"): crear un caso por la API del backoffice con el solicitante de `E2E_REQUESTER_EMAIL`/`E2E_REQUESTER_PASSWORD`, leer el enlace en `MAILPIT_URL`, abrir `/acceso#t=…`, leer el código en Mailpit, completar persona jurídica, arrastrar y soltar los cuatro documentos obligatorios (`setInputFiles` sobre el input de cada zona), enviar y ver la confirmación; segundo test de persona humana en el proyecto `mobile` (sin sección de beneficiarios). Depende de T053, T057
- [X] T059 Agregar al job `e2e` de `.github/workflows/ci.yml` el paso `npm run e2e -w apps/portal` con `BASE_URL`, `MAILPIT_URL` y las credenciales del solicitante. Depende de T058
- [X] T060 [P] Crear `IT/Logging/PortalNoSecretsInLogsTests.cs` (patrón de `SolicitudNoSecretsInLogsTests.cs`): tras pedir código, ingresar con códigos buenos y malos, guardar, subir un archivo y enviar, ningún log capturado contiene el token del enlace, el código, el valor de la cookie ni bytes del archivo (FR-018/021)
- [X] T061 [P] Confirmar que `src/Hosts/Diligencia.Worker/Program.cs` y `src/Hosts/Diligencia.MigrationService/MigrationEntryPoint.cs` siguen arrancando con `ValidateOnBuild`: los handlers nuevos de Casos dependen de `IPortalSignIn`, `IPortalActor`, `IDocumentStore` e `IEmailTransport`; registrar lo que falte (`AddIdentidadModule`, `AddDocumentosModule`, `AddEmailTransport`) sin exigir `Documentos:EncryptionKey` ni claves JWT en esos hosts (R8, lectura perezosa)
- [X] T062 [P] Actualizar la nota de archivos de ejemplo en [quickstart.md](quickstart.md): `SampleFiles` genera los archivos en código (`IT/Support/SampleFiles.cs`), no hay `tests/Fixtures/archivos/`
- [X] T063 [P] Agregar a la tabla de FR-016 de `specs/002-backoffice-auth-rbac/spec.md` la fila "Configurar el tamaño máximo de documentos" con "Sí" solo para Administrador
- [X] T064 [P] Verificar `npm run lint` (el portal no importa `apps/backoffice`; `features/acceso` y `features/caso` solo importan `shared/` y `packages/`), `npm run typecheck`, `npm test` y `dotnet test --filter-not-trait "Category=Performance"` en verde, incluidos los tests de arquitectura
- [X] T065 Ejecutar todas las secciones de [quickstart.md](quickstart.md) con el AppHost (persona jurídica y humana, desde un celular o DevTools móvil, y el cambio de tamaño máximo) y corregir lo que falle; anotar si iOS entrega HEIC y cómo se ve el rechazo

---

## Dependencies & Execution Order

- **Setup (Ph.1)** → sin dependencias; todas en paralelo.
- **Foundational (Ph.2)** → depende de Setup; **bloquea** las historias. T008 y T010 antes de T013; T006 antes de T015; el resto en paralelo.
- **US1 (Ph.3)** → depende de Foundational. Es el MVP: sin sesión no hay portal.
- **US3 (Ph.4)** → depende de US1 (sesión de portal y `GET /caso`); no depende de US2.
- **US2 (Ph.5)** → depende de US1 y de US3 (T049 usa `IDocumentStore` y T041).
- **US4 (Ph.6)** → depende de US2 (comparte `FormularioSection`, `FormularioDatos` y `GetFormulario`).
- **Polish (Ph.7)** → al final; T058 necesita las cuatro historias.

### Dentro de cada historia

Tests primero (deben fallar), luego persistencia, contratos y slices, luego mapeo de endpoints, luego frontend.

## Parallel Examples

```text
Setup:       T001 T002 T003 T004 T005 T006 T007
Foundation:  T008 T009 T011 T012 T014 T016   |   T008 + T010 → T013   |   T006 → T015
US1 tests:   T017 T018 T019 T020 T021   (T022 tras T023)
US1 impl:    T023 → T022, T024   |   T025   (→ T026 → T027 → T028)   |   T029 → T030
US3 tests:   T031 T032 T033 T034
US3 impl:    T035 → T038 → T039/T040 → T041 → T043   |   T036 T037   |   T042
US2 tests:   T044 T045
US2 impl:    T046 → T047 → T049 → T050   |   T048   |   T051 → T052 → T053
US4 tests:   T054 T055
US4 impl:    T056 → T057
Polish:      T058 T060 T061 T062 T063 T064
```

## Implementation Strategy

1. **MVP = Setup + Foundational + US1**: el tercero abre su enlace, recibe el código e ingresa; ya valida la seguridad del acceso (SC-005) y habilita las demás historias.
2. **Incremento 2 = US3**: documentos subidos y verificados; reutilizable por la feature de verificación (RF-09/RF-14).
3. **Incremento 3 = US2**: formulario y envío; con esto el caso llega a "En verificación" (SC-004), el objetivo del portal.
4. **Incremento 4 = US4**: guardado parcial y aviso de salida; reduce abandonos.
5. Validar con [quickstart.md](quickstart.md) al cerrar cada fase.

---

## Phase 8: Convergence

**Purpose**: trabajo restante detectado al comparar el código con spec, plan y constitución tras `/speckit-implement`.

- [X] T066 Resolver explícitamente la tensión con el Principio VI de la constitución ("la API MUST NOT ejecutar inline una tarea cuyo costo dependa del tamaño de un documento"): `DocumentStore.PutAsync` (`DO/Features/Store/DocumentStore.cs`) lee, hashea (SHA-256) y cifra (AES-GCM) hasta 50 MB dentro de la request; el plan (R8) lo justifica como almacenamiento de E/S y no como procesamiento, pero la constitución no distingue. Decidir y dejarlo escrito: o bien enmendar el Principio VI para aclarar que recibir/hashear/cifrar/guardar queda fuera del alcance de "procesamiento pesado" (con su rationale), o bien mover el cifrado y hash al worker; en ambos casos actualizar la fila "VI" del Constitution Check de `plan.md` según corresponda per Constitution VI (contradicts)
- [X] T067 Registrar tamaño y SHA-256 del documento también en los eventos `Updated` de reemplazo y eliminación en `DO/Features/Store/DocumentStore.cs` (hoy solo llevan `Tipo`, `NombreOriginal` y `Baja`) y ajustar `IT/Portal/DocumentosAuditTests.cs` para exigirlos en los tres eventos per FR-021 / research R11 (partial)
- [X] T068 Verificar con el host real (Kestrel, no TestServer, que ignora `[RequestSizeLimit]`) una carga del tamaño máximo configurable (50 MB, p. ej. en un test contra el AppHost o un test con `UseKestrel`) y corregir los límites (`MaxRequestBodySize`, `MultipartBodyLengthLimit`) si el rechazo o la aceptación no coinciden con el máximo vigente per FR-014 / research R10 (partial)
- [X] T069 Anotar en `quickstart.md` (sección de recorrido manual) el resultado de probar la carga desde un iPhone: si iOS entrega HEIC al elegir "archivo" y cómo se ve el rechazo con "Formatos aceptados: PDF, JPG, PNG"; si hay HEIC, evaluar aceptar conversión en el cliente o dejar el aviso per FR-022 / T065 (partial)
- [X] T070 Persistir la declaración "No hay beneficiarios identificables" aunque la explicación esté vacía al guardar (hoy `FormularioSection.tsx` omite el objeto vacío y al recargar el checkbox aparece desmarcado); enviar `sinBeneficiarios: { explicacion: "" }` al guardar y cubrirlo en `FormularioSection.test.tsx` per US4/AC2 / FR-008 (partial)
- [X] T071 Mostrar los errores de nivel lista del esquema Zod ("Agregá al menos un beneficiario", representantes) apenas el usuario intenta enviar, no solo cuando el resto del formulario es válido, ajustando `PO/features/caso/schema.ts` y `FormularioSection.tsx` con su test per US2/AC4 (partial)
- [X] T072 Evitar que un 401 a mitad de edición con cambios sin guardar dispare el bloqueador de salida con un aviso confuso: ante sesión vencida mostrar un mensaje claro ("Tu sesión venció") y permitir salir a `/acceso` sin el diálogo de cambios, en `PO/app/router.tsx` y `FormularioSection.tsx` con test per FR-005 / FR-020 (partial)
- [X] T073 Revisar el registro por convención de `FormularioFormaValidator` como `IValidator<FormularioDatos>` (`CA/Features/Formulario/FormularioFormaValidator.cs`), que ningún slice resuelve: quitarlo del auto-registro o justificar su uso per plan: Features/Formulario (unrequested)
