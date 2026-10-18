/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Aspire inyecta la URL de la API por service discovery (services__api__http__0).
const api = process.env['services__api__https__0'] ?? process.env['services__api__http__0'] ?? 'http://localhost:5000';

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env['PORT'] ?? 5173),
    proxy: { '/api': { target: api, changeOrigin: true, secure: false } },
  },
  test: { environment: 'jsdom', globals: true, setupFiles: ['./src/test-setup.ts'], exclude: ['e2e/**', 'node_modules/**'] },
});
