# DEPLOY — Wunderkind LMS

Bu fayl amaldagi deploy hujjati. Ildizdagi `DEPLOY.md` ESKIRGAN (SQL Server / Control Plane
davridan qolgan) — unga ishonmang.

Stack: PostgreSQL 17 + Redis 7 + .NET 10 backend (SPA obraz ichida) + Caddy.

---

## 0. Qaysi compose fayl

| Muhit | Buyruq |
|---|---|
| Lokal (mac) | `docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build` |
| Server (prod) | `docker compose -f docker-compose.yml -f docker-compose.server.yml up -d --build` |

Prod va lokal fayllar ALOHIDA turadi — ularni birlashtirmang.
`docker-compose.yml` o'zi tashqariga hech qanday port ochmaydi.

---

## 1. Deploy tartibi (prod)

```bash
# 1) BACKUP — har doim birinchi
docker exec wunderkind-database pg_dump -U schoollms -d schoollms \
    --format=custom --file=/backups/pre-deploy_$(date -u +%Y%m%d_%H%M).dump
docker exec wunderkind-database ls -lh /backups | tail -3

# 2) Obrazni qurish (hali almashtirmaydi)
docker compose -f docker-compose.yml -f docker-compose.server.yml build backend

# 3) MIGRATSIYA
#    Migratsiya konteyner startida qo'llanadi, lekin endi `ConnectionStrings__Migrator`
#    (sxema egasi `schoollms_owner`) bilan — ilova roli `app_rw` da DDL huquqi yo'q.
#    Yangi migratsiya bo'lsa: avval backup (1-qadam), keyin 4-qadam.
#    Migratsiyani butunlay alohida qadamga ajratish kerak bo'lsa:
#    `Database__AutoMigrate=false` → deploy/README.md § "Migratsiyani alohida qadamga".

# 4) Almashtirish
docker compose -f docker-compose.yml -f docker-compose.server.yml up -d

# 5) ROLLARNI QAYTA QO'LLASH — migratsiya YANGI JADVAL qo'shgan bo'lsa MAJBURIY.
#    Yangi jadval `app_rw` ga to'liq CRUD bilan keladi; moliyaviy jadvallardan
#    UPDATE/DELETE ni aynan shu skript qaytarib oladi. Idempotent — har doim xavfsiz.
docker exec -i wunderkind-database \
    psql -U schoollms -d schoollms -v ON_ERROR_STOP=1 -f - < deploy/init-roles.sql
#    Chiqishda `app_rw` qatorida `owns_in_public` = 0 bo'lishi SHART.

# 6) Tekshirish (pastdagi 3-bo'lim, jumladan 3.5)
```

### Rollback

| Nima buzildi | Qaytarish |
|---|---|
| Yangi obraz ishlamayapti | `docker compose ... up -d --force-recreate` oldingi image tag bilan; yoki `git revert <commit> && docker compose ... up -d --build` |
| Migratsiya ma'lumotni buzdi | `docker exec -i wunderkind-database pg_restore -U schoollms -d schoollms --clean --if-exists /backups/pre-deploy_<ts>.dump` |
| Redis muammo qilmoqda | `docker-compose.yml` dagi `ConnectionStrings__Redis` qatorini o'chiring → `docker compose ... up -d backend`. Ilova jarayon ichidagi xotira keshiga qaytadi. **Qayta build SHART EMAS.** |
| Log rotatsiyasi xalaqit bermoqda | xizmatdan `logging: *default-logging` qatorini oling |
| .NET 10 muammo qilmoqda | `git revert` (4 ta csproj + Dockerfile) → `--build` bilan qayta ko'taring. Baza sxemasi O'ZGARMAGAN, shuning uchun .NET 8 ga qaytish baza bilan mos |
| Ilova bazaga ulana olmayapti (`app_rw`) | `.env` da `DB_APP_PASSWORD` bormi? `init-roles.sql` ni qayta ishga tushiring — u parolni `ALTER ROLE` bilan yangilaydi. To'liq qaytarish: `deploy/README.md` § "Rollback" |
| Migratsiya "permission denied" beryapti | `ConnectionStrings__Migrator` / `DB_MIGRATOR_PASSWORD` berilmagan — ilova `app_rw` bilan DDL qilmoqchi. Bu ATAYLAB baland xato: konfiguratsiyani to'g'rilang, `Default` ni egaga QAYTARMANG |

