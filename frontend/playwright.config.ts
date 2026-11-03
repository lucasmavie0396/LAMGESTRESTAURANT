import { defineConfig } from '@playwright/test'
import { PORTA_API, PORTA_WEB } from './e2e/comum.mjs'

// Testes E2E contra uma base própria (LAMGestRestaurantDB_E2E), recriada em cada execução.
// Usa o Edge instalado no Windows (sem descarregar browsers); noutra máquina: PW_CANAL=chrome.
export default defineConfig({
  testDir: './e2e',
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 20_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  globalTeardown: './e2e/limpar.ts',
  use: {
    baseURL: `http://localhost:${PORTA_WEB}`,
    channel: process.env.PW_CANAL ?? 'msedge',
    locale: 'pt-PT',
    timezoneId: 'Africa/Maputo',
    viewport: { width: 1366, height: 768 },
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'node e2e/servidor.mjs',
      url: `http://localhost:${PORTA_API}/api/saude`,
      timeout: 300_000,
      reuseExistingServer: false,
      stdout: 'pipe',
    },
    {
      command: `npx vite --port ${PORTA_WEB} --strictPort`,
      url: `http://localhost:${PORTA_WEB}`,
      env: { LAMGEST_API: `http://localhost:${PORTA_API}` },
      reuseExistingServer: false,
    },
  ],
})
