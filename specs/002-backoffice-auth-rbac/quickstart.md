# Quickstart: validar autenticación y RBAC del backoffice

Guía para comprobar de punta a punta que la feature funciona. Endpoints en
[contracts/backoffice-identity-api.openapi.yaml](contracts/backoffice-identity-api.openapi.yaml),
modelo en [data-model.md](data-model.md), decisiones en [research.md](research.md).

## Prerequisitos

Los mismos de `specs/001-project-setup-foundation/quickstart.md` (WSL2 + Docker Engine, .NET 10,
Node 24). La clave de firma del backoffice ya no sale de `dotnet user-jwts`: la genera el AppHost
(parámetro `backoffice-signing-key`). Para el portal sigue haciendo falta
`dotnet user-jwts create --scheme Portal ...` una vez.

## 1. Arranque y primer administrador (FR-014)

```bash
dotnet run --project src/Aspire/Diligencia.AppHost
```

Esperado en el dashboard de Aspire: además de los recursos de la 001, `mailpit` en *Running*.
`migrations` termina con código 0 y su log indica que creó el administrador inicial
`admin@diligencia.local` (o que ya existía uno). Volver a arrancar no crea otro.

1. Abrir la URL de `backoffice` → redirige a `/login`.
2. "Olvidé mi contraseña" con `admin@diligencia.local` → mensaje neutro.
3. Abrir la UI de `mailpit` (endpoint http del recurso) → correo con enlace a
   `/restablecer?token=...`. Establecer una contraseña de 12+ caracteres.
4. Ingresar → se ven las secciones **Usuarios** y **Áreas**.

## 2. Ingreso, mensajes y sesión (US1)

| Paso | Esperado |
|---|---|
| Ingresar con contraseña errónea, con un correo inexistente y con un usuario desactivado | Mismo mensaje "Correo o contraseña incorrectos." en los tres (SC-006) |
| `curl -si -X POST <api>/api/backoffice/auth/login -H 'Content-Type: application/json' -d '{"email":" Admin@Diligencia.Local ","password":"..."}'` | `204` con `Set-Cookie: dlg_bo=...; HttpOnly; Secure; SameSite=Strict` (correo normalizado) |
| `GET /api/backoffice/auth/me` con la cookie | `200` con roles `["Administrador"]` |
| `POST /auth/logout` y repetir `GET /auth/me` con la **misma** cookie/token | `401` |
| Token del portal (`dotnet user-jwts ... --scheme Portal -o token`) contra `GET /api/backoffice/auth/me` | `401` (FR-007) |

## 3. Gestión de usuarios y áreas (US2)

Como administrador, desde la UI:

1. Crear área "Compras". Crear usuario "Ana" (`ana@empresa.test`, Compras, rol Analista) en menos
   de 2 min (SC-002) → llega correo de invitación a Mailpit; Ana establece su contraseña e ingresa.
2. Crear otro usuario con `ANA@empresa.test ` → rechazado: correo ya registrado.
3. Con Ana logueada en otra ventana: agregarle Aprobador → su siguiente acción ya ve ambos roles
   (`/auth/me`). Quitarle Analista o desactivarla → su siguiente request da `401`/`403`, sin
   esperar (SC-004 pide ≤ 5 min).
4. Reactivar a Ana → recupera los roles que tenía.
5. Intentar desactivar al único administrador, o quitarle el rol → `409`, y la UI lo explica.
6. Desactivar "Compras" → no aparece al crear usuarios nuevos; Ana la conserva.
7. Como Ana (solo Solicitante/Analista): `curl -si -X POST <api>/api/backoffice/users ...` con su
   token → `403` aunque la UI no muestre la opción (US1-5).

Bitácora (`GET /api/backoffice/audit/events?entityType=Usuario&entityId=<id de Ana>` como
administrador o auditor): un evento por alta, cambio de roles, desactivación y reactivación, con
actor, fecha y valores anterior/nuevo (SC-005). Ningún evento contiene contraseña ni token.

## 4. Protección de la cuenta (US3)

- 5 ingresos fallidos seguidos con Ana → el 6.º, con la contraseña **correcta**, también falla con
  el mismo mensaje. En la bitácora: 5 `LoginFailed` y un `AccountLocked`. A los 15 min ingresa.
- Recuperación: el enlace funciona una vez; reutilizarlo o usarlo pasados 60 min → "enlace inválido
  o vencido". Tras restablecer, las sesiones abiertas de Ana quedan cerradas.
- Inactividad: tras 30 min sin requests, la siguiente da `401` y la UI vuelve a `/login`.

Los plazos (30 min, 15 min, 60 min) se prueban de forma determinista en los tests de integración con
`FakeTimeProvider`; manualmente pueden acortarse con `Identidad__SessionIdleMinutes=1`, etc.

## 5. Tests automáticos

```bash
dotnet test                       # unit + integración (Testcontainers) + arquitectura
npm run lint && npm run typecheck && npm test   # frontend
```

Esperado: todo verde. En particular:

- La matriz de permisos (SC-003) prueba cada endpoint del backoffice con cada uno de los cinco roles
  y compara con la tabla de FR-016.
- El test de superficie de endpoints acepta `AllowAnonymous` solo en `auth/login`,
  `auth/password/forgot` y `auth/password/reset`.
- Los tests de arquitectura siguen verdes (todo lo nuevo de Identidad es `internal`).
