const base = '/api/backoffice';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly errors: Record<string, string[]> = {},
  ) {
    super(title);
  }
}

let onUnauthorized: () => void = () => {};

/** Se invoca ante cualquier 401 (sesión vencida o revocada). */
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

export async function api<T = void>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const hasBody = init.body !== undefined;
  const res = await fetch(base + path, {
    method: init.method ?? (hasBody ? 'POST' : 'GET'),
    credentials: 'same-origin',
    headers: hasBody ? { 'Content-Type': 'application/json' } : undefined,
    body: hasBody ? JSON.stringify(init.body) : undefined,
  });
  if (res.ok) {
    // 202/204 no traen cuerpo.
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  const problem = (await res.json().catch(() => ({}))) as { title?: string; errors?: Record<string, string[]> };
  if (res.status === 401) onUnauthorized();
  throw new ApiError(res.status, problem.title ?? 'Ocurrió un error inesperado.', problem.errors);
}
