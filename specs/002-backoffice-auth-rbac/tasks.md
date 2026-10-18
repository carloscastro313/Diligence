---

description: "Tasks: Autenticación y control de acceso por roles (RBAC) del backoffice"
---

# Tasks: Autenticación y control de acceso por roles (RBAC) del backoffice

**Input**: `specs/002-backoffice-auth-rbac/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: INCLUIDOS. La constitución exige al menos un test de backend por regla de negocio o permiso nuevo, y SC-003 pide la matriz endpoint × rol.

**Organization**: por historia de usuario. US1 (P1) ingreso, US2 (P1) gestión de usuarios y áreas, US3 (P3) protección de la cuenta. `ResetPassword` (consumido por el enlace de invitación del alta) va en US2; `ForgotPassword` y el bloqueo, en US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: se puede ejecutar en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: US1–US3 según la spec
- Rutas relativas a la raíz del repo. `ID` = `src/Modules/Identidad/Diligencia.Modules.Identidad`; `IT` = `tests/Diligencia.IntegrationTests`; `UT` = `tests/Diligencia.UnitTests`; `BO` = `apps/backoffice/src`. Todo tipo nuevo en Identidad es `internal`.

---

## Phase 1: Setup

**Purpose**: dependencias y configuración compartida

- [X] T001 [P] Agregar `BCrypt.Net-Next` a `Directory.Packages.props` y como `PackageReference` en `src/Modules/Identidad/Diligencia.Modules.Identidad/Diligencia.Modules.Identidad.csproj`
- [X] T002 [P] Agregar `react-hook-form`, `zod` y `@hookform/resolvers` a `apps/backoffice/package.json` y ejecutar `npm install` en la raíz
- [X] T003 [P] Agregar a `src/Modules/Auditoria/Diligencia.Modules.Auditoria.Contracts/AuditContracts.cs` los valores `LoginSucceeded, LoginFailed, LoggedOut, AccountLocked, PasswordResetRequested, PasswordSet, AccessDenied` al final de `AuditAction` (ver [contracts/module-contracts.md](contracts/module-contracts.md))
- [X] T004 [P] Agregar `UserDirectoryReader = "UserDirectoryReader"` a `AuthPolicies` en `src/Modules/Identidad/Diligencia.Modules.Identidad.Contracts/IdentityContracts.cs`
- [X] T005 [P] Crear `IdentidadOptions` (sección `Identidad`: `SessionIdleMinutes=30`, `SessionAbsoluteHours=8`, `MaxFailedLogins=5`, `LockoutMinutes=15`, `ResetTokenMinutes=60`, `InvitationTokenHours=72`, `AuthRateLimitPerMinute=10`, `BackofficeBaseUrl` obligatoria, `BootstrapAdmin:{Email,Name,Area}`) en `ID/IdentidadOptions.cs` (R12)

---

## Phase 2: Foundational (bloquea todas las historias)

**Purpose**: modelo de datos, sesión validada por request, policies y helpers de test

**⚠️ CRITICAL**: ninguna historia empieza antes de terminar esta fase

- [X] T006 [P] Crear entidades `Area`, `User`, `UserRole`, `Session`, `PasswordToken` (con `ITimestamped` en `Area` y `User`) y sus `IEntityTypeConfiguration` (índices únicos de `Areas.Name`, `Users.Email`, `PasswordTokens.TokenHash`; `CHECK` de `UserRoles.Role` contra `BackofficeRoles`; `PasswordHash varchar(60)`) en `ID/Persistence/Entities.cs` y `ID/Persistence/EntityConfigurations.cs` según [data-model.md](data-model.md)
- [X] T007 Registrar los `DbSet` y las configuraciones en `ID/Persistence/IdentidadDbContext.cs` y generar la migración `AddUsersAndSessions` (`dotnet ef migrations add AddUsersAndSessions --project ID --context IdentidadDbContext --output-dir Persistence/Migrations`)
- [X] T008 [P] Implementar `PasswordHashing` (`Hash` BCrypt factor 12, `Verify`, `VerifyDummy` contra hash fijo, constante de longitud 12–64 y ≤ 72 bytes UTF-8) en `ID/Authentication/PasswordHashing.cs` (R3, R4)
- [X] T009 [P] Implementar `OpaqueToken` (32 bytes de `RandomNumberGenerator` en base64url + `Hash` SHA-256) en `ID/Authentication/OpaqueToken.cs` (R6), con su test unitario (longitud, base64url, no se repite, mismo token → mismo hash) en `UT/Identidad/OpaqueTokenTests.cs`
- [X] T010 [P] Implementar `TokenIssuer` (crea el JWT del esquema `Backoffice` con `sub`, `sid`, `exp` = `ExpiresAtUtc`, usando emisor/audiencia/primera clave de `Authentication:Schemes:Backoffice`) en `ID/Authentication/TokenIssuer.cs` (R1)
- [X] T011 Extender `ID/Authentication/AuthenticationSetup.cs` para el esquema `Backoffice`: `OnMessageReceived` toma el token de la cookie `dlg_bo` si no hay header `Authorization`; `OnTokenValidated` carga `Session`+`User`+roles por PK, hace `context.Fail` si la sesión está revocada, vencida (absoluta o 30 min de inactividad) o el usuario desactivado, reemplaza los claims `role` por los de la base y actualiza `LastActivityUtc` solo si pasó más de 60 s; todo con `TimeProvider` (R1, R2). Depende de T006–T007, T005
- [X] T012 [P] Agregar la policy `UserDirectoryReader` (esquema `Backoffice`, roles Administrador o Auditor) en `ID/Authorization/PolicySetup.cs` (R11). Depende de T004
- [X] T013 [P] Implementar `AuditingAuthorizationResultHandler` (`IAuthorizationMiddlewareResultHandler`): ante `Forbidden` en `/api/backoffice` registra `AccessDenied` con `IAuditLog.RecordStandaloneAsync` (`EntityType="Endpoint"`, `EntityId` = path recortado a 128) y delega en el handler por defecto, en `ID/Authorization/AuditingAuthorizationResultHandler.cs` (R10, FR-020). Depende de T003
- [X] T014 Registrar en `ID/IdentidadModule.cs`: `IdentidadOptions` (validación `BackofficeBaseUrl` obligatoria en la API), `TimeProvider.System`, `TokenIssuer`, `PasswordHashing`, el handler de T013 y `AddRateLimiter` (política de ventana fija por IP con límite `IdentidadOptions.AuthRateLimitPerMinute`, nombre `auth-anonymous`; IP nula → partición `"unknown"`); en `src/Hosts/Diligencia.Api/Program.cs` agregar `app.UseRateLimiter()` entre `UseAuthentication` y `UseAuthorization`
- [X] T015 [P] Reemplazar `IT/Support/TestTokens.cs` por `IT/Support/TestUsers.cs`: helper que inserta `Area`+`User`+`UserRole`+`Session` en la base del test y devuelve el JWT (con `sid`) firmado con la clave de test del backoffice; conservar `For(surface)` y la creación de tokens de portal. Agregar `IT/Support/FakeEmailSender.cs` (guarda los correos en memoria para leer los enlaces de invitación/recuperación). Depende de T006–T007
- [X] T016 Ajustar el soporte y migrar los tests existentes. En `IT/Support/ApiFactory.cs` (y `DbApiFactory.cs`): fijar `Identidad:BackofficeBaseUrl=http://test.local`, `Identidad:AuthRateLimitPerMinute=100000` (en `TestServer` la IP remota es nula y todos los tests comparten la partición), reemplazar `IEmailSender` por `FakeEmailSender` y `TimeProvider` por un `FakeTimeProvider` accesible desde los tests. Migrar a `TestUsers` los usos de `TestTokens.Create(ActorSurface.Backoffice, ...)` en `IT/Api/AuthenticationSurfaceTests.cs`, `IT/Auditoria/QueryEventsTests.cs` e `IT/Logging/*.cs`. En `IT/Smoke/AppHostSmokeTests.cs` no setear la clave de firma del backoffice (ahora la fija el AppHost con `backoffice-signing-key` y gana sobre la variable del proceso) y reducir la verificación del backoffice a "sin token → 401" y health; la del portal queda igual. Depende de T015
- [X] T017 Extender `IT/Api/EndpointSurfaceTests.cs` para aceptar `AllowAnonymous` únicamente en `auth/login`, `auth/password/forgot` y `auth/password/reset` (lista cerrada) y exigir policy en el resto (R11)

