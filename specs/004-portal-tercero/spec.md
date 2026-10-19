# Feature Specification: Portal del tercero

**Feature Branch**: `004-portal-tercero`

**Created**: 2026-10-18

**Status**: Ready for planning

**Input**: User description: "Necesito que implementes los requerimientos funcionales \"5.2 Portal del tercero\" tal como indica el documento @\"docs/Documento funcional app de debida diligencia de terceros.md\""

## Clarifications

### Session 2026-10-18

- Q: RF-09 (responder pedidos de información) depende de RF-14 (el analista pide información, sección 5.3), que no existe todavía. ¿Qué alcance tiene esta feature? → A: RF-09 sale de esta feature y se hace junto con RF-14 en la feature de verificación. Esta feature cubre RF-05 a RF-08.

## User Scenarios & Testing *(mandatory)*

Esta feature cubre lo que hace el tercero (cliente o proveedor, usuario externo) desde que abre el
enlace que recibió por correo (feature 003, RF-03) hasta que entrega su información a compliance
(sección 5.2 del documento funcional, RF-05 a RF-08): ingreso con enlace y código, formulario según
tipo de persona, carga de documentos y guardado parcial. La respuesta a pedidos de información
(RF-09) se hace junto con RF-14 en la feature de verificación. El trabajo del analista sobre el
caso (bandeja, checklist, hallazgos: RF-10 a RF-14) no forma parte de esta feature.

### User Story 1 - Ingresar al portal con enlace y código (Priority: P1)

El tercero abre el enlace que recibió por correo. El portal le pide un código de un solo uso que el
sistema le envía en ese momento al mismo correo. Con enlace vigente y código correcto, el tercero
entra al portal y ve solo su caso (RF-05).

**Why this priority**: sin ingreso seguro no hay portal; es la base de todas las demás historias y
la principal protección de los datos del tercero.

**Independent Test**: con un caso en "Espera del tercero", abrir su enlace, recibir el código en el
buzón del contacto, ingresarlo y ver la pantalla de inicio del caso.

**Acceptance Scenarios**:

1. **Given** un caso en "Espera del tercero" con enlace vigente, **When** el tercero abre el enlace,
   **Then** el portal le informa que se envió un código a su correo (mostrando el correo
   parcialmente enmascarado) y le pide ingresarlo.
2. **Given** un código recién enviado, **When** el tercero lo ingresa dentro de su vigencia,
   **Then** entra al portal y ve la razón social, el estado de su caso y las tareas pendientes.
3. **Given** un código incorrecto, **When** el tercero lo ingresa, **Then** se rechaza indicando que
   el código no es válido; al quinto intento fallido el código se invalida y debe pedir uno nuevo.
4. **Given** un enlace vencido, o un caso que ya no está en "Espera del tercero", **When** el tercero
   lo abre, **Then** el portal le informa que el enlace no es válido, sin revelar datos del caso, y
   le indica que se comunique con quien le pidió el alta.
5. **Given** un tercero que no recibió el código, **When** pide reenviarlo, **Then** se envía un
   código nuevo (el anterior deja de valer), con un límite de reenvíos por período.
6. **Given** un tercero dentro del portal, **When** intenta acceder a datos de otro caso, incluso
   invocando directamente la acción, **Then** el servidor la rechaza.
7. **Given** cualquier ingreso exitoso o fallido, **When** ocurre, **Then** la bitácora lo registra
   sin el código ni el enlace.

---

### User Story 2 - Completar el formulario según tipo de persona y enviarlo (Priority: P1)

Dentro del portal, el tercero completa un formulario que se adapta a si es persona humana o
jurídica: datos fiscales, domicilio, actividad, beneficiarios finales, declaración PEP y origen de
fondos. Al enviarlo completo junto con los documentos obligatorios (historia 3), el caso pasa a
"En verificación" (RF-06).

**Why this priority**: es el objetivo del portal; sin formulario completo el caso no puede avanzar a
verificación.

**Independent Test**: un tercero persona jurídica y otro persona humana completan cada uno su
formulario, lo envían y sus casos pasan a "En verificación" con los datos cargados.

**Acceptance Scenarios**:

1. **Given** un tercero persona jurídica, **When** abre el formulario, **Then** ve las secciones
   datos fiscales, domicilio, actividad, beneficiarios finales, declaración PEP y origen de fondos.
2. **Given** un tercero persona humana, **When** abre el formulario, **Then** ve datos fiscales,
   domicilio, actividad, declaración PEP propia y origen de fondos, sin la sección de beneficiarios
   finales.
