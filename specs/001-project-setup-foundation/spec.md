# Feature Specification: Setup base del proyecto (estructura, base de datos, logs y auditoría)

**Feature Branch**: `001-project-setup-foundation`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "Empieza con el setup del proyecto, creando las soluciones, estructura de carpetas, conexion a base de datos, configuracion de logs y auditorias."

## Clarifications

### Session 2026-10-16

- Q: ¿Cómo se registra el evento de auditoría en la misma transacción que la acción de negocio, si Auditoría y cada módulo tienen su propio schema y DbContext? → A: Transacción compartida: el contrato público de Auditoría recibe la conexión y la transacción activas del módulo llamador y el DbContext de Auditoría se enlista en ellas (atómico y síncrono).
- Q: ¿Cómo impide la base de datos modificar o borrar eventos de auditoría con la identidad de la aplicación, si esa identidad también aplica migraciones? → A: Dos identidades: un login migrador (dueño de los schemas) aplica las migraciones; API y worker usan un login de runtime con DENY UPDATE/DELETE sobre la tabla de auditoría.
- Q: ¿Cuál es el "único comando" que levanta la solución en local: Aspire AppHost o Docker Compose? → A: Aspire AppHost (`dotnet run` del AppHost); SQL Server en contenedor sobre Docker Engine/WSL; API, worker y frontends tienen Dockerfile que se construye en CI.
- Q: ¿Cómo obtienen desarrolladores y tests un token de backoffice o portal sin el login real? → A: Sin endpoint de emisión: en local `dotnet user-jwts` (emisor/audiencia por superficie, roles como claims); los tests de integración firman tokens con un helper y una clave de test.

## User Scenarios & Testing *(mandatory)*

Esta feature no entrega funcionalidad de negocio visible para compliance. Entrega la base sobre la
que se construyen todas las demás: un entorno que cualquier desarrollador levanta de forma
reproducible, persistencia separada por módulo, registros técnicos para operar el sistema y una
bitácora de auditoría inalterable. Los "usuarios" de esta feature son el equipo de desarrollo, quien
opera el sistema y, a través de la bitácora, el auditor.

### User Story 1 - Levantar la solución completa desde cero (Priority: P1)

Un desarrollador recién incorporado clona el repositorio, ejecuta un único comando documentado y
obtiene la solución completa funcionando en su máquina: la API (con sus dos superficies, backoffice
y portal), el proceso worker, la base de datos y las dos aplicaciones web (backoffice y portal).
Cada pieza reporta que está sana.

**Why this priority**: sin un entorno reproducible no se puede construir ni probar ninguna otra
feature. Es el prerequisito de todo lo demás.

**Independent Test**: en una máquina con solo los prerequisitos documentados instalados, clonar el
repositorio, ejecutar el comando de arranque y verificar que cada componente responde su chequeo de
salud y que las dos aplicaciones web cargan una página inicial.

**Acceptance Scenarios**:

1. **Given** una máquina limpia con los prerequisitos documentados, **When** el desarrollador
   ejecuta el comando de arranque, **Then** API, worker, base de datos, backoffice y portal quedan en
   ejecución y cada uno responde "sano" en su chequeo de salud.
2. **Given** la solución en ejecución, **When** se consulta el chequeo de salud de la API, **Then**
   informa por separado el estado de la conexión a la base de datos.
3. **Given** el repositorio recién clonado, **When** se ejecuta el comando de build y tests,
   **Then** compila sin errores y la suite (incluidos los tests de arquitectura) pasa.
4. **Given** la estructura inicial, **When** se inspecciona el repositorio, **Then** existen los
   módulos de negocio iniciales vacíos (cada uno con su parte pública de contratos y su parte
   interna) y las dos aplicaciones web organizadas por features, siguiendo la constitución.

---

### User Story 2 - Persistencia separada por módulo (Priority: P1)

