# Feature Specification: Solicitudes de alta de terceros

**Feature Branch**: `003-solicitudes`

**Created**: 2026-10-18

**Status**: Ready for planning

**Input**: User description: "Necesito que implemente los requerimiento funcionales \"5.1 Solicitudes\" como indica el documento @\"docs/Documento funcional app de debida diligencia de terceros.md\""

## Clarifications

### Session 2026-10-18

- Q: Si ya existe un tercero con el mismo CUIT y un caso abierto, ¿qué pasa con la nueva solicitud? → A: No se crea otro caso; el solicitante se suma al caso abierto como interesado y lo ve en sus solicitudes.
- Q: ¿El solicitante puede guardar un borrador sin enviar? → A: No; guardar siempre envía el enlace (RF-03). El estado Borrador no se usa en esta versión.

## User Scenarios & Testing *(mandatory)*

Esta feature cubre el inicio del proceso de debida diligencia (sección 5.1 del documento
funcional, RF-01 a RF-04): el solicitante (compras o comercial) pide el alta de un cliente o
proveedor, el sistema detecta si ese tercero ya existe, envía al tercero un enlace seguro al
portal y el solicitante sigue sus solicitudes. Lo que el tercero hace al abrir el enlace (ingreso
con código, formulario, documentos: RF-05 a RF-09) no forma parte de esta feature.

### User Story 1 - Crear una solicitud y enviar el enlace al tercero (Priority: P1)

El solicitante carga una solicitud con tipo de tercero (cliente o proveedor), tipo de persona
(humana o jurídica), CUIT, razón social, contacto, monto anual estimado y motivo. Al guardarla, el
sistema crea el caso de alta del tercero y le envía por correo un enlace seguro al portal; el caso
queda en "Espera del tercero" (RF-01, RF-03).

**Why this priority**: es la puerta de entrada de todo el proceso; sin solicitud no hay caso que
verificar, evaluar ni aprobar.

**Independent Test**: un usuario con rol solicitante crea una solicitud válida; el caso aparece en
"Espera del tercero" y el buzón del contacto recibe un correo con el enlace.

**Acceptance Scenarios**:

1. **Given** un usuario con rol solicitante, **When** completa todos los datos obligatorios y
   guarda, **Then** se crea el tercero y su caso de alta en "Espera del tercero", con el usuario
   como solicitante, y el contacto recibe un correo con el enlace al portal.
2. **Given** un CUIT con dígito verificador inválido, **When** el solicitante guarda, **Then** el
   sistema rechaza la solicitud indicando el error en el CUIT.
3. **Given** datos obligatorios faltantes o un correo de contacto mal formado, **When** el
   solicitante guarda, **Then** el sistema rechaza la solicitud indicando cada campo a corregir.
4. **Given** una solicitud enviada, **When** se consulta el enlace generado, **Then** es único
   para ese caso, no se puede deducir a partir de datos del tercero y vence al terminar el plazo de
   espera del tercero.
5. **Given** un usuario sin rol solicitante, **When** intenta crear una solicitud invocando
   directamente la acción, **Then** el servidor la rechaza.
6. **Given** cualquier solicitud creada, **When** se confirma, **Then** la bitácora registra quién
   la creó, cuándo, los datos cargados y el envío del enlace (sin el enlace en sí).

---

### User Story 2 - Aviso de tercero existente por CUIT (Priority: P1)

Mientras el solicitante carga la solicitud, el sistema le avisa si ya existe un tercero con el
mismo CUIT y le muestra su estado, para evitar altas duplicadas (RF-02).

**Why this priority**: es prioridad alta en el documento y evita casos duplicados que consumen
trabajo de compliance y fragmentan la evidencia.

**Independent Test**: con un tercero existente, iniciar una solicitud con su CUIT y verificar que se
muestra el aviso con su estado antes de guardar.

**Acceptance Scenarios**:

