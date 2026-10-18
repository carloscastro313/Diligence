import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { LoginPage } from './LoginPage';

const json = (status: number, body?: unknown) =>
  Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));

afterEach(() => vi.restoreAllMocks());

function fill() {
  fireEvent.change(screen.getByLabelText('Correo'), { target: { value: 'ana@test.local' } });
  fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'secreta-123456' } });
  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
}

test('muestra el mensaje del servidor ante un 401', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(401, { title: 'Correo o contraseña incorrectos.' }));
  const onSuccess = vi.fn();
  render(<LoginPage onSuccess={onSuccess} />);

  fill();

  expect(await screen.findByText('Correo o contraseña incorrectos.')).toBeInTheDocument();
  expect(onSuccess).not.toHaveBeenCalled();
});

test('navega al ingresar correctamente', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) =>
    String(input).endsWith('/auth/login')
      ? json(204)
      : json(200, { id: '1', name: 'Ana', email: 'ana@test.local', areaName: 'Compliance', roles: ['Analista'] }),
  );
  const onSuccess = vi.fn();
  render(<LoginPage onSuccess={onSuccess} />);

  fill();

  await waitFor(() => expect(onSuccess).toHaveBeenCalled());
});

test('valida campos vacíos sin llamar a la API', async () => {
  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  render(<LoginPage onSuccess={vi.fn()} />);

  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));

  expect(await screen.findByText('Ingresa tu correo.')).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
});