Cada módulo de negocio guarda sus datos en su propio espacio aislado dentro de la base de datos y
gestiona su propia evolución de esquema. Al arrancar el entorno local, la base se crea y los
esquemas de cada módulo quedan al día sin pasos manuales.

**Why this priority**: la separación de datos por módulo es un principio no negociable de la
constitución; establecerla al inicio evita migraciones costosas después.

**Independent Test**: arrancar el entorno con una base vacía y verificar que existe un espacio de
datos por cada módulo, que cada uno tiene su historial de cambios de esquema propio y que un test
de arquitectura falla si un módulo intenta usar el acceso a datos de otro.

**Acceptance Scenarios**:

1. **Given** una base de datos vacía, **When** se arranca el entorno local, **Then** se crea un
   espacio de datos por módulo y se aplican los cambios de esquema pendientes de cada uno.
2. **Given** un módulo que agrega un cambio de esquema, **When** se aplica, **Then** solo afecta el
   espacio de datos de ese módulo.
3. **Given** código de un módulo que referencia el acceso a datos de otro módulo, **When** corren
   los tests de arquitectura, **Then** fallan e identifican la violación.
4. **Given** la base de datos no disponible, **When** la API arranca o atiende una solicitud,
   **Then** el chequeo de salud lo informa como no sano y el error queda registrado sin exponer
   credenciales.

---

### User Story 3 - Registros técnicos para operar y diagnosticar (Priority: P2)

Quien opera el sistema puede seguir una solicitud de punta a punta (desde que entra a la API hasta
que el worker procesa el trabajo derivado) mediante registros estructurados que comparten un
identificador de correlación, con niveles de detalle configurables por entorno y sin datos
sensibles.

**Why this priority**: es necesario para diagnosticar problemas desde la primera feature de negocio,
pero el sistema puede arrancar sin él.

**Independent Test**: enviar una solicitud a la API que encole un trabajo de prueba en el worker y
verificar que todos los registros de ambos procesos relacionados con esa solicitud se pueden
filtrar por un mismo identificador de correlación, y que ninguno contiene contraseñas, tokens ni
contenido de documentos.

**Acceptance Scenarios**:

1. **Given** una solicitud entrante, **When** la API la procesa, **Then** se registra al menos el
   método, la ruta, el resultado, la duración y un identificador de correlación que se devuelve al
   cliente en la respuesta.
2. **Given** una solicitud que trae su propio identificador de correlación, **When** la API la
   procesa, **Then** reutiliza ese identificador en lugar de generar uno nuevo.
3. **Given** una solicitud que encola trabajo en el worker, **When** el worker lo ejecuta, **Then**
   sus registros llevan el mismo identificador de correlación.
4. **Given** una solicitud con contraseña, token o archivo adjunto, **When** se registra, **Then**
   esos valores no aparecen en ningún registro.
5. **Given** un error no controlado, **When** ocurre, **Then** se registra con su detalle técnico y
   el cliente recibe una respuesta de error genérica con el identificador de correlación, sin
   detalles internos.
6. **Given** un entorno configurado con un nivel de detalle distinto, **When** se cambia esa
   configuración, **Then** el volumen de registros cambia sin modificar código.

---

### User Story 4 - Bitácora de auditoría inalterable (Priority: P1)

Cualquier módulo puede registrar en la bitácora de auditoría una acción relevante (creación, cambio,
consulta de datos sensibles, descarga o decisión) con quién la hizo, cuándo, sobre qué entidad, y
los valores anterior y nuevo. Una vez escrito, ningún usuario, ni siquiera el administrador, puede
modificar ni borrar un evento. Un auditor puede consultar la bitácora filtrando por entidad,
usuario y rango de fechas.

**Why this priority**: el valor del producto es la evidencia auditable (constitución, Principio
VIII; documento funcional, sección 9). Todas las features de negocio dependen de poder registrar
eventos desde su primer día.

