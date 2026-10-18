# Quickstart: validar Solicitudes de alta de terceros

Guía de validación de punta a punta. Contratos en [contracts/](contracts/), tablas en
[data-model.md](data-model.md).

## Requisitos

- Docker Engine en WSL con el bridge de la 001/002 (`DOCKER_HOST` para los tests).
- .NET 10 SDK, Node 24.
- CUIT de prueba válido de persona jurídica: `30-71234567-1` (DV 1). Inválido: `30-71234567-2`.
  Persona humana con prefijo jurídico (rechazado): `30-71234567-1` con tipo `Humana`.

## 1. Tests automáticos

```bash
dotnet test tests/Diligencia.UnitTests          # Cuit, BusinessCalendar, fecha estimada, validador
dotnet test tests/Diligencia.ArchitectureTests  # Casos solo usa Terceros.Contracts
dotnet test tests/Diligencia.IntegrationTests   # ver lista abajo
npm run lint && npm test                        # ESLint de fronteras + Vitest
```

Integración (Testcontainers SQL Server) que debe pasar:

| Escenario | Spec |
|---|---|
| Alta válida → tercero `EnAlta`, caso `EsperaTercero`, solicitud original, enlace `Pendiente`, job encolado | US1-1, FR-003, FR-008 |
| CUIT con DV inválido / prefijo inconsistente / campos vacíos / correo mal formado / monto ≤ 0 → 400 por campo | US1-2/3, FR-002 |
| Usuario sin rol Solicitante → 403 en los tres endpoints (matriz de permisos) | US1-5, FR-016 |
| Bitácora: Tercero, Caso, Solicitud `Created` en la misma tx; ninguna fila contiene el token | US1-6, FR-010, FR-017 |
| Job: éxito → `Enviado`, hash guardado, correo con `/acceso#t=`; el hash del enlace corresponde al token del correo | US1-4, FR-009 |
| Job: transporte falla 5 veces → `Fallido`, `enlaceNoEntregado = true` en la lista | FR-011 |
| Aviso con CUIT existente: razón social, estado, etapa; sin campos de riesgo | US2-1/4 |
| CUIT con caso abierto → `SumadoACasoAbierto`, un solo caso, un solo job | US2-2, FR-007 |
| Segunda vez el mismo usuario → 409 "ya lo sigue" | edge case |
| Último caso `Abandonado`/`Rechazado` → nuevo caso sobre el mismo tercero | US2-3 |
| Tercero `Activo` o `Bloqueado` → 409 | edge cases, FR-007 |
| Dos POST concurrentes con CUIT nuevo → 1 tercero, 1 caso, 2 solicitudes | edge case, SC-003 |
| Mis solicitudes: dos solicitantes, cada uno ve solo las suyas; filtro por estado y búsqueda | US3-1/2/5, SC-005 |
| Fecha estimada con `FakeTimeProvider` (en plazo, plazo vencido, caso cerrado) | US3-3, FR-013 |

## 2. Recorrido manual (AppHost)

```bash
dotnet run --project src/Aspire/Diligencia.AppHost
```

1. Como administrador (`admin@diligencia.local`), crear un usuario con rol **Solicitante** y
   activarlo desde el correo en Mailpit.
2. Ingresar como solicitante → menú **Mis solicitudes** → **Nueva solicitud**.
3. Escribir un CUIT inválido: el formulario lo marca. Escribir uno válido nuevo: sin aviso.
4. Completar y guardar → redirige a la lista con el caso en **Espera del tercero** y fecha estimada
   ≈ hoy + 23 días hábiles.
5. En Mailpit (UI `ui` del recurso `mailpit`) llega el correo al contacto con un enlace
   `…/acceso#t=…` en menos de un minuto.
6. Con otro solicitante, cargar el mismo CUIT: aparece el aviso con razón social y "Espera del
   tercero"; al guardar queda sumado y el caso aparece en su lista. Mailpit no recibe un segundo
   correo.
7. Como auditor, en la bitácora (`GET /api/backoffice/audit/events`) están los eventos de Tercero,
   Caso, Solicitud y EnlacePortal; ninguno contiene el token.
8. Detener Mailpit y crear otra solicitud: la entrega pasa a `Reintentando`; tras 5 intentos
   (unos 10 min con el backoff por defecto de Hangfire) la lista muestra "enlace no entregado".

## 3. End-to-end (Playwright)

Con el AppHost corriendo:

```bash
BASE_URL=<url backoffice> MAILPIT_URL=<url ui mailpit> npm run e2e -w apps/backoffice
```

Escenario `solicitudes.spec.ts`: login solicitante → nueva solicitud → aparece en la lista → el
correo está en Mailpit.
