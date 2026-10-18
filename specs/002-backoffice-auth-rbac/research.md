# Research: Autenticación y RBAC del backoffice

Decisiones técnicas de la Fase 0. Parte de lo que ya existe desde la feature 001 (ver
`specs/001-project-setup-foundation/research.md`, en particular R5, R9 y R10) y lo extiende sin
reemplazarlo.

## R1. Forma de la sesión: JWT con `sid` + sesión en base validada en cada request

- **Decisión**: al ingresar, la API emite un JWT del esquema `Backoffice` (mismo emisor, audiencia
  y clave que hoy valida `AuthenticationSetup`) con `sub` = id del usuario, `sid` = id de una fila
  `identidad.Sessions` y `exp` = ingreso + 8 h (vencimiento absoluto, FR-004). En el evento
  `JwtBearerEvents.OnTokenValidated` del esquema `Backoffice` se carga la sesión y el usuario
  (lectura por PK, mismo módulo) y se rechaza (401) si la sesión está revocada, si
  `LastActivityUtc` tiene más de 30 min, o si el usuario está desactivado. Si pasa, los claims
  `role` del principal se **reemplazan** por los roles actuales del usuario en base, y
  `LastActivityUtc` se actualiza solo si tiene más de 60 s (una escritura por minuto por sesión, no
  por request).
- **Rationale**: cumple la constitución (JWT, Principio VIII) y a la vez da cierre de sesión
  inmediato (escenario US1-4), vencimiento por inactividad (FR-004) y cambios de rol/estado
  efectivos en la siguiente acción, muy por debajo de los 5 min de FR-012/SC-004. El tráfico del
  backoffice es interno y bajo; una lectura por PK por request es despreciable.
- **Alternativas descartadas**:
  - JWT corto (5 min) + refresh token rotativo: más código (endpoint de refresh, rotación, detección
    de reuso, lógica en el cliente) y el logout no es inmediato sin una lista de revocación, que es
    justamente la consulta por request que se quería evitar.
  - Cookie de sesión de ASP.NET Core sin JWT: contradice el Principio VIII y rompe el modelo de dos
    esquemas JWT del Principio V.
  - Caché en memoria de la sesión: innecesaria al volumen actual y complica la revocación entre
    instancias. Se agrega solo si una medición lo pide.

## R2. Transporte del token en el navegador: cookie HttpOnly

- **Decisión**: `POST /auth/login` responde `204` con `Set-Cookie: dlg_bo=<jwt>; HttpOnly; Secure;
  SameSite=Strict; Path=/api/backoffice`. `OnMessageReceived` del esquema `Backoffice` toma el token
  de esa cookie si no viene header `Authorization` (el header se sigue aceptando: lo usan los tests
  y las herramientas). Logout borra la cookie y revoca la sesión.
- **Rationale**: el token nunca es legible por JavaScript (mitiga robo por XSS). `SameSite=Strict`
  con SPA y API en el mismo sitio (proxy de Vite en local, mismo origen detrás del proxy inverso en
  despliegue) cubre CSRF sin tokens antiforgery. La cookie del portal (feature RF-05) tendrá otro
  nombre y `Path=/api/portal`, y aunque se enviara, la firma/emisor/audiencia distintos la rechazan
  (FR-007).
- **Alternativas**: token en `localStorage` (expuesto a XSS); token en memoria (se pierde al
  recargar y obliga a refresh tokens).
- **Supuesto**: SPA y API se sirven desde el mismo sitio. Si se separan dominios hace falta revisar
  `SameSite` y agregar antiforgery.

## R3. Hash de contraseñas: BCrypt

- **Decisión**: paquete `BCrypt.Net-Next`, `HashPassword(pwd, workFactor: 12)` /
  `Verify`. Política: 12 a 64 caracteres y como máximo 72 bytes UTF-8 (límite de BCrypt; más allá
  trunca en silencio). Validado con FluentValidation en el borde.
