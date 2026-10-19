# Quickstart: validar el Portal del tercero

Guía de validación de punta a punta. Contratos en [contracts/](contracts/), tablas y reglas del
formulario en [data-model.md](data-model.md), decisiones en [research.md](research.md).

## Requisitos

- Docker Engine en WSL con el bridge de la 001/002 (`DOCKER_HOST` para los tests).
- .NET 10 SDK, Node 24.
- Un caso en "Espera del tercero" con su correo de enlace en Mailpit (se crea como en el
  quickstart de la 003). CUIT jurídico de prueba: `30-71234567-1`. Humano: `20-12345678-6`.
- Archivos de prueba generados en código por `tests/Diligencia.IntegrationTests/Support/SampleFiles.cs`
  (PDF, JPG y PNG válidos; PDF cifrado; PDF truncado; `.txt` con extensión `.pdf`; archivo
  grande). Para el recorrido manual, crear a mano un `.txt` renombrado a `.pdf` y usar cualquier
  PDF, JPG o PNG real.

## 1. Tests automáticos

```bash
dotnet test tests/Diligencia.UnitTests          # validadores, inspector de contenido, cifrado, diff JSON, correo enmascarado
dotnet test tests/Diligencia.ArchitectureTests  # Casos → Documentos/Identidad solo por Contracts
dotnet test tests/Diligencia.IntegrationTests   # ver tabla
npm run lint && npm test                        # fronteras ESLint (portal sin backoffice) + Vitest
```

Integración (Testcontainers SQL Server, `FakeTimeProvider`, `FakeEmailTransport`) que debe pasar:

| Escenario | Spec |
|---|---|
| Enlace válido → 202, correo enmascarado, código de 6 dígitos en el transporte falso, solo el hash en la base | US1-1, FR-001/002 |
| Código correcto → 204 + cookie `dlg_pt`; `GET /caso` devuelve razón social, estado y pendientes | US1-2 |
| Código incorrecto ×5 → `codigoInvalidado`; el correcto ya no sirve | US1-3, FR-002 |
| Código a los 10 min + 1 s → 401 | FR-002 |
| Enlace vencido / token inexistente / caso en `EnVerificacion` → mismo 404 y mismo mensaje | US1-4, FR-004 |
| Reenviar → el código anterior deja de valer; el 6.º envío en una hora → 429 y `AccessDenied` en la bitácora | US1-5, FR-003 |
| 10 fallidos repartidos en varios códigos → enlace bloqueado 30 min (429), `AccountLocked`; a los 31 min vuelve a funcionar | edge case, FR-003 |
| Sesión de portal en `/api/backoffice/*` → 401; sin actividad 30 min → 401 | FR-005 |
| Dos terceros: el segundo nunca ve ni modifica formulario ni documentos del primero (todas las rutas `/caso/**`) | US1-6, FR-006, SC-005 |
| `GET /caso` no contiene hallazgos, puntajes, verificaciones ni notas | FR-006 |
| Guardar a medias con obligatorios vacíos → 200; nuevo ingreso con otro código → mismos datos y documentos | US4-1/2, FR-019, SC-007 |
| Guardar con `version` vieja → 200 `sobrescrito = true` | edge case |
| Envío jurídica sin beneficiarios ni declaración / suma 100,01 % / PEP sin cargo → 400 con ruta por campo | US2-3/4, FR-008/009/010 |
| Envío humana con `beneficiarios` → 400 "no corresponde"; sin `pep` → 400 | US2-2 |
| Envío completo sin un documento obligatorio → 400 `documentos` | FR-011, SC-004 |
| Envío completo con documentos → caso `EnVerificacion`, formulario `Enviado`, bitácora de ambos con autor `tercero:{id}`; PUT/PUT documento/DELETE → 409; GET sigue respondiendo | US2-5/6, FR-011/012/017 |
| Después del envío, `POST /acceso/codigo` → 404 | FR-011 |
| PDF, JPG y PNG válidos → 200 con tipo detectado, nombre y fecha | US3-2, FR-015 |
| `.txt` renombrado a `.pdf` / GIF → 400 con formatos aceptados | US3-3, FR-013 |
| PDF truncado / JPEG sin cierre → 400 "no se puede leer"; PDF con `/Encrypt` (contraseña de propietario) → 200 | edge case, research R9 |
| Recargar `/acceso` o volver tras una sesión vencida no envía otro código (Vitest de `AccesoPage`) | research R5 |
| > máximo → 413 con el máximo en MB; el admin baja el máximo a 2 MB → un archivo de 3 MB que antes pasaba ahora da 413; los ya cargados siguen | US3-4/6, FR-014 |
| `PUT /api/backoffice/documentos/configuracion` sin rol Administrador → 403; 0 o 51 MB → 400 | FR-014 |
| Reemplazar → un solo vigente, el anterior con `Baja = Reemplazado`; eliminar → `Eliminado`; bitácora de cada uno | US3-5 |
| `DocumentoContenidos.Cifrado` ≠ bytes subidos y descifra igual (test de ida y vuelta) | FR-018 |
| Carga y envío simultáneos → o el documento entra antes del envío, o la carga recibe 409 | research R7 |
| Logs y bitácora no contienen token, código ni contenido de archivos | FR-018, FR-021 |
| `EndpointSurfaceTests`: los dos endpoints anónimos del portal están en la lista cerrada | Principio V |

