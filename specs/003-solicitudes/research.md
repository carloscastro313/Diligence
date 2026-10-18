# Research: Solicitudes de alta de terceros

Decisiones técnicas de la feature 003. Cada una: decisión, motivo y alternativas descartadas.

## R1. Módulo dueño de cada dato

- **Decisión**: el tercero vive en **Terceros** (schema `terceros`); el caso, las solicitudes y el
  enlace al portal viven en **Casos** (schema `casos`). Los tres endpoints de la feature son slices
  de Casos, que usa Terceros solo por `Terceros.Contracts`.
- **Rationale**: el modelo de datos (sección 7) pone al tercero como entidad propia con muchos
  casos, y la constitución ya separa ambos módulos. La solicitud no tiene vida fuera de su caso.
- **Alternativas**: todo en Casos (Terceros quedaría vacío y la unicidad del CUIT viviría en el
  módulo equivocado); un módulo nuevo "Solicitudes" (un tercer schema para dos tablas que no existen
  sin el caso).

## R2. Alta atómica de tercero + caso entre dos módulos

- **Decisión**: el handler de Casos abre su transacción y llama a
  `ITerceroRegistry.GetOrCreateForOnboardingAsync(datos, DbTransaction)`, que trabaja sobre la
  conexión y la transacción del llamador con un `DbContext` efímero, igual que
  `IAuditLog.RecordAsync`. Tercero, caso, solicitud, enlace y eventos de bitácora se confirman o
  revierten juntos. A diferencia de `AuditLog`, ese contexto efímero MUST agregar el
  `TimestampsInterceptor` (resuelto por DI) para que `Tercero` reciba `CreatedAtUtc`/`UpdatedAtUtc`.
- **Rationale**: el patrón ya existe y está probado en la 001 (Auditoría). Todos los schemas están en
  la misma base, así que no hace falta coordinación distribuida.
- **Alternativas**: eventos de integración + saga (complejidad sin beneficio en un monolito con una
  sola base); dos transacciones con compensación (deja terceros huérfanos si la segunda falla).

## R3. Concurrencia por CUIT (dos solicitantes a la vez)

- **Decisión**: la búsqueda dentro de `GetOrCreateForOnboardingAsync` usa
  `WITH (UPDLOCK, HOLDLOCK)` sobre el índice único de `Cuit`. El segundo request espera al primero,
  luego ve el tercero y su caso abierto y se suma como interesado. Respaldo: índice único en
  `terceros.Terceros(Cuit)` e índice único filtrado en `casos.Casos(TerceroId)` para estados
  abiertos.
- **Rationale**: los locks U son incompatibles entre sí, así que no hay deadlock; el rango queda
  bloqueado también cuando el CUIT todavía no existe. Cumple el edge case "queda un solo tercero y un
  solo caso" sin reintentos en el código.
- **Alternativas**: capturar la violación de índice único y reintentar (dos caminos de código y un
  reintento que la estrategia de EF no hace solo); aislamiento `SERIALIZABLE` en toda la
  transacción (bloquea más de lo necesario).

## R4. Validación del CUIT

- **Decisión**: se normaliza quitando todo lo que no sea dígito; debe quedar en 11 dígitos. Dígito
  verificador: pesos `5,4,3,2,7,6,5,4,3,2` sobre los 10 primeros, `r = 11 - (suma mod 11)`;
  `r = 11 → 0`; `r = 10 → inválido`; si no, `r` debe ser igual al último dígito. Prefijo: `20, 23,
  24, 27` para persona humana; `30, 33, 34` para persona jurídica. Vive en
  `Casos/Features/Shared/Cuit.cs` (lo usan el aviso y el alta) con test unitario; el frontend repite
  la regla en Zod solo para UX.
- **Rationale**: algoritmo público de AFIP/ARCA; no requiere servicio externo.
- **Alternativas**: validar contra el padrón de ARCA (es la verificación de RF-10, otra feature).

## R5. Enlace seguro al portal