**Checkpoint**: la solución compila, `dotnet test` sigue verde con los tokens migrados.

---

## Phase 3: User Story 1 — Ingreso de un usuario interno (Priority: P1) 🎯 MVP

**Goal**: un usuario activo ingresa con correo y contraseña, ve y puede hacer solo lo que sus roles permiten, y cierra sesión; cuentas desactivadas y tokens del portal son rechazados.

**Independent Test**: con un usuario de prueba (sembrado con `TestUsers`) y contraseña conocida: ingresar → `/auth/me` con sus roles → acción de otro rol da 403 → logout → el mismo token da 401.

### Tests for User Story 1

- [X] T018 [P] [US1] Test: login correcto devuelve 204 con cookie `dlg_bo` `HttpOnly; Secure; SameSite=Strict; Path=/api/backoffice`, normaliza el correo (mayúsculas/espacios) y `/auth/me` devuelve todos los roles, en `IT/Identidad/LoginTests.cs`
- [X] T019 [P] [US1] Test: correo inexistente, usuario desactivado y contraseña errónea devuelven 401 con el mismo `title` y cuerpo (FR-002, SC-006) y dejan `LoginFailed` en bitácora sin correo tipeado ni contraseña, en `IT/Identidad/LoginTests.cs`
- [X] T020 [P] [US1] Test: tras `POST /auth/logout`, el mismo token/cookie recibe 401; sesión con más de 30 min de inactividad o más de 8 h recibe 401 (`FakeTimeProvider`), en `IT/Identidad/SessionTests.cs`
- [X] T021 [P] [US1] Test: token con firma/emisor del portal contra `/api/backoffice/auth/me` da 401 (FR-007), y sin sesión da 401 mientras que sesión sin rol da 403 (FR-018), en `IT/Identidad/SessionTests.cs`
- [X] T022 [P] [US1] Test: usuario Solicitante invocando `GET /api/backoffice/audit/events` recibe 403 y queda `AccessDenied` en bitácora, en `IT/Identidad/AccessDeniedTests.cs`
- [X] T023 [P] [US1] Test unitario de `PasswordHashing` (hash verifica, no repite, longitudes límite y > 72 bytes rechazadas) en `UT/Identidad/PasswordHashingTests.cs`

