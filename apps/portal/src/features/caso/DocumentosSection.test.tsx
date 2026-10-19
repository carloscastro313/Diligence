import type { MockInstance } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { DocumentosSection, type Documentos } from './DocumentosSection';

const json = (status: number, body?: unknown) =>
  Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));

const cargado = { id: '1', nombreOriginal: 'estatuto.pdf', contentType: 'application/pdf', tamanoBytes: 2 * 1024 * 1024, cargadoUtc: '2026-03-10T12:00:00Z' };
const datos: Documentos = {
  tamanoMaximoBytes: 1024 * 1024,
  formatos: ['PDF', 'JPG', 'PNG'],
  documentos: [
    { tipo: 'Estatuto', nombre: 'Estatuto o contrato social', obligatorio: true, cargado: null },
    { tipo: 'ConstanciaArca', nombre: 'Constancia de inscripción en ARCA', obligatorio: true, cargado },
  ],
};

let fetchMock: MockInstance<typeof fetch>;
beforeEach(() => {
  fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(204));
});
afterEach(() => vi.restoreAllMocks());

const archivo = (nombre: string, tipo: string, tamano = 10) => {
  const f = new File(['x'], nombre, { type: tipo });
  Object.defineProperty(f, 'size', { value: tamano });
  return f;
};
const subir = (etiqueta: string, f: File) => fireEvent.change(screen.getByLabelText(etiqueta), { target: { files: [f] } });

test('un archivo válido se sube como multipart y avisa el cambio', async () => {
  const onChanged = vi.fn();
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={onChanged} />);

  subir('Archivo: Estatuto o contrato social', archivo('a.pdf', 'application/pdf'));

  await waitFor(() => expect(onChanged).toHaveBeenCalled());
  const [url, init] = fetchMock.mock.calls[0]!;
  expect(url).toBe('/api/portal/caso/documentos/Estatuto');
  expect(init).toMatchObject({ method: 'PUT' });
  expect((init!.body as FormData).get('archivo')).toBeInstanceOf(File);
});

test('un GIF se rechaza en el cliente', async () => {
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={() => {}} />);
  subir('Archivo: Estatuto o contrato social', archivo('a.gif', 'image/gif'));
  expect(await screen.findByText('Formatos aceptados: PDF, JPG, PNG')).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

test('un archivo grande se rechaza en el cliente', async () => {
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={() => {}} />);
  subir('Archivo: Estatuto o contrato social', archivo('a.pdf', 'application/pdf', 5 * 1024 * 1024));
  expect(await screen.findByText('El máximo es 1 MB')).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

test('muestra el error de la API por documento', async () => {
  fetchMock.mockImplementation(() => json(400, { title: 'El archivo está dañado.' }));
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={() => {}} />);
  subir('Archivo: Estatuto o contrato social', archivo('a.pdf', 'application/pdf'));
  expect(await screen.findByText('El archivo está dañado.')).toBeInTheDocument();
});

test('muestra lo cargado y permite reemplazar', async () => {
  const onChanged = vi.fn();
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={onChanged} />);
  expect(screen.getByText(/estatuto.pdf · 2 MB/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Reemplazar' })).toBeInTheDocument();

  subir('Archivo: Constancia de inscripción en ARCA', archivo('nueva.png', 'image/png'));

  await waitFor(() => expect(onChanged).toHaveBeenCalled());
  expect(fetchMock.mock.calls[0]![0]).toBe('/api/portal/caso/documentos/ConstanciaArca');
});

test('elimina un documento', async () => {
  const onChanged = vi.fn();
  render(<DocumentosSection datos={datos} readOnly={false} onChanged={onChanged} />);
  fireEvent.click(screen.getByRole('button', { name: 'Eliminar' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalled());
  expect(fetchMock.mock.calls[0]![0]).toBe('/api/portal/caso/documentos/ConstanciaArca');
  expect(fetchMock.mock.calls[0]![1]).toMatchObject({ method: 'DELETE' });
});

test('en solo lectura no hay reemplazar ni eliminar', () => {
  render(<DocumentosSection datos={datos} readOnly onChanged={() => {}} />);
  expect(screen.getByText(/estatuto.pdf/)).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/^Archivo:/)).not.toBeInTheDocument();
});
