import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// O'qituvchi PWA — `/teacher/` ostida xizmat qilinadi (asosiy domen bilan bir origin).
// Build natijasi asosiy client'ning dist/teacher papkasiga tushadi — shunda Docker
// `COPY client/dist -> wwwroot` qilganda PWA avtomatik wwwroot/teacher ga ko'chadi.
const proxyTarget = process.env.VITE_PROXY_TARGET || 'https://localhost:7013'

export default defineConfig({
  base: '/teacher/',
  plugins: [react()],
  build: {
    outDir: '../../../../dist/teacher',
    emptyOutDir: true,
  },
  server: {
    host: true,
    allowedHosts: true,
    proxy: {
      '^/api': { target: proxyTarget, secure: false, changeOrigin: true },
      '^/uploads': { target: proxyTarget, secure: false, changeOrigin: true },
      '^/hubs': { target: proxyTarget, secure: false, ws: true, changeOrigin: true },
    },
  },
})
