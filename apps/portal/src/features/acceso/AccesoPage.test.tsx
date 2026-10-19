import type { MockInstance } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { StrictMode } from 'react';
import { AccesoPage } from './AccesoPage';

const json = (status: number, body?: unknown, headers: Record<string, string> = {}) =>
  Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status, headers }));

let fetchMock: MockInstance<typeof fetch>;
const llamadas = (url: string) => fetchMock.mock.calls.filter(([u]) => String(u).endsWith(url));

function responder(codigo: () => Promise<Response>, acceso: () => Promise<Response> = () => json(204)) {
  fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input) =>
    String(input).endsWith('/acceso/codigo') ? codigo() : acceso(),
  );
}
const ok = () => json(202, { correo: 'c***@example.com', venceEnMinutos: 10 });

beforeEach(() => {
  sessionStorage.clear();
  history.replaceState(null, '', '/acceso#t=tok123');
});
afterEach(() => vi.restoreAllMocks());

function ingresarCodigo(valor = '123456') {
  fireEvent.change(screen.getByLabelText('Código de 6 dígitos'), { target: { value: valor } });
  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
}

test('limpia el fragmento, pide el código una sola vez con StrictMode y enmascara el correo', async () => {
  responder(ok);
  render(
    <StrictMode>
      <AccesoPage onSuccess={() => {}} />
    </StrictMode>,
  );
  expect(await screen.findByText('Te enviamos un código a c***@example.com.')).toBeInTheDocument();
  expect(location.hash).toBe('');
  expect(llamadas('/acceso/codigo')).toHaveLength(1);
  expect(JSON.parse(String(llamadas('/acceso/codigo')[0]![1]!.body))).toEqual({ token: 'tok123' });
  expect(screen.getByLabelText('Código de 6 dígitos')).toHaveAttribute('autocomplete', 'one-time-code');
  expect(screen.getByLabelText('Código de 6 dígitos')).toHaveAttribute('inputmode', 'numeric');
});

test('al recargar no reenvía el código y ofrece reenviar', async () => {
  responder(ok);
  const primera = render(<AccesoPage onSuccess={() => {}} />);
  await screen.findByText(/Te enviamos un código/);
  primera.unmount();
  history.replaceState(null, '', '/acceso');

  render(<AccesoPage onSuccess={() => {}} />);

  expect(screen.getByLabelText('Código de 6 dígitos')).toBeInTheDocument();
  expect(llamadas('/acceso/codigo')).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Reenviar código' }));
  await waitFor(() => expect(llamadas('/acceso/codigo')).toHaveLength(2));
  expect(await screen.findByText(/c\*\*\*@example.com/)).toBeInTheDocument();
});

test('sin token no llama a la API', () => {
  history.replaceState(null, '', '/acceso');
  responder(ok);
  render(<AccesoPage onSuccess={() => {}} />);
  expect(screen.getByText('Abrí el enlace que recibiste por correo.')).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

test('ingresa con el código y navega', async () => {
  responder(ok);
  const onSuccess = vi.fn();
  render(<AccesoPage onSuccess={onSuccess} />);
  await screen.findByText(/Te enviamos un código/);

  ingresarCodigo();

  await waitFor(() => expect(onSuccess).toHaveBeenCalled());
  expect(JSON.parse(String(llamadas('/acceso')[0]![1]!.body))).toEqual({ token: 'tok123', codigo: '123456' });
});

test('enlace inválido (404)', async () => {
  responder(() => json(404, { title: 'x' }));
  render(<AccesoPage onSuccess={() => {}} />);
  expect(await screen.findByText('El enlace no es válido o venció. Comunicate con quien te pidió el alta.')).toBeInTheDocument();
  expect(screen.queryByLabelText('Código de 6 dígitos')).not.toBeInTheDocument();
});

test('código incorrecto y código invalidado (401)', async () => {
  let n = 0;
  responder(ok, () => json(401, { title: 'x', ...(++n > 1 && { codigoInvalidado: true }) }));
  render(<AccesoPage onSuccess={() => {}} />);
  await screen.findByText(/Te enviamos un código/);

  ingresarCodigo();
  expect(await screen.findByText('El código no es válido.')).toBeInTheDocument();
  ingresarCodigo();
  expect(await screen.findByText(/ya no se puede usar/)).toBeInTheDocument();
});

test('código con formato inválido no llama a la API', async () => {
  responder(ok);
  render(<AccesoPage onSuccess={() => {}} />);
  await screen.findByText(/Te enviamos un código/);
  ingresarCodigo('12');
  expect(await screen.findByText('El código tiene 6 dígitos.')).toBeInTheDocument();
  expect(llamadas('/acceso')).toHaveLength(0);
});

test('429 muestra el mensaje y una cuenta regresiva en Reenviar', async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  responder(() => json(429, { title: 'x' }, { 'Retry-After': '30' }));
  render(<AccesoPage onSuccess={() => {}} />);
  expect(await screen.findByText(/demasiados intentos/)).toBeInTheDocument();
  const boton = screen.getByRole('button', { name: /Reenviar código/ });
  expect(boton).toHaveTextContent('(30 s)');
  expect(boton).toBeDisabled();

  for (let i = 0; i < 30; i++) await act(() => vi.advanceTimersByTimeAsync(1000));
  vi.useRealTimers();
  expect(screen.getByRole('button', { name: 'Reenviar código' })).toBeEnabled();
});

test('503 pide reintentar', async () => {
  responder(() => json(503, { title: 'x' }));
  render(<AccesoPage onSuccess={() => {}} />);
  expect(await screen.findByText(/No pudimos enviarte el correo/)).toBeInTheDocument();
});