---

## 2. Ma'lumotlar yo'qolishi mumkin bo'lgan buyruqlar

- `docker compose down -v` — **BAZANI O'CHIRADI** (`pg-data` volume). Hech qachon
  so'ramasdan ishlatmang. Oddiy to'xtatish: `docker compose down` (`-v` SIZ).
- `docker volume rm wunderkindlms_pg-data` — xuddi shunday.
- `docker volume rm wunderkindlms_dpkeys` — DataProtection kalitlari; yo'qolsa barcha
  sessiyalar bekor bo'ladi (ma'lumot yo'qolmaydi, lekin hamma qayta login qiladi).
- `docker volume rm wunderkindlms_redis-data` — **XAVFSIZ.** Redis faqat kesh.

---

## 3. Mijoz tizim ishlayotganini QANDAY tekshiradi

### 3.1 Sog'liq (healthcheck)

```bash
curl -s https://<domen>/api/health
# ishlayotgan bo'lsa:  {"status":"healthy"}     (HTTP 200)
# baza tushgan bo'lsa: {"status":"unhealthy",...} (HTTP 503)
```

Bu endpoint **bazaga haqiqiy so'rov yuboradi** (`SELECT 1`) — ya'ni "jarayon tirik" emas,
"ilova ishlayapti" degani. Docker ham har 30 sekundda shuni chaqiradi:

```bash
docker compose ps            # STATUS ustunida (healthy) turishi kerak
```

Redis ATAYLAB tekshirilmaydi: u ixtiyoriy kesh. Redis tushsa ilova sekinlashadi, lekin
ishlaydi — uni "unhealthy" deb belgilash keraksiz restart tsikliga olib kelardi.

### 3.2 Loglar

```bash
docker logs -f wunderkind-backend                  # jonli
docker logs --since 1h wunderkind-backend | grep -iE "error|exception|fail:"
docker compose logs --since 1h                     # barcha xizmatlar
```

Rotatsiya: har xizmatga `10 MB × 3 fayl` (jami ≈30 MB). To'lgan disk sababli tushib
qolish xavfi yo'q. Sozlama `docker-compose.yml` boshidagi `x-logging` anchor'ida.

Disk holatini tekshirish:
```bash
df -h /
docker system df                 # obrazlar/volume'lar qancha joy egallagan
du -sh /var/lib/docker/volumes/wunderkindlms_pg-backups/_data
```

### 3.3 Konteyner restartlari

```bash
docker inspect wunderkind-backend --format '{{.RestartCount}} {{.State.Status}}'
docker ps -a                     # "Restarting" yoki tez-tez o'zgarayotgan "Up N seconds" — muammo belgisi
```

### 3.4 Backup

Avtomatik: `backup` xizmati har kuni 21:00 UTC (= 02:00 Toshkent) da `pg-backups`
volume'iga `.dump` oladi, 7 kundan eskisini o'chiradi.

```bash
docker exec wunderkind-database ls -lh /backups      # oxirgi nusxa BUGUNGI bo'lishi kerak
docker logs wunderkind-backup | tail -5              # "[backup] tayyor"
```

**Restore SINOVDAN O'TKAZILGAN** (2026-09-11): `pre-net10-backup.dump` alohida
`restore_test` bazasiga tiklandi — 54 jadval, 31 user, 10 o'quvchi — jonli baza bilan mos.
Sinov tartibi (jonli bazaga TEGMAYDI):