**Independent Test**: desde un módulo de prueba, registrar un cambio sobre una entidad ficticia;
verificar que el evento aparece en la consulta del auditor con todos sus campos, y que cualquier
intento de modificarlo o borrarlo, por la aplicación o directamente en la base, es rechazado.

**Acceptance Scenarios**:

1. **Given** un usuario autenticado que modifica una entidad, **When** el módulo registra el
   evento, **Then** la bitácora guarda fecha y hora, usuario, superficie de origen (backoffice o
   portal), acción, tipo e identificador de entidad, valor anterior, valor nuevo e identificador de
   correlación.
2. **Given** una acción de negocio cuyo guardado falla, **When** se revierte, **Then** no queda un
   evento de auditoría de esa acción; y si el registro del evento falla, la acción tampoco se
   confirma.
3. **Given** un evento ya registrado, **When** cualquier usuario o proceso intenta modificarlo o
   borrarlo, **Then** la operación es rechazada y el evento queda intacto.
4. **Given** un usuario con rol auditor, **When** consulta la bitácora filtrando por entidad,
   usuario o rango de fechas, **Then** obtiene los eventos coincidentes en orden cronológico.
5. **Given** un usuario sin rol auditor ni administrador, **When** intenta consultar la bitácora,
   **Then** el acceso es denegado.
6. **Given** un cambio que incluye datos sensibles (p. ej. contraseñas), **When** se registra,
   **Then** esos valores se guardan enmascarados y nunca en claro.

---

### Edge Cases

- ¿Qué pasa si la base de datos no está lista cuando arranca la API o el worker? Reintentan la
  conexión durante un tiempo acotado y reportan "no sano" hasta lograrla, sin terminar en un estado
  inconsistente.
- ¿Qué pasa si dos módulos aplican cambios de esquema al mismo tiempo al arrancar? Cada uno opera
  sobre su propio espacio y su propio historial; no hay bloqueo cruzado.
- ¿Qué pasa si una acción es ejecutada por un proceso del sistema (p. ej. un job del worker) y no
  por una persona? El evento se registra con un actor de sistema identificable, nunca vacío.
- ¿Qué pasa si los valores anterior/nuevo son muy grandes? Se guardan solo los campos que
  cambiaron, no el objeto completo ni contenido de archivos.
- ¿Qué pasa si el reloj de un proceso difiere del otro? Todas las fechas se registran en UTC con la
  hora del servidor que persiste el evento.
- ¿Qué pasa si falta una variable de configuración obligatoria (p. ej. cadena de conexión)? El
  proceso no arranca y lo informa con un mensaje claro que nombra la configuración faltante sin
  mostrar su valor.

## Requirements *(mandatory)*

### Functional Requirements

**Estructura y entorno**

- **FR-001**: El repositorio MUST contener una solución de backend con: un host de API, un proceso
  worker separado, los módulos de negocio iniciales (Terceros, Casos, Riesgo, Aprobaciones,
  Documentos, Auditoría, Identidad), cada uno dividido en una parte pública de contratos y una parte
  interna, y los proyectos de tests (unitarios, integración y arquitectura).
- **FR-002**: El repositorio MUST contener dos aplicaciones web (backoffice y portal) organizadas por
  features, y un área de paquetes compartidos que el portal pueda usar sin depender del backoffice.
- **FR-003**: La solución completa MUST poder levantarse en local con un único comando documentado:
  ejecutar el AppHost de .NET Aspire, que orquesta API, worker y las dos aplicaciones web y corre
  SQL Server en un contenedor sobre Docker Engine dentro de WSL, sin depender de herramientas de
  escritorio propietarias (Docker Desktop). API, worker, backoffice y portal MUST tener además un
  Dockerfile cuya imagen se construye en CI.
- **FR-004**: La API MUST exponer dos grupos de rutas separados, backoffice y portal, cada uno con su
  propio esquema de autenticación; un token de un grupo MUST ser rechazado en el otro. La API MUST
  NOT exponer ningún endpoint de emisión de tokens en esta feature: en local los tokens se generan
  con `dotnet user-jwts` y en los tests de integración con un helper que firma con una clave de
  test.