## 2. Recorrido manual (AppHost)

```bash
dotnet run --project src/Aspire/Diligencia.AppHost
```

1. Crear una solicitud de persona jurídica en el backoffice (quickstart 003) y abrir el enlace
   `…/acceso#t=…` del correo en Mailpit. El fragmento desaparece de la barra de direcciones.
2. El portal muestra "Te enviamos un código a c***@…". En Mailpit llega el código en segundos.
3. Ingresar un código equivocado: "El código no es válido". Ingresar el correcto: pantalla de
   inicio con razón social, "Espera del tercero" y la lista de pendientes.
4. Completar datos fiscales y un beneficiario, **Guardar**, cerrar la pestaña (el navegador avisa si
   había cambios sin guardar). Reabrir el enlace, pedir código nuevo: los datos siguen.
5. Arrastrar un `.gif` a la zona de carga: aviso inmediato en el portal. Subir un `.txt` renombrado
   a `.pdf`: lo rechaza la API con "Formatos aceptados: PDF, JPG, PNG". Subir los cuatro
   obligatorios (arrastrando y con clic); reemplazar uno.
6. **Enviar** con un beneficiario al 60 % y otro al 50 %: error en el porcentaje. Corregir y enviar:
   confirmación de recepción, formulario en solo lectura. En el backoffice, "Mis solicitudes" muestra
   **En verificación**.
7. Reabrir el enlace: "El enlace no es válido o venció…".
8. Repetir 1–6 con persona humana desde un celular (o DevTools en modo móvil): no aparece la sección
   de beneficiarios; el botón de carga ofrece la cámara.
9. Como auditor, `GET /api/backoffice/audit/events`: ingresos, códigos enviados (sin código),
   guardados con campo anterior/nuevo, documentos y el cambio de estado del caso.
10. Como administrador, desde la consola del navegador en el backoffice:
    `fetch('/api/backoffice/documentos/configuracion', {method:'PUT', headers:{'Content-Type':'application/json'}, body:'{"tamanoMaximoMb":2}'})`
    y comprobar que el portal rechaza un archivo de 3 MB.

## 3. End-to-end (Playwright)

Con el AppHost corriendo:

```bash
BASE_URL=<url portal> BACKOFFICE_URL=<url backoffice> MAILPIT_URL=<url ui mailpit> npm run e2e -w apps/portal
```

`portal.spec.ts`: crea un caso por la API del backoffice (solicitante de `E2E_REQUESTER_*`) → lee el
enlace en Mailpit → pide el código y lo lee en Mailpit → completa persona jurídica → sube los cuatro
documentos obligatorios → envía → ve la confirmación. Segundo test igual con persona humana en
viewport de celular (`devices['Pixel 7']`).

## 4. Notas de la validación manual

- **iOS y HEIC (pendiente de verificar en un iPhone real)**: no se pudo probar en un dispositivo. Por diseño,
  el `<input>` de `react-dropzone` declara `accept` de PDF, JPG y PNG; Safari en iOS suele convertir las fotos
  HEIC a JPEG cuando el `accept` incluye `image/jpeg`, así que la cámara y la fototeca entregan JPEG. Si el
  usuario elige "archivo" y llega un `.heic`, el portal lo rechaza en el cliente con "Formatos aceptados: PDF,
  JPG, PNG" y, si llegara a la API, esta lo rechaza igual por contenido real (`FormatoNoPermitido`). Si en un
  iPhone real aparece HEIC, evaluar convertirlo en el cliente.
- **Carga grande**: `IT/Portal/DocumentosKestrelTests` sube 45 MB sobre Kestrel real (más que el límite por
  defecto de 30 MB) con el máximo en 50 MB, y verifica el 413 a los 51 MB.

