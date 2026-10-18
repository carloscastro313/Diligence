import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../shared';
import { NuevaSolicitudPage } from './NuevaSolicitudPage';

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});
const renderPage = (onCreated = vi.fn()) =>
  render(
    <QueryClientProvider client={queryClient}>
      <NuevaSolicitudPage onCreated={onCreated} />
    </QueryClientProvider>,
  );

function fill(cuit: string) {
  const set = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
  set('CUIT', cuit);
  set('Razón social', 'ACME SA');
  set('Nombre del contacto', 'Ana');
  set('Correo del contacto', 'ana@acme.test');
  set('Monto anual estimado (ARS)', '1500,50');
  set('Motivo', 'Proveedor nuevo');
}
const send = () => fireEvent.click(screen.getByRole('button', { name: 'Enviar solicitud' }));
const mockFetch = (status: number, body: unknown) =>
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body), { status }));

test('un CUIT inválido bloquea el envío', async () => {
  const fetchSpy = mockFetch(201, {});
  renderPage();
  fill('30712345670');
  send();

  expect(await screen.findByText('El CUIT no es válido.')).toBeInTheDocument();
  expect(fetchSpy).not.toHaveBeenCalled();
});

test('envío correcto llama onCreated con el casoId', async () => {
  const fetchSpy = mockFetch(201, { casoId: 'c1', resultado: 'CasoAbierto' });
  const onCreated = vi.fn();
  renderPage(onCreated);
  fill('30-71234567-1');
  send();

  await waitFor(() => expect(onCreated).toHaveBeenCalledWith('c1'));
  const [url, init] = fetchSpy.mock.calls[0]!;
  expect(String(url)).toBe('/api/backoffice/solicitudes');
  expect(JSON.parse(String(init?.body))).toMatchObject({ cuit: '30-71234567-1', tipoPersona: 'Juridica', montoAnualEstimado: 1500.5, contactoTelefono: null });
});

test('el tipo de persona cambia la etiqueta de razón social', () => {
  renderPage();
  fireEvent.mouseDown(screen.getByLabelText('Tipo de persona'));
  fireEvent.click(screen.getByRole('option', { name: 'Humana' }));

  expect(screen.getByLabelText('Nombre y apellido')).toBeInTheDocument();
});

test('muestra errores del servidor por campo y el title de un 409', async () => {
  mockFetch(400, { title: 'Validación', errors: { Motivo: ['Motivo no permitido.'] } });
  renderPage();
  fill('30712345671');
  send();
  expect(await screen.findByText('Motivo no permitido.')).toBeInTheDocument();

  mockFetch(409, { title: 'El tercero ya está activo.' });
  send();
  expect(await screen.findByText('El tercero ya está activo.')).toBeInTheDocument();
});

test('avisa del tercero existente y deshabilita el envío si no se admite', async () => {
  mockFetch(200, { existe: true, razonSocial: 'ACME SA', estadoTercero: 'Bloqueado', etapaCasoAbierto: null, yaLoSigue: false, admiteSolicitud: false });
  renderPage();
  fill('30-71234567-1');

  expect(await screen.findByText(/ACME SA — estado: Bloqueado/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Enviar solicitud' })).toBeDisabled();
});

test('informa que se sumó al caso existente', async () => {
  const onCreated = vi.fn();
  mockFetch(201, { casoId: 'c9', resultado: 'SumadoACasoAbierto' });
  renderPage(onCreated);
  fill('30-71234567-1');
  send();

  expect(await screen.findByText('Te sumaste al caso existente.')).toBeInTheDocument();
  expect(onCreated).not.toHaveBeenCalled();
});
