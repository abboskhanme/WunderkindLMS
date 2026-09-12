import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Telegram Mini App — `/tg/` ostida xizmat qilinadi (API bilan bir origin, ya'ni
// CORS ham, alohida domen ham kerak emas). Build natijasi asosiy client'ning
// dist/tg papkasiga tushadi: Docker `COPY client/dist -> wwwroot` qilganda
// ilova avtomatik wwwroot/tg ga ko'chadi — o'qituvchi PWA'sidagi bilan bir xil.
const proxyTarget = process.env.VITE_PROXY_TARGET || 'http://localhost:8080'

export default defineConfig({
  base: '/tg/',
  plugins: [react()],
  build: {
    outDir: '../../../../dist/tg',
    emptyOutDir: true,
  },
  server: {
    host: true,
    allowedHosts: true,
    proxy: {
      '^/api': { target: proxyTarget, secure: false, changeOrigin: true },
      '^/uploads': { target: proxyTarget, secure: false, changeOrigin: true },
    },
  },
})
