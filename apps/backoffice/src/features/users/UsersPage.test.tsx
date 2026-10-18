import { QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { queryClient } from '../../shared';
import { UserDialog } from './UserDialog';
import { UsersPage } from './UsersPage';

const user = {
  id: 'u1',
  name: 'Ana',
  email: 'ana@test.local',
  areaId: 'a1',
  areaName: 'Compliance',
  roles: ['Analista'],
  isActive: true,
  hasPassword: true,
};
const areas = [{ id: 'a1', name: 'Compliance', isActive: true }];

function mockApi(roles: string[]) {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
    const url = String(input);
    const body = url.endsWith('/auth/me')
      ? { id: 's', name: 'Yo', email: 'yo@test.local', areaName: 'Compliance', roles }
      : url.includes('/users')
        ? [user]
        : areas;
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
  });
}

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

function renderPage() {
  render(
    <QueryClientProvider client={queryClient}>
      <UsersPage filters={{}} onFiltersChange={vi.fn()} />
    </QueryClientProvider>,
  );
}

test('el Auditor ve la lista pero no las acciones de escritura', async () => {
  mockApi(['Auditor']);
  renderPage();

  expect(await screen.findByText('ana@test.local')).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Nuevo usuario' })).not.toBeInTheDocument());
  expect(screen.queryByRole('button', { name: 'Desactivar' })).not.toBeInTheDocument();
});

test('el Administrador ve las acciones de escritura', async () => {
  mockApi(['Administrador']);
  renderPage();

  expect(await screen.findByRole('button', { name: 'Nuevo usuario' })).toBeInTheDocument();
  expect(await screen.findByRole('button', { name: 'Desactivar' })).toBeInTheDocument();
});

test('UserDialog exige al menos un rol', async () => {
  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  render(<UserDialog user={null} areas={areas} onClose={vi.fn()} onSaved={vi.fn()} />);

  fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Luis' } });
  fireEvent.change(screen.getByLabelText('Correo'), { target: { value: 'luis@test.local' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

  expect(await screen.findByText('Selecciona al menos un rol.')).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
});

test('UserDialog muestra el error 409 del servidor', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify({ title: 'Ya existe un usuario con ese correo.' }), { status: 409 }),
  );
  render(<UserDialog user={user} areas={areas} onClose={vi.fn()} onSaved={vi.fn()} />);

  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

  expect(await screen.findByText('Ya existe un usuario con ese correo.')).toBeInTheDocument();
});
