import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import fs from 'fs'
import path from 'path'
import child_process from 'child_process'
import { env } from 'process'

// ASP.NET Core SPA proxy uchun HTTPS sertifikat sozlamasi.
const baseFolder =
    env.APPDATA !== undefined && env.APPDATA !== ''
        ? `${env.APPDATA}/ASP.NET/https`
        : `${env.HOME}/.aspnet/https`

const certificateName = 'schoollms.client'
const certFilePath = path.join(baseFolder, `${certificateName}.pem`)
const keyFilePath = path.join(baseFolder, `${certificateName}.key`)

if (!fs.existsSync(baseFolder)) {
    fs.mkdirSync(baseFolder, { recursive: true })
}

if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
    if (
        0 !==
        child_process.spawnSync(
            'dotnet',
            ['dev-certs', 'https', '--export-path', certFilePath, '--format', 'Pem', '--no-password'],
            { stdio: 'inherit' },
        ).status
    ) {
        throw new Error('Could not create certificate.')
    }
}

// Backend (SchoolLms.Server) manzili — proxy uchun.
const target = env.ASPNETCORE_HTTPS_PORT
    ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
    : env.ASPNETCORE_URLS
        ? env.ASPNETCORE_URLS.split(';')[0]
        : 'https://localhost:7013'

// https://vite.dev/config/
export default defineConfig({
    plugins: [react(), tailwindcss()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url)),
        },
    },
    server: {
        // LAN'dagi boshqa qurilmalar (telefon va h.k.) ham kira olishi uchun
        // barcha tarmoq interfeyslarida tinglaymiz (faqat localhost emas).
        host: true,
        // Frontend so'rovlarini ASP.NET backendiga yo'naltiramiz.
        proxy: {
            '^/api': {
                target,
                secure: false,
            },
            // SignalR guruh chati hub'i (WebSocket) — backendga yo'naltiramiz.
            '^/hubs': {
                target,
                secure: false,
                ws: true,
            },
            // Yuklangan materiallar (fayllar) — backenddan.
            '^/uploads': {
                target,
                secure: false,
            },
            '^/weatherforecast': {
                target,
                secure: false,
            },
        },
        port: parseInt(env.DEV_SERVER_PORT || '57472'),
        https: {
            key: fs.readFileSync(keyFilePath),
            cert: fs.readFileSync(certFilePath),
        },
    },
})
