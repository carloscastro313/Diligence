import { z } from 'zod';
import { esPais } from '../../shared';

// Solo para dar respuesta inmediata: la API valida de nuevo y decide siempre (Principio VII).
// Mismos nombres de campo que FormularioDatos; la obligatoriedad depende del tipo de persona.
export type TipoPersona = 'Humana' | 'Juridica';

const OBLIGATORIO = 'Este dato es obligatorio.';
const texto = (max: number) => z.string().trim().min(1, OBLIGATORIO).max(max, `Máximo ${max} caracteres.`);
const opcional = (max: number) => z.string().trim().max(max, `Máximo ${max} caracteres.`).optional();
const pais = z.string().refine(esPais, 'Elegí un país.');
const fecha = z.iso.date('Ingresá una fecha válida.');

const hoy = () => new Date().toISOString().slice(0, 10);
const edad = (nacimiento: string) => {
  const [a, m, d] = nacimiento.split('-').map(Number) as [number, number, number];
  const t = new Date();
  return t.getFullYear() - a - (t.getMonth() + 1 < m || (t.getMonth() + 1 === m && t.getDate() < d) ? 1 : 0);
};

const pep = z
  .object({ esPep: z.boolean(), cargo: opcional(200), organismo: opcional(200) })
  .superRefine((v, ctx) => {
    if (!v.esPep) return;
    for (const campo of ['cargo', 'organismo'] as const)
      if (!v[campo]) ctx.addIssue({ code: 'custom', path: [campo], message: OBLIGATORIO });
  });

const domicilio = z.object({
  calle: texto(200),
  numero: texto(20),
  piso: opcional(10),
  depto: opcional(10),
  localidad: texto(100),
  provincia: texto(100),
  codigoPostal: texto(10),
  pais,
});

const beneficiario = z.object({
  nombre: texto(200),
  documento: texto(20),
  nacionalidad: pais,
  porcentaje: z
    .number('Ingresá un porcentaje.')
    .gt(0, 'El porcentaje debe ser mayor a 0.')
    .max(100, 'El porcentaje no puede superar 100.')
    .refine((p) => Math.round(p * 100) / 100 === p, 'Usá hasta 2 decimales.'),
  pep,
});

const comun = {
  condicionIva: z.enum(['ResponsableInscripto', 'Monotributo', 'Exento', 'NoAlcanzado', 'ConsumidorFinal'], 'Elegí una opción.'),
  domicilioFiscal: domicilio,
  actividad: z.object({ descripcion: texto(200), pais }),
  origenFondos: z.string().trim().min(10, 'Contanos un poco más (mínimo 10 caracteres).').max(2000, 'Máximo 2000 caracteres.'),
};

const humana = z.object({
  ...comun,
  fechaNacimiento: fecha.refine((f) => edad(f) >= 18 && edad(f) <= 120, 'La edad debe estar entre 18 y 120 años.'),
  nacionalidad: pais,
  documento: z
    .object({ tipo: z.enum(['DNI', 'Pasaporte'], 'Elegí una opción.'), numero: texto(20) })
    .refine((d) => d.tipo !== 'DNI' || /^\d{7,8}$/.test(d.numero), { path: ['numero'], message: 'El DNI tiene 7 u 8 dígitos.' }),
  pep,
});

const juridica = z
  .object({
    ...comun,
    fechaConstitucion: fecha.refine((f) => f <= hoy(), 'La fecha no puede ser futura.'),
    numeroInscripcion: texto(50),
    domicilioLegalIgualFiscal: z.boolean().optional(),
    domicilioLegal: domicilio.partial().optional(),
    beneficiarios: z.array(beneficiario).max(20, 'Máximo 20 beneficiarios.').optional(),
    sinBeneficiarios: z
      .object({ explicacion: z.string().trim().min(20, 'Explicá el motivo (mínimo 20 caracteres).').max(1000, 'Máximo 1000 caracteres.') })
      .optional(),
    representantes: z
      .array(z.object({ nombre: texto(200), documento: texto(20), pep }), 'Agregá al menos un representante.')
      .min(1, 'Agregá al menos un representante.')
      .max(10, 'Máximo 10 representantes.'),
  })
  .superRefine((v, ctx) => {
    if (!v.domicilioLegalIgualFiscal) {
      const r = domicilio.safeParse(v.domicilioLegal ?? {});
      if (!r.success)
        for (const i of r.error.issues) ctx.addIssue({ code: 'custom', path: ['domicilioLegal', ...i.path], message: i.message });
    }
    const lista = v.beneficiarios ?? [];
    if (v.sinBeneficiarios) {
      if (lista.length) ctx.addIssue({ code: 'custom', path: ['beneficiarios'], message: 'Quitá los beneficiarios o desmarcá "No hay beneficiarios identificables".' });
    } else if (!lista.length) {
      ctx.addIssue({ code: 'custom', path: ['beneficiarios'], message: 'Agregá al menos un beneficiario final o indicá que no hay beneficiarios identificables.' });
    } else if (lista.reduce((s, b) => s + b.porcentaje, 0) > 100 + 1e-9) {
      ctx.addIssue({ code: 'custom', path: ['beneficiarios'], message: 'La suma de los porcentajes no puede superar 100.' });
    }
    // when: por defecto Zod omite esta regla si otro campo falló, y el error de lista aparecía recién al corregir todo lo demás.
  }, { when: ({ value }) => typeof value === 'object' && value !== null });

export const formularioSchema = (persona: TipoPersona) => (persona === 'Humana' ? humana : juridica);