### Implementation for User Story 1

- [X] T024 [P] [US1] Slice `Login`: `LoginRequest`+`LoginRequestValidator` (FluentValidation), handler (normaliza correo; usuario inexistente → `VerifyDummy`; desactivado/sin contraseña/errónea → 401 único; éxito → crea `Session`, actualiza `LastLoginAtUtc`, `IAuditLog.RecordAsync` `LoginSucceeded`/`LoginFailed` en la misma transacción con `ExecuteInTransactionAsync`, escribe la cookie) y endpoint `POST /auth/login` `AllowAnonymous` + `RequireRateLimiting("auth-anonymous")` en `ID/Features/Login/`
- [X] T025 [P] [US1] Slice `Logout`: revoca la `Session` actual (`RevokedAtUtc`), audita `LoggedOut`, borra la cookie; `POST /auth/logout` en `ID/Features/Logout/`
- [X] T026 [P] [US1] Slice `Me`: `GET /auth/me` devuelve `id, name, email, areaName, roles` del usuario de la sesión en `ID/Features/Me/`
- [X] T027 [US1] Mapear los tres endpoints en el grupo `Backoffice` desde `MapIdentidadEndpoints` en `ID/IdentidadModule.cs`. Depende de T024–T026
- [X] T028 [P] [US1] Cliente `fetch` (`credentials: 'same-origin'`, convierte ProblemDetails en error tipado, ante 401 limpia la sesión) en `BO/shared/api.ts` y `useSession()` (TanStack Query sobre `GET /auth/me`, helper `hasRole`) en `BO/shared/session.ts`; exportar desde `BO/shared/index.ts`
- [X] T029 [P] [US1] `LoginPage` (React Hook Form + Zod, MUI, mensaje genérico del servidor) y botón de cierre de sesión en el layout, en `BO/features/auth/LoginPage.tsx` y `BO/features/auth/LogoutButton.tsx`
- [X] T030 [US1] Rutas `/login` y guarda `beforeLoad` en las rutas protegidas (sin sesión → `/login`; con sesión en `/login` → `/`), layout con `LogoutButton` y navegación que oculta opciones según roles (solo comodidad), en `BO/app/router.tsx` y `BO/features/home/HomePage.tsx`. Depende de T028–T029
- [X] T031 [P] [US1] Test Vitest: `LoginPage` muestra el mensaje del servidor ante 401 y navega al éxito; la guarda redirige a `/login` sin sesión, en `BO/features/auth/LoginPage.test.tsx`

