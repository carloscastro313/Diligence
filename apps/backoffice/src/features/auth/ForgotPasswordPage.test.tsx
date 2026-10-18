import { fireEvent, render, screen } from '@testing-library/react';
import { ForgotPasswordPage } from './ForgotPasswordPage';

afterEach(() => vi.restoreAllMocks());

const neutral = 'Si el correo corresponde a una cuenta activa, te enviamos un enlace para restablecer tu contraseña.';

async function request(email: string) {
  const { unmount } = render(<ForgotPasswordPage />);
  fireEvent.change(screen.getByLabelText('Correo'), { target: { value: email } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar enlace' }));
  const message = await screen.findByText(neutral);
  unmount();
  return message.textContent;
}

test('muestra el mismo mensaje neutro para cualquier correo', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation(() => Promise.resolve(new Response(null, { status: 202 })));

  const existing = await request('existe@test.local');
  const unknown = await request('nadie@test.local');

  expect(existing).toBe(unknown);
});

test('valida el formato del correo sin llamar a la API', async () => {
  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  render(<ForgotPasswordPage />);

  fireEvent.change(screen.getByLabelText('Correo'), { target: { value: 'no-es-correo' } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar enlace' }));

  expect(await screen.findByText('Ingresa un correo válido.')).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
});
