import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryHistory, createRootRoute, createRoute, createRouter, RouterProvider } from '@tanstack/react-router';
import type { ComponentProps } from 'react';
import type { MockInstance } from 'vitest';
import { FormularioSection } from './FormularioSection';

// El formulario tiene cientos de opciones de país: jsdom tarda en renderizarlo.
vi.setConfig({ testTimeout: 20_000 });

type Props = ComponentProps<typeof FormularioSection>;

const json = (status: number, body?: unknown) =>
  Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));

const juridica = { cuit: '30-12345678-1', razonSocial: 'ACME SA', tipoPersona: 'Juridica', editable: true };
const humana = { cuit: '20-12345678-6', razonSocial: 'Ana Pérez', tipoPersona: 'Humana', editable: true };
const borrador = (datos = {}, version?: string): Props['formulario'] => ({ estado: 'Borrador', datos, version });
const guardado = (sobrescrito = false, version = 'v1') => ({ version, guardadoUtc: '2026-03-10T12:00:00Z', sobrescrito });

let fetchMock: MockInstance<typeof fetch>;
beforeEach(() => {
  fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(200, guardado()));
});
afterEach(() => vi.restoreAllMocks());

// useBlocker necesita un router: la sección se monta en "/" y hay otra ruta para probar la salida.
async function montar(caso: Props['caso'], formulario: Props['formulario'], onEnviado: Props['onEnviado'] = () => {}) {
  const root = createRootRoute();
  const inicio = createRoute({
    getParentRoute: () => root,
    path: '/',
    component: () => <FormularioSection caso={caso} formulario={formulario} onEnviado={onEnviado} />,
  });
  const otra = createRoute({ getParentRoute: () => root, path: '/otra', component: () => <p>Otra página</p> });
  const router = createRouter({ routeTree: root.addChildren([inicio, otra]), history: createMemoryHistory({ initialEntries: ['/'] }) });
  render(<RouterProvider router={router} />);
  await screen.findByRole('heading', { name: 'Formulario' });
  return router;
}

// Por la leyenda del fieldset: getByRole('group') es muy lento con las 249 opciones de país en el DOM.
const legend = (nombre: string) => screen.queryByText(nombre, { selector: 'legend' });
const grupo = (nombre: string) => within(screen.getByText(nombre, { selector: 'legend' }).closest('fieldset')!);
const poner = (w: ReturnType<typeof within>, etiqueta: string, valor: string) =>
  fireEvent.change(w.getByLabelText(etiqueta), { target: { value: valor } });
const domicilio = (nombre: string) => {
  const g = grupo(nombre);
  poner(g, 'Calle', 'Mitre');
  poner(g, 'Número', '123');
  poner(g, 'Localidad', 'CABA');
  poner(g, 'Provincia', 'Buenos Aires');
  poner(g, 'Código postal', '1000');
  poner(g, 'País', 'AR');
};
const llenarComun = (fiscales: string) => {
  poner(grupo(fiscales), 'Condición frente al IVA', 'Monotributo');
  domicilio('Domicilio fiscal');
  poner(grupo('Actividad'), 'Descripción de la actividad', 'Software');
  poner(grupo('Actividad'), 'País donde se desarrolla', 'AR');
  poner(grupo('Origen de los fondos'), 'Origen de los fondos', 'Venta de servicios');
};
const llenarHumana = () => {
  llenarComun('Datos personales');
  const g = grupo('Datos personales');
  poner(g, 'Fecha de nacimiento', '1985-03-10');
  poner(g, 'Nacionalidad', 'AR');
  poner(g, 'Tipo de documento', 'DNI');
  poner(g, 'Número de documento', '12345678');
};
const llenarJuridica = () => {
  llenarComun('Datos fiscales');
  const g = grupo('Datos fiscales');
  poner(g, 'Fecha de constitución', '2010-05-01');
  poner(g, 'Número de inscripción', '12345');
  fireEvent.click(grupo('Domicilio legal').getByLabelText('Es igual al domicilio fiscal'));
  for (const [i, pct] of ['60', '40'].entries()) {
    fireEvent.click(screen.getByRole('button', { name: 'Agregar beneficiario' }));
    const b = grupo(`Beneficiario ${i + 1}`);
    poner(b, 'Nombre y apellido', `Socio ${i + 1}`);
    poner(b, 'Documento', `2011122${i}`);
    poner(b, 'Nacionalidad', 'AR');
    poner(b, 'Porcentaje de participación (%)', pct);
  }
  poner(grupo('Representante 1'), 'Nombre y apellido', 'Ana');
  poner(grupo('Representante 1'), 'Documento', '20111222');
};
const cuerpo = (n: number) => JSON.parse(String(fetchMock.mock.calls[n]![1]!.body));
const url = (n: number) => String(fetchMock.mock.calls[n]![0]);