**Checkpoint**: US1 funcional y verificable de forma independiente (usuarios sembrados por `TestUsers`).

---

## Phase 4: User Story 2 — El administrador gestiona usuarios, roles y áreas (Priority: P1)

**Goal**: el administrador crea, edita, desactiva y reactiva usuarios con roles y área, mantiene las áreas, y el usuario recién creado establece su contraseña con el enlace; todo auditado y con efecto inmediato sobre sesiones abiertas.

**Independent Test**: el administrador crea un usuario Analista, este establece su contraseña e ingresa; el administrador le quita el rol o lo desactiva y el usuario pierde el acceso en su siguiente request.

### Tests for User Story 2

- [X] T032 [P] [US2] Test: matriz de permisos SC-003 — los casos se generan desde `EndpointDataSource` (todo endpoint bajo `/api/backoffice` salvo los tres anónimos) × cada uno de los 5 roles, contra una tabla esperada derivada de FR-016; el test falla si un endpoint no tiene fila en la tabla (así un endpoint nuevo, incluido `diagnostics/jobs` o `auth/logout`, no queda sin probar). Incluye un caso multirrol (p. ej. Solicitante+Auditor accede a `audit/events` y `GET /users`, pero no a `POST /users`) para FR-017, en `IT/Identidad/PermissionMatrixTests.cs`
- [X] T033 [P] [US2] Test: alta de usuario (201, correo normalizado, activo, sin contraseña, correo de invitación enviado), correo duplicado con otras mayúsculas/espacios → 409, área desactivada → 409, sin roles → 400, en `IT/Identidad/UsersTests.cs`
- [X] T034 [P] [US2] Test: cambio de roles/área/desactivación con sesión abierta surte efecto en la siguiente request (`/auth/me` y acción protegida) y la desactivación revoca sesiones; reactivar recupera los roles previos, en `IT/Identidad/UsersTests.cs`
- [X] T035 [P] [US2] Test: último administrador — desactivarlo o quitarle el rol → 409, incluido el caso concurrente de dos administradores que se desactivan mutuamente, en `IT/Identidad/LastAdministratorTests.cs`
- [X] T036 [P] [US2] Test: áreas — crear, renombrar (nombre duplicado → 409), desactivar/reactivar; un área desactivada no se asigna a usuarios nuevos ni en un cambio de área (409), pero los existentes la conservan y pueden editarse (p. ej. el nombre) sin cambiarla, en `IT/Identidad/AreasTests.cs`
- [X] T037 [P] [US2] Test: listado de usuarios filtra por `role`, `areaId` y `status` (FR-013), en `IT/Identidad/UsersTests.cs`
- [X] T038 [P] [US2] Test: cada alta/edición/roles/área/desactivación/reactivación de usuario y cada cambio de área deja evento con autor, valor anterior y nuevo (SC-005), sin contraseña ni token, en `IT/Identidad/UserAuditTests.cs`
- [X] T039 [P] [US2] Test: `ResetPassword` con token de invitación establece la contraseña (login posterior OK), segundo uso → 400, token vencido (72 h) → 400, el token no se guarda en claro, audita `PasswordSet`, en `IT/Identidad/ResetPasswordTests.cs`
- [X] T040 [P] [US2] Test: `BootstrapAdministrator` crea área + administrador sin contraseña solo si no hay administrador, es idempotente y audita con actor `system:*`, en `IT/Identidad/BootstrapAdministratorTests.cs`
- [X] T041 [P] [US2] Tests unitarios de los validadores de usuario (correo, nombre, roles válidos y sin duplicados, ≥ 1 rol) en `UT/Identidad/UserValidatorsTests.cs`

