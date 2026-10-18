<!--
Sync Impact Report
- Version change: (template sin ratificar) → 1.0.0
- Principios definidos (antes placeholders):
  - [PRINCIPLE_1_NAME] → I. Monolito modular con vertical slices
  - [PRINCIPLE_2_NAME] → II. Cada módulo es dueño de sus datos
  - [PRINCIPLE_3_NAME] → III. Comunicación solo por contratos públicos
  - [PRINCIPLE_4_NAME] → IV. Fronteras verificadas por tests de arquitectura (NON-NEGOTIABLE)
  - [PRINCIPLE_5_NAME] → V. Dos superficies de API: backoffice y portal
- Principios agregados: VI. Worker en proceso aparte, VII. Frontends por features sin lógica
  de negocio, VIII. La API es la autoridad de seguridad y auditoría
- Secciones agregadas: Stack tecnológico y entorno; Flujo de desarrollo y quality gates
- Secciones eliminadas: ninguna
- Templates: no modificados (este comando solo escribe la constitución; plan/spec/tasks la leen
  en runtime)
- Decisiones / TODOs:
  - El input pedía "un esquema de Postgres por módulo" pero el stack fija SQL Server. Se tomó
    SQL Server (un schema de SQL Server por módulo). Confirmar.
  - "Fluent" se interpretó como FluentValidation; "TanStack" como TanStack Query + TanStack
    Router; "Rechats" como Recharts. Confirmar.
-->

# Diligencia Service Constitution

Diligencia Service es una aplicación de debida diligencia de terceros (clientes y proveedores):
alta, verificación, evaluación de riesgo, aprobación por niveles y monitoreo, con evidencia
auditable de cada paso. Referencia funcional: `docs/Documento funcional app de debida diligencia
de terceros.md`.

## Core Principles

### I. Monolito modular con vertical slices

- El backend es un único host desplegable organizado en módulos de negocio (p. ej. Terceros,
  Casos, Riesgo, Aprobaciones, Documentos, Auditoría, Identidad).
- Dentro de cada módulo el código se organiza por feature (vertical slice): endpoint, request,
  validación, handler y acceso a datos de una feature viven juntos. No hay capas horizontales
  globales (`Services/`, `Repositories/`) compartidas entre features.
- Un módulo nuevo o una feature nueva MUST poder agregarse sin modificar el interior de otro
  módulo.

**Rationale**: mantiene el despliegue simple de un monolito con fronteras que permitan extraer un
módulo más adelante si hiciera falta.

### II. Cada módulo es dueño de sus datos

- Cada módulo tiene su propio `DbContext` de Entity Framework y su propio schema de SQL Server.
- Ningún módulo MUST leer, escribir ni hacer join contra tablas de otro módulo, ni siquiera en
  consultas de solo lectura o reportes.
- Las migraciones son por módulo y solo tocan el schema del módulo.
- Si un módulo necesita datos de otro, los obtiene por su contrato público (Principio III) o
  mantiene su propia copia derivada de eventos.

**Rationale**: compartir tablas acopla los módulos más que compartir código y anula las fronteras.

### III. Comunicación solo por contratos públicos

- Cada módulo expone un proyecto/namespace `Contracts` con interfaces, DTOs y eventos de
  integración. Es lo único que otro módulo puede referenciar.
- Todo lo demás (entidades, handlers, `DbContext`, validadores, endpoints) MUST ser `internal`.
- La comunicación entre módulos es por interfaz pública (llamada síncrona en proceso) o por
  eventos en memoria. Los eventos que deban sobrevivir a un fallo o disparar trabajo pesado se
  encolan en Hangfire.
- Los contratos públicos son API: un cambio incompatible requiere actualizar a todos los
  consumidores en el mismo cambio.

**Rationale**: `internal` por defecto hace que el compilador, y no la disciplina, proteja las
fronteras.

### IV. Fronteras verificadas por tests de arquitectura (NON-NEGOTIABLE)

- Existe una suite de tests de arquitectura (NetArchTest o ArchUnitNET, sobre xUnit) que falla si:
  - un módulo referencia algo de otro módulo que no esté en su `Contracts`;
  - un tipo fuera de `Contracts` es `public` sin estar justificado;
  - un `DbContext` de un módulo es usado desde otro módulo;
  - el host de API referencia código del worker que ejecuta procesamiento pesado o viceversa
    fuera de los contratos.
- En el frontend, ESLint con `import/no-restricted-paths` (o equivalente) falla si una feature
  importa el interior de otra o si el portal importa código del backoffice.
- Estas reglas corren en CI y bloquean el merge. No se desactivan para "destrabar" un cambio; si
  una regla es incorrecta, se enmienda esta constitución.

**Rationale**: sin verificación automática, las fronteras se erosionan con el tiempo.

### V. Dos superficies de API: backoffice y portal

- Las rutas viven en el mismo host bajo dos grupos: `/api/backoffice` (usuarios internos:
  solicitante, analista, aprobador, administrador, auditor) y `/api/portal` (terceros externos).
- Cada grupo tiene su propio esquema de autenticación JWT (emisor/audiencia distintos) y sus
  propias authorization policies. Un token de portal MUST NOT ser aceptado en backoffice y
  viceversa.
- Cada feature declara explícitamente en qué grupo se publica. Ningún endpoint queda fuera de un
  grupo ni sin policy.