- **Rationale**: BCrypt es lo que fija la constitución (Principio VIII). Work factor 12 ≈ 250 ms por
  verificación, compatible con SC-001.
- **Alternativas**: `PasswordHasher<T>` de ASP.NET Core Identity (PBKDF2): sin dependencia nueva,
  pero la constitución nombra BCrypt explícitamente.

## R4. Mensaje genérico y tiempo constante (FR-002, SC-006)

- **Decisión**: inexistente, desactivado, bloqueado y contraseña errónea responden igual: `401`
  ProblemDetails con `title = "Correo o contraseña incorrectos."`. Si el correo no existe se
  ejecuta igual un `BCrypt.Verify` contra un hash fijo para que el tiempo de respuesta no delate la
  existencia. El motivo real queda solo en la bitácora.
- **Rationale**: un mensaje distinto para "bloqueado" revelaría que la cuenta existe (SC-006 manda
  sobre la comodidad).

## R5. Bloqueo por intentos fallidos (FR-005)

- **Decisión**: columnas `FailedLoginCount` y `LockoutEndUtc` en `Users`. Cada fallo con usuario
  identificado incrementa el contador; al llegar a 5 se fija `LockoutEndUtc = ahora + 15 min`, se
  pone el contador en 0 y se audita `AccountLocked`. Mientras `LockoutEndUtc > ahora` el ingreso se
  rechaza sin verificar la contraseña ni incrementar. Un ingreso exitoso o un restablecimiento de
  contraseña ponen contador y bloqueo en 0. Los plazos salen de `IdentidadOptions` (R12).
- **Alternativas**: tabla de intentos con ventana deslizante: más datos y consultas para el mismo
  resultado.

## R6. Enlaces de un solo uso: alta y recuperación (FR-001, FR-006)

- **Decisión**: tabla `identidad.PasswordTokens` con `TokenHash` (SHA-256 de 32 bytes aleatorios de
  `RandomNumberGenerator`, el token en claro solo viaja en el correo, en base64url), `Purpose`
  (`Invitation` | `Reset`), `ExpiresAtUtc`, `UsedAtUtc`. Vencimientos: recuperación 60 min (FR-006),
  invitación 72 h (supuesto: el spec no lo fija). Un mismo endpoint `POST /auth/password/reset`
  consume ambos: valida hash, no usado, no vencido y usuario activo; guarda el hash BCrypt, marca el
  token usado, invalida los demás tokens pendientes del usuario, revoca sus sesiones y limpia el
  bloqueo. Si una invitación vence, el usuario usa "olvidé mi contraseña": no hace falta un
  endpoint de reenvío.
- **Rationale**: SHA-256 alcanza para un secreto de 256 bits aleatorio (no es una contraseña
  elegida por humanos); comparar por hash evita guardar el token en claro.
- **Alternativas**: tokens de Data Protection firmados (sin tabla): no permiten "un solo uso" sin
  estado.

## R7. Envío de correo

- **Decisión**: interfaz interna `IEmailSender` en Identidad con implementación sobre
  `System.Net.Mail.SmtpClient` (sin paquete nuevo), configurada por `Email:Smtp:Host`, `Port`,
  `From`. En local el AppHost levanta un contenedor **Mailpit** (`AddContainer`, sin paquete nuevo)
  y le pasa host/puerto a la API; la UI web de Mailpit es el buzón de prueba. El envío es inline en
  la API, después de confirmar la transacción.
- **Rationale**: el correo no es procesamiento pesado (Principio VI no aplica). Encolarlo en
  Hangfire obligaría a guardar el token en claro en los argumentos del job (tablas `hangfire`), que
  es peor. Si el envío falla: en el alta, el usuario igual queda creado y puede usar "olvidé mi
  contraseña"; en la recuperación, se loguea el error (sin el token) y se responde igual `202`.
- **Alternativas**: MailKit (dependencia nueva; se adopta si el proveedor real exige OAuth u otra
  cosa que `SmtpClient` no soporte); CommunityToolkit.Aspire.Hosting.MailPit (paquete extra para
  dos líneas de `AddContainer`).
