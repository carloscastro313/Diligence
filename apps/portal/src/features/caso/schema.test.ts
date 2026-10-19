import { PAISES } from '../../shared';
import { formularioSchema } from './schema';

const domicilio = { calle: 'Calle', numero: '123', localidad: 'CABA', provincia: 'CABA', codigoPostal: '1000', pais: 'AR' };
const pep = { esPep: false };
const juridica = {
  condicionIva: 'ResponsableInscripto',
  domicilioFiscal: domicilio,
  actividad: { descripcion: 'Software', pais: 'AR' },
  origenFondos: 'Ventas de servicios',
  fechaConstitucion: '2010-05-01',
  numeroInscripcion: '12345',
  domicilioLegalIgualFiscal: true,
  beneficiarios: [{ nombre: 'Ana', documento: '20111222', nacionalidad: 'AR', porcentaje: 60, pep }],
  representantes: [{ nombre: 'Ana', documento: '20111222', pep }],
};
const humana = {
  condicionIva: 'Monotributo',
  domicilioFiscal: domicilio,
  actividad: { descripcion: 'Consultoría', pais: 'AR' },
  origenFondos: 'Honorarios profesionales',
  fechaNacimiento: '1985-03-10',
  nacionalidad: 'AR',
  documento: { tipo: 'DNI', numero: '12345678' },
  pep,
};

const paths = (r: { success: boolean; error?: { issues: { path: PropertyKey[] }[] } }) =>
  r.error?.issues.map((i) => i.path.join('.')) ?? [];
const j = formularioSchema('Juridica');
const h = formularioSchema('Humana');

test('países: códigos ISO y nombres en español', () => {
  expect(PAISES).toHaveLength(249);
  expect(PAISES.find((p) => p.codigo === 'AR')?.nombre).toBe('Argentina');
  expect(PAISES.find((p) => p.codigo === 'ES')?.nombre).toBe('España');
});

test('persona jurídica válida', () => {
  expect(j.safeParse(juridica).success).toBe(true);
});

test('persona jurídica vacía marca los campos obligatorios', () => {
  const p = paths(j.safeParse({}));
  expect(p).toEqual(expect.arrayContaining(['condicionIva', 'fechaConstitucion', 'numeroInscripcion', 'domicilioFiscal', 'actividad', 'origenFondos', 'representantes']));
  expect(p).not.toContain('fechaNacimiento');
});

test('persona humana válida, e ignora campos de jurídica', () => {
  const r = h.safeParse({ ...humana, beneficiarios: [{}] });
  expect(r.success).toBe(true);
  expect(r.data).not.toHaveProperty('beneficiarios');
});

test('persona humana exige sus campos', () => {
  const p = paths(h.safeParse({}));
  expect(p).toEqual(expect.arrayContaining(['fechaNacimiento', 'nacionalidad', 'documento', 'pep']));
  expect(p).not.toContain('representantes');
});

test('domicilio legal: igual al fiscal o completo', () => {
  const sin = { ...juridica, domicilioLegalIgualFiscal: false };
  expect(paths(j.safeParse(sin))).toContain('domicilioLegal.calle');
  expect(j.safeParse({ ...sin, domicilioLegal: domicilio }).success).toBe(true);
});

test('beneficiarios: al menos uno o "sin beneficiarios", excluyentes y suma ≤ 100', () => {
  expect(paths(j.safeParse({ ...juridica, beneficiarios: [] }))).toContain('beneficiarios');
  const explicacion = { explicacion: 'Sociedad cotizante sin accionista controlante.' };
  expect(j.safeParse({ ...juridica, beneficiarios: [], sinBeneficiarios: explicacion }).success).toBe(true);
  expect(paths(j.safeParse({ ...juridica, sinBeneficiarios: explicacion }))).toContain('beneficiarios');
  expect(paths(j.safeParse({ ...juridica, beneficiarios: [], sinBeneficiarios: { explicacion: 'corto' } }))).toContain('sinBeneficiarios.explicacion');
  const otro = { ...juridica.beneficiarios[0]!, porcentaje: 50 };
  expect(paths(j.safeParse({ ...juridica, beneficiarios: [juridica.beneficiarios[0], otro] }))).toContain('beneficiarios');
});

test('PEP exige cargo y organismo', () => {
  const r = j.safeParse({ ...juridica, representantes: [{ ...juridica.representantes[0], pep: { esPep: true } }] });
  expect(paths(r)).toEqual(expect.arrayContaining(['representantes.0.pep.cargo', 'representantes.0.pep.organismo']));
  expect(h.safeParse({ ...humana, pep: { esPep: true, cargo: 'Concejal', organismo: 'HCD' } }).success).toBe(true);
});

test('DNI de 7 u 8 dígitos y edad entre 18 y 120', () => {
  expect(paths(h.safeParse({ ...humana, documento: { tipo: 'DNI', numero: '123' } }))).toContain('documento.numero');
  expect(h.safeParse({ ...humana, documento: { tipo: 'Pasaporte', numero: 'AB123' } }).success).toBe(true);
  expect(paths(h.safeParse({ ...humana, fechaNacimiento: '2999-01-01' }))).toContain('fechaNacimiento');
  expect(paths(h.safeParse({ ...humana, fechaNacimiento: '1800-01-01' }))).toContain('fechaNacimiento');
});

test('fecha de constitución no puede ser futura y país debe ser ISO', () => {
  expect(paths(j.safeParse({ ...juridica, fechaConstitucion: '2999-01-01' }))).toContain('fechaConstitucion');
  expect(paths(j.safeParse({ ...juridica, actividad: { descripcion: 'x', pais: 'ZZ' } }))).toContain('actividad.pais');
});

test('los errores de lista se informan aunque falten otros campos (al intentar enviar)', () => {
  expect(paths(j.safeParse({}))).toContain('beneficiarios');
  const p = paths(j.safeParse({ beneficiarios: [], representantes: [] }));
  expect(p).toEqual(expect.arrayContaining(['beneficiarios', 'representantes', 'condicionIva']));
  expect(paths(j.safeParse({ sinBeneficiarios: {} }))).toContain('sinBeneficiarios.explicacion');
});