### Implementation for User Story 2

- [X] T042 [P] [US2] `IEmailSender` interno + `SmtpEmailSender` (`System.Net.Mail.SmtpClient`, config `Email:Smtp:Host/Port/From`; los errores se loguean sin el token) y plantillas de invitación y recuperación en `ID/Email/IEmailSender.cs`, `ID/Email/SmtpEmailSender.cs`; registrarlo en `ID/IdentidadModule.cs` (R7)
- [X] T043 [P] [US2] Slice `ListAreas`: `GET /areas?status=` con policy `UserDirectoryReader` en `ID/Features/ListAreas/`
- [X] T044 [P] [US2] Slice `CreateArea`: `POST /areas` (policy `Administrator`, validador, 409 por nombre duplicado, audita `Created` en la misma transacción) en `ID/Features/CreateArea/`
- [X] T045 [P] [US2] Slice `RenameArea`: `PUT /areas/{id}` (404/409, audita `Updated` con `Nombre` antes/después) en `ID/Features/RenameArea/`
- [X] T046 [P] [US2] Slice `SetAreaStatus`: `POST /areas/{id}/deactivate` y `/reactivate` idempotentes, audita `Updated` con `Estado` en `ID/Features/SetAreaStatus/`
- [X] T047 [P] [US2] Slice `ListUsers` y `GetUser`: `GET /users?role=&areaId=&status=` y `GET /users/{id}` con policy `UserDirectoryReader`; el DTO nunca incluye `PasswordHash`; `hasPassword` derivado, en `ID/Features/ListUsers/` y `ID/Features/GetUser/`
- [X] T048 [US2] Slice `CreateUser`: `POST /users` (policy `Administrator`; validador con nombre, correo normalizado, área activa, ≥ 1 rol de `BackofficeRoles` sin repetidos; 409 correo duplicado/área inactiva; crea usuario + `PasswordToken` `Invitation` 72 h; audita `Created` con todos los campos; tras confirmar envía el correo con `BackofficeBaseUrl/restablecer?token=`) en `ID/Features/CreateUser/`. Depende de T042
- [X] T049 [US2] Regla compartida `LastAdministratorGuard` (`SELECT COUNT … WITH (UPDLOCK, HOLDLOCK)` de administradores activos dentro de la transacción; 409 si el cambio deja 0) en `ID/Features/Shared/LastAdministratorGuard.cs` (R9)
- [X] T050 [US2] Slice `UpdateUser`: `PUT /users/{id}` (cambia nombre, correo, área y roles; validador de nombre, correo normalizado único y ≥ 1 rol; "área activa" se valida **solo si `AreaId` cambia**, así un usuario cuya área ya fue desactivada puede editarse sin cambiarla; audita solo los campos cambiados con `Roles` como lista ordenada; si el cambio le quita el rol Administrador a un usuario activo, aplica `LastAdministratorGuard`) en `ID/Features/UpdateUser/`. Depende de T049
- [X] T051 [US2] Slice `SetUserStatus`: `POST /users/{id}/deactivate` (usa `LastAdministratorGuard`, revoca todas las sesiones del usuario, audita `Updated`/`Estado`) y `/reactivate` (conserva roles) idempotentes en `ID/Features/SetUserStatus/`. Depende de T049
- [X] T052 [P] [US2] Slice `ResetPassword`: `POST /auth/password/reset` `AllowAnonymous` + rate limit (valida token por hash, no usado, no vencido, usuario activo; nueva contraseña por el mismo validador; guarda hash, marca usado e invalida los demás tokens, revoca sesiones, limpia bloqueo y contador; audita `PasswordSet` con `Origen`; error único para token inválido/usado/vencido) en `ID/Features/ResetPassword/`
- [X] T053 [US2] Mapear todos los endpoints de usuarios, áreas y `ResetPassword` en `MapIdentidadEndpoints` de `ID/IdentidadModule.cs`. Depende de T043–T052 (con T049 `LastAdministratorGuard` antes que T050–T051)
- [X] T054 [P] [US2] `BootstrapAdministrator` y `IdentidadModule.BootstrapAdministratorAsync(IServiceProvider, CancellationToken)`: si no hay administrador y `Identidad:BootstrapAdmin:Email` está configurado, crea el área (por defecto `Compliance`) y el administrador sin contraseña y audita `Created`, en `ID/Bootstrap/BootstrapAdministrator.cs` (R8)
- [X] T055 [US2] Llamar `BootstrapAdministratorAsync` después de migrar en `src/Hosts/Diligencia.MigrationService/MigrationEntryPoint.cs`. Depende de T054
- [X] T056 [P] [US2] AppHost: contenedor Mailpit (`AddContainer`, puertos SMTP y UI), parámetro secreto persistido `backoffice-signing-key` (como `Authentication__Schemes__Backoffice__SigningKeys__0__Value`), y `Email__Smtp__*`, `Identidad__BackofficeBaseUrl` e `Identidad__BootstrapAdmin__Email=admin@diligencia.local` para `api` y `migrations`, en `src/Aspire/Diligencia.AppHost/Program.cs` (R7, R8, R13)
- [X] T057 [P] [US2] Config por defecto (`Identidad`, `Email`) en `src/Hosts/Diligencia.Api/appsettings.json` y `appsettings.Development.json`
- [X] T058 [P] [US2] `ResetPasswordPage` (ruta `/restablecer?token=`, React Hook Form + Zod, mín. 12 caracteres; al montar guarda el token en estado y lo quita de la URL con `history.replaceState`) en `BO/features/auth/ResetPasswordPage.tsx`; agregar `<meta name="referrer" content="no-referrer">` en `apps/backoffice/index.html` para que el token no viaje en `Referer`
- [X] T059 [P] [US2] `AreasPage` (lista, alta, renombrar, activar/desactivar; visible solo con rol Administrador o Auditor — Auditor en solo lectura) en `BO/features/areas/AreasPage.tsx`
- [X] T060 [P] [US2] `UsersPage` (tabla MUI con filtros rol/área/estado en la query string, activar/desactivar) y `UserDialog` (alta/edición con nombre, correo, área activa, roles; muestra el 409 del servidor) en `BO/features/users/UsersPage.tsx` y `BO/features/users/UserDialog.tsx`
- [X] T061 [US2] Registrar las rutas `/restablecer`, `/usuarios` y `/areas` y los enlaces de navegación por rol en `BO/app/router.tsx` y el layout. Depende de T058–T060
- [X] T062 [P] [US2] Tests Vitest: `UsersPage` oculta acciones de escritura al Auditor, `UserDialog` valida ≥ 1 rol y muestra el error 409, `ResetPasswordPage` valida longitud, en `BO/features/users/UsersPage.test.tsx` y `BO/features/auth/ResetPasswordPage.test.tsx`

