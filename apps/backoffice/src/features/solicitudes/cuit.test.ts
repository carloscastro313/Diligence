import { cuitMatchesPersona, isValidCuit, normalizeCuit } from './cuit';

test('normaliza quitando todo lo que no es dígito', () => {
  expect(normalizeCuit('20-12345678-6')).toBe('20123456786');
});

test('valida largo y dígito verificador', () => {
  expect(isValidCuit('20-12345678-6')).toBe(true);
  expect(isValidCuit('30712345671')).toBe(true);
  expect(isValidCuit('20123456787')).toBe(false);
  expect(isValidCuit('2012345678')).toBe(false);
});

test('el prefijo debe corresponder al tipo de persona', () => {
  expect(cuitMatchesPersona('20123456786', 'Humana')).toBe(true);
  expect(cuitMatchesPersona('20123456786', 'Juridica')).toBe(false);
  expect(cuitMatchesPersona('30712345671', 'Juridica')).toBe(true);
  expect(cuitMatchesPersona('99123456786', 'Humana')).toBe(false);
});