```bash
docker exec wunderkind-database createdb -U schoollms restore_test
docker exec wunderkind-database pg_restore -U schoollms -d restore_test --no-owner /backups/<fayl>.dump
docker exec wunderkind-database psql -U schoollms -d restore_test -c "select count(*) from students;"
docker exec wunderkind-database dropdb -U schoollms restore_test
```

Bu sinovni **har chorakda** takrorlang.

### 3.5 Moliyaviy himoya yoqilganmi (SPEC §4.1)

Bu himoya buzilganda **hech qanday xato chiqmaydi** — tizim xuddi shunday ishlayveradi.
Shuning uchun uni ko'z bilan emas, buyruq bilan tekshirish kerak. Har deploydan keyin:

```bash
# 1) Ilova QAYSI rol bilan ulangan? Faqat `app_rw` bo'lishi kerak.
#    `schoollms` chiqsa (psql'dan boshqa) — HIMOYA O'CHIQ.
docker exec wunderkind-database psql -U schoollms -d schoollms -c \
  "select usename, count(*) from pg_stat_activity where datname='schoollms' group by 1;"

# 2) `app_rw` hech narsaga ega emasmi? Javob 0 bo'lishi SHART.
#    Egalik paydo bo'lsa REVOKE ishlamay qoladi.
docker exec wunderkind-database psql -U schoollms -d schoollms -c \
  "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
    where n.nspname='public' and c.relowner='app_rw'::regrole;"

# 3) To'lovni o'chirib ko'ring — RAD ETILISHI kerak.
#    (`payments` jadvali P1-04 dan keyin paydo bo'ladi.)
DB_APP=$(grep '^DB_APP_PASSWORD=' /opt/schoollms/.env | cut -d= -f2-)
docker exec -e PGPASSWORD="$DB_APP" wunderkind-database \
  psql -U app_rw -d schoollms -h 127.0.0.1 --set=VERBOSITY=verbose \
  -c "update payments set amount = 1;"
```

Kutilgan javob: `ERROR: 42501: permission denied for table payments`.
`UPDATE <son>` chiqsa — **himoya ishlamayapti**, `deploy/README.md` § "Baza rollari" ga qarang.

Tafsilot va rollar jadvali: `deploy/README.md` § "Baza rollari".

---

## 4. Hali YO'Q (ochiq kamchiliklar)

Bular bor deb hisoblamang — ular hali qurilmagan:

1. **Xato bildirishnomasi yo'q.** Prodda ilova exception tashlasa hech kimga xabar
   bormaydi. Telegram bot allaqachon loyihada bor (`TelegramService`) — global exception
   handler'dan admin chatga yuborish eng arzon yechim.
2. **Disk va restart alertlari yo'q.** Hozircha faqat qo'lda tekshiriladi (3.2 / 3.3).
3. **Backup faqat shu serverda.** Server yo'qolsa backup ham yo'qoladi. Off-site nusxa kerak.
4. **Migratsiya hamon konteyner startida qo'llanadi**, garchi endi u to'g'ri rol
   (`ConnectionStrings__Migrator` = `schoollms_owner`) bilan ishlasa ham. Uni alohida
   qadamga aylantirish mumkin — `Database__AutoMigrate=false` (P1-02 da qo'shildi) —
   lekin hozircha yoqilmagan. P1-04 (moliya jadvallari) bilan birga yoqish tavsiya etiladi.
5. **`docker-compose.local.yml` PostgreSQL 5432 ni `0.0.0.0` ga ochadi.** Lokalda,
   lekin `127.0.0.1:5432:5432` bo'lgani to'g'riroq. (Redis `127.0.0.1` ga bog'langan.)

---

## 5. Faza 0 o'zgarishlari (2026-09-11)

- **.NET 8 → .NET 10 LTS.** 4 ta csproj (`net10.0`), EF Core / Npgsql / JwtBearer →
  10.x, Dockerfile `sdk:10.0` / `aspnet:10.0`. Baza sxemasi o'zgarmagan —
  `dotnet ef migrations has-pending-model-changes` → "No changes".
