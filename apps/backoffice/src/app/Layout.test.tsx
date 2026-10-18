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
