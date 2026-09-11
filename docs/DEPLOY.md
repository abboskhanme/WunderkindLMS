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

# 3) MIGRATSIYA — alohida qadam
#    Hozircha migratsiya konteyner startida `db.Database.Migrate()` orqali qo'llanadi.
#    Yangi migratsiya bo'lsa: avval backup (1-qadam), keyin 4-qadam.
#    KUTILAYOTGAN ISH: migratsiyani startdan ajratib, alohida `alembic upgrade head`
#    ekvivalenti (`dotnet ef database update`) qilib qo'yish.

# 4) Almashtirish
docker compose -f docker-compose.yml -f docker-compose.server.yml up -d

# 5) Tekshirish (pastdagi 3-bo'lim)
```

### Rollback

| Nima buzildi | Qaytarish |
|---|---|
| Yangi obraz ishlamayapti | `docker compose ... up -d --force-recreate` oldingi image tag bilan; yoki `git revert <commit> && docker compose ... up -d --build` |
| Migratsiya ma'lumotni buzdi | `docker exec -i wunderkind-database pg_restore -U schoollms -d schoollms --clean --if-exists /backups/pre-deploy_<ts>.dump` |
| Redis muammo qilmoqda | `docker-compose.yml` dagi `ConnectionStrings__Redis` qatorini o'chiring → `docker compose ... up -d backend`. Ilova jarayon ichidagi xotira keshiga qaytadi. **Qayta build SHART EMAS.** |
| Log rotatsiyasi xalaqit bermoqda | xizmatdan `logging: *default-logging` qatorini oling |
| .NET 10 muammo qilmoqda | `git revert` (4 ta csproj + Dockerfile) → `--build` bilan qayta ko'taring. Baza sxemasi O'ZGARMAGAN, shuning uchun .NET 8 ga qaytish baza bilan mos |

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

---

## 4. Hali YO'Q (ochiq kamchiliklar)

Bular bor deb hisoblamang — ular hali qurilmagan:

1. **Xato bildirishnomasi yo'q.** Prodda ilova exception tashlasa hech kimga xabar
   bormaydi. Telegram bot allaqachon loyihada bor (`TelegramService`) — global exception
   handler'dan admin chatga yuborish eng arzon yechim.
2. **Disk va restart alertlari yo'q.** Hozircha faqat qo'lda tekshiriladi (3.2 / 3.3).
3. **Backup faqat shu serverda.** Server yo'qolsa backup ham yo'qoladi. Off-site nusxa kerak.
4. **Migratsiya konteyner startida avtomatik qo'llanadi** (`Program.cs` dagi
   `db.Database.Migrate()`). Prodda migratsiya alohida, nazorat ostidagi qadam bo'lishi kerak.
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