- **Redis kesh (`cache` xizmati).** `ReferenceCache` endi `IDistributedCache` ishlatadi
  (JSON). Redis bo'lmasa xotira keshiga qaytadi; Redis ishlash paytida tushsa so'rov
  DB'dan javob oladi va log'ga `warn` yoziladi — ilova TO'XTAMAYDI.
- **`/api/health` endi bazani tekshiradi** (ilgari faqat `{"status":"healthy"}` qaytarardi).
- **`backend` va `proxy` ga healthcheck**, `cloudflared`/`proxy` → `service_healthy` sharti.
- **Log rotatsiyasi** barcha xizmatlarga.

---

## 6. Faza 1 — P1-02 o'zgarishlari (2026-09-11)

- **Baza rollari ajratildi (SPEC §4.1).** Ilova endi `app_rw` bilan ulanadi — u hech
  narsaga EGA EMAS, shuning uchun `REVOKE` unga haqiqatan ta'sir qiladi. Migratsiya
  `schoollms_owner` (sxema egasi) bilan. `schoollms` superuser saqlanadi, lekin faqat
  `pg_isready` / `pg_dump` / favqulodda kirish uchun.
  **Sababi:** jadval egasiga `REVOKE` ta'sir qilmaydi — serverda sinovdan o'tkazilgan.
  Ya'ni "kassir to'lovni o'chira olmaydi" himoyasi ilgari UMUMAN ishlamasdi.
- **`deploy/init-roles.sql`** — idempotent bootstrap. Toza bazada avtomatik, mavjud
  bazada qo'lda, va **har migratsiyadan keyin qayta**. Tafsilot: `deploy/README.md`.
- **`.env` da ikkita yangi parol:** `DB_APP_PASSWORD`, `DB_MIGRATOR_PASSWORD`.
- **`Microsoft.EntityFrameworkCore.SqlServer` olib tashlandi** — baza PostgreSQL,
  bu paket hech qayerda ishlatilmagan.
- **Yangi tekshiruv:** 3.5-bo'lim. Himoya buzilsa xato chiqmaydi, shuning uchun uni
  har deploydan keyin buyruq bilan tekshirish kerak.

---

## 7. AI ulanish (MCP, faqat o'qish) — 2026-09-26

To'liq qo'llanma va deploy qadamlari: **docs/MCP.md §6**. Qisqasi:

- Yangi `.env`: `DB_RO_PASSWORD` (bo'sh = AI ulanish o'chiq, ilova ishlayveradi).
- Compose avtomatik: backend `ConnectionStrings__ReadOnly` (`app_ro`), database `RO_DB_PASSWORD`;
  server fayli: `Mcp__PublicBaseUrl=https://lms.wunderkindedu.uz`.
- `deploy/init-roles.sql` endi `app_ro` ni ham yaratadi (SELECT only, `default_transaction_read_only=on`,
  hech narsaga ega emas) va `mcp_audit` ni `app_rw` uchun append-only qiladi — migratsiyadan
  OLDIN va KEYIN ishga tushiring (mavjud bazada `docker exec -e RO_DB_PASSWORD=… wunderkind-database psql … -f …`).
- Yangi migratsiya `20260926150755_McpReadOnly` — 5 ta `mcp_*` jadval + `mcp_apply_app_ro_grants()`
  (app_ro ruxsat ro'yxati) va `mcp_prune_audit()` (jurnal 1 yil), DROP yo'q, Down to'liq qaytaradi.
- `Mcp__PublicBaseUrl` Production'da MAJBURIY (server faylida bor); `ConnectionStrings__Default`
  endi `Maximum Pool Size=40`.
- Cloudflare: bot-challenge'dan faqat `/mcp`, `/oauth/token|register|revoke`, `/.well-known/*` ozod;
  `/oauth/authorize` himoyada qoladi (docs/MCP.md §6.4).
- Caddy: o'zgarish yo'q.

