import { QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { RouterProvider } from '@tanstack/react-router';
import { setUnauthorizedHandler } from '../shared';
import { queryClient } from './queryClient';
import { createAppRouter, createMemoryHistory, sesionVencida } from './router';

vi.setConfig({ testTimeout: 20_000 });

afterEach(() => vi.restoreAllMocks());

test('/caso sin sesión (401) redirige a /acceso', async () => {
  sessionStorage.clear();
  vi.spyOn(globalThis, 'fetch').mockImplementation(() => Promise.resolve(new Response('{"title":"x"}', { status: 401 })));
  setUnauthorizedHandler(() => {});
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/caso'] }));
  render(<RouterProvider router={router} />);
  expect(await screen.findByText('Abrí el enlace que recibiste por correo.')).toBeInTheDocument();
  expect(router.state.location.pathname).toBe('/acceso');
});

test('la raíz redirige a /acceso', async () => {
  sessionStorage.clear();
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/'] }));
  render(<RouterProvider router={router} />);
  expect(await screen.findByText('Abrí el enlace que recibiste por correo.')).toBeInTheDocument();
  expect(router.state.location.pathname).toBe('/acceso');
});

test('un 401 con cambios sin guardar vuelve a /acceso con aviso claro y sin el diálogo de cambios', async () => {
  sessionStorage.clear();
  queryClient.clear();
  const caso = { razonSocial: 'Ana Pérez', cuit: '20-12345678-6', tipoPersona: 'Humana', estado: 'EsperaTercero', editable: true, pendientes: [] };
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = String(input);
    if (init?.method === 'PUT') return Promise.resolve(new Response('{"title":"x"}', { status: 401 }));
    const cuerpo = url.endsWith('/caso') ? caso : url.endsWith('/formulario') ? { estado: 'Borrador', datos: {} } : { tamanoMaximoBytes: 1, formatos: [], documentos: [] };
    return Promise.resolve(new Response(JSON.stringify(cuerpo), { status: 200 }));
  });
  const router = createAppRouter(createMemoryHistory({ initialEntries: ['/caso'] }));
  setUnauthorizedHandler(() => sesionVencida(router));
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  const domicilio = (await screen.findByText('Domicilio fiscal', { selector: 'legend' })).closest('fieldset')!;
  fireEvent.change(within(domicilio).getByLabelText('Calle'), { target: { value: 'Mitre' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

  expect(await screen.findByText('Tu sesión venció. Volvé a ingresar con el enlace y un código nuevo; lo guardado se conserva.')).toBeInTheDocument();
  expect(router.state.location.pathname).toBe('/acceso');
  expect(screen.queryByText('Tenés cambios sin guardar')).not.toBeInTheDocument();
});