**Checkpoint**: US1 y US2 funcionan; el administrador inicial se crea al arrancar con el AppHost.

---

## Phase 5: User Story 3 — Protección de la cuenta y trazabilidad del ingreso (Priority: P3)

**Goal**: bloqueo tras 5 fallos, registro de ingresos/rechazos y recuperación de acceso por enlace de un solo uso.

**Independent Test**: fallar el ingreso 5 veces y comprobar el bloqueo de 15 min; pedir recuperación, usar el enlace una vez y ver los eventos en la bitácora.

### Tests for User Story 3

- [X] T063 [P] [US3] Test: 5 fallos seguidos bloquean 15 min (el 6.º con contraseña correcta da el mismo 401 genérico), a los 15 min ingresa, un éxito reinicia el contador, y quedan 5 `LoginFailed` + 1 `AccountLocked` (`FakeTimeProvider`), en `IT/Identidad/LockoutTests.cs`
- [X] T064 [P] [US3] Test: `forgot` responde 202 con y sin correo existente (mismo cuerpo), solo envía correo a usuarios activos, el enlace vence a los 60 min, es de un solo uso, al usarlo se revocan sesiones y se limpia el bloqueo, y audita `PasswordResetRequested`/`PasswordSet`, en `IT/Identidad/ForgotPasswordTests.cs`
- [X] T065 [P] [US3] Test: con una factory propia que fija `Identidad:AuthRateLimitPerMinute=3`, la 4.ª solicitud del minuto a `auth/login` devuelve 429 y los endpoints autenticados no se limitan, en `IT/Identidad/RateLimitTests.cs`
- [X] T066 [P] [US3] Test: ningún log ni evento de bitácora de los flujos de login/recuperación contiene contraseña, hash ni token (extiende el patrón de `IT/Logging/NoSecretsInLogsTests.cs`), en `IT/Identidad/NoCredentialsLeakTests.cs`