1. **Given** un tercero existente con un CUIT, **When** el solicitante ingresa ese CUIT,
   **Then** el sistema le muestra que el tercero ya existe, su razón social, su estado y, si tiene
   un caso abierto, la etapa de ese caso.
2. **Given** un tercero existente con un caso abierto (en espera del tercero, verificación o
   aprobación), **When** el solicitante intenta guardar una nueva solicitud para ese CUIT,
   **Then** no se crea otro caso ni se reenvía el enlace: el solicitante queda sumado al caso
   abierto como interesado, con su motivo registrado, y el caso aparece en sus solicitudes.
3. **Given** un tercero cuyo último caso terminó como abandonado, **When** el solicitante crea una
   nueva solicitud con ese CUIT, **Then** el sistema la permite y abre un nuevo caso de alta sobre
   el mismo tercero (sección 4: "se puede reabrir con una nueva solicitud").
4. **Given** el aviso de tercero existente, **When** se muestra, **Then** no incluye hallazgos,
   puntajes ni detalle de verificaciones (sección 3: el solicitante no ve hallazgos ni screening).

---

### User Story 3 - Consultar mis solicitudes (Priority: P2)

El solicitante ve la lista de sus solicitudes con el tercero, el estado actual del caso y una fecha
estimada de resolución (RF-04).

**Why this priority**: RF-04 es de prioridad media; el flujo funciona sin ella, pero reduce las
consultas por correo a compliance sobre "en qué está mi alta".

**Independent Test**: un solicitante con varias solicitudes en distintos estados ve solo las suyas,
cada una con su estado y fecha estimada.

**Acceptance Scenarios**:

1. **Given** un solicitante con solicitudes propias, **When** consulta "mis solicitudes",
   **Then** ve solo las que creó o a las que se sumó, con razón social, CUIT, tipo de tercero, estado, fecha de
   creación y fecha estimada de resolución.
2. **Given** solicitudes de otro solicitante, **When** el solicitante intenta verlas, incluso
   invocando directamente la consulta, **Then** el servidor no las devuelve.
3. **Given** un caso en estado final (aprobado, rechazado o abandonado), **When** aparece en la
   lista, **Then** muestra el resultado y la fecha en que terminó, en lugar de una fecha estimada.
4. **Given** un caso rechazado, **When** el solicitante lo consulta, **Then** ve el resultado pero
   no los hallazgos ni el comentario interno del aprobador.
5. **Given** muchas solicitudes, **When** el solicitante consulta la lista, **Then** puede filtrar
   por estado y buscar por razón social o CUIT.

---

### Edge Cases

- El CUIT se ingresa con o sin guiones o espacios; se normaliza a 11 dígitos antes de validar y de
  buscar duplicados.
- El prefijo del CUIT no coincide con el tipo de persona (20, 23, 24, 27 para persona humana; 30,
  33, 34 para persona jurídica): el sistema rechaza la solicitud indicando la inconsistencia.
- Dos solicitantes guardan al mismo tiempo una solicitud para un CUIT nuevo: queda un solo tercero
  y un solo caso; el segundo solicitante queda sumado como interesado.
- El envío del correo falla (buzón inexistente, servicio caído): la solicitud queda guardada, el
  envío se reintenta y, si no se logra, el solicitante ve en su lista que el enlace no se pudo
  entregar.
- El tercero existe pero está bloqueado: el aviso muestra el estado "bloqueado" y la nueva
  solicitud no se permite.
- El tercero existe y está activo (ya aprobado): el aviso lo informa y no se abre un nuevo caso de
  alta; una revisión extraordinaria es otra feature (RF-26).
- El monto anual estimado es cero o negativo: se rechaza.
- Un usuario con roles solicitante y analista: en "mis solicitudes" ve solo las que creó o a las
  que se sumó como solicitante.
- El solicitante intenta sumarse a un caso que ya creó o al que ya se sumó: no se duplica; se le
  indica que ya lo sigue.