3. **Given** un tercero persona jurídica, **When** carga beneficiarios finales, **Then** puede
   agregar uno o más, cada uno con nombre, documento, nacionalidad, porcentaje de participación y
   si es PEP.
4. **Given** un formulario con campos obligatorios vacíos o inválidos, **When** el tercero intenta
   enviarlo, **Then** el sistema lo rechaza indicando cada campo a corregir y por qué.
5. **Given** un formulario completo y todos los documentos obligatorios cargados, **When** el tercero
   lo envía y confirma, **Then** el caso pasa a "En verificación", el formulario queda en solo
   lectura para el tercero y el portal le confirma la recepción.
6. **Given** un formulario enviado, **When** se registra el envío, **Then** la bitácora guarda quién
   envió, cuándo y el cambio de estado del caso.
7. **Given** la razón social y el CUIT cargados por el solicitante, **When** el tercero ve el
   formulario, **Then** se muestran como datos precargados no editables.

---

### User Story 3 - Subir documentos (Priority: P1)

El tercero sube los documentos que se le piden, en PDF o imagen, con un tamaño máximo configurable
(RF-07).

**Why this priority**: los documentos son la evidencia que el analista verifica; un caso sin
documentos obligatorios no puede avanzar (sección 6.3).

**Independent Test**: subir un PDF y una imagen válidos y ver que quedan asociados al caso; intentar
subir un archivo de otro tipo o demasiado grande y ver que se rechaza.

**Acceptance Scenarios**:

1. **Given** el formulario de un tercero, **When** ve la sección de documentos, **Then** ve la lista
   de documentos requeridos para su tipo de persona, indicando cuáles son obligatorios.
2. **Given** un archivo PDF, JPG o PNG dentro del tamaño máximo, **When** el tercero lo sube para un
   documento requerido, **Then** queda asociado al caso y al tercero con su tipo, fecha de carga y
   nombre original.
3. **Given** un archivo de otro tipo, o con extensión permitida pero contenido que no corresponde,
   **When** el tercero lo sube, **Then** se rechaza indicando los formatos aceptados.
4. **Given** un archivo que supera el tamaño máximo vigente, **When** el tercero lo sube, **Then** se
   rechaza indicando el máximo permitido.
5. **Given** un documento ya cargado y el formulario aún no enviado, **When** el tercero lo
   reemplaza o lo elimina, **Then** queda la nueva versión y la bitácora registra el cambio.
6. **Given** el tamaño máximo configurado por un administrador, **When** se cambia, **Then** las
   cargas siguientes usan el nuevo valor y las ya realizadas no se ven afectadas.

---

### User Story 4 - Guardar a medias y retomar (Priority: P2)

El tercero puede dejar el formulario incompleto y volver más tarde sin perder lo cargado (RF-08).

**Why this priority**: prioridad media; el flujo funciona sin ella, pero el formulario es largo y
suele requerir buscar documentos, así que reduce abandonos.

**Independent Test**: cargar parte del formulario y un documento, salir, volver a ingresar con el
enlace y un código nuevo y ver todo lo cargado.

**Acceptance Scenarios**:

1. **Given** un formulario incompleto, **When** el tercero guarda, **Then** el sistema guarda lo
   cargado aunque falten campos obligatorios y le confirma el guardado.
2. **Given** un formulario guardado a medias, **When** el tercero vuelve a ingresar con el mismo
   enlace (todavía vigente) y un código nuevo, **Then** ve los datos y documentos que había cargado.
3. **Given** cambios sin guardar, **When** el tercero intenta salir, **Then** el portal le avisa que
   perderá esos cambios.
4. **Given** un formulario guardado a medias, **When** vence el enlace, **Then** el tercero ya no
   puede ingresar (el cierre por abandono es de otra feature).

---

### Edge Cases

- El tercero abre el enlace desde otro dispositivo o navegador: puede ingresar igual, siempre con un
  código nuevo.
- El tercero pide muchos códigos seguidos: tras el límite de reenvíos debe esperar antes de pedir
  otro; el intento queda en la bitácora.
- Muchos intentos fallidos desde distintos códigos sobre el mismo enlace: el enlace se bloquea
  temporalmente para evitar adivinación.
- La sesión del portal queda inactiva: vence por inactividad; lo guardado se conserva y para seguir
  debe volver a ingresar con código.
- Un tercero persona humana declara ser PEP: la declaración se guarda tal cual; el efecto en el
  riesgo (nivel mínimo alto) lo aplica la feature de evaluación de riesgo.
- Persona jurídica sin beneficiarios finales identificables: el tercero puede declararlo
  explícitamente con una explicación; no se lo obliga a inventar datos (sección 6.3: la regla de
  aprobación la aplica otra feature).