- En el portal, el tercero solo accede a sus propios datos y pedidos pendientes; el filtro por
  tercero se aplica en la API, no en el cliente.

**Rationale**: los usuarios externos tienen otro modelo de confianza; separar esquemas evita que
un error de policy exponga el backoffice.

### VI. Worker en proceso aparte

- El procesamiento pesado o lento (OCR, extracción de texto con PdfPig, lectura de documentos
  Office con DocumentFormat.OpenXml, generación de expedientes PDF con QuestPDF, revisiones
  periódicas y vencimientos de plazos) corre en un proceso worker separado con Hangfire.
- La API solo encola trabajo y responde; MUST NOT ejecutar inline una tarea cuyo costo dependa del
  tamaño de un documento.
- Los jobs son idempotentes y reintentables.
- El worker reutiliza los módulos y sus contratos; no duplica lógica de negocio.

**Rationale**: un OCR pesado no debe degradar la latencia de la API.

### VII. Frontends por features, sin lógica de negocio

- Hay dos aplicaciones: `apps/backoffice` y `apps/portal`, ambas organizadas por feature
  (vertical slices).
- Una feature MUST NOT importar el interior de otra. Lo compartido va a `packages/` (p. ej.
  `packages/ui`, cliente de API, schemas) o a `shared/` dentro de la app.
- El portal MUST NOT importar código de `apps/backoffice`; solo de `packages/`.
- El frontend no decide reglas de negocio ni permisos. Las validaciones con Zod en formularios son
  para UX; la API vuelve a validar todo. Ocultar acciones según rol es solo comodidad.

**Rationale**: la lógica en el cliente se puede saltear; la única fuente de verdad es la API.

### VIII. La API es la autoridad de seguridad y auditoría

- Autenticación con JWT; contraseñas con BCrypt; autorización por roles (RBAC) mediante policies
  de ASP.NET Core.
- La segregación de funciones se aplica en la API caso por caso: quien analizó un caso MUST NOT
  aprobarlo, aunque tenga ambos roles.
- Toda acción que cambie el estado de un caso, una evaluación de riesgo o una decisión deja
  registro en la bitácora de auditoría (quién, qué, cuándo). La bitácora es de solo agregado.
- Toda entrada externa (requests, archivos subidos) se valida en el borde de la API
  (FluentValidation) y los archivos se validan por tipo y tamaño antes de procesarse.
- No se registran secretos, contraseñas ni contenido de documentos en los logs.

**Rationale**: el valor del producto es la evidencia auditable; un permiso decidido en el cliente
o una acción sin registro invalida esa evidencia.

## Stack tecnológico y entorno

**Backend**

- .NET 10, ASP.NET Core minimal APIs, orquestación local con .NET Aspire.
- SQL Server con Entity Framework Core (un `DbContext` y un schema por módulo).
- FluentValidation, logging nativo de .NET (`ILogger`, structured logging).
- Hangfire para jobs en background (proceso worker separado).
- PdfPig (lectura de PDF), DocumentFormat.OpenXml (documentos Office), QuestPDF (generación de
  PDF).
- xUnit para tests unitarios, de integración y de arquitectura (NetArchTest o ArchUnitNET).

**Frontend**

- React + Vite + TypeScript (strict), MUI.
- TanStack Query (estado de servidor), TanStack Router, TanStack Table.
- React Hook Form + Zod, react-dropzone, Recharts, react-pdf.
- Vitest + Testing Library (unit/componentes), Playwright (end-to-end).

**Entorno y despliegue**

- La solución completa (API, worker, SQL Server, frontends) está dockerizada y corre con Docker
  Engine dentro de WSL. No se depende de Docker Desktop.
- Agregar una dependencia nueva fuera de este stack requiere justificarla en el plan de la
  feature.

## Flujo de desarrollo y quality gates

- Cada feature arranca con spec y plan de Spec Kit; el plan MUST incluir un "Constitution Check"
  que indique módulo dueño, grupo de API (backoffice/portal), contratos públicos tocados y si
  requiere trabajo en el worker.
- Un cambio se puede mergear solo si pasan: build, tests xUnit, tests de arquitectura, ESLint
  (incluidas las reglas de fronteras), Vitest y, para flujos críticos (alta de tercero, carga de
  documentos en portal, aprobación), Playwright.
- Toda regla de negocio, permiso o transición de estado nueva tiene al menos un test en el
  backend.
- Code review verifica cumplimiento de esta constitución; una excepción se documenta en el plan
  con su justificación y la alternativa más simple descartada.

## Governance

- Esta constitución prevalece sobre cualquier otra convención del proyecto. Ante un conflicto,
  gana la constitución o se enmienda.
- Enmiendas: se proponen como cambio a este archivo con motivo e impacto (Sync Impact Report), se
  aprueban en review y, si rompen código existente, incluyen el plan de migración.
- Versionado semántico: MAJOR para eliminar o redefinir un principio de forma incompatible; MINOR
  para agregar un principio o sección o ampliar materialmente una guía; PATCH para aclaraciones y
  redacción.
- Cumplimiento: cada plan de feature pasa el Constitution Check antes de diseñar y después de
  diseñar; los tests de arquitectura y reglas de ESLint son la verificación continua.

**Version**: 1.0.0 | **Ratified**: 2026-10-16 | **Last Amended**: 2026-10-16