test('persona jurídica: secciones, CUIT y razón social deshabilitados, sin declaración PEP propia', async () => {
  await montar(juridica, borrador());
  for (const g of ['Datos fiscales', 'Domicilio fiscal', 'Domicilio legal', 'Actividad', 'Beneficiarios finales', 'Representantes legales', 'Origen de los fondos'])
    expect(legend(g)).toBeInTheDocument();
  expect(legend('Declaración PEP')).not.toBeInTheDocument();
  expect(screen.getByLabelText('CUIT')).toHaveValue('30-12345678-1');
  expect(screen.getByLabelText('CUIT')).toBeDisabled();
  expect(screen.getByLabelText('Razón social')).toHaveValue('ACME SA');
  expect(screen.getByLabelText('Razón social')).toBeDisabled();
  expect(legend('Representante 1')).toBeInTheDocument();
  // El domicilio legal se oculta al marcar "igual al fiscal".
  expect(grupo('Domicilio legal').getByLabelText('Calle')).toBeInTheDocument();
  fireEvent.click(grupo('Domicilio legal').getByLabelText('Es igual al domicilio fiscal'));
  expect(grupo('Domicilio legal').queryByLabelText('Calle')).not.toBeInTheDocument();
});

test('persona humana: sin beneficiarios, con nacimiento, nacionalidad, documento y PEP propia', async () => {
  await montar(humana, borrador());
  expect(legend('Beneficiarios finales')).not.toBeInTheDocument();
  expect(legend('Domicilio legal')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Nombre y apellido')).toHaveValue('Ana Pérez');
  const g = grupo('Datos personales');
  for (const l of ['Fecha de nacimiento', 'Nacionalidad', 'Tipo de documento', 'Número de documento']) expect(g.getByLabelText(l)).toBeInTheDocument();
  // Cargo y organismo aparecen solo si es PEP.
  const pep = grupo('Declaración PEP');
  expect(pep.queryByLabelText('Cargo')).not.toBeInTheDocument();
  fireEvent.click(pep.getByLabelText('Es una persona expuesta políticamente (PEP)'));
  expect(pep.getByLabelText('Cargo')).toBeInTheDocument();
  expect(pep.getByLabelText('Organismo')).toBeInTheDocument();
});

test('beneficiarios: agregar, quitar y excluyente con "No hay beneficiarios identificables"', async () => {
  await montar(juridica, borrador());
  const sin = screen.getByLabelText('No hay beneficiarios identificables');
  fireEvent.click(screen.getByRole('button', { name: 'Agregar beneficiario' }));
  fireEvent.click(screen.getByRole('button', { name: 'Agregar beneficiario' }));
  expect(legend('Beneficiario 2')).toBeInTheDocument();
  expect(sin).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Quitar beneficiario 1' }));
  fireEvent.click(screen.getByRole('button', { name: 'Quitar beneficiario 1' }));
  expect(legend('Beneficiario 1')).not.toBeInTheDocument();

  fireEvent.click(sin);
  expect(screen.getByLabelText(/Explicá por qué no hay beneficiarios/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Agregar beneficiario' })).toBeDisabled();
  fireEvent.click(sin);
  expect(screen.queryByLabelText(/Explicá por qué no hay beneficiarios/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Agregar beneficiario' })).toBeEnabled();
});

test('Enviar con datos incompletos marca los campos y no abre la confirmación', async () => {
  await montar(juridica, borrador());
  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  expect(await grupo('Domicilio fiscal').findAllByText('Este dato es obligatorio.')).not.toHaveLength(0);
  expect(screen.queryByText('¿Enviar la información?')).not.toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

test('Enviar sin completar muestra ya el error de beneficiarios y de representantes', async () => {
  await montar(juridica, borrador());
  fireEvent.click(screen.getByRole('button', { name: 'Quitar representante 1' }));
  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  expect(await screen.findByText('Agregá al menos un beneficiario final o indicá que no hay beneficiarios identificables.')).toBeInTheDocument();
  expect(screen.getByText('Agregá al menos un representante.')).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

test('"No hay beneficiarios identificables" se guarda con la explicación vacía y se recupera marcada', async () => {
  await montar(juridica, borrador());
  fireEvent.click(screen.getByLabelText('No hay beneficiarios identificables'));
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  expect(await screen.findByText('Guardado')).toBeInTheDocument();
  expect(cuerpo(0).datos.sinBeneficiarios).toEqual({ explicacion: '' });
});

test.each([[{ explicacion: '' }], [{}]])('al cargar con sinBeneficiarios %j el checkbox aparece marcado', async (sinBeneficiarios) => {
  await montar(juridica, borrador({ sinBeneficiarios }));
  expect(screen.getByLabelText('No hay beneficiarios identificables')).toBeChecked();
  expect(screen.getByLabelText(/Explicá por qué no hay beneficiarios/)).toHaveValue('');
});

test('Enviar pide confirmación, manda los datos sin vacíos y avisa', async () => {
  const enviado = { estado: 'Enviado', datos: {}, enviadoUtc: '2026-03-10T12:00:00Z' };
  fetchMock.mockImplementation(() => json(200, enviado));
  const onEnviado = vi.fn();
  await montar(humana, borrador(), onEnviado);
  llenarHumana();

  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  expect(await screen.findByText('¿Enviar la información?')).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar envío' }));

  await waitFor(() => expect(onEnviado).toHaveBeenCalledWith(enviado));
  expect(url(0)).toBe('/api/portal/caso/formulario/envio');
  expect(fetchMock.mock.calls[0]![1]).toMatchObject({ method: 'POST' });
  expect(cuerpo(0)).toEqual({
    datos: {
      condicionIva: 'Monotributo',
      fechaNacimiento: '1985-03-10',
      nacionalidad: 'AR',
      documento: { tipo: 'DNI', numero: '12345678' },
      domicilioFiscal: { calle: 'Mitre', numero: '123', localidad: 'CABA', provincia: 'Buenos Aires', codigoPostal: '1000', pais: 'AR' },
      actividad: { descripcion: 'Software', pais: 'AR' },
      pep: { esPep: false },
      origenFondos: 'Venta de servicios',
    },
  });
});

test('los errores del servidor se mapean a campos con índices y a Alert', async () => {
  fetchMock.mockImplementation(() =>
    json(400, {
      title: 'Uno o más datos no son válidos.',
      errors: {
        'domicilioFiscal.calle': ['La calle no es válida.'],
        'beneficiarios[1].porcentaje': ['El porcentaje no es válido.'],
        beneficiarios: ['La suma de los porcentajes supera 100.'],
        documentos: ['Falta subir: Estatuto o contrato social'],
      },
    }),
  );
  const onEnviado = vi.fn();
  await montar(juridica, borrador(), onEnviado);
  llenarJuridica();

  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirmar envío' }));

  expect(await grupo('Domicilio fiscal').findByText('La calle no es válida.')).toBeInTheDocument();
  expect(grupo('Beneficiario 2').getByText('El porcentaje no es válido.')).toBeInTheDocument();
  expect(screen.getByText('La suma de los porcentajes supera 100.')).toBeInTheDocument();
  expect(screen.getByText('Falta subir: Estatuto o contrato social')).toBeInTheDocument();
  expect(onEnviado).not.toHaveBeenCalled();
  // El diálogo se cierra y se puede volver a enviar.
  await waitFor(() => expect(screen.queryByText('¿Enviar la información?')).not.toBeInTheDocument());
  expect(screen.getByRole('button', { name: 'Enviar' })).toBeEnabled();
});

test('un 409 muestra el aviso del servidor', async () => {
  fetchMock.mockImplementation(() => json(409, { title: 'El trámite ya no se puede modificar.' }));
  await montar(humana, borrador());
  llenarHumana();
  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirmar envío' }));
  expect(await screen.findByText('El trámite ya no se puede modificar.')).toBeInTheDocument();
});

test('enviado: solo lectura con el aviso "Recibimos tu información"', async () => {
  const datos = { condicionIva: 'Monotributo', origenFondos: 'Venta de servicios', domicilioFiscal: { calle: 'Mitre' } };
  await montar(humana, { estado: 'Enviado', datos, enviadoUtc: '2026-03-10T12:00:00Z' });
  expect(screen.getByText(/Recibimos tu información/)).toBeInTheDocument();
  expect(grupo('Domicilio fiscal').getByLabelText('Calle')).toHaveValue('Mitre');
  expect(grupo('Domicilio fiscal').getByLabelText('Calle')).toBeDisabled();
  expect(screen.getByLabelText('Origen de los fondos')).toBeDisabled();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('con el caso no editable el formulario queda en solo lectura sin aviso de envío', async () => {
  await montar({ ...humana, editable: false }, borrador());
  expect(screen.queryByText(/Recibimos tu información/)).not.toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('Guardar con campos vacíos no valida obligatorios y no manda strings vacíos', async () => {
  await montar(juridica, borrador());
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

  expect(await screen.findByText('Guardado')).toBeInTheDocument();
  expect(url(0)).toBe('/api/portal/caso/formulario');
  expect(fetchMock.mock.calls[0]![1]).toMatchObject({ method: 'PUT' });
  const { datos, version } = cuerpo(0);
  expect(version).toBeNull();
  expect(datos).toEqual({ domicilioLegalIgualFiscal: false, representantes: [{ pep: { esPep: false } }] });
  expect(screen.queryByText('Este dato es obligatorio.')).not.toBeInTheDocument();
});

test('Guardar omite los vacíos, manda la última versión y avisa si otra sesión se sobrescribió', async () => {
  fetchMock
    .mockImplementationOnce(() => json(200, guardado(true, 'v2')))
    .mockImplementationOnce(() => json(200, guardado(false, 'v3')));
  await montar(humana, borrador({ origenFondos: 'Honorarios' }, 'v1'));
  poner(grupo('Domicilio fiscal'), 'Calle', 'Mitre');
  poner(grupo('Domicilio fiscal'), 'Piso (opcional)', '  ');

  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  expect(await screen.findByText('Otra sesión guardó cambios y se sobrescribieron.')).toBeInTheDocument();
  expect(cuerpo(0)).toEqual({
    datos: { origenFondos: 'Honorarios', domicilioFiscal: { calle: 'Mitre' }, pep: { esPep: false } },
    version: 'v1',
  });

  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  expect(await screen.findByText('Guardado')).toBeInTheDocument();
  expect(cuerpo(1).version).toBe('v2');
});

test('un error de forma al guardar se muestra en el campo', async () => {
  fetchMock.mockImplementation(() => json(400, { title: 'Datos inválidos.', errors: { 'actividad.pais': ['El país no es válido.'] } }));
  await montar(humana, borrador());
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  expect(await grupo('Actividad').findByText('El país no es válido.')).toBeInTheDocument();
});

test('Guardar y Enviar quedan deshabilitados mientras se guarda', async () => {
  let terminar!: () => void;
  fetchMock.mockImplementation(() => new Promise<Response>((r) => (terminar = () => r(new Response(JSON.stringify(guardado()), { status: 200 })))));
  await montar(humana, borrador());
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled());
  expect(screen.getByRole('button', { name: 'Enviar' })).toBeDisabled();
  await act(async () => terminar());
  await waitFor(() => expect(screen.getByRole('button', { name: 'Enviar' })).toBeEnabled());
  expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled();
});

test('con cambios sin guardar avisa al salir; guardado, deja salir', async () => {
  const router = await montar(humana, borrador());
  const salir = new Event('beforeunload', { cancelable: true });

  // Sin cambios no hay aviso.
  window.dispatchEvent(salir);
  expect(salir.defaultPrevented).toBe(false);

  poner(grupo('Domicilio fiscal'), 'Calle', 'Mitre');
  const conCambios = new Event('beforeunload', { cancelable: true });
  await waitFor(() => {
    window.dispatchEvent(conCambios);
    expect(conCambios.defaultPrevented).toBe(true);
  });

  // La navegación bloqueada no resuelve hasta que el usuario decide: no se espera.
  act(() => void router.navigate({ to: '/otra' as never }));
  expect(await screen.findByText('Tenés cambios sin guardar')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Quedarme' }));
  await waitFor(() => expect(screen.queryByText('Tenés cambios sin guardar')).not.toBeInTheDocument());
  expect(router.state.location.pathname).toBe('/');

  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  await screen.findByText('Guardado');
  await act(() => router.navigate({ to: '/otra' as never }));
  expect(await screen.findByText('Otra página')).toBeInTheDocument();
});

test('"Salir sin guardar" sigue con la navegación', async () => {
  const router = await montar(humana, borrador());
  poner(grupo('Domicilio fiscal'), 'Calle', 'Mitre');
  await waitFor(() => expect(router.state.status).toBe('idle'));
  void router.navigate({ to: '/otra' as never });
  fireEvent.click(await screen.findByRole('button', { name: 'Salir sin guardar' }));
  expect(await screen.findByText('Otra página')).toBeInTheDocument();
});