- `IEmailSender` se queda en Identidad hasta que otro módulo lo necesite (RF-05 portal); ahí se
  mueve a `BuildingBlocks`.

## R8. Primer administrador (FR-014)

- **Decisión**: el MigrationService, después de migrar, ejecuta `BootstrapAdministrator`: si no
  existe ningún usuario con rol Administrador y está configurado `Identidad:BootstrapAdmin:Email`
  (+ `Name`, `Area`, por defecto `Compliance`), crea el área si falta y el usuario activo con rol
  Administrador, **sin contraseña**, y lo audita (`Created`, actor `system:Diligencia.MigrationService`).
  El administrador entra por primera vez con "olvidé mi contraseña". Es idempotente: si ya hay un
  administrador no hace nada. En local el AppHost pasa `admin@diligencia.local`.
- **Rationale**: el MigrationService ya corre una vez por despliegue con la identidad de
  migraciones y ya registra los módulos (incluido `IAuditLog`). No envía correos ni maneja tokens.
- **Alternativas**: comando CLI aparte (otro punto de entrada que mantener); seed en la API al
  arrancar (la API corre con el login de runtime y en varias instancias).

## R9. Último administrador (FR-010) sin carreras

- **Decisión**: toda operación que pueda dejar de contar a un administrador activo (desactivar,
  quitar el rol) corre en la transacción del módulo y antes del cambio ejecuta
  `SELECT COUNT(*) ... WITH (UPDLOCK, HOLDLOCK)` sobre los administradores activos; si el cambio
  dejaría 0, responde `409`. El bloqueo serializa dos administradores que se desactivan
  mutuamente a la vez.
- **Alternativas**: chequeo sin bloqueo (dos requests concurrentes pueden dejar 0
  administradores); `sp_getapplock` (equivalente, menos legible).

## R10. Bitácora: acciones nuevas y "acceso denegado" (FR-019, FR-020)

- **Decisión**: se agregan valores a `AuditAction` (cambio aditivo del contrato de Auditoría):
  `LoginSucceeded`, `LoginFailed`, `LoggedOut`, `AccountLocked`, `PasswordResetRequested`,
  `PasswordSet`, `AccessDenied`. Alta = `Created`; edición, roles, área, desactivación y
  reactivación = `Updated` con `AuditChange` por campo (`Nombre`, `Correo`, `Area`, `Roles` como
  lista ordenada separada por comas, `Estado`). Los cambios de usuarios y áreas se auditan con
  `RecordAsync` dentro de la transacción del cambio (atómico, R5 de la 001). Los ingresos y
  rechazos también, en la misma transacción que actualiza contador/último ingreso.
  `AccessDenied`: un `IAuthorizationMiddlewareResultHandler` propio de Identidad que, si el
  resultado es `Forbidden` en `/api/backoffice`, registra con `RecordStandaloneAsync`
  (`EntityType = "Endpoint"`, `EntityId` = nombre de la ruta o path recortado a 128) y delega en el
  handler por defecto. Nunca se audita contraseña, hash ni token.
- Intentos con correo desconocido: `EntityType = "Usuario"`, `EntityId = "desconocido"`; no se
  guarda el correo tipeado (spec: "usuario si se identificó").

## R11. Policies (FR-015 a FR-018)

- **Decisión**: se agregan a `AuthPolicies`: `UserDirectoryReader` (Administrador o Auditor:
  listar y ver usuarios y áreas) y se reutiliza `Administrator` para toda escritura de usuarios y
  áreas. `AuditReader` ya cubre "Consultar bitácora". `GET /auth/me` usa la policy del grupo
  (`Backoffice`: cualquier usuario autenticado). Login, forgot y reset llevan `AllowAnonymous`
  explícito. 401 vs 403 lo resuelve ASP.NET Core (FR-018).
- El test existente que recorre `EndpointDataSource` y exige autorización en todo endpoint se
  extiende para aceptar `AllowAnonymous` solo en una lista cerrada (las tres rutas anónimas de
  `/auth`).

