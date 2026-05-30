This file explains how Visual Studio created the project.

The following tools were used to generate this project:
- create-vite

The following steps were used to generate this project:
- Create react project with create-vite: `npm init --yes vite@latest schoollms.client -- --template=react  --no-rolldown --no-immediate`.
- Update `vite.config.js` to set up proxying and certs.
- Update `App` component to fetch and display weather information.
- Create project file (`schoollms.client.esproj`).
- Create `launch.json` to enable debugging.
- Add project to solution.
- Update proxy endpoint to be the backend server endpoint.
- Add project to the startup projects list.
- Write this file.

## Integratsiya: Maktab LMS frontend (2026-05-20)

Bu loyiha endi to'liq Maktab LMS (React + TypeScript) ilovasidan iborat.
Avval `frontend/` ichki papkasida turgan tayyor ilova `schoollms.client`
loyihasining o'ziga ko'chirildi va ASP.NET SPA integratsiyasiga moslandi:

- Namunaviy "Weather forecast" ilovasi (`src/App.jsx` va h.k.) Maktab LMS
  ilovasi bilan almashtirildi (TypeScript, `src/main.tsx`).
- `vite.config.js` → `vite.config.ts`: HTTPS sertifikat va backend proxy
  sozlamalari saqlanib qoldi, ustiga Tailwind CSS plagini va `@` aliasi qo'shildi.
- `/api` va `/weatherforecast` so'rovlari ASP.NET backendiga proxy qilinadi.
- `package.json` LMS bog'liqliklari (react-router, axios, recharts, tailwind,
  @dnd-kit, lucide-react) bilan yangilandi; build skripti `tsc -b && vite build`.
- TypeScript (`tsconfig*.json`) va TS-aware ESLint sozlamalari qo'shildi.
- `index.html` "Maktab LMS" sarlavhasiga va `/src/main.tsx` ga yo'naltirildi.
- `.env` integratsiyalangan rejim uchun nisbiy `/api` manzilini ishlatadi.
- `schoollms.client.esproj` tozalandi (avval `node_modules` fayllari bilan
  to'lib ketgan edi).
- Endi ortiqcha bo'lgan `frontend/` papkasi olib tashlandi.