- **FR-005**: API y worker MUST exponer un chequeo de salud que informe por separado el estado del
  proceso y de la base de datos.
- **FR-006**: La configuración (cadenas de conexión, secretos, niveles de registro) MUST leerse del
  entorno y no estar versionada en claro en el repositorio; si falta una obligatoria, el proceso
  MUST negarse a arrancar con un mensaje que nombre la clave faltante.
- **FR-007**: El repositorio MUST incluir tests de arquitectura y reglas de lint que fallen ante
  violaciones de fronteras entre módulos, entre features del frontend y entre portal y backoffice.

**Base de datos**

- **FR-008**: Cada módulo MUST tener su propio contexto de acceso a datos y su propio espacio
  (schema) en la base de datos, con su propio historial de cambios de esquema.
- **FR-009**: En el entorno local, la base y los cambios de esquema pendientes de todos los módulos
  MUST aplicarse automáticamente al arrancar, incluida la creación de los logins migrador y de
  runtime (FR-019); en otros entornos MUST poder aplicarse como paso explícito y separado. Las
  migraciones siempre se ejecutan con el login migrador, nunca con el de runtime.
- **FR-010**: API y worker MUST reintentar la conexión a la base de datos ante fallos transitorios
  durante un tiempo acotado.
- **FR-011**: Todas las entidades persistidas MUST registrar fecha de creación y de última
  modificación en UTC, excepto el evento de auditoría, que es inmutable: su única fecha es la de
  ocurrencia (FR-017) y hace de fecha de creación.

**Registros técnicos (logs)**

- **FR-012**: API y worker MUST emitir registros estructurados (campos con nombre, no solo texto
  libre) con nivel, fecha UTC, proceso de origen, módulo e identificador de correlación.
- **FR-013**: La API MUST asignar a cada solicitud un identificador de correlación (o reutilizar el
  recibido), devolverlo en la respuesta y propagarlo al trabajo que encola en el worker.
- **FR-014**: Los registros MUST NOT contener contraseñas, tokens, secretos, cadenas de conexión ni
  contenido de documentos.
- **FR-015**: El nivel de detalle de los registros MUST ser configurable por entorno y por módulo sin
  cambiar código.
- **FR-016**: Los errores no controlados MUST registrarse con su detalle técnico y devolver al
  cliente una respuesta genérica con el identificador de correlación, sin trazas internas.

**Auditoría**

- **FR-017**: El sistema MUST proveer a todos los módulos, a través de un contrato público, una forma
  de registrar eventos de auditoría con: fecha y hora UTC, actor (usuario o proceso del sistema),
  superficie de origen, acción, tipo e identificador de entidad, valor anterior, valor nuevo e
  identificador de correlación.
- **FR-018**: El registro de un evento de auditoría MUST confirmarse o revertirse junto con la acción
  de negocio que lo origina: no puede existir una acción confirmada sin su evento ni un evento de
  una acción revertida. Para lograrlo, el contrato público de Auditoría MUST aceptar la conexión y
  la transacción activas del módulo llamador y escribir el evento dentro de ellas (misma base,
  schema de Auditoría); el módulo llamador nunca accede directamente a las tablas de Auditoría.
- **FR-019**: La bitácora MUST ser de solo agregado: ningún usuario, rol (incluido administrador) ni
  proceso de la aplicación puede modificar ni borrar eventos, y la base de datos MUST rechazar
  esas operaciones aunque se intenten por fuera de la aplicación con la identidad de la aplicación.
  Para ello existen dos identidades de base de datos: un login migrador, dueño de los schemas, que
  solo aplica cambios de esquema, y un login de runtime, sin propiedad sobre ningún schema y con
  DENY UPDATE/DELETE sobre la tabla de auditoría, que es el único que usan API y worker.
