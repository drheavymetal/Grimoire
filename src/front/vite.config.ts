/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
  },
  // core/ tests run without a browser (D12): a plain Node environment.
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
