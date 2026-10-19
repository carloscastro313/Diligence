# Research: Portal del tercero

Decisiones de la fase 0. Ninguna NEEDS CLARIFICATION queda abierta.

## R1. Módulo dueño de cada parte

- **Decisión**:
  - **Casos**: ingreso con enlace y código (el enlace ya es suyo), formulario del tercero, catálogo
    de documentos requeridos por tipo de persona, transición `EsperaTercero → EnVerificacion` y
    todos los endpoints `/api/portal/*` de esta feature.
  - **Identidad**: sesión del portal (emitir, validar en cada request, inactividad). Ya es dueño de
    los dos esquemas JWT y de `IActorContext`; agregar sesiones de portal es una feature de
    Identidad, no tocar el interior de otro módulo.
  - **Documentos**: almacén de archivos (cifrado, verificación de contenido, tamaño máximo) y el
    parámetro de tamaño máximo. Genérico: lo reutilizan RF-09/RF-14 (pedidos del analista).
  - **Terceros**: sin cambios. Casos obtiene el tipo de persona con `ITerceroRegistry.FindByCuitAsync`
    (el caso ya guarda el CUIT).
- **Rationale**: el formulario es evidencia del caso (un caso de actualización futuro tendrá el suyo);
  copiarlo a la ficha del tercero al aprobar es de otra feature. Casos mapea los endpoints de
  documentos porque es quien sabe si el caso es editable y qué documentos pide; Documentos queda como
  almacén sin reglas de caso. Una sola dirección de dependencia: Casos → Documentos.Contracts.
- **Alternativas**: formulario en Terceros (obliga a versionar la ficha por caso); endpoints en
  Documentos (necesitaría preguntarle a Casos tipo de persona y editabilidad en cada request);
  sesión del portal en Casos con un filtro propio (cada módulo futuro con endpoints de portal
  tendría que repetirlo).

## R2. Sesión del portal (FR-005)

- **Decisión**: mismo diseño que el backoffice (feature 002, R1–R2). Tabla `identidad.PortalSessions`
  (caso, tercero, actividad, vencimiento, revocación). JWT del esquema `Portal` con `sub =
  tercero:{terceroId}` y `sid`; cookie `dlg_pt` `HttpOnly`, `Secure`, `SameSite=Strict`,
  `Path=/api/portal`. `OnTokenValidated` del esquema Portal carga la sesión, rechaza si venció,
  fue revocada o pasaron 30 min sin actividad, y agrega los claims `caso` y `tercero` desde la base
  (no desde el token). `TokenIssuer` pasa a recibir el esquema.
- Contratos nuevos en `Identidad.Contracts`: `IPortalSignIn` (crea la sesión dentro de la
  transacción del llamador, escribe la cookie y deja la request actuando como el tercero para la
  bitácora) e `IPortalActor` (caso y tercero de la sesión actual).
- **Rationale**: 30 min de inactividad exige estado en el servidor; el patrón ya existe y está
  probado. El backoffice no acepta el token por esquema/emisor distintos (ya verificado por
  `AuthenticationSurfaceTests`).
- **Alternativas**: JWT deslizante sin tabla (no se puede revocar ni medir inactividad sin reemitir
  en cada respuesta); sesión en Casos (ver R1).
- **Fuera de alcance**: botón "Salir" (la sesión vence sola a los 30 min). Vencimiento absoluto:
  8 h, como el backoffice.

## R3. Código de ingreso (FR-001, FR-002)

- **Decisión**: 6 dígitos con `RandomNumberGenerator.GetInt32(0, 1_000_000)`, vence a los 10 min,
  se invalida al 5.º intento fallido o al emitir otro. Se guarda `SHA-256(enlaceId ‖ código)` en
  `casos.CodigosIngreso`; comparación con `CryptographicOperations.FixedTimeEquals`.
- **Envío**: inline desde la API con `IEmailTransport`, como `ForgotPassword`. Si el SMTP falla, la
  API responde 503 y el tercero reintenta con "reenviar"; el código emitido queda anulado.
- **Rationale**: un código de 6 dígitos se puede invertir desde el hash, pero solo sirve junto con
  el token del enlace (256 bits, también hasheado) y vive 10 min: el hash cumple "no en claro" sin
  sumar una clave secreta. Inline da respuesta inmediata (SC-002) y evita que el código viaje en los
  argumentos de un job de Hangfire (quedaría en claro en la base).
- **Alternativas**: job de Hangfire que genera el código dentro del job, como el enlace en 003 (más
  latencia por el poll y un estado "pendiente" sin valor: el reintento lo hace el usuario); HMAC con
  clave (una clave más sin mejorar la seguridad real, ver arriba).

