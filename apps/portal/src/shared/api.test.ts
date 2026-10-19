import { api, ApiError, setUnauthorizedHandler } from './api';

function respond(status: number, body?: unknown, headers: Record<string, string> = {}) {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(body === undefined ? null : JSON.stringify(body), { status, headers }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  setUnauthorizedHandler(() => {});
});

test('envía JSON con la cookie y devuelve el cuerpo', async () => {
  const fetchMock = respond(200, { ok: 1 });
  await expect(api('/caso/formulario', { method: 'PUT', body: { a: 1 } })).resolves.toEqual({ ok: 1 });
  const [url, init] = fetchMock.mock.calls[0]!;
  expect(url).toBe('/api/portal/caso/formulario');
  expect(init).toMatchObject({ method: 'PUT', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' } });
  expect(init.body).toBe('{"a":1}');
});

test('con FormData no fija el Content-Type', async () => {
  const fetchMock = respond(200, {});
  const form = new FormData();
  form.append('archivo', new Blob(['x']), 'a.pdf');
  await api('/caso/documentos/Estatuto', { method: 'PUT', body: form });
  const init = fetchMock.mock.calls[0]![1];
  expect(init.body).toBe(form);
  expect(init.headers).toBeUndefined();
});

test('un 401 invoca el handler y lanza ApiError con extras y Retry-After', async () => {
  const handler = vi.fn();
  setUnauthorizedHandler(handler);
  respond(401, { title: 'No', codigoInvalidado: true }, { 'Retry-After': '30' });
  const error = await api('/acceso', { body: {} }).catch((e: unknown) => e);
  expect(handler).toHaveBeenCalledOnce();
  expect(error).toBeInstanceOf(ApiError);
  expect(error).toMatchObject({ status: 401, title: 'No', extras: { codigoInvalidado: true }, retryAfter: 30 });
});

test('un 204 devuelve undefined', async () => {
  respond(204);
  await expect(api('/acceso', { body: {} })).resolves.toBeUndefined();
});
