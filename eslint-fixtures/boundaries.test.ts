import { ESLint } from 'eslint';
import { expect, test } from 'vitest';

const eslint = new ESLint({ cwd: process.cwd() });

async function boundaryErrors(file: string) {
  const [result] = await eslint.lintFiles([file]);
  return result!.messages.filter((m) => m.ruleId === 'boundaries/dependencies' && m.severity === 2);
}

test.each([
  'eslint-fixtures/apps/backoffice/src/features/b/violation-feature.ts',
  'eslint-fixtures/apps/portal/src/features/home/violation-portal.ts',
  'eslint-fixtures/packages/lib/src/violation-package.ts',
])('la violación sembrada falla: %s', async (file) => {
  expect((await boundaryErrors(file)).length).toBeGreaterThan(0);
});

test('el import válido no falla', async () => {
  expect(await boundaryErrors('eslint-fixtures/apps/backoffice/src/features/b/ok.ts')).toHaveLength(0);
});