- **Decisión**: token opaco de 256 bits (`RandomNumberGenerator`, Base64Url), guardado solo como
  SHA-256 en `casos.EnlacesPortal.TokenHash`, igual que los tokens de contraseña de Identidad. URL:
  `{Casos:PortalBaseUrl}/acceso#t={token}`. Vencimiento: fecha de apertura + 15 días hábiles (R8).
  Se considera válido si no venció **y** el caso sigue en "Espera del tercero"; esa verificación la
  hace la feature del portal (RF-05), que buscará por hash.
- **Rationale**: no deducible de datos del tercero (FR-009); nunca en claro (FR-010). El token va en
  el fragmento (`#`): el navegador no lo manda al servidor que sirve el portal, así no aparece en
  logs de acceso ni en `Referer`.
- **Alternativas**: JWT firmado con `casoId` y vencimiento (no se puede invalidar cuando el caso
  sale de espera sin guardar estado igual); query string `?t=` como el backoffice (queda en logs del
  servidor estático).
- **Nota**: `OpaqueToken` es interno de Identidad. Casos tiene su propia copia de dos líneas (no se
  justifica un contrato ni un BuildingBlock por dos llamadas a la librería estándar).

## R6. Envío del correo con reintentos (FR-011)

- **Decisión**: el handler confirma la transacción y encola en Hangfire
  `IPortalLinkDelivery.SendAsync(enlaceId)`. El job, en el worker:
  1. si el enlace ya está `Enviado` o `Fallido`, termina (idempotente);
  2. genera un token nuevo, guarda su hash, incrementa `Intentos`;
  3. envía el correo por `IEmailTransport` (lanza si falla);
  4. éxito → `Enviado` + bitácora; fallo con `Intentos < 5` → `Reintentando` y relanza (Hangfire
     reintenta con su backoff); fallo con `Intentos = 5` → `Fallido` + bitácora, sin relanzar.

  `[AutomaticRetry(Attempts = 5)]` va en el método de la **interfaz**: Hangfire lee los filtros del
  tipo con que se encoló el job (`Enqueue<IPortalLinkDelivery>`), no de la implementación.
- **Rationale**: el token se genera en el job, así que nunca viaja en los argumentos de Hangfire
  (la objeción de la R7 de la 002 no aplica). Regenerarlo en cada intento es seguro porque un enlace
  no entregado nunca llegó a nadie. Los reintentos y el backoff los da Hangfire.
- **Alternativas**: envío inline + job solo para reintentos (dos caminos con la misma lógica);
  outbox propio (Hangfire ya es la cola durable del proyecto).
- **Riesgo aceptado**: si el SMTP entrega pero responde error, el reintento invalida el primer
  correo; el tercero usa el último. Si encolar falla después de confirmar (Hangfire usa la misma
  base, improbable), el enlace queda `Pendiente`: shortcut anotado en el código, se resuelve con un
  job recurrente de barrido cuando exista la feature de plazos.

## R7. Transporte de correo compartido

- **Decisión**: `BuildingBlocks/Email`: `public interface IEmailTransport` (lanza si falla) +
  `SmtpEmailTransport` (lee `Email:Smtp` igual que Identidad, sin `try/catch`), registrado por
  `AddEmailTransport()`. Identidad **no se toca**: su `SmtpEmailSender` sigue igual.
