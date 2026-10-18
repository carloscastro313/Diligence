import js from '@eslint/js';
import boundaries from 'eslint-plugin-boundaries';
import tseslint from 'typescript-eslint';

// Fronteras del frontend: ver specs/001-project-setup-foundation/contracts/cross-cutting.md §7.
const sameApp = '{{ from.element.captured.app }}';
const sameFeature = '{{ from.element.captured.feature }}';

export default tseslint.config(
  { ignores: ['**/dist/**', '**/node_modules/**', '**/coverage/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    plugins: { boundaries },
    settings: {
      'import/resolver': { typescript: { alwaysTryTypes: true } },
      'boundaries/elements': [
        { type: 'feature', pattern: 'apps/*/src/features/*', capture: ['app', 'feature'] },
        { type: 'shared', pattern: 'apps/*/src/shared', capture: ['app'] },
        { type: 'app-shell', pattern: 'apps/*/src/app', capture: ['app'] },
        { type: 'package', pattern: 'packages/*', capture: ['package'] },
      ],
    },
    rules: {
      'boundaries/dependencies': [
        'error',
        {
          default: 'disallow',
          policies: [
            { allow: { to: { module: { origin: 'external' } } } },
            { allow: { to: { module: { origin: 'core' } } } },
            // Una feature: ella misma, el shared de su app y packages.
            {
              from: { element: { type: 'feature' } },
              allow: {
                to: [
                  { element: { type: 'feature', captured: { app: sameApp, feature: sameFeature } } },
                  { element: { type: 'shared', captured: { app: sameApp } } },
                  { element: { type: 'package' } },
                ],
              },
            },
            // Shared: solo shared de su app y packages.
            {
              from: { element: { type: 'shared' } },
              allow: {
                to: [{ element: { type: 'shared', captured: { app: sameApp } } }, { element: { type: 'package' } }],
              },
            },
            // App shell: features y shared de su misma app, y packages.
            {
              from: { element: { type: 'app-shell' } },
              allow: {
                to: [
                  { element: { type: 'app-shell', captured: { app: sameApp } } },
                  { element: { type: 'feature', captured: { app: sameApp } } },
                  { element: { type: 'shared', captured: { app: sameApp } } },
                  { element: { type: 'package' } },
                ],
              },
            },
            // Packages: solo packages (nunca apps).
            { from: { element: { type: 'package' } }, allow: { to: { element: { type: 'package' } } } },
          ],
        },
      ],
    },
  },
);
