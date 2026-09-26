# AI ulanish (MCP) — rahbariyat uchun qo'llanma

Wunderkind tizimini **ChatGPT**, **Claude** yoki boshqa AI yordamchiga ulab, maktab haqida oddiy
tilda savol berish mumkin: *"Bugun qaysi sinflarda davomat olinmagan?"*, *"5-A sinfda qarzdorlar
kim?"*, *"Sentabr oyida daromad va xarajat qancha bo'ldi?"*.

Ulanish **faqat o'qish uchun**:

- AI hech narsani **o'zgartira olmaydi**, o'chira olmaydi, hech kimga xabar **yubora olmaydi**.
  Buni uch qavat himoya ta'minlaydi: vositalarning o'zi faqat o'qiydi; ular bazaga alohida
  **faqat-o'qish** roli (`app_ro`) bilan ulanadi; va bu rolga yozish huquqi umuman berilmagan.
- AI faqat **sizga ruxsat berilgan** bo'limlarni ko'radi — xuddi veb-paneldagidek. Masalan,
  moliya ruxsati yo'q xodim AI orqali ham qarz va to'lovlarni ko'rmaydi.
- Parol **AI ga berilmaydi**. Ulashda brauzerda **bizning** kirish sahifamiz ochiladi, login va
  parol faqat o'sha yerga, maktab serveriga kiritiladi.
- Login, parol, tokenlar va boshqa maxfiy ma'lumotlar hech qachon AI ga chiqmaydi.
- Har bir AI so'rovi jurnalga yoziladi (Boshqaruv → **AI ulanishlar**).

## 1. Kimga ruxsat bor

| Kim | AI ulana oladimi |
|---|---|
| Direktor (superadmin) | Ha |
| Administrator (admin) | Ha |
| Xodim (staff) | Faqat rolida **«AI ulanish»** belgisi yoqilgan bo'lsa |
| O'qituvchi, kassir, o'quvchi, ota-ona | Yo'q |

Xodimga ruxsat berish: **Boshqaruv → Rollar** → rolni tahrirlash → pastdagi **«AI ulanish»**
tugmasini yoqing → Saqlash. Rol belgisi olib tashlansa, o'sha roldagi xodimlarning AI
ulanishlari **darhol** ishlamay qoladi (qayta kirish shart emas).

## 2. Ulanish manzili

```
https://lms.wunderkindedu.uz/mcp
```

(Aniq manzil Boshqaruv → **AI ulanishlar** sahifasining yuqorisida — «Nusxalash» tugmasi bilan.)

## 3. Qanday ulanadi

