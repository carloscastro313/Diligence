import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ResetPasswordPage } from './ResetPasswordPage';

afterEach(() => vi.restoreAllMocks());

function fill(password: string, confirm = password) {
  fireEvent.change(screen.getByLabelText('Nueva contraseña'), { target: { value: password } });
  fireEvent.change(screen.getByLabelText('Repite la contraseña'), { target: { value: confirm } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
}

test('valida la longitud mínima sin llamar a la API', async () => {
  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  render(<ResetPasswordPage token="abc" onDone={vi.fn()} />);

  fill('corta');

  expect(await screen.findByText('Usa al menos 12 caracteres.')).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
});

test('exige que las contraseñas coincidan', async () => {
  render(<ResetPasswordPage token="abc" onDone={vi.fn()} />);

  fill('una-contrasena-larga', 'otra-contrasena-larga');

  expect(await screen.findByText('Las contraseñas no coinciden.')).toBeInTheDocument();
});

test('envía el token y la contraseña, y quita el token de la URL', async () => {
  window.history.replaceState(null, '', '/restablecer?token=abc');
  const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));
  render(<ResetPasswordPage token="abc" onDone={vi.fn()} />);

  fill('una-contrasena-larga');

  expect(await screen.findByText('Tu contraseña quedó establecida.')).toBeInTheDocument();
  expect(JSON.parse(fetchSpy.mock.calls[0]![1]!.body as string)).toEqual({ token: 'abc', newPassword: 'una-contrasena-larga' });
  expect(window.location.search).toBe('');
});

test('muestra el error del servidor para un enlace vencido', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify({ title: 'El enlace no es válido o ya venció.' }), { status: 400 }),
  );
  render(<ResetPasswordPage token="abc" onDone={vi.fn()} />);

  fill('una-contrasena-larga');

  await waitFor(() => expect(screen.getByText('El enlace no es válido o ya venció.')).toBeInTheDocument());
});
