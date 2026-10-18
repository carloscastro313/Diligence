import { defineConfig } from 'vitest/config';

// Solo el test de fixtures de ESLint; las apps tienen su propio vitest.
export default defineConfig({ test: { include: ['eslint-fixtures/**/*.test.ts'], testTimeout: 60_000 } });