## R4. Límites y bloqueo (FR-003)

- **Decisión**, tres capas:
  1. Por IP: política de rate limiting `portal-acceso` (10/min) registrada por Casos en los dos
     endpoints anónimos.
  2. Envíos: máximo 5 códigos por enlace en la última hora (se cuentan filas de `CodigosIngreso`);
     el 6.º responde 429 con los minutos de espera y queda en la bitácora.
  3. Adivinación: `EnlacesPortal.FallidosConsecutivos`; al llegar a 10 (sumando todos los códigos)
     `BloqueadoHastaUtc = ahora + 30 min` y se reinicia el contador. Un ingreso exitoso lo pone en 0.
- Las tres decisiones sobre un enlace se toman con la fila del enlace bloqueada (`UPDLOCK`) en la
  transacción: dos requests simultáneas no superan los límites.
- **Rationale**: el límite por IP solo no frena a quien rota IPs; el contador por enlace sí.
  10 intentos sobre 10⁶ códigos posibles por bloqueo de 30 min hacen inviable adivinar.

## R5. Validez del enlace y mensajes (FR-004)

- **Decisión**: un enlace es válido si existe su hash, `VenceUtc > ahora`, el caso está en
  `EsperaTercero` y no está bloqueado. Inexistente, vencido o caso en otro estado responden igual:
  404 con "El enlace no es válido o venció. Comunicate con quien te pidió el alta." Bloqueado
  responde 429 (no revela datos del caso). El correo enmascarado (`c***@dominio.com`) solo se
  muestra con enlace válido.
- El token viaja en el cuerpo (`POST`), nunca en la URL de la API. El portal lo lee del fragmento
  `#t=`, lo guarda en `sessionStorage` y limpia el fragmento con `history.replaceState`.
- **Envío automático del código, una sola vez**: al abrir el enlace (token nuevo en el fragmento) el
  portal pide el código sin que el tercero haga nada (US1-1). Lo hace una sola vez por token y
  pestaña: marca `codigoPedido` en `sessionStorage` y protege con un `useRef` contra el doble montaje
  de React StrictMode. Recargar `/acceso`, o llegar desde una sesión vencida, no envía otro: muestra
  el campo de código y el botón "Reenviar código". Así un segundo envío no invalida el código que el
  tercero está leyendo ni consume el límite de 5 por hora.
- Sin token (ni en el fragmento ni en `sessionStorage`), `/acceso` muestra "Abrí el enlace que
  recibiste por correo" sin llamar a la API.
- **Después del envío** (FR-012): el tercero ve lo enviado en solo lectura durante la sesión en curso.
  Un ingreso nuevo con el enlace ya no es posible porque el caso salió de "Espera del tercero"
  (FR-004, FR-011). Aclarado en el spec.

## R6. Formulario como documento JSON (FR-007 a FR-010, FR-019)

- **Decisión**: `casos.FormulariosTercero` con una columna `Datos nvarchar(max)` (JSON) por caso,
  estado `Borrador`/`Enviado` y `rowversion`. El JSON se deserializa a un record tipado
  (`FormularioDatos`) con todos los campos opcionales. Dos validadores FluentValidation:
  - **Guardado** (borrador): solo forma (longitudes, formatos, enums, fechas válidas, porcentajes
    0–100) de lo que venga; nada obligatorio.
  - **Envío**: obligatorios según tipo de persona, reglas de beneficiarios (suma ≤ 100 % o
    declaración de "sin beneficiarios identificables" con explicación), PEP (si es PEP: cargo y
    organismo), y documentos obligatorios cargados.
- Errores con la ruta completa en camelCase (`beneficiarios[0].porcentaje`): `ApiProblems.Validation`
  pasa a convertir cada segmento, no solo el primero.
- **Rationale**: un borrador puede estar a medias en cualquier campo; con columnas tipadas todas
  serían nulables igual. Ningún otro proceso consulta campos sueltos en esta feature; la evaluación de
  riesgo los leerá por contrato desde el record tipado.
- **Alternativas**: tablas normalizadas (`Beneficiarios`, `Domicilios`…) — más migraciones y
  mapeo sin un consumidor que lo necesite hoy.

## R7. Concurrencia

- **Dos pestañas**: el `PUT` manda la `version` que leyó. Si no coincide se guarda igual (gana el
  último) y la respuesta trae `sobrescrito: true` para que el portal avise.
- **Carga de documento vs envío**: las dos operaciones bloquean la fila del caso (`UPDLOCK`) dentro de
  su transacción y verifican que siga editable; el almacén de documentos escribe en esa misma
  transacción (patrón de `IAuditLog`). Así no queda un documento cargado o borrado después del envío,
  ni un envío con un documento que se estaba borrando.

