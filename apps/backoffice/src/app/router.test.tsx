import { queryClient } from '../shared';
import { createAppRouter, createMemoryHistory } from './router';

const session = { id: '1', name: 'Ana', email: 'a@test.local', areaName: 'Compliance', roles: ['Analista'] };

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

const withMe = (status: number) =>
  vi.spyOn(globalThis, 'fetch').mockImplementation(() =>
    Promise.resolve(new Response(status === 200 ? JSON.stringify(session) : JSON.stringify({ title: 'x' }), { status })),
  );

test('sin sesión, una ruta protegida redirige a /login', async () => {
  withMe(401);
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/'] }));

  await router.load();

  expect(router.state.location.pathname).toBe('/login');
});

test('con sesión, /login redirige a /', async () => {
  withMe(200);
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/login'] }));

  await router.load();

  expect(router.state.location.pathname).toBe('/');
});
