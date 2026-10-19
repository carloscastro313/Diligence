import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryHistory, createRootRoute, createRoute, createRouter, RouterProvider } from '@tanstack/react-router';
import { CasoPage } from './CasoPage';

const json = (status: number, body?: unknown) =>
  Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));

// El formulario tiene cientos de opciones de país: jsdom tarda en renderizarlo.
vi.setConfig({ testTimeout: 20_000 });

afterEach(() => vi.restoreAllMocks());

// El formulario usa useBlocker, que necesita un router.
function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const root = createRootRoute();
  const ruta = createRoute({ getParentRoute: () => root, path: '/', component: CasoPage });
  const router = createRouter({ routeTree: root.addChildren([ruta]), history: createMemoryHistory({ initialEntries: ['/'] }) });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

const formulario = { estado: 'Borrador', datos: {} };
// Enruta por URL: caso, documentos y formulario.
const ruteo = (input: unknown, c: object, d: object, f: object = formulario) => {
  const url = String(input);
  return json(200, url.endsWith('/caso') ? c : url.endsWith('/formulario') ? f : d);
};

const caso = {
  razonSocial: 'ACME SA',
  cuit: '30-12345678-1',
  tipoPersona: 'Juridica',
  estado: 'EsperaTercero',
  editable: true,
  pendientes: ['Subir: Estatuto o contrato social'],
};
const documentos = (cargado: object | null) => ({
  tamanoMaximoBytes: 10 * 1024 * 1024,
  formatos: ['PDF', 'JPG', 'PNG'],
  documentos: [{ tipo: 'Estatuto', nombre: 'Estatuto o contrato social', obligatorio: true, cargado }],
});

test('muestra razón social, CUIT, estado legible y pendientes', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) => ruteo(input, caso, documentos(null)));
  renderPage();
  expect(await screen.findByRole('heading', { name: 'ACME SA' })).toBeInTheDocument();
  expect(screen.getByText(/CUIT 30-12345678-1/)).toHaveTextContent('Estado: Espera del tercero');
  expect(screen.getByText('Subir: Estatuto o contrato social')).toBeInTheDocument();
  expect(await screen.findByRole('heading', { name: 'Estatuto o contrato social' })).toBeInTheDocument();
});

test('tras subir un documento recarga documentos y caso', async () => {
  let subido = false;
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = String(input);
    if (init?.method === 'PUT') {
      subido = true;
      return json(200, {});
    }
    return ruteo(
      url,
      { ...caso, pendientes: subido ? [] : caso.pendientes },
      documentos(subido ? { id: '1', nombreOriginal: 'estatuto.pdf', contentType: 'application/pdf', tamanoBytes: 1024, cargadoUtc: '2026-03-10T12:00:00Z' } : null),
    );
  });
  renderPage();
  await screen.findByRole('heading', { name: 'Estatuto o contrato social' });

  fireEvent.change(screen.getByLabelText('Archivo: Estatuto o contrato social'), {
    target: { files: [new File(['x'], 'estatuto.pdf', { type: 'application/pdf' })] },
  });

  expect(await screen.findByText(/estatuto.pdf · 0 MB/)).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByText('Te falta')).not.toBeInTheDocument());
  expect(fetchMock.mock.calls.filter(([u]) => String(u).endsWith('/caso'))).toHaveLength(2);
});

test('con el caso no editable los documentos quedan en solo lectura', async () => {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) =>
    ruteo(input, { ...caso, editable: false, estado: 'EnVerificacion', pendientes: [] }, documentos(null), { estado: 'Enviado', datos: {} }),
  );
  renderPage();
  await screen.findByRole('heading', { name: 'Estatuto o contrato social' });
  expect(await screen.findByText(/Recibimos tu información/)).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('tras enviar el formulario recarga caso y documentos y queda en solo lectura', async () => {
  const completo = {
    condicionIva: 'Monotributo',
    fechaNacimiento: '1985-03-10',
    nacionalidad: 'AR',
    documento: { tipo: 'DNI', numero: '12345678' },
    domicilioFiscal: { calle: 'Mitre', numero: '123', localidad: 'CABA', provincia: 'Buenos Aires', codigoPostal: '1000', pais: 'AR' },
    actividad: { descripcion: 'Software', pais: 'AR' },
    pep: { esPep: false },
    origenFondos: 'Venta de servicios',
  };
  let enviado = false;
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    if (init?.method === 'POST') {
      enviado = true;
      return json(200, { estado: 'Enviado', datos: completo });
    }
    return ruteo(
      input,
      enviado ? { ...caso, tipoPersona: 'Humana', editable: false, estado: 'EnVerificacion', pendientes: [] } : { ...caso, tipoPersona: 'Humana' },
      documentos(null),
      { estado: enviado ? 'Enviado' : 'Borrador', datos: completo },
    );
  });
  renderPage();
  fireEvent.click(await screen.findByRole('button', { name: 'Enviar' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirmar envío' }));

  expect(await screen.findByText(/Recibimos tu información/)).toBeInTheDocument();
  await waitFor(() => expect(screen.getByText(/CUIT.*Estado: En verificación/)).toBeInTheDocument());
  const pedidos = (fin: string) => fetchMock.mock.calls.filter(([u]) => String(u).endsWith(fin)).length;
  expect(pedidos('/caso')).toBe(2);
  expect(pedidos('/caso/documentos')).toBe(2);
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Enviar' })).not.toBeInTheDocument());
});