### ChatGPT
1. ChatGPT → **Settings → Connectors** (yoki *Apps & Connectors*) → **Add / Create**.
   (Tarmoq tarifiga qarab "Developer mode" yoqilishi kerak bo'lishi mumkin.)
2. Nomi: `Wunderkind maktab`, URL: `https://lms.wunderkindedu.uz/mcp`, autentifikatsiya: **OAuth**.
3. Saqlang — brauzerda Wunderkind kirish sahifasi ochiladi. Login va parolingizni kiriting,
   so'ng **«Ruxsat berish»** ni bosing.
4. Suhbatda ulagichni tanlang va savol bering.

### Claude (claude.ai yoki Claude Desktop)
1. **Settings → Connectors → Add custom connector**.
2. Nomi: `Wunderkind maktab`, URL: `https://lms.wunderkindedu.uz/mcp`.
3. **Connect** — ochilgan Wunderkind sahifasida kiring va **«Ruxsat berish»** ni bosing.

### Boshqa MCP mijozlari
Xavfsizlik uchun kalit faqat **ruxsat etilgan manzillarga** yuboriladi: `claude.ai`, `claude.com`,
`chatgpt.com`, `chat.openai.com` (https) va kompyuterning o'zidagi dasturlar (`http://127.0.0.1`,
`http://localhost`). Boshqa sayt yoki `cursor://`, `vscode://` kabi maxsus manzil bilan qaytadigan
ilovalar **ulana olmaydi** (masalan, Cursor hozircha qo'llab-quvvatlanmaydi). Yangi AI xizmatini
qo'shish kerak bo'lsa — dasturchi `Mcp__AllowedRedirectHosts` sozlamasiga uning domenini qo'shadi.

Ruxsat sahifasida **kalit qayerga yuborilishi** yoziladi («Ruxsat bersangiz, kalit claude.ai ga
yuboriladi»). Tanimagan manzilni ko'rsangiz — **«Rad etish»** ni bosing.

> Eslatma: ulanish 1 soatlik kalit bilan ishlaydi va AI ilovasi uni o'zi yangilab turadi.
> 30 kun ishlatilmasa yoki ulanganiga **90 kun** bo'lsa, qayta kirish so'raladi.
> **Parolingizni o'zgartirsangiz** (yoki direktor uni tiklasa) — barcha AI ulanishlaringiz darhol uziladi.

### Xavfsizlik bo'yicha maslahat (prompt injection)
AI o'qigan ma'lumot ichida (masalan, ota-ona yozgan izoh yoki ariza matnida) AI ni aldashga
urinadigan "ko'rsatma" bo'lishi mumkin. Shuning uchun:

- Maktab ulagichini **xat yuboradigan, internetga so'rov jo'natadigan yoki fayl yozadigan** boshqa
  ulagichlar (Gmail, Slack, veb-qidiruv, "fetch" va h.k.) bilan **bitta suhbatda birga yoqmang**.
- AI biror narsani "yuborish", "joylash" yoki "havolaga o'tish"ni taklif qilsa — ehtiyot bo'ling.
- Maxfiy xulosalarni (qarzdorlar, baholar) AI dan boshqa joyga ko'chirishda o'zingiz tekshiring.

## 4. Nimani so'rash mumkin

| Mavzu | Namuna savollar | Kerakli ruxsat |
|---|---|---|
| Umumiy holat | "Maktabda nechta o'quvchi, sinf va o'qituvchi bor? Bugungi davomat qanday?" | Bosh sahifa |
| O'quvchilar | "Karimov Ali haqida ma'lumot ber", "7-B sinf ro'yxati" | O'quvchilar / Sinflar |
| Sinflar, guruhlar | "Qaysi sinfning o'zlashtirishi eng past?", "Aniq fanlar yo'nalishida kimlar bor?" | Sinflar |
| Dars jadvali | "Seshanba kuni 9-A ning darslari", "Karimova ustozning haftalik darslari" | Dars jadvali |
| Davomat | "Kecha kim kelmagan?", "Sentabr bo'yicha sababsiz qoldirishlar", "Yotoqxona davomati" | Davomat / Kechki dars / Yotoqxona |
| Baholar | "6-A matematika 1-chorak baholari", "Oxirgi blok test natijalari", "Mavsumiy baholar" | Jurnal / Mavsumiy baholash / Imtihonlar |
| Moliya | "Eng katta 10 ta qarzdor", "Sentabr daromad va xarajat", "Chegirmalar ro'yxati", "Maosh to'lovlari" | Moliya (to'liq yoki faqat ko'rish) |
| Xodimlar | "O'qituvchilar ro'yxati va fanlari" (maosh — faqat moliya ruxsati bilan) | Xodimlar |
| Qabul | "Lidlar voronkasi", "Qabul nomzodlari natijalari", "Ariza formasidan kelganlar" | Lidlar / Marketing / Qabul |
| Intizom | "Intizom balli eng past 10 o'quvchi", "Oxirgi oy intizomiy holatlar" | Xulq-atvor |
| Boshqa | "Oxirgi e'lonlar tarixi", "Muddati tugayotgan sertifikatlar", "Shartnomalar" | Xabarlar / O'quvchilar / Shartnomalar |

Ruxsat yo'q bo'limni so'rasangiz, AI "Ruxsat yo'q" javobini oladi — bu jurnalda ham ko'rinadi.

Cheklovlar: bitta ro'yxat ko'pi bilan **200 qator**; sana oralig'i ko'pi bilan **1 yil**
(davomat tahlili — 92 kun); bitta ulanishdan daqiqasiga 120 tadan ortiq so'rov qabul qilinmaydi.

## 5. Ulanishni bekor qilish

- **Direktor**: Boshqaruv → **AI ulanishlar** → kerakli qatorda **«Bekor qilish»**. Ulanish darhol
  to'xtaydi; AI ilovasi qayta ulanmoqchi bo'lsa, yana login va parol so'raladi.
- **Xodimning ruxsatini olish**: Boshqaruv → Rollar → «AI ulanish» belgisini o'chirish — rolning
  barcha xodimlari uchun darhol.
- **Xodimni o'chirish** — uning ulanishlari ham darhol ishlamay qoladi.
- Shu sahifada har bir so'rov ko'rinadi: kim, qaysi ilova, qaysi vosita, qancha qator,
  natija (Bajarildi / Ruxsat yo'q / Xato).

Telefon yoki kompyuter yo'qolsa — parolni almashtiring (bu barcha AI ulanishlarini ham uzadi) va
sahifada ulanishlar «Bekor qilingan» bo'lganini tekshiring.

Jurnal (so'rovlar tarixi) **1 yil** saqlanadi, keyin avtomatik o'chiriladi.

---

## 6. Texnik: deploy (dasturchi uchun)

Bu bo'lim — server administratori uchun. Batafsil arxitektura: `docs/modules/mcp-readonly.md`.

### 6.1 Yangi sozlamalar

| Qayerda | O'zgaruvchi | Qiymat |
|---|---|---|
| `.env` | `DB_RO_PASSWORD` | yangi tasodifiy parol: `openssl rand -hex 24` |
| backend (compose avtomatik quradi) | `ConnectionStrings__ReadOnly` | `Host=database;…;Username=app_ro;Password=${DB_RO_PASSWORD}` — `DB_RO_PASSWORD` bo'sh bo'lsa **bo'sh** qoladi |
| database (compose) | `RO_DB_PASSWORD` | `${DB_RO_PASSWORD}` — `init-roles.sql` o'qiydi |
| `docker-compose.server.yml` | `Mcp__PublicBaseUrl` | `https://lms.wunderkindedu.uz` — **majburiy** (Development'dan tashqarida bo'sh bo'lsa MCP o'chiq). Lokal: `.env` da `MCP_PUBLIC_BASE_URL` |
| ixtiyoriy | `Mcp__AllowedRedirectHosts` | vergul bilan: sukut `claude.ai,claude.com,chatgpt.com,chat.openai.com` (+ loopback http har doim) |
| backend (compose) | `ConnectionStrings__Default` | endi `Maximum Pool Size=40` (60 ulanishli serverda app_ro 8 ta + zaxira sig'ishi uchun) |

MCP **o'chiq** (fail-closed: `/mcp` → 503, `/oauth/*` va `/.well-known/oauth-*` → 404, logda
`[mcp] ... O'CHIQ: <sabab>`) bo'ladi, agar: `ConnectionStrings__ReadOnly` bo'sh; `Mcp__PublicBaseUrl`
bo'sh (Production); yoki startda o'qish-ulanishi o'z-o'zini tekshirishdan o'tmasa (superuser,
`app_rw`/egasi, yozuvchi rolga a'zo, sessiya read-only emas yoki SELECT dan boshqa huquq bor).
Ilovaning qolgan qismi o'zgarishsiz ishlaydi. Hech qachon `app_rw` ga qaytib ketmaydi.

`app_ro` huquqlari — **ruxsat ro'yxati** (`public.mcp_apply_app_ro_grants()`, migratsiya yaratadi,
`init-roles.sql` chaqiradi): faqat vositalar o'qiydigan jadvallar; `users`/`school_meta`/`guardians`
da faqat maxfiy bo'lmagan ustunlar. Yangi jadval avtomatik YOPIQ. Ulanishlar soni ≤ 8.

### 6.2 Deploy tartibi

```bash
# 0) BACKUP (DEPLOY.md §1, 1-qadam)
# 1) .env ga DB_RO_PASSWORD qo'shing (bir marta)
echo "DB_RO_PASSWORD=$(openssl rand -hex 24)" >> .env
RO="$(grep ^DB_RO_PASSWORD= .env | cut -d= -f2)"
# 2) ROLLAR (migratsiyadan OLDIN): app_ro yaratiladi (huquqlarni migratsiya beradi)
docker exec -e RO_DB_PASSWORD="$RO" wunderkind-database \
  psql -v ON_ERROR_STOP=1 -U schoollms -d schoollms -f /docker-entrypoint-initdb.d/10-init-roles.sql
# 3) Obraz + migratsiya (20260926150755_McpReadOnly: 5 ta mcp_* jadval + 2 funksiya, DROP yo'q) + ishga tushirish
docker compose -f docker-compose.yml -f docker-compose.server.yml build backend
docker compose -f docker-compose.yml -f docker-compose.server.yml up -d --no-deps backend
# 4) ROLLAR YANA (migratsiyadan KEYIN — DEPLOY.md §1 5-qadam, MAJBURIY): mcp_audit append-only,
#    app_ro token jadvallarini o'qiy olmaydi.
docker exec -e RO_DB_PASSWORD="$RO" wunderkind-database \
  psql -v ON_ERROR_STOP=1 -U schoollms -d schoollms -f /docker-entrypoint-initdb.d/10-init-roles.sql
#    Chiqishda: app_ro | superuser f | owns_in_public 0 bo'lishi SHART.
```

`--no-deps` — database konteyneri qayta yaratilmasin (unga yangi `RO_DB_PASSWORD` qo'shilgan;
bu faqat bo'sh bazada kerak, mavjud bazada 3-qadam `docker exec -e` bilan beradi).

### 6.3 Tekshirish

```bash
curl -s https://lms.wunderkindedu.uz/.well-known/oauth-authorization-server   # JSON, issuer = https://lms…
curl -si -X POST https://lms.wunderkindedu.uz/mcp | grep -i www-authenticate  # 401 + resource_metadata
# app_ro yoza olmasligi:
docker exec wunderkind-database psql -U schoollms -d schoollms -c \
  "select rolname, rolsuper, rolconfig from pg_roles where rolname='app_ro'"
#   rolconfig: {default_transaction_read_only=on,statement_timeout=30s}
```

### 6.4 Caddy / Cloudflare

- **Caddy**: o'zgartirish shart emas — `deploy/Caddyfile` barcha yo'llarni backend'ga uzatadi
  (`/mcp`, `/oauth/*`, `/.well-known/*` ham). `X-Forwarded-For` bitta yozuv — login chastota
  chegarasi to'g'ri ishlaydi.
- **Cloudflare** — ehtiyot bo'ling, hamma `/oauth/*` bir xil emas:
  - Bot Fight Mode / JS challenge ni **faqat** mashina yo'llari uchun o'tkazib yuboring (Skip):
    `/mcp`, `/oauth/token`, `/oauth/register`, `/oauth/revoke`, `/.well-known/*` — ularni AI
    ilovasining serveri chaqiradi va brauzer tekshiruvidan o'ta olmaydi.
  - `/oauth/authorize` uchun **hech narsani o'chirmang** — bu odam brauzerda ochadigan login sahifasi,
    Cloudflare himoyasi (WAF, bot tekshiruvi) unda qolsin.
  - **Rate limiting rule** qo'shing (yoki borini saqlang): `POST /oauth/authorize` va
    `POST /api/auth/login` — masalan 20 so'rov/daqiqa/IP. Ilova ichidagi chegara (10/daq, IPv6 /64,
    bitta login uchun 5 xato → 15 daqiqa) bilan birga ishlaydi.
  - Keshlanmasin (javoblar `Cache-Control: no-store`). Javob SSE (`text/event-stream`) bo'lishi
    mumkin — Cloudflare buni qo'llab-quvvatlaydi.

### 6.5 Rollback

`.env` dan `DB_RO_PASSWORD` ni olib tashlab backend'ni qayta ishga tushiring → MCP o'chadi.
Jadvallar qoladi (zarari yo'q). `app_ro` rolini butunlay olib tashlash:
`DROP OWNED BY app_ro; DROP ROLE app_ro;`. Migratsiyani qaytarish (faqat kerak bo'lsa):
`dotnet ef database update TrackMembersFromYearStart` — `mcp_*` jadvallari (tokenlar, jurnal) va
ikkala funksiya o'chadi.
