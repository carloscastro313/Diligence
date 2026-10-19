# Implementation Plan: Portal del tercero

**Branch**: `004-portal-tercero` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-portal-tercero/spec.md`

## Summary

El tercero abre el enlace del correo (feature 003), recibe un código de un solo uso y entra a un
portal limitado a su caso (RF-05); completa un formulario según tipo de persona (RF-06), sube los
documentos requeridos (RF-07), puede guardar a medias (RF-08) y al enviar el caso pasa a
"En verificación".

Enfoque técnico: **Casos** suma los endpoints `/api/portal/*`: ingreso (código hasheado en
`CodigosIngreso`, límites por enlace y bloqueo ante adivinación), formulario guardado como JSON
tipado en `FormulariosTercero` con validación laxa al guardar y estricta al enviar, catálogo de
documentos requeridos y la transición `EsperaTercero → EnVerificacion`. **Identidad** agrega
sesiones de portal con el mismo diseño que el backoffice (cookie, validación por request,
30 min de inactividad) detrás de `IPortalSignIn`/`IPortalActor`. **Documentos** recibe su primer
contenido: `IDocumentStore` guarda los archivos cifrados con AES-GCM en SQL Server, verifica el
contenido real (firma y trailer, costo fijo) y aplica el tamaño máximo que un administrador cambia
por endpoint. El código se envía inline por `IEmailTransport`; no hay trabajo nuevo en el worker.
Portal: pantallas de acceso y caso (inicio, formulario, documentos) con React Hook Form + Zod,
usables en el celular; Playwright para el flujo de carga de documentos.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 5.x `strict` / Node 24 LTS (sin cambios).

**Primary Dependencies**: existentes (ASP.NET Core minimal APIs, EF Core 10, FluentValidation,
React, MUI, TanStack Query/Router). Nuevas en `apps/portal`, todas del stack y ya usadas en el
backoffice salvo `react-dropzone`: `react-hook-form`, `@hookform/resolvers`, `zod`,
`react-dropzone` (carga de documentos), `@playwright/test` (dev). Backend sin
paquetes nuevos (`AesGcm`, `SHA256`, `RandomNumberGenerator` son de la BCL).

**Storage**: SQL Server, schemas `casos` (`CodigosIngreso`, `FormulariosTercero`; columnas nuevas
en `EnlacesPortal`), `documentos` (`Documentos`, `DocumentoContenidos`, `Configuracion`) e
`identidad` (`PortalSessions`); una migración por módulo ([data-model.md](data-model.md)).
Bitácora vía `IAuditLog`.

**Testing**: xUnit v3 + Testcontainers.MsSql + `WebApplicationFactory`, `FakeTimeProvider`,
`FakeEmailTransport` (ya existe); NetArchTest; Vitest + Testing Library; Playwright contra el AppHost.

**Target Platform**: contenedores Linux; local con Aspire sobre Docker Engine en WSL. Portal en
navegadores móviles y de escritorio actuales.

**Project Type**: web-service (monolito modular) + SPA de portal (+ un endpoint de backoffice).

**Performance Goals**: código en el buzón < 1 min (SC-002: envío inline, segundos); formulario
completo en < 20 min (SC-003: depende de la UI, se observa en el quickstart). Carga de 10 MB en una
sola request; verificación de contenido de costo fijo (research R9).

**Constraints**: código y token nunca en claro, en logs ni en bitácora (FR-002, FR-021); documentos
cifrados en reposo y fuera de logs (FR-018); el caso siempre sale de la sesión, nunca de un
parámetro (FR-006, SC-005); la API no procesa el contenido de los documentos (Principio VI): solo
lo recibe, hashea, cifra y guarda, trabajo de E/S lineal y acotado a 50 MB que es inherente a
almacenar un archivo. Toda lectura del contenido (parseo de PDF, OCR, extracción) va al worker
(research R8, R9).

**Scale/Scope**: cientos de casos al mes, ~6 archivos por caso; 9 endpoints de portal, 2 de
backoffice; 3 pantallas.

No quedan NEEDS CLARIFICATION: decisiones en [research.md](research.md) (R1–R13).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Datos exigidos por la constitución**

| Pregunta | Respuesta |
|---|---|
| Módulo dueño | **Casos** (ingreso, formulario, requisitos de documentos, transición, endpoints de portal). **Identidad** (sesión de portal). **Documentos** (almacén, cifrado, tamaño máximo). Terceros y Auditoría sin cambios (R1). |
| Grupo de API | **portal**: `/api/portal/acceso*` (anónimos, rate limit, lista cerrada) y `/api/portal/caso/**` (policy Portal). **backoffice**: `/api/backoffice/documentos/configuracion` (policy Administrator). |
| Contratos públicos tocados | `Identidad.Contracts`: + `IPortalSignIn`, `IPortalActor`. `Documentos.Contracts`: + `IDocumentStore` y tipos. Aditivos ([contracts/module-contracts.md](contracts/module-contracts.md)). |
| Trabajo en worker | **No**. El código se envía inline (R3); la verificación de archivos es de costo fijo (R9). La validación profunda de PDF queda para la extracción en el worker (feature de verificación). |

**Gates por principio**

| Principio | Cómo se cumple | Pre | Post |
|---|---|---|---|
| I. Monolito modular / vertical slices | Casos: `Features/{SolicitarCodigo,Ingresar,GetCasoPortal,GetFormulario,GuardarFormulario,EnviarFormulario,ListDocumentosPortal,SubirDocumento,EliminarDocumento}` + `Features/Shared` (requisitos, acceso al caso de la sesión). Identidad: sesión de portal dentro de `Authentication/`. Documentos: `Features/{Store,Configuracion}`. Cada módulo cambia solo su interior. | ✅ | ✅ |
| II. Dueño de sus datos | Tablas nuevas solo en su schema; Casos no lee `documentos` ni `identidad`: usa los contratos con la transacción compartida (patrón `IAuditLog`). Tipo de persona por `ITerceroRegistry`. | ✅ | ✅ |
| III. Solo contratos públicos | Casos referencia `Documentos.Contracts`, `Identidad.Contracts`, `Terceros.Contracts`, `Auditoria.Contracts`. Todo lo nuevo en los módulos es `internal`. | ✅ | ✅ |
| IV. Fronteras verificadas | Reglas NetArchTest existentes cubren las referencias nuevas. ESLint: `apps/portal/src/features/{acceso,caso}` solo importan `shared/` y `packages/`; el portal no importa el backoffice (el cliente HTTP se copia, R12). | ✅ | ✅ |
| V. Dos superficies | Endpoints de portal en el grupo Portal con su esquema JWT; la sesión de portal no sirve en backoffice. Solo los dos endpoints de ingreso son anónimos, con rate limit y en la lista cerrada de `EndpointSurfaceTests`. El filtro por caso sale de la sesión en la API. | ✅ | ✅ |
| VI. Worker aparte | La API no procesa documentos: recibir, hashear, cifrar y guardar es almacenamiento (E/S lineal, tope 50 MB), no la clase de tarea que el principio manda al worker (OCR, extracción, generación de PDF). Constitución 1.1.0 eliminó la viñeta que prohibía ejecutar inline tareas dependientes del tamaño de un documento, así que no hay excepción que registrar. La verificación de tipo mira solo firma y cola (costo fijo). El correo de un código es inline como `ForgotPassword`. Sin jobs nuevos (research R8, R9). | ✅ | ✅ |
| VII. Frontend sin lógica de negocio | Zod repite reglas solo para UX; obligatoriedad, suma de porcentajes, documentos obligatorios, editabilidad y transición los decide la API. | ✅ | ✅ |
| VIII. API autoridad de seguridad y auditoría | FluentValidation en todos los cuerpos; archivos validados por tipo real y tamaño antes de guardarse; bitácora en la misma tx para ingresos, códigos, guardados (diff por campo), documentos, envío y cambio de estado; sin token, código ni contenido en logs. | ✅ | ✅ |
| Stack | Dependencias nuevas solo del stack (RHF, Zod, react-dropzone, Playwright) (R12). | ✅ | ✅ |
| Quality gates | Test de backend por cada regla (código, límites, bloqueo, sesión, aislamiento, validación por persona, documentos obligatorios, transición, tipo real, tamaño, cifrado, configuración). Playwright para "carga de documentos en portal" (flujo crítico). | ✅ | ✅ |

**Resultado**: PASS (pre y post diseño). Sin excepciones.

## Project Structure

### Documentation (this feature)

```text
specs/004-portal-tercero/
├── plan.md              # Este archivo
├── research.md          # Fase 0: decisiones R1–R13
├── data-model.md        # Fase 1: tablas, JSON del formulario, documentos requeridos, estados
├── quickstart.md        # Fase 1: validación end-to-end
├── contracts/
│   ├── portal-api.openapi.yaml   # 9 endpoints de portal + configuración de backoffice
│   └── module-contracts.md       # Identidad, Documentos, Casos, configuración
└── tasks.md             # Fase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── Aspire/Diligencia.AppHost/Program.cs        # + parámetros portal-signing-key y documentos-encryption-key → api
├── Hosts/Diligencia.Api/appsettings.json       # + Identidad:PortalSession*, Casos:AccesoRateLimitPerMinute
└── Modules/
    ├── Identidad/
    │   ├── Diligencia.Modules.Identidad.Contracts/IdentityContracts.cs   # + IPortalSignIn, IPortalActor
    │   └── Diligencia.Modules.Identidad/
    │       ├── Authentication/AuthenticationSetup.cs  # + eventos del esquema Portal (cookie dlg_pt, sesión, claims)
    │       ├── Authentication/TokenIssuer.cs          # recibe el esquema
    │       ├── Authentication/PortalSignIn.cs         # IPortalSignIn + cookie
    │       ├── HttpPortalActor.cs                     # IPortalActor desde claims
    │       └── Persistence/                           # PortalSession + configuración + migración
    ├── Documentos/
    │   ├── Diligencia.Modules.Documentos.Contracts/DocumentosContracts.cs  # IDocumentStore y tipos
    │   └── Diligencia.Modules.Documentos/
    │       ├── DocumentosModule.cs                    # registra store, lee la clave, mapea configuración
    │       ├── Features/Store/                        # DocumentStore, ContentInspector, DocumentCipher
    │       ├── Features/Configuracion/                # GET/PUT /documentos/configuracion
    │       └── Persistence/                           # Documento, DocumentoContenido, Configuracion + migración
    └── Casos/Diligencia.Modules.Casos/
        ├── CasosModule.cs                             # + subgrupo portal, política portal-acceso
        ├── CasosOptions.cs                            # + AccesoRateLimitPerMinute
        ├── Persistence/                               # CodigoIngreso, FormularioTercero, columnas de EnlacePortal + migración
        └── Features/
            ├── Shared/        # ApiProblems (rutas camelCase), AccessCode, EnlaceAcceso (validez/lock), CasoPortalAccess, DocumentosRequeridos, JsonDiff
            ├── SolicitarCodigo/   # POST /acceso/codigo (+ correo)
            ├── Ingresar/          # POST /acceso
            ├── GetCasoPortal/     # GET /caso
            ├── Formulario/        # FormularioDatos + validadores de guardado y envío
            ├── GetFormulario/  GuardarFormulario/  EnviarFormulario/
            └── ListDocumentosPortal/  SubirDocumento/  EliminarDocumento/

tests/
├── Diligencia.UnitTests/{Casos,Documentos}/    # validadores, diff JSON, enmascarado, inspector, cifrado
├── Diligencia.IntegrationTests/
│   ├── Portal/                                 # acceso, límites, sesión, aislamiento, formulario, envío, documentos, bitácora
│   ├── Documentos/                             # configuración, cifrado en reposo
│   ├── Api/EndpointSurfaceTests.cs             # + 2 anónimos de portal
│   ├── Identidad/PermissionMatrixTests.cs      # + configuración (Administrador)
│   ├── Logging/PortalNoSecretsInLogsTests.cs
│   └── Support/                                # PortalClient (enlace + código + cookie), DbApiFactory con clave de cifrado
└── Fixtures/archivos/                          # pdf/jpg/png válidos, cifrado, truncado, renombrado

apps/portal/
├── package.json                                # + react-hook-form, @hookform/resolvers, zod, react-dropzone, @playwright/test
├── src/shared/        # api.ts (base /api/portal, FormData), acceso.ts (token en sessionStorage), paises.ts
├── src/app/router.tsx # /acceso, /caso; 401 → /acceso
├── src/features/acceso/   # AccesoPage (pedir código, ingresar, reenviar) + test
├── src/features/caso/     # CasoPage (inicio + pendientes), FormularioSection, DocumentosSection (react-dropzone), schema.ts + tests
├── e2e/portal.spec.ts
└── playwright.config.ts
```

**Structure Decision**: se extiende la estructura existente sin proyectos nuevos. Los slices siguen
el patrón de `Modules/Casos/Features/*` (endpoint + request + validador + handler juntos, registro
por convención de nombre). Documentos recibe su primer contenido como almacén genérico detrás de
contrato; la sesión de portal replica el diseño de la sesión de backoffice dentro de Identidad.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | Sin violaciones. | Formulario como JSON en una columna en vez de tablas normalizadas (R6); archivos en SQL Server en vez de blob storage (R8). |

## Riesgos y notas

- La verificación de PDF es de firma y cola: un PDF dañado por dentro o protegido con contraseña de
  apertura se acepta en la carga y lo detectará la extracción con PdfPig en el worker (feature de
  verificación). Rechazar por `/Encrypt` habría rechazado constancias legítimas que solo tienen
  contraseña de propietario (R9, shortcut; supuesto en el spec).
- Una sola clave de cifrado sin `KeyId`: rotarla exige agregar la columna y recifrar (R8, shortcut).
- El token del enlace queda en `sessionStorage` del portal para pedir códigos nuevos sin reabrir el
  correo; un XSS en el portal podría leerlo (sigue haciendo falta el código del correo).
- Sin botón "Salir": la sesión vence a los 30 min de inactividad (R2).
- Tamaño máximo sin pantalla: el administrador lo cambia por el endpoint hasta RF-30 (spec).
- Cliente HTTP duplicado entre backoffice y portal (~35 líneas, R12).
- iOS puede entregar fotos HEIC si el usuario elige "archivo" en vez de cámara; se rechazan como
  formato no permitido con el mensaje de formatos aceptados.
- El AppHost necesita dos parámetros secretos nuevos; en CI los tests los inyectan por
  configuración del `WebApplicationFactory`.
- Playwright del portal necesita backoffice, portal y Mailpit levantados (mismo paso de CI que la 003).