- La suma de porcentajes de beneficiarios finales supera el 100 %: se rechaza.
- Un archivo truncado (PDF sin cierre, imagen incompleta): se rechaza indicando que no se puede
  leer. Un PDF dañado por dentro o protegido con contraseña de apertura se acepta en la carga y lo
  detecta la verificación (ver Assumptions); uno con solo contraseña de propietario es válido.
- Dos pestañas abiertas guardando el mismo formulario: gana el último guardado y el portal avisa si
  los datos cambiaron desde que se abrió.
- El portal se usa desde un celular: todas las pantallas y la carga de archivos (incluida la cámara)
  funcionan en pantallas chicas (RNF-08).

## Requirements *(mandatory)*

### Functional Requirements

**Ingreso (RF-05)**

- **FR-001**: El tercero MUST ingresar al portal solo con un enlace vigente de su caso más un código
  de un solo uso enviado al correo de contacto del caso.
- **FR-002**: El código MUST ser numérico de 6 dígitos, vencer a los 10 minutos, invalidarse tras 5
  intentos fallidos o al emitirse uno nuevo, y no guardarse en claro.
- **FR-003**: El sistema MUST limitar los envíos de código (máximo 5 por enlace por hora) y los
  intentos de ingreso por enlace, bloqueando temporalmente el enlace ante abuso.
- **FR-004**: Un enlace vencido, revocado o de un caso que no está en "Espera del tercero" MUST ser
  rechazado con un mensaje genérico que no revele datos del caso.
- **FR-005**: La sesión del portal MUST limitarse al caso del enlace, vencer a los 30 minutos de
  inactividad y no ser aceptada por el backoffice.
- **FR-006**: El tercero MUST ver y modificar solo los datos de su propio caso; el filtro MUST
  aplicarse en el servidor. MUST NOT ver hallazgos, verificaciones, puntajes ni notas internas.

**Formulario (RF-06)**

- **FR-007**: El formulario MUST mostrar secciones según el tipo de persona del caso:
  - *Persona jurídica*: datos fiscales (CUIT y razón social precargados, condición frente al IVA,
    fecha de constitución, número de inscripción), domicilio fiscal y legal, actividad principal y
    país, beneficiarios finales, declaración PEP de beneficiarios y representantes, origen de fondos.
  - *Persona humana*: datos fiscales (CUIT y nombre precargados, condición frente al IVA, fecha de
    nacimiento, nacionalidad, documento), domicilio, actividad, declaración PEP propia, origen de
    fondos.
- **FR-008**: Cada beneficiario final MUST registrar nombre, documento, nacionalidad, porcentaje de
  participación y si es PEP. La suma de porcentajes MUST NOT superar 100 %. Si no hay beneficiarios
  identificables, el tercero MUST declararlo con una explicación.
- **FR-009**: La declaración PEP MUST pedir si la persona es PEP y, si lo es, cargo y organismo.
- **FR-010**: El sistema MUST validar en el servidor todos los campos al enviar el formulario.
- **FR-011**: Al enviar un formulario completo con todos los documentos obligatorios, el caso MUST
  pasar de "Espera del tercero" a "En verificación" y el enlace MUST dejar de servir para editar el
  formulario.
- **FR-012**: Después del envío el tercero MUST poder ver lo enviado pero no modificarlo durante la
  sesión en curso. Un ingreso posterior no es posible porque el caso ya no está en "Espera del
  tercero" (FR-004).

**Documentos (RF-07)**

- **FR-013**: El sistema MUST aceptar solo PDF, JPG y PNG, verificando el contenido real del archivo
  y no solo su extensión.
- **FR-014**: El tamaño máximo por archivo MUST ser configurable por un administrador (10 MB por
  defecto) y aplicarse en el servidor.
- **FR-015**: Cada documento MUST guardar tipo, nombre original, fecha de carga, cargado por (el
  tercero) y pertenecer al tercero y al caso.
- **FR-016**: Los documentos requeridos por tipo de persona MUST ser:
  - *Persona jurídica*: estatuto o contrato social (obligatorio), constancia de inscripción en ARCA
    (obligatorio), acta de designación de autoridades (obligatorio), documento del representante
    legal (obligatorio), último balance (opcional).
  - *Persona humana*: documento de identidad, frente y dorso (obligatorio), constancia de
    inscripción en ARCA (obligatorio), constancia de domicilio (opcional).
- **FR-017**: Antes de enviar el formulario, el tercero MUST poder reemplazar o eliminar los
  documentos que cargó; después, MUST NOT poder eliminarlos.
