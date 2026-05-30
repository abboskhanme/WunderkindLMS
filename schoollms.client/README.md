# Maktab LMS — Client (schoollms.client)

Maktab boshqaruv tizimi (LMS) frontendi. React 19 + TypeScript + Vite + Tailwind CSS v4.
Bu loyiha `SchoolLms.Server` (ASP.NET Core) bilan **SPA proxy** orqali integratsiyalangan.

## Texnologiyalar

- **React 19** + **TypeScript** + **Vite 8**
- **Tailwind CSS v4** (`@tailwindcss/vite`)
- **React Router v7** — sahifa marshrutlash
- **Axios** — API klient (`src/api/client.ts`)
- **Recharts** — grafiklar, **@dnd-kit** — drag & drop, **lucide-react** — ikonkalar

## Loyiha tuzilishi

```
src/
  api/            # axios klient, servislar va vaqtinchalik mock ma'lumotlar
  components/     # umumiy UI, layout va chart komponentlari
  config/         # navigatsiya, ranglar, konstantalar
  context/        # autentifikatsiya konteksti
  hooks/          # useAsync va boshqalar
  lib/            # yordamchi funksiyalar
  pages/          # sahifalar (admin paneli: o'quvchilar, o'qituvchilar, ...)
  types/          # TypeScript tiplar
```

## Ishga tushirish

Odatda butun yechim Visual Studio'da `SchoolLms.Server` startup loyiha sifatida
ishga tushiriladi — ASP.NET SPA proxy avtomatik ravishda `npm run dev` ni ishga
tushiradi va `https://localhost:57472` ga proxy qiladi.

Faqat frontendni alohida ishga tushirish uchun:

```bash
npm install
npm run dev      # https://localhost:57472
npm run build    # tsc -b && vite build  ->  dist/
npm run lint
```

## Backend bilan bog'lanish

- `vite.config.ts` `/api` va `/weatherforecast` so'rovlarini ASP.NET backendiga
  (`https://localhost:7013`) proxy qiladi.
- API manzili va mock rejim `.env` orqali boshqariladi:
  - `VITE_API_BASE_URL=/api`
  - `VITE_USE_MOCK=true` — backend API tayyor bo'lganda `false` qiling.