## R8. Almacenamiento y cifrado de documentos (FR-015, FR-018)

- **Decisión**: el archivo va a SQL Server, schema `documentos`: metadatos en `Documentos` y el
  contenido aparte en `DocumentoContenidos` (`varbinary(max)`), cifrado con AES-256-GCM
  (`System.Security.Cryptography.AesGcm`, nonce aleatorio de 12 bytes por archivo, tag de 16 bytes).
  Clave: `SHA-256(Documentos:EncryptionKey)` (secreto de configuración; en local, parámetro
  persistido del AppHost como las claves JWT). Un reemplazo o una eliminación dan de baja la fila
  vigente (`BajaUtc`, `Baja = Reemplazado|Eliminado`) sin borrar el contenido.
- **Rationale**: misma base, misma transacción y mismo backup que el resto; sin infraestructura
  nueva. El volumen es chico (cientos de casos al mes × ~6 archivos ≤ 10 MB). El contenido nunca pasa
  por `ILogger` ni por la bitácora (solo nombre, tipo, tamaño y hash).
- **Alternativas**: volumen en disco o blob storage con Azurite (otra pieza que levantar, respaldar y
  mantener consistente con la base); TDE de SQL Server (cifra el disco pero no protege de quien lee
  con la cuenta de la aplicación ni de los backups exportados).
- **Shortcut**: una sola clave sin `KeyId`; agregar la columna y el anillo de claves cuando haga
  falta rotar.
- **Principio VI**: recibir, hashear (SHA-256), cifrar (AES-GCM) y guardar cuesta lineal en el tamaño,
  pero es almacenamiento, no procesamiento del contenido: es lo mínimo que exige aceptar un archivo,
  corre a velocidad de E/S (milisegundos para 10 MB) y está acotado por el límite de 50 MB. El
  principio apunta a tareas como OCR, extracción o generación de PDF, cuyo costo es órdenes de
  magnitud mayor; esas siguen yendo al worker. Mover el guardado al worker obligaría a dejar el
  archivo en claro en algún lado mientras espera el job, contra FR-018.

## R9. Verificación del contenido (FR-013, edge case de PDF ilegible)

- **Decisión**: verificación acotada en la API, independiente del tamaño del archivo (Principio VI):
  - PDF: empieza con `%PDF-` y tiene `%%EOF` en los últimos 1024 bytes.
  - JPEG: `FF D8 FF` al inicio y `FF D9` al final. PNG: firma de 8 bytes y chunk `IEND` al final.
  - Lo que no coincide con ninguno se rechaza como "formato no permitido" sin mirar la extensión;
    el `ContentType` guardado es el detectado, no el que mandó el navegador.
- **Cifrados**: no se rechaza por `/Encrypt`. Un PDF con contraseña de propietario (solo restringe
  imprimir o editar; se abre sin contraseña) también tiene `/Encrypt`, y muchas constancias
  oficiales vienen así: rechazarlo bloquearía documentos válidos. Distinguir la contraseña de apertura
  exige leer el diccionario de cifrado y probar la contraseña vacía, es decir, parsear el PDF.
- **Rationale**: detecta en la carga los casos del spec que se ven en firma y cola (otro tipo,
  extensión cambiada, truncado) sin leer el contenido en la API.
- **Shortcut**: un PDF dañado por dentro o con contraseña de apertura se acepta; lo detectará la
  extracción con PdfPig en el worker (feature de verificación), que marcará el documento como
  ilegible y lo pedirá de nuevo al tercero (RF-14). Supuesto agregado al spec.
- **Alternativas**: abrir con PdfPig en la API (parseo proporcional al documento, contra el
  Principio VI); validación asíncrona en el worker (estado "validando" y polling en el portal para un
  caso raro).

## R10. Tamaño máximo configurable (FR-014)

- **Decisión**: tabla de una fila `documentos.Configuracion` (`TamanoMaximoBytes`, 10 MB sembrado en
  la migración). Endpoints de backoffice `GET`/`PUT /api/backoffice/documentos/configuracion` con la
  policy `Administrator`, en MB enteros de 1 a 50, con bitácora. Sin pantalla hasta RF-30.
- El endpoint de carga tiene un límite fijo de request de 55 MB (`RequestSizeLimit`), corta primero
  por `IFormFile.Length` contra el valor vigente (413 con el máximo en el mensaje) y vuelve a medir
  los bytes leídos.
- Las cargas ya hechas no se revalidan (no hay nada que hacer con ellas).

## R11. Bitácora (FR-021)