- **FR-018**: Los documentos MUST guardarse cifrados y su contenido MUST NOT aparecer en logs.

**Guardado parcial (RF-08)**

- **FR-019**: El tercero MUST poder guardar el formulario incompleto en cualquier momento antes de
  enviarlo y retomarlo en un ingreso posterior mientras el enlace esté vigente.
- **FR-020**: El portal MUST avisar al tercero antes de salir con cambios sin guardar.

**Auditoría y usabilidad**

- **FR-021**: Cada ingreso (exitoso o fallido), envío de código, guardado, carga o eliminación de
  documento, envío de formulario y cambio de estado del caso MUST registrarse en la bitácora con
  autor, fecha y hora, valor anterior y valor nuevo; nunca con el código, el enlace ni el contenido
  de archivos.
- **FR-022**: El portal MUST estar en español y ser usable desde el celular, incluida la carga de
  archivos desde la cámara (RNF-08).

### Key Entities *(include if feature involves data)*

- **Enlace de acceso al portal** (existe desde la feature 003): credencial de un caso con emisión,
  vencimiento y estado. Esta feature lo valida al ingresar.
- **Código de ingreso**: código de un solo uso asociado a un enlace; vencimiento, intentos fallidos,
  estado. Nunca en claro.
- **Sesión del portal**: acceso del tercero limitado a un caso, con vencimiento por inactividad.
- **Formulario del tercero**: datos cargados por el tercero para un caso (fiscales, domicilio,
  actividad, PEP, origen de fondos), estado (borrador, enviado), fecha de último guardado y de envío.
- **Beneficiario final**: nombre, documento, nacionalidad, porcentaje de participación, es PEP.
  Pertenece a un tercero.
- **Representante legal** (persona jurídica): nombre, documento y declaración PEP (FR-007, FR-009).
  Al menos uno.
- **Documento**: tipo, archivo, nombre original, fecha de carga, cargado por. Pertenece a un tercero
  y a un caso.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El 95 % de los terceros que abren un enlace vigente logra ingresar al portal al primer
  o segundo intento de código.
- **SC-002**: El 95 % de los códigos llega al buzón en menos de 1 minuto.
- **SC-003**: Un tercero con la documentación a mano completa y envía el formulario en menos de 20
  minutos, también desde un celular.
- **SC-004**: El 100 % de los casos que pasan a "En verificación" desde el portal tienen el
  formulario completo y todos los documentos obligatorios.
- **SC-005**: Ningún tercero puede ver ni modificar datos de otro caso, verificado probando con dos
  terceros distintos.
- **SC-006**: El 100 % de los archivos de tipo no permitido o mayores al máximo son rechazados.
- **SC-007**: Ningún dato guardado a medias se pierde entre ingresos mientras el enlace esté
  vigente.
- **SC-008**: El 100 % de los ingresos, guardados, cargas y envíos pueden reconstruirse desde la
  bitácora.

## Assumptions

- Esta feature parte del enlace emitido por la feature 003 (único por caso, no deducible, vence a
  los 15 días hábiles o cuando el caso sale de "Espera del tercero"). Validar esas reglas al abrirlo
  es parte de esta feature (RF-05).
- El código se envía al correo de contacto cargado por el solicitante; cambiarlo queda fuera de
  alcance.
- Los campos del formulario y la lista de documentos requeridos (FR-007, FR-016) son fijos en esta
  versión; hacerlos configurables es RF-30.
- El tamaño máximo de archivo es la única configuración de esta feature; se edita como parámetro
  del sistema por el administrador hasta que exista la pantalla de RF-30.
- Los efectos en el riesgo de la declaración PEP y de los beneficiarios (sección 6.3) los aplica la
  feature de evaluación de riesgo; esta feature solo captura los datos.
- RF-09 (responder pedidos de información) se hace junto con RF-14 en la feature de verificación.
  Esa feature reutiliza el ingreso (FR-001 a FR-006) y la carga de documentos (FR-013 a FR-018) de
  esta.
- El cierre del caso como abandonado y los recordatorios al tercero pertenecen a la feature de
  plazos y notificaciones.
- Extracción de texto, OCR o validación de vencimiento de documentos quedan fuera de esta feature.
- La carga verifica el tipo real y que el archivo esté completo (firma y cierre), sin leer su
  contenido. Un PDF dañado por dentro o con contraseña de apertura lo detecta la extracción de la
  feature de verificación, que lo pide de nuevo al tercero (RF-14).
- Los días hábiles son lunes a viernes, sin descontar feriados, igual que en la feature 003.
- La autenticación del portal es independiente de la del backoffice; la bitácora y el canal de
  correo existentes se reutilizan.
