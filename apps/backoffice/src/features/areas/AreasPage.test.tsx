import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { queryClient } from '../../shared';
import { AreasPage } from './AreasPage';

function mockApi(roles: string[]) {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
    const body = String(input).endsWith('/auth/me')
      ? { id: 's', name: 'Yo', email: 'yo@test.local', areaName: 'Compliance', roles }
      : [{ id: 'a1', name: 'Compras', isActive: true }];
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
  });
}

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

const renderPage = () =>
  render(
    <QueryClientProvider client={queryClient}>
      <AreasPage />
    </QueryClientProvider>,
  );

test('el Auditor ve la lista sin acciones de escritura', async () => {
  mockApi(['Auditor']);
  renderPage();

  expect(await screen.findByText('Compras')).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Crear' })).not.toBeInTheDocument());
  expect(screen.queryByRole('button', { name: 'Desactivar' })).not.toBeInTheDocument();
});

test('el Administrador ve las acciones de escritura', async () => {
  mockApi(['Administrador']);
  renderPage();

  expect(await screen.findByRole('button', { name: 'Crear' })).toBeInTheDocument();
  expect(await screen.findByRole('button', { name: 'Desactivar' })).toBeInTheDocument();
});
