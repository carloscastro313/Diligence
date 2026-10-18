# Feature Specification: Autenticación y control de acceso por roles (RBAC) del backoffice

**Feature Branch**: `002-backoffice-auth-rbac`

**Created**: 2026-10-18

**Status**: Ready for planning

**Input**: User description: "Necesito que se implemente el sistema de autenticacion usando RBAC basandote en los requerimiento RF-28, RF-29 y RF-30 del documento funcional @\"docs/Documento funcional app de debida diligencia de terceros.md\""

## Clarifications

### Session 2026-10-18

- Q: ¿Esta feature implementa también RF-29 (matriz) y RF-30 (formularios, checklists, documentos y plazos)? → A: No, solo RF-28; RF-29 y RF-30 quedan fuera de alcance.
- Q: ¿Método de ingreso de los usuarios internos? → A: Correo y contraseña propios de la aplicación, sin segundo factor por ahora.

## User Scenarios & Testing *(mandatory)*

Esta feature reemplaza los tokens de desarrollo de la feature 001 por un ingreso real de los
usuarios internos (solicitante, analista, aprobador, administrador, auditor) y le da al
administrador la gestión de usuarios, roles y áreas (RF-28). La edición de la matriz de riesgo
(RF-29), la configuración de formularios, checklists, documentos y plazos (RF-30) y el acceso del
tercero al portal (RF-05) no forman parte de esta feature.

### User Story 1 - Ingreso de un usuario interno (Priority: P1)

Un usuario interno ingresa al backoffice con sus credenciales, ve solo las secciones y acciones
que sus roles le permiten, y cierra sesión al terminar. Si su cuenta está desactivada, no puede
ingresar.

**Why this priority**: sin ingreso real nadie puede usar el backoffice fuera del entorno de
desarrollo; es la condición para todo lo demás.

**Independent Test**: con un usuario de prueba con un rol dado, ingresar, verificar que las
acciones de otros roles son rechazadas por el servidor y cerrar sesión.

**Acceptance Scenarios**:

1. **Given** un usuario activo con credenciales válidas, **When** ingresa, **Then** accede al
   backoffice y su sesión refleja todos sus roles.
2. **Given** credenciales inválidas, **When** intenta ingresar, **Then** el sistema rechaza el
   ingreso con un mensaje genérico que no revela si el correo existe.
3. **Given** un usuario desactivado, **When** intenta ingresar, **Then** el ingreso es rechazado.
4. **Given** un usuario con sesión abierta, **When** cierra sesión, **Then** cualquier acción
   posterior con esa sesión es rechazada.
5. **Given** un usuario con rol solicitante, **When** invoca directamente una acción reservada al
   administrador, **Then** el servidor la rechaza aunque la pantalla no la muestre.
6. **Given** una sesión del portal del tercero, **When** se usa contra el backoffice, **Then** es
   rechazada.

---

### User Story 2 - El administrador gestiona usuarios, roles y áreas (Priority: P1)

El administrador da de alta usuarios internos con nombre, correo y área; les asigna uno o más
roles; los edita, los desactiva y los reactiva. También mantiene el catálogo de áreas (RF-28).

**Why this priority**: es el requerimiento RF-28 (prioridad alta) y sin él no hay forma de que
compliance incorpore o retire personas sin intervención técnica.

**Independent Test**: el administrador crea un usuario con rol analista, el usuario ingresa; el
administrador le quita el rol o lo desactiva y el usuario pierde el acceso correspondiente.

**Acceptance Scenarios**:

1. **Given** un administrador, **When** crea un usuario con nombre, correo, área y al menos un
   rol, **Then** el usuario queda activo y puede establecer su credencial para ingresar.
2. **Given** un correo ya registrado, **When** el administrador intenta crear otro usuario con el
   mismo correo, **Then** el sistema lo rechaza.
3. **Given** un usuario con rol analista, **When** el administrador le agrega el rol aprobador,
   **Then** el usuario tiene ambos roles a partir de su siguiente acción.
4. **Given** un usuario con sesión abierta, **When** el administrador lo desactiva o le quita un
   rol, **Then** las acciones que dependían de ese acceso son rechazadas como máximo 5 minutos
   después.
5. **Given** que hay un único administrador activo, **When** se intenta desactivarlo o quitarle el
   rol administrador, **Then** el sistema lo impide.
6. **Given** un administrador, **When** crea, renombra o desactiva un área, **Then** el cambio se
   refleja al asignar áreas a usuarios; un área desactivada no puede asignarse a usuarios nuevos.
7. **Given** cualquier alta, cambio de roles, cambio de área, desactivación o reactivación,
   **When** se confirma, **Then** queda en la bitácora con quién lo hizo, cuándo, valor anterior y
   valor nuevo.