### Implementation for User Story 3

- [X] T067 [US3] Agregar bloqueo a `ID/Features/Login/`: ya bloqueada → 401 genérico sin verificar contraseña ni sumar; fallo identificado → `FailedLoginCount++`; al llegar a `MaxFailedLogins` fija `LockoutEndUtc`, reinicia el contador y audita `AccountLocked`; éxito reinicia contador y bloqueo (R5, FR-005)
- [X] T068 [P] [US3] Slice `ForgotPassword`: `POST /auth/password/forgot` `AllowAnonymous` + rate limit; validador FluentValidation (correo obligatorio, formato, ≤ 254); siempre 202 si es válido; si el correo existe y está activo crea `PasswordToken` `Reset` 60 min, audita `PasswordResetRequested` y envía el correo después de confirmar, en `ID/Features/ForgotPassword/`. Depende de T042
- [X] T069 [US3] Mapear `ForgotPassword` en `MapIdentidadEndpoints` de `ID/IdentidadModule.cs`. Depende de T068
- [X] T070 [P] [US3] `ForgotPasswordPage` (ruta `/olvide`, mensaje neutro) y enlace "Olvidé mi contraseña" en `LoginPage`, en `BO/features/auth/ForgotPasswordPage.tsx`, `BO/features/auth/LoginPage.tsx` y `BO/app/router.tsx`
- [X] T071 [P] [US3] Test Vitest de `ForgotPasswordPage` (mensaje neutro igual para cualquier correo) en `BO/features/auth/ForgotPasswordPage.test.tsx`

**Checkpoint**: las tres historias funcionan de forma independiente.

---

## Phase 6: Polish & Cross-Cutting

