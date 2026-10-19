const base = '/api/portal';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly errors: Record<string, string[]> = {},
    // Campos extra del ProblemDetails (p. ej. codigoInvalidado) y Retry-After en segundos.
    readonly extras: Record<string, unknown> = {},
    readonly retryAfter?: number,
  ) {
    super(title);
  }
}

let onUnauthorized: () => void = () => {};

/** Se invoca ante cualquier 401 (sesión vencida). */
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

// shortcut: copia de apps/backoffice/src/shared/api.ts (~35 líneas); mover a packages/api-client al tercer uso
export async function api<T = void>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const hasBody = init.body !== undefined;
  const isForm = init.body instanceof FormData;
  const res = await fetch(base + path, {
    method: init.method ?? (hasBody ? 'POST' : 'GET'),
    credentials: 'same-origin',
    // Con FormData el navegador pone el Content-Type con el boundary.
    headers: hasBody && !isForm ? { 'Content-Type': 'application/json' } : undefined,
    body: !hasBody ? undefined : isForm ? (init.body as FormData) : JSON.stringify(init.body),
  });
  if (res.ok) {
    // 202/204 no traen cuerpo.
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  const { title, errors, ...extras } = (await res.json().catch(() => ({}))) as {
    title?: string;
    errors?: Record<string, string[]>;
  } & Record<string, unknown>;
  if (res.status === 401) onUnauthorized();
  const retryAfter = Number(res.headers.get('Retry-After'));
  throw new ApiError(res.status, title ?? 'Ocurrió un error inesperado.', errors, extras, retryAfter > 0 ? retryAfter : undefined);
}
