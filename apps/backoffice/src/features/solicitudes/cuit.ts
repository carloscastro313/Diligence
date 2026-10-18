// Solo UX: la regla vinculante es Cuit.cs en el servidor.
const pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
export type TipoPersona = 'Humana' | 'Juridica';

export const normalizeCuit = (cuit: string) => cuit.replace(/\D/g, '');

export function isValidCuit(cuit: string) {
  const d = normalizeCuit(cuit);
  if (d.length !== 11) return false;
  const suma = pesos.reduce((s, p, i) => s + p * Number(d[i]), 0);
  const r = 11 - (suma % 11);
  return r !== 10 && (r === 11 ? 0 : r) === Number(d[10]);
}

export function cuitMatchesPersona(cuit: string, persona: TipoPersona) {
  const p = normalizeCuit(cuit).slice(0, 2);
  if (['20', '23', '24', '27'].includes(p)) return persona === 'Humana';
  if (['30', '33', '34'].includes(p)) return persona === 'Juridica';
  return false;
}