- [X] T072 [P] Confirmar que los tests de arquitectura (`tests/Diligencia.ArchitectureTests`) y ESLint de fronteras siguen verdes (todo lo nuevo es `internal`; `users` y `areas` no se importan entre sí); corregir lo que falle
- [X] T073 [P] Actualizar `specs/001-project-setup-foundation/quickstart.md` y `specs/001-project-setup-foundation/contracts/cross-cutting.md` §6: el backoffice ya no usa `dotnet user-jwts`; remitir a [quickstart.md](quickstart.md) de esta feature
- [X] T074 Ejecutar la validación completa de [quickstart.md](quickstart.md) (secciones 1–5) y `dotnet test` + `npm run lint && npm run typecheck && npm test`; anotar desvíos
  - **Desvíos**: `dotnet test` (unit 42, arquitectura 2, integración 174 sin el smoke) y `npm run lint/typecheck/test` en verde. **No se ejecutó** la sección 1 del quickstart (AppHost + Mailpit + UI) ni `AppHostSmokeTests`: Aspire exige el CLI `docker`, que este entorno no tiene (solo el puente `DOCKER_HOST` para Testcontainers). Pendiente validar a mano en una máquina con Docker: contenedor `mailpit`, `backoffice-signing-key`, `Identidad__BackofficeBaseUrl` y el administrador inicial.
  - **Resuelto en T077**: con un `docker.exe` de reenvío a `wsl -e docker`, `AppHostSmokeTests` pasa y las secciones 1–4 del quickstart se validaron contra el AppHost (24 comprobaciones por API + login y lista de usuarios en la UI).
- [X] T075 [P] Anotar el `shortcut:` de limpieza de `Sessions`/`PasswordTokens` vencidos como comentario en `ID/Persistence/EntityConfigurations.cs` (ver [data-model.md](data-model.md))

---

## Dependencies & Execution Order

- **Setup (Ph.1)** → sin dependencias; T001–T005 en paralelo.
- **Foundational (Ph.2)** → depende de Setup; **bloquea** todas las historias. T006→T007; T011 depende de T005–T007; T015→T016.
- **US1 (Ph.3)** → depende de Foundational. Es el MVP.
- **US2 (Ph.4)** → depende de Foundational y del login de US1 para ejercitarse de punta a punta (sus tests de API pueden usar `TestUsers`). `ResetPassword` y `IEmailSender` quedan aquí y los reutiliza US3.
- **US3 (Ph.5)** → depende de US1 (login) y de T042/T052 de US2.
- **Polish (Ph.6)** → al final.

### Dentro de cada historia

Tests primero (deben fallar), luego slices, luego mapeo de endpoints, luego frontend.

## Parallel Examples

```text
Setup:       T001 T002 T003 T004 T005
Foundation:  T006 | T008 T009 T010 T012 T013 (tras T006: T007 → T011, T015)
US1 tests:   T018 T019 T020 T021 T022 T023
US1 slices:  T024 T025 T026   |   frontend: T028 T029 (→ T030)
US2 tests:   T032–T041 (todos)
US2 slices:  T043 T044 T045 T046 T047 T052 T054 T056 T057 T058 T059 T060
US3:         T063–T066 tests; T068 T070 T071
```

## Implementation Strategy

1. **MVP = Setup + Foundational + US1**: ingreso real y cierre de sesión con usuarios sembrados; ya elimina `user-jwts` del backoffice en los tests.
2. **Incremento 2 = US2**: sin esto no hay forma de dar de alta a nadie ni de crear el primer administrador (RF-28). Entregable como el valor de negocio principal.
3. **Incremento 3 = US3**: endurece la cuenta y habilita la recuperación.
4. Validar con [quickstart.md](quickstart.md) al cerrar cada fase.

---

## Phase 7: Convergence

- [X] T076 Al quedar la sesión en `null` (401 de cualquier request), redirigir a `/login` desde el layout protegido (`BO/app/Layout.tsx` o la guarda del router), con test Vitest, per US3/AC4 (partial)
- [X] T077 Ejecutar `AppHostSmokeTests` y las secciones 1–4 de [quickstart.md](quickstart.md) en una máquina con Docker (Mailpit, `backoffice-signing-key`, `Identidad__BackofficeBaseUrl`, administrador inicial) y corregir lo que falle, per plan: AppHost R7/R8/R13 (partial)
- [X] T078 Test Vitest de `AreasPage`: el Auditor ve la lista sin acciones de escritura y el Administrador sí, en `BO/features/areas/AreasPage.test.tsx`, per plan: estructura del frontend (partial)
- [X] T079 Mover `LastAdministratorGuard` de `ID/Features/Shared/Directory.cs` a `ID/Features/Shared/LastAdministratorGuard.cs`, per T049 (partial)