- **FR-020**: Los valores anterior y nuevo MUST guardar solo los campos modificados y MUST enmascarar
  los campos marcados como sensibles.
- **FR-021**: Los usuarios con rol auditor o administrador MUST poder consultar la bitácora con
  filtros por entidad, actor y rango de fechas, en orden cronológico y paginada; el resto de los
  roles y todos los usuarios del portal MUST recibir acceso denegado.
- **FR-022**: Las consultas a la bitácora MUST quedar a su vez registradas como evento de auditoría
  (consulta de datos sensibles).

### Key Entities

- **Módulo**: unidad de negocio con su parte pública (contratos) y su parte interna; dueño exclusivo
  de su espacio de datos.
- **Evento de auditoría**: registro inmutable de una acción. Atributos: identificador, fecha y hora
  UTC, actor (usuario o sistema), superficie de origen, acción, tipo de entidad, identificador de
  entidad, valor anterior, valor nuevo, identificador de correlación. Referencia cualquier entidad
  de cualquier módulo sin depender de sus tablas.
- **Registro técnico (log)**: entrada operativa estructurada; no es evidencia de negocio y tiene su
  propia retención, distinta de la bitácora.
- **Identificador de correlación**: valor que une la solicitud original, sus registros técnicos, los
  trabajos derivados en el worker y los eventos de auditoría que generó.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un desarrollador nuevo, con solo los prerequisitos documentados, tiene la solución
  completa en ejecución en menos de 15 minutos desde que clona el repositorio (sin contar la primera
  descarga de imágenes).
- **SC-002**: El 100 % de los componentes (API, worker, base de datos, backoffice, portal) reporta
  estado sano tras el arranque local.
- **SC-003**: El 100 % de las violaciones de fronteras introducidas a propósito en un test de
  control (módulo a módulo, feature a feature, portal a backoffice) es detectado por la verificación
  automática.
- **SC-004**: Para cualquier solicitud de prueba, el 100 % de sus registros técnicos en API y worker
  se recupera filtrando por un único identificador de correlación.
- **SC-005**: Cero apariciones de contraseñas, tokens o secretos en los registros técnicos y en la
  bitácora durante la suite de pruebas.
- **SC-006**: El 100 % de los intentos de modificar o borrar un evento de auditoría, por la
  aplicación o directamente en la base con la identidad de la aplicación, es rechazado.
- **SC-007**: Un auditor obtiene los eventos de una entidad en menos de 2 segundos con 1.000.000 de
  eventos almacenados.

## Assumptions

- El stack, la organización en módulos y las dos superficies de API vienen fijados por la
  constitución (v1.0.0); esta spec no los re-decide, solo exige que la base los respete.
- Los módulos iniciales se crean vacíos (sin entidades de negocio); el único módulo con
  funcionalidad en esta feature es Auditoría. Las entidades de negocio llegan en sus features.
- La autenticación queda configurada en su estructura (dos esquemas, policies, rol auditor) con
  tokens de desarrollo generados fuera de la API (FR-004); el login real (SSO corporativo con segundo factor, RNF-01) es una
  feature posterior.
- La retención de la bitácora no se purga en esta fase; el plazo configurable de conservación
  (RNF-05, típicamente 10 años) se resuelve junto con el módulo de expedientes.
- Los registros técnicos se emiten a la salida estándar de cada proceso y se visualizan con las
  herramientas del entorno local; la integración con una plataforma centralizada de logs en
  producción queda fuera de alcance.
- Los entornos objetivo de esta feature son desarrollo local y CI; la infraestructura de producción
  (cifrado en reposo, backups, RNF-03/RNF-06) se define en una feature de despliegue.
- La integridad criptográfica de la bitácora (encadenamiento por hash o firma) queda fuera de
  alcance; la inmutabilidad se garantiza por permisos de base de datos y por la aplicación.