8. **Given** un usuario sin rol administrador, **When** intenta gestionar usuarios o áreas,
   **Then** el servidor lo rechaza.

---

### User Story 3 - Protección de la cuenta y trazabilidad del ingreso (Priority: P3)

El sistema protege las cuentas ante intentos repetidos, deja registro de ingresos y rechazos, y
permite a un usuario recuperar su acceso de forma segura.

**Why this priority**: refuerza la seguridad (RNF-01, RNF-02) pero el backoffice funciona sin ella
en un primer corte.

**Independent Test**: fallar el ingreso 5 veces seguidas y comprobar el bloqueo temporal; consultar
la bitácora y ver los intentos registrados.

**Acceptance Scenarios**:

1. **Given** 5 intentos fallidos seguidos para una cuenta, **When** se intenta un sexto,
   **Then** la cuenta queda bloqueada 15 minutos aunque la credencial sea correcta.
2. **Given** un ingreso exitoso o fallido, **When** ocurre, **Then** queda registrado con fecha,
   hora, usuario (si se identificó) y resultado, sin guardar la credencial.
3. **Given** un usuario que olvidó su credencial, **When** solicita recuperarla, **Then** recibe
   un enlace por correo de un solo uso que vence en 60 minutos.
4. **Given** un usuario sin actividad durante 30 minutos, **When** intenta una acción,
   **Then** debe volver a ingresar.

---

### Edge Cases

- El primer administrador: con la base vacía no hay quién cree usuarios; debe existir un paso
  inicial de instalación que cree el primer administrador.
- Un administrador intenta quitarse a sí mismo el rol administrador siendo el último: se impide
  (FR-010).
- Un usuario con varios roles (por ejemplo, analista y aprobador): recibe la unión de permisos; la
  segregación de funciones por caso no se resuelve acá sino en la feature de aprobación.
- El correo se compara sin distinguir mayúsculas ni espacios al inicio o final.
- Un usuario desactivado conserva su historial: sus acciones previas siguen en la bitácora con su
  identidad.
- Un usuario reactivado recupera los roles que tenía al desactivarse.
- Un área con usuarios asignados no puede eliminarse, solo desactivarse.
- Una petición sin sesión o con sesión vencida recibe "no autenticado"; una con sesión válida pero
  sin permiso recibe "acceso denegado", y ambos rechazos se distinguen.

## Requirements *(mandatory)*

### Functional Requirements

**Autenticación**

- **FR-001**: El sistema MUST autenticar a los usuarios internos del backoffice mediante
  correo y contraseña propios de la aplicación. La contraseña MUST tener al menos 12 caracteres y
  el usuario la establece él mismo con el enlace que recibe al ser dado de alta (nunca la conoce el
  administrador).
- **FR-002**: El sistema MUST rechazar el ingreso de usuarios inexistentes, desactivados o con
  credenciales inválidas con un mensaje único que no revele cuál de esas condiciones ocurrió.
- **FR-003**: Las credenciales MUST guardarse de forma irreversible (nunca en claro) y nunca
  aparecer en logs ni en la bitácora.
- **FR-004**: Las sesiones MUST vencer tras 30 minutos sin actividad y, en cualquier caso, a las 8
  horas del ingreso; el usuario MUST poder cerrar sesión voluntariamente.
- **FR-005**: Tras 5 intentos fallidos consecutivos, la cuenta MUST bloquearse durante 15 minutos.
- **FR-006**: Un usuario MUST poder recuperar el acceso mediante un enlace de un solo uso enviado
  a su correo, que vence a los 60 minutos.
- **FR-007**: Una sesión del portal del tercero MUST NOT ser aceptada en el backoffice y viceversa.

**Gestión de usuarios, roles y áreas (RF-28)**

- **FR-008**: El administrador MUST poder crear, editar, desactivar y reactivar usuarios internos
  con nombre, correo (único), área y uno o más roles.
- **FR-009**: Los roles asignables MUST ser los cinco roles internos del documento funcional:
  solicitante, analista de compliance, aprobador, administrador y auditor. Un usuario MUST tener al
  menos un rol para estar activo.
- **FR-010**: El sistema MUST impedir que quede cero administradores activos.
- **FR-011**: El administrador MUST poder crear, renombrar, desactivar y reactivar áreas; un área
  desactivada no se asigna a usuarios nuevos y un área con usuarios no se elimina.
- **FR-012**: Los cambios de roles, área o estado de un usuario MUST aplicarse a sus sesiones
  abiertas en un máximo de 5 minutos.
- **FR-013**: El administrador MUST poder listar usuarios filtrando por rol, área y estado.
- **FR-014**: Debe existir un procedimiento de instalación inicial que cree el primer
  administrador sin requerir otro administrador.

**Control de acceso (RBAC)**