## Requirements *(mandatory)*

### Functional Requirements

**Creación de la solicitud (RF-01)**

- **FR-001**: Un usuario con rol solicitante MUST poder crear una solicitud con: tipo de tercero
  (cliente o proveedor), tipo de persona (humana o jurídica), CUIT, razón social (o nombre y
  apellido si es persona humana), contacto (nombre y correo obligatorios, teléfono opcional), monto
  anual estimado en pesos y motivo.
- **FR-002**: El sistema MUST validar en el servidor que el CUIT tenga 11 dígitos, dígito
  verificador correcto y prefijo consistente con el tipo de persona; que el correo de contacto
  tenga formato válido; que el monto sea mayor a cero y que razón social, nombre de contacto y
  motivo no estén vacíos.
- **FR-003**: Al guardar una solicitud nueva para un CUIT inexistente, el sistema MUST crear el
  tercero y su caso de alta, registrando al usuario como solicitante del caso.

**Duplicados por CUIT (RF-02)**

- **FR-004**: El sistema MUST informar al solicitante, antes de guardar, si existe un tercero con el
  mismo CUIT, mostrando razón social, estado del tercero y etapa del caso abierto si lo hay.
- **FR-005**: El aviso MUST NOT exponer hallazgos, puntajes de riesgo, verificaciones ni comentarios
  internos.
- **FR-006**: El CUIT MUST ser único entre terceros: una solicitud para un CUIT existente MUST
  asociarse al tercero existente, nunca crear otro.
- **FR-007**: Si el tercero está bloqueado o activo, el sistema MUST rechazar una nueva solicitud de
  alta; si su último caso terminó abandonado o rechazado, MUST permitirla abriendo un nuevo caso de
  alta sobre el mismo tercero. Si tiene un caso abierto, MUST NOT abrir otro ni reenviar el enlace:
  el solicitante MUST quedar sumado a ese caso como interesado, con su motivo y monto registrados.

**Enlace seguro al tercero (RF-03)**

- **FR-008**: Guardar una solicitud que abre un caso MUST enviar el enlace en ese mismo acto (no
  hay borradores en esta versión). El sistema MUST enviar al correo de contacto un enlace al
  portal y el caso MUST pasar a "Espera del tercero".
- **FR-009**: El enlace MUST ser único por caso, imposible de adivinar o deducir de datos del
  tercero, y MUST vencer al cumplirse el plazo de espera del tercero (15 días hábiles por defecto,
  sección 8) o cuando el caso deja de estar en espera del tercero.
- **FR-010**: El enlace MUST NOT guardarse en claro ni aparecer en logs ni en la bitácora.
- **FR-011**: Si el correo no se puede entregar, la solicitud MUST seguir guardada, el envío MUST
  reintentarse y el fallo definitivo MUST quedar visible para el solicitante.

**Consulta de solicitudes (RF-04)**

- **FR-012**: El solicitante MUST poder listar las solicitudes que creó o a las que se sumó, con razón social, CUIT, tipo
  de tercero, estado del caso, fecha de su propia solicitud (la de creación o la de cuando se sumó) y fecha estimada de resolución, filtrando por
  estado y buscando por razón social o CUIT.
- **FR-013**: La fecha estimada de resolución MUST calcularse sumando a la fecha actual los plazos
  de las etapas que le faltan al caso según la sección 8 (espera del tercero 15, verificación 5,
  aprobación 3 días hábiles). Si una etapa ya superó su plazo, se toma el día actual como fin de
  esa etapa. Los casos en estado final muestran el resultado y la fecha de cierre.
- **FR-014**: El solicitante MUST ver solo los casos que creó o a los que se sumó; el filtro se aplica en el
  servidor.
- **FR-015**: El solicitante MUST ver el estado y el resultado final, pero no hallazgos, screening,
  puntajes ni comentarios internos.

**Control de acceso y auditoría**