- **Rationale**: es infraestructura técnica, sin reglas de negocio; Casos necesita saber si el envío
  falló, por eso el transporte lanza. No modificar Identidad respeta el Principio I ("una feature
  nueva se agrega sin modificar el interior de otro módulo").
- **Alternativas**: hacer que `Identidad.SmtpEmailSender` delegue en el transporte (elimina ~5
  líneas duplicadas, pero cambia el interior de Identidad; se puede hacer en un refactor aparte);
  publicar `IEmailSender` en `Identidad.Contracts` (Identidad no es dueño del correo y su sender
  traga los errores).

## R8. Días hábiles y fecha estimada (FR-013)

- **Decisión**: `BusinessCalendar.AddBusinessDays(DateOnly, int)` (lunes a viernes, sin feriados) en
  `Casos/Features/Shared`. Las fechas se calculan en hora de Argentina (UTC−3 fijo; shortcut: sin
  horario de verano desde 2009, se cambia a `TimeZoneInfo` si vuelve). Plazos fijos en una clase
  `StagePlazos` (15, 5, 3) hasta RF-30.
  Fecha estimada para un caso abierto en la etapa *e* que empezó el día *d*:
  `fin_e = max(hoy, AddBusinessDays(d, plazo_e))`; luego se suman los plazos de las etapas
  siguientes. Casos cerrados: resultado + `CerradoUtc`.
- **Rationale**: cumple literalmente FR-013 ("si una etapa ya superó su plazo, se toma el día actual
  como fin de esa etapa"). Se calcula al leer, no se guarda: depende de "hoy".
- **Alternativas**: guardar la fecha estimada (queda vieja al día siguiente).

## R9. Autorización

- **Decisión**: Casos mapea un subgrupo `surfaces.Backoffice.MapGroup("/solicitudes")
  .RequireAuthorization(p => p.RequireRole(BackofficeRoles.Solicitante))`; los tres endpoints
  heredan la regla del grupo Backoffice (esquema) más el rol. "Mis solicitudes" filtra en SQL por
  `Solicitudes.SolicitanteId == IActorContext.ActorId` (FR-014).
- **Rationale**: no modifica `Identidad/Authorization/PolicySetup.cs` (Principio I) ni agrega
  contratos; solo usa `BackofficeRoles`, que ya es público. Un 403 sigue pasando por
  `AuditingAuthorizationResultHandler` (global) y queda en la bitácora. El filtro por actor es la
  regla de datos; el rol es la regla de capacidad (FR-016).
- **Alternativas**: nueva constante `AuthPolicies.Requester` registrada en `PolicySetup` (misma
  forma que `Administrator`, pero obliga a tocar el interior de Identidad en cada feature con un
  rol nuevo); chequear el rol dentro del handler (la matriz de permisos no lo vería como metadata).

## R10. Copia del tercero en Casos

- **Decisión**: `casos.Casos` guarda `Cuit`, `RazonSocial` y `TipoTercero` copiados del tercero al
  abrir el caso. La lista y la búsqueda de "mis solicitudes" son una sola consulta en `casos`.
- **Rationale**: Principio II prohíbe join contra `terceros`; buscar por razón social a través del
  contrato obligaría a paginar en memoria. El CUIT es inmutable y ninguna feature edita la razón
  social todavía.
- **Alternativas**: pedir a Terceros los datos por id en cada listado (no permite buscar ni filtrar en
  SQL).

## R11. Pruebas end-to-end

- **Decisión**: se agrega Playwright (`@playwright/test`, en el stack) en `apps/backoffice/e2e` con
  un escenario: ingresar como solicitante → crear solicitud → verla en "mis solicitudes" en "Espera
  del tercero" → el correo llega a Mailpit (API de Mailpit `GET /api/v1/messages`). Corre contra el
  AppHost levantado (`BASE_URL`, `MAILPIT_URL` por variable de entorno).
- **Rationale**: la constitución exige Playwright para el flujo crítico de alta de tercero y esta es
  su primera pieza. El tramo del portal se agrega al mismo spec en RF-05.
- **Alternativas**: diferir Playwright hasta tener el flujo completo (incumple el quality gate).

## R12. Frontend

- **Decisión**: feature `apps/backoffice/src/features/solicitudes/` con `SolicitudesPage` (lista +
  filtro por estado + búsqueda) y `NuevaSolicitudPage` (React Hook Form + Zod). El aviso de tercero
  existente es una query de TanStack Query habilitada cuando el CUIT pasa la validación local, con
  debounce de 400 ms. Rutas `/solicitudes` y `/solicitudes/nueva`; ítem de menú visible con
  `hasRole(Solicitante)`.
- **Rationale**: mismo patrón que `users`. SC-002 (< 2 s) se cumple con una lectura por índice único.
- **Alternativas**: validar el duplicado solo al guardar (incumple FR-004 "antes de guardar").
