# SchoolLms — Docker + Cloudflare Tunnel bilan ishga tushirish

Domen: **intellectschool.uz** (asosiy = Control Plane / loyiha boshlig'i; subdomen = maktab).

## 0. Sizdagi xato nega chiqayotgan edi

Loglardan:
```
ingress: admin.intellectschool.uz → https://localhost:57472
ERR dial tcp [::1]:57472: connect: connection refused
```
Sabablari:
1. **`localhost` cloudflared KONTEYNERINING o'zini bildiradi**, sizning Windows host'ingizni emas.
   Shuning uchun u 57472'dagi hech narsaga yetib bora olmayapti.
2. Ingress `https://...57472` (Vite dev porti)ga ishora qilgan — bu prod uchun emas.

To'g'risi: cloudflared **app bilan bitta compose tarmog'ida** ishlaydi va `http://app:8080`'ga ishora qiladi.
Asosiy domenni alohida hech qayerga "yuborish" SHART EMAS — ilova hostni o'zi tekshiradi:
`intellectschool.uz` → owner dashboard, `school1.intellectschool.uz` → maktab.

## 1. Sozlamalar (.env)

`.env.example` dan `.env` yarating:
```
ROOT_DOMAIN=intellectschool.uz
SA_PASSWORD=<kuchli-parol>
JWT_KEY=<48+ tasodifiy belgi>     # PowerShell: [Convert]::ToBase64String((1..48|%{Get-Random -Max 256}))
TUNNEL_TOKEN=<sizning tunnel tokeningiz>
```
Tokeningiz allaqachon bor (loglardagi `eyJhIjoiM2Fj...`). Uni `.env`ga qo'ying.

## 2. Cloudflare dashboard — Public Hostnames

Zero Trust → Networks → Tunnels → (tunnelingiz) → **Public Hostname**. Mavjud
`admin... → https://localhost:57472` yozuvini O'CHIRIB, quyidagi **ikkitasini** qo'shing:

| Subdomain | Domain | Type | URL |
|---|---|---|---|
| *(bo'sh)* | intellectschool.uz | HTTP | `app:8080` |
| `*` | intellectschool.uz | HTTP | `app:8080` |

- **HTTP** (HTTPS emas!) — TLS Cloudflare'da tugaydi, konteynerga HTTP boradi.
- `*` (wildcard) — barcha maktab subdomenlari. Cloudflare avtomatik `*` CNAME DNS yozuvini yaratadi.
- Universal SSL `intellectschool.uz` va `*.intellectschool.uz` ni (bir daraja) qoplaydi — qo'shimcha sertifikat shart emas.

> Eslatma: `service` ni `app:8080` deb yozasiz, chunki cloudflared app bilan bitta Docker tarmog'ida.
> Avval ishga tushirgan **alohida `docker run cloudflared ...` ni to'xtating** — endi u compose ichida.

## 3. Ishga tushirish

Loyiha papkasida (`C:\Users\abduh\source\repos\SchoolLms`):
```powershell
docker compose build
docker compose up -d
docker compose logs -f app        # birinchi marta DB'lar yaratiladi
```
Birinchi ishga tushganda:
- Yagona `SchoolLms` bazasi yaratiladi (barcha maktablar + Control Plane, har qator TenantId bilan) + default owner: **owner@schoollms.uz / owner123**.
- Loglarda "Now listening on http://[::]:8080" ko'rinadi.

## 4. Tekshirish

- `https://intellectschool.uz` → **Control Plane** (owner@schoollms.uz / owner123) → parolni almashtiring.
- "Yangi maktab" → masalan `school1` → reestr + superadmin yagona bazada (TenantId bilan) yaratiladi.
- `https://school1.intellectschool.uz` → o'sha maktab, siz bergan superadmin parol bilan.

## 5. Yangilanish (kod o'zgarsa)
```powershell
docker compose build app
docker compose up -d app
```
Yangi EF migratsiyalar barcha mavjud maktab DB'lariga avtomatik qo'llanadi (boot'da).

## Xavfsizlik eslatmalari
- `app` konteyner porti TASHQARIGA OCHILMAGAN (`ports:` yo'q) — faqat cloudflared kiradi. Shunday qoldiring.
- `.env` ni git'ga qo'shmang (`.gitignore`da). `JWT_KEY`/`SA_PASSWORD` ni hech kimga bermang.
- Default owner parolini (owner123) DARHOL almashtiring.
- Backup: `mssql-data` va `uploads` Docker volume'larini muntazam zaxiralang.

## Tez-tez uchraydigan muammolar
- **502 / "connection refused"**: ingress `service` `app:8080` (HTTP) ekanini va `docker compose ps`da app "healthy" ekanini tekshiring.
- **Subdomen ochilmaydi**: dashboardda `*` wildcard public hostname borligini va DNS'da `*` CNAME yaratilganini tekshiring.
- **Maktab ilovasi bo'sh/mock**: obraz `VITE_USE_MOCK=false` bilan qurilganini tekshiring (compose'da shunday).
- **"Jwt:Key sozlanmagan" bilan to'xtaydi**: `.env`da `JWT_KEY` ≥32 belgi ekanini tekshiring.
