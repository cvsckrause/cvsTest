import { fileURLToPath } from 'node:url'
import { dirname } from 'node:path'
import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

const root = dirname(fileURLToPath(import.meta.url))

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, root, '')
  const apiOrigin = env.ERP_API_ORIGIN || process.env.ERP_API_ORIGIN || 'http://localhost:5080'

  return {
    plugins: [react()],
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': {
          target: apiOrigin,
          changeOrigin: true,
          timeout: 60_000,
        },
        '/auth-config.json': {
          target: apiOrigin,
          changeOrigin: true,
          timeout: 60_000,
        },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: './src/test-setup.ts',
    },
  }
})