- **FR-016**: La tabla de permisos de la feature 002 (FR-016 de `specs/002-backoffice-auth-rbac/spec.md`)
  MUST ampliarse con: "Crear solicitudes" y "Consultar mis solicitudes" para el rol solicitante. Los demás roles no obtienen estas
  capacidades en esta feature.
- **FR-017**: La creación de la solicitud, la creación del tercero, la apertura del caso, cada
  cambio de estado y cada envío o fallo de envío del enlace MUST registrarse en la bitácora con
  autor, fecha y hora, valor anterior y valor nuevo.

### Key Entities *(include if feature involves data)*

- **Tercero**: cliente o proveedor sujeto a diligencia. Tipo (cliente o proveedor), tipo de persona
  (humana o jurídica), CUIT único, razón social, estado (en alta, activo, bloqueado, inactivo).
  Tiene muchos casos.
- **Caso (de alta)**: instancia del proceso para un tercero. Tipo (alta), estado (sección 4),
  solicitante, fechas por etapa. Pertenece a un tercero.
- **Solicitud**: datos con que el solicitante inicia el caso de alta: contacto (nombre, correo,
  teléfono), monto anual estimado, motivo, fecha de creación. Un caso de alta tiene una solicitud
  original (la que lo abrió y envió el enlace) y cero o más solicitudes de interesados que se
  sumaron después.
- **Enlace de acceso al portal**: credencial de un solo caso para que el tercero llegue al portal.
  Fecha de emisión, vencimiento, estado de entrega (pendiente, enviado, reintentando, fallido). Nunca se guarda
  en claro.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un solicitante crea y envía una solicitud completa en menos de 3 minutos.
- **SC-002**: El aviso de tercero existente aparece en menos de 2 segundos de ingresado el CUIT.
- **SC-003**: El 100 % de las solicitudes con CUIT existente quedan asociadas al tercero existente;
  no hay dos terceros con el mismo CUIT.
- **SC-004**: El 95 % de los correos con el enlace llega al tercero en menos de 5 minutos desde que
  se guarda la solicitud.
- **SC-005**: Ninguna consulta de "mis solicitudes" devuelve solicitudes de otro usuario ni datos de
  hallazgos o riesgo, verificado probando con dos solicitantes distintos.
- **SC-006**: El 100 % de las solicitudes y sus cambios de estado pueden reconstruirse desde la
  bitácora.

## Assumptions

- El ingreso del tercero al portal con el enlace y el código por correo (RF-05), el formulario
  (RF-06 a RF-09) y el cierre automático del caso como abandonado al vencer el plazo (sección 6.3)
  son features aparte; esta feature solo emite el enlace con su vencimiento. La regla "el enlace
  deja de valer cuando el caso sale de espera del tercero" (FR-009) la aplica RF-05 al validar el
  enlace; esta feature solo guarda los datos que la hacen posible (vencimiento y caso).
- Los recordatorios al tercero a los 3 días y el aviso al solicitante a los 7 (sección 8) quedan
  para la feature de notificaciones de plazos.
- Los plazos (15, 5 y 3 días hábiles) son los propuestos en la sección 8 y se toman como valores
  fijos hasta que RF-30 permita configurarlos. Los días hábiles son lunes a viernes; los feriados
  no se descuentan en esta versión.
- El monto anual estimado se expresa en pesos argentinos.
- Las solicitudes iniciadas por esta feature son siempre casos de alta; las revisiones periódicas
  y extraordinarias (RF-25, RF-26) son otras features.
- La autenticación, los roles, la bitácora y el canal de correo de la feature 002 se reutilizan.
- El estado Borrador de la sección 4 no se usa en esta versión: guardar siempre envía.
- El solicitante no puede editar ni cancelar una solicitud ya enviada; reenviar el enlace o cambiar
  el correo de contacto queda para una feature posterior.
- Otros roles (analista, auditor) consultan casos desde sus propias features (bandeja de
  verificación, auditoría).
