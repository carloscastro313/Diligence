import { QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { queryClient } from '../../shared';
import { SolicitudesPage } from './SolicitudesPage';

const base = { cuit: '30712345671', tipoTercero: 'Proveedor', creadaUtc: '2026-03-05T10:00:00Z', esOriginal: true, enlaceNoEntregado: false };
const rows = [
  { ...base, casoId: '1', razonSocial: 'ACME SA', estado: 'EsperaTercero', fechaEstimada: '2026-03-20', enlaceNoEntregado: true },
  { ...base, casoId: '2', razonSocial: 'Beta SRL', estado: 'Aprobado', esOriginal: false, cerradoUtc: '2026-03-10T12:00:00Z' },
];

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

function renderPage(onFiltersChange = vi.fn()) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(rows), { status: 200 }));
  render(
    <QueryClientProvider client={queryClient}>
      <SolicitudesPage filters={{ estado: 'Aprobado' }} onFiltersChange={onFiltersChange} onNueva={vi.fn()} />
    </QueryClientProvider>,
  );
  return onFiltersChange;
}

test('muestra estados abiertos y finales, chip Sumado y enlace no entregado', async () => {
  renderPage();

  expect(await screen.findByText(/ACME SA/)).toBeInTheDocument();
  expect(screen.getByText('Espera del tercero')).toBeInTheDocument();
  expect(screen.getByText('20/3/2026')).toBeInTheDocument();
  expect(screen.getByText('Enlace no entregado')).toBeInTheDocument();
  expect(screen.getByText('Resultado: Aprobado el 10/3/2026')).toBeInTheDocument();
  expect(screen.getByText('Sumado')).toBeInTheDocument();
});

test('el filtro viaja a la API y el cambio se notifica', async () => {
  const onChange = renderPage();
  await screen.findByText(/ACME SA/);
  expect(String(vi.mocked(fetch).mock.calls[0]![0])).toBe('/api/backoffice/solicitudes?estado=Aprobado');

  fireEvent.change(screen.getByLabelText('Buscar'), { target: { value: 'acme' } });
  expect(onChange).toHaveBeenCalledWith({ estado: 'Aprobado', q: 'acme' });
});