- Acciones existentes, sin cambiar el enum de `Auditoria.Contracts`:

| Evento | Acción | Entidad |
|---|---|---|
| Código enviado | `Created` | `CodigoIngreso` (enlace, vencimiento; nunca el código) |
| Límite de envíos | `AccessDenied` | `EnlacePortal` (`Motivo = LimiteCodigos`) |
| Ingreso exitoso | `LoginSucceeded` | `EnlacePortal` (+ `Created` de la sesión en Identidad) |
| Ingreso fallido | `LoginFailed` | `EnlacePortal` o `desconocido` (`Motivo`) |
| Enlace bloqueado | `AccountLocked` | `EnlacePortal` (`BloqueadoHasta`) |
| Guardado | `Updated` | `FormularioTercero`: un cambio por campo modificado (ruta JSON, anterior, nuevo) |
| Envío | `Updated` | `FormularioTercero` (`Estado`) y `Caso` (`Estado`) |
| Carga / reemplazo / eliminación | `Created` / `Updated` | `Documento` (tipo, nombre, tamaño, SHA-256, `Baja`) |
| Tamaño máximo | `Updated` | `ConfiguracionDocumentos` |

- El diff del formulario aplana ambos JSON a `ruta → valor` (`beneficiarios[0].porcentaje`) y
  registra solo las rutas distintas. El token del enlace y el código nunca llegan a la bitácora (el
  enmascarador además oculta cualquier campo que contenga "token").

## R12. Frontend del portal (FR-020, FR-022)

- Dependencias nuevas en `apps/portal`, todas del stack y ya usadas por el backoffice:
  `react-hook-form`, `@hookform/resolvers`, `zod`, `react-dropzone`; dev: `@playwright/test`.
- Carga de archivos: `react-dropzone` (`useDropzone`) en cada documento requerido, con
  `accept: { 'application/pdf': ['.pdf'], 'image/jpeg': ['.jpg', '.jpeg'], 'image/png': ['.png'] }`,
  `maxFiles: 1`, `multiple: false` y `maxSize` = `tamanoMaximoBytes` de `GET /caso/documentos`. En
  escritorio se arrastra y suelta o se hace clic; en el celular el `<input>` que genera
  `getInputProps()` hace que el sistema ofrezca cámara o archivos. Los rechazos de `react-dropzone`
  (`fileRejections`) dan aviso inmediato en español; la API vuelve a verificar tipo real y tamaño
  (Principio VII). Los tests de componente usan `fireEvent.drop`/`userEvent.upload` sobre el input.
- Cliente HTTP: copia de `apps/backoffice/src/shared/api.ts` en `apps/portal/src/shared/api.ts` con
  base `/api/portal` y soporte de `FormData` (el portal no puede importar el backoffice).
  Shortcut: ~35 líneas duplicadas; moverlo a `packages/api-client` cuando aparezca un tercer uso.
- Aviso de salida: `useBlocker` de TanStack Router + `beforeunload` mientras `formState.isDirty`.
- Una sola feature `features/caso` (inicio, formulario, documentos) y `features/acceso`; Zod repite
  las reglas solo para UX (Principio VII).
- Países: códigos ISO 3166-1 alfa-2 fijos en `shared/paises.ts`, nombres con `Intl.DisplayNames('es')`.

## R13. Pruebas

- Integración (xUnit + Testcontainers + `FakeTimeProvider` + `FakeEmailTransport`): validez del
  enlace, código (vencimiento, 5 intentos, reemplazo), límites y bloqueo, sesión (inactividad, no
  aceptada en backoffice, aislamiento entre dos terceros: SC-005), guardado y retomado, envío
  (validación por persona, documentos obligatorios, transición, solo lectura), documentos (tipo
  real, tamaño, PDF cifrado/truncado, reemplazo, eliminación bloqueada tras el envío, cifrado en
  reposo), configuración, bitácora y ausencia de código/token/contenido en logs.
- Rate limiting en tests: en TestServer todas las requests comparten la IP `unknown`, así que la
  factory compartida fija `Casos:AccesoRateLimitPerMinute` alto y el test del límite por IP usa una
  factory propia con el valor real (mismo problema que el límite de autenticación del backoffice).
- Unitarios: validadores del formulario, inspector de contenido, cifrado ida y vuelta, diff JSON,
  enmascarado del correo.
- Superficie: los dos endpoints anónimos se suman a la lista cerrada de `EndpointSurfaceTests`.
- Playwright (flujo crítico "carga de documentos en portal"): crea un caso por la API del
  backoffice, lee enlace y código de Mailpit, completa el formulario de persona jurídica, sube los
  documentos y envía; un segundo test con viewport de celular.
