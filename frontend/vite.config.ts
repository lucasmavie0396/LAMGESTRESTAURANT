/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Em desenvolvimento o Vite reencaminha /api e /hubs para a API (mesma origem → cookie de sessão SameSite=Strict funciona).
// Em produção o frontend compilado é servido pela própria API (wwwroot).
const API = process.env.LAMGEST_API ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  // A app de venda (Android) usa o WebView do PDA, que pode ser antigo.
  build: { target: ['es2019', 'chrome70'] },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: API, changeOrigin: false },
      '/hubs': { target: API, ws: true, changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