- **FR-015**: Toda acción del backoffice MUST verificar en el servidor que el usuario tiene un rol
  habilitado para ella; ocultar opciones en pantalla es solo comodidad.
- **FR-016**: Los permisos por rol MUST seguir esta tabla base (los roles y su alcance son fijos en
  esta versión; el administrador asigna roles, no los redefine):

  | Capacidad | Solicitante | Analista | Aprobador | Administrador | Auditor |
  | --- | --- | --- | --- | --- | --- |
  | Gestionar usuarios y áreas (RF-28) | — | — | — | Sí | Consulta |
  | Consultar bitácora | — | — | — | Sí | Sí |
  | Crear solicitudes (RF-01..03) | Sí | — | — | — | — |
  | Consultar mis solicitudes (RF-04) | Sí | — | — | — | — |

  Las capacidades de casos (crear solicitudes, verificar, aprobar) y de configuración (matriz de
  riesgo RF-29; formularios, checklists, documentos y plazos RF-30) se agregan a esta tabla en sus
  propias features.
- **FR-017**: Un usuario con varios roles MUST recibir la unión de los permisos de sus roles.
- **FR-018**: Las peticiones sin sesión válida MUST rechazarse como "no autenticado" y las que
  tienen sesión sin permiso como "acceso denegado".

**Auditoría**

- **FR-019**: Toda alta, edición, cambio de roles, cambio de área, desactivación y reactivación de
  usuarios, y todo cambio de áreas, MUST registrarse en la bitácora con autor, fecha y hora, valor
  anterior y valor nuevo.
- **FR-020**: Los ingresos exitosos y fallidos, los bloqueos de cuenta, las recuperaciones de
  acceso y los rechazos por "acceso denegado" MUST registrarse en la bitácora.

### Key Entities *(include if feature involves data)*

- **Usuario**: persona interna que usa el backoffice. Nombre, correo único, área, roles, estado
  (activo o desactivado), fecha del último ingreso. Actúa en casos y eventos de auditoría.
- **Rol**: uno de los cinco roles internos fijos; determina las capacidades del usuario.
- **Área**: unidad organizativa a la que pertenece el usuario (por ejemplo, Compras, Comercial,
  Compliance). Nombre y estado.
- **Credencial**: el secreto o vínculo con el que el usuario se autentica; nunca se guarda en
  claro. Su forma depende del método de ingreso (FR-001).
- **Sesión**: ingreso vigente de un usuario, con su inicio, última actividad y vencimiento.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un usuario interno con credenciales válidas ingresa al backoffice en menos de 30
  segundos.
- **SC-002**: El administrador da de alta un usuario con roles y área en menos de 2 minutos, sin
  asistencia técnica.
- **SC-003**: El 100 % de las acciones del backoffice rechaza en el servidor a usuarios sin el rol
  requerido, verificado probando cada acción con cada rol de la tabla de FR-016.
- **SC-004**: El retiro de un rol o la desactivación de un usuario corta su acceso en 5 minutos o
  menos.
- **SC-005**: El 100 % de los cambios sobre usuarios, roles y áreas puede reconstruirse desde la
  bitácora (quién, cuándo, antes y después).
- **SC-006**: Ningún intento de ingreso fallido revela si el correo existe en el sistema.

## Assumptions

- RF-29 (matriz de riesgo con versionado) y RF-30 (formularios, checklists, documentos requeridos
  y plazos) quedan fuera de alcance; sus permisos se definen en sus propias features.
- El ingreso es con correo y contraseña propios de la aplicación; el segundo factor y el login
  corporativo (SSO) de RNF-01 quedan para una feature posterior, por lo que esta versión no cumple
  RNF-01 completo.
- Esta feature cubre solo a los usuarios internos del backoffice. El acceso del tercero al portal
  (enlace que vence y código por correo, RF-05) es una feature aparte.
- Los roles y sus permisos son fijos en esta versión; crear roles personalizados queda fuera de
  alcance.
- Los niveles de aprobación (analista senior, jefe de compliance, gerente, comité, sección 6.2) y
  la segregación de funciones caso por caso se resuelven en la feature de aprobaciones; acá solo
  se asigna el rol aprobador.
- La bitácora, las dos superficies (backoffice y portal) y la estructura de autenticación por
  superficie ya existen desde la feature 001 y se reutilizan.
- El envío de correos (alta y recuperación de acceso) usa el canal de correo de la aplicación; si
  todavía no existe, en local se usa un buzón de prueba.
- Los plazos de sesión (30 minutos / 8 horas), el bloqueo (5 intentos / 15 minutos), el
  vencimiento del enlace de recuperación (60 minutos) y el del enlace de alta para establecer la
  contraseña (72 horas; si vence, el usuario usa la recuperación de acceso) son valores estándar a
  validar con compliance.