## R12. Configuración de plazos y límite de tasa

- **Decisión**: `IdentidadOptions` (sección `Identidad`) con defaults del spec: `SessionIdleMinutes
  = 30`, `SessionAbsoluteHours = 8`, `MaxFailedLogins = 5`, `LockoutMinutes = 15`,
  `ResetTokenMinutes = 60`, `InvitationTokenHours = 72`, `AuthRateLimitPerMinute = 10`, `BackofficeBaseUrl` (obligatoria, para
  armar los enlaces). Se usan `TimeProvider` en todo cálculo de tiempo para poder testear con
  `FakeTimeProvider` (paquete ya presente). Las rutas anónimas tienen el limitador de tasa nativo de
  ASP.NET Core (`AddRateLimiter`, ventana fija por IP, límite `AuthRateLimitPerMinute = 10`), que
  protege sobre todo contra el envío masivo de correos de recuperación. El límite es configurable
  porque en `TestServer` la IP remota es nula y todos los tests caerían en la misma partición; los
  tests lo suben y solo el test del limitador lo baja.
- **Rationale**: el spec marca los plazos como "a validar con compliance"; dejarlos en configuración
  evita un cambio de código cuando lo validen.

## R13. Clave de firma en local

- **Decisión**: el AppHost genera y persiste un parámetro secreto `backoffice-signing-key`
  (64 caracteres alfanuméricos, válidos como base64 → 48 bytes) y lo pasa a la API como
  `Authentication__Schemes__Backoffice__SigningKeys__0__Value`. `dotnet user-jwts` deja de hacer
  falta para el backoffice (sigue para el portal hasta RF-05). En despliegue la clave viene del
  gestor de secretos, igual que hoy.

## R14. Frontend del backoffice

- **Decisión**: features `auth` (login, olvidé/restablecer contraseña, logout), `users` y `areas`;
  en `shared/` un cliente `fetch` (`credentials: 'same-origin'`, convierte ProblemDetails en error
  tipado) y `useSession()` (TanStack Query sobre `GET /auth/me`), que comparten las features sin
  importarse entre sí. Rutas protegidas con `beforeLoad` del router: sin sesión → `/login`. Los
  menús y botones se ocultan según roles solo como comodidad (Principio VII). Formularios con React
  Hook Form + Zod (`react-hook-form`, `zod`, `@hookform/resolvers`, dentro del stack). La lista de
  usuarios usa `Table` de MUI con filtros en la query string; sin TanStack Table ni paginación
  (decenas de usuarios internos).
- Playwright no se agrega: el login no está entre los flujos críticos que la constitución exige con
  E2E. Vitest cubre formulario de login, guarda de rutas y ocultamiento por rol.

## R15. Estrategia de tests

- **Integración (Testcontainers + `WebApplicationFactory`, `FakeTimeProvider`)**: login OK/KO con
  mensaje idéntico para inexistente/desactivado/bloqueado; bloqueo al 5.º fallo y desbloqueo a los
  15 min; vencimiento por inactividad y absoluto; logout y uso posterior del token → 401; cambio de
  rol y desactivación con sesión abierta → efecto en la siguiente request; token del portal → 401
  en backoffice; último administrador → 409 (incluido el caso concurrente); unicidad de correo sin
  distinguir mayúsculas/espacios; enlaces de un solo uso y vencidos; bitácora de cada acción con
  antes/después; y **matriz SC-003**: cada endpoint del backoffice × cada uno de los 5 roles contra
  la tabla de FR-016 (test parametrizado).
- **Unit**: validadores (contraseña, correo, roles), hashing de tokens.
- **Impacto en tests existentes**: `TestTokens.Create` firma tokens sin `sid`, que ahora se
  rechazan. Se reemplaza por un helper que crea usuario + sesión en la base del test y devuelve el
  token; los tests de bitácora y diagnóstico pasan a usarlo.
