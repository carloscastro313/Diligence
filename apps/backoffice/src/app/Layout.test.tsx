import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { act, render, screen, waitFor } from '@testing-library/react';
import { api, queryClient } from '../shared';
import { createAppRouter, createMemoryHistory } from './router';

const session = { id: '1', name: 'Ana', email: 'a@test.local', areaName: 'Compliance', roles: ['Analista'] };

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

test('un 401 en una pantalla protegida lleva a /login', async () => {
  let expired = false;
  vi.spyOn(globalThis, 'fetch').mockImplementation(() =>
    Promise.resolve(
      expired
        ? new Response(JSON.stringify({ title: 'Sesión vencida' }), { status: 401 })
        : new Response(JSON.stringify(session), { status: 200 }),
    ),
  );
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/'] }));
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  expect(await screen.findByText('Ana')).toBeInTheDocument();

  expired = true;
  await act(() => api('/algo').catch(() => {}));

  await waitFor(() => expect(router.state.location.pathname).toBe('/login'));
});

test('el enlace Mis solicitudes solo se ve con rol Solicitante', async () => {
  const render_ = async (roles: string[]) => {
    queryClient.clear();
    vi.spyOn(globalThis, 'fetch').mockImplementation(() =>
      Promise.resolve(new Response(JSON.stringify({ ...session, roles }), { status: 200 })),
    );
    const router = createAppRouter(createMemoryHistory({ initialEntries: ['/'] }));
    return render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );
  };

  const first = await render_(['Solicitante']);
  expect(await screen.findByRole('link', { name: 'Mis solicitudes' })).toBeInTheDocument();
  first.unmount();
  vi.restoreAllMocks();

  await render_(['Analista']);
  expect(await screen.findByText('Ana')).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Mis solicitudes' })).not.toBeInTheDocument();
});
