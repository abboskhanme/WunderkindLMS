# Deploy

Server: `91.99.187.196` · Domen: `https://lms.wunderkindedu.uz` (Cloudflare)

## Birinchi marta o'rnatish

```bash
ssh root@91.99.187.196
git clone https://github.com/<akkaunt>/WunderkindLMS.git /opt/schoollms
cd /opt/schoollms
cp .env.example .env && nano .env      # parollar va JWT_KEY ni to'ldiring
./deploy/deploy.sh
```

`.env` da endi UCHTA baza paroli bor (`DB_PASSWORD`, `DB_APP_PASSWORD`,
`DB_MIGRATOR_PASSWORD`) — nega ekanini keyingi bo'lim tushuntiradi. Har birini
alohida generatsiya qiling:

```bash
openssl rand -base64 32
```

Toza bazada `deploy/init-roles.sql` AVTOMATIK bajariladi (u
`/docker-entrypoint-initdb.d/` ga ulangan) — qo'lda hech narsa qilish shart emas.

Toza bazada hech qanday foydalanuvchi bo'lmaydi — birinchi adminni yarating:

```bash
DB=$(grep '^DB_PASSWORD=' .env | cut -d= -f2-)
python3 tools/create_user.py --login superadmin --password '<parol>' \
    --name "Tizim administratori" --role superadmin --db-password "$DB"
```

Namunaviy ma'lumot (ixtiyoriy, demo uchun):

```bash
python3 tools/seed_demo.py --base http://localhost --user superadmin --password '<parol>'
```

---

## Baza rollari — moliyaviy himoyaning ASOSI (SPEC §4.1)

### Nega kerak

Ilova ilgari `schoollms` roli bilan ulanardi. Bu rol 54 ta jadvalning EGASI (va
ustiga superuser). **PostgreSQL'da jadval egasiga `REVOKE` ta'sir qilmaydi** —
serverda amaliy sinovdan o'tkazilgan: `REVOKE DELETE` dan keyin ham qator
o'chirildi. Ya'ni "kassir to'lovni o'chira olmaydi" degan himoya **umuman
ishlamayotgan edi**.

Shuning uchun uchta rol ajratildi:

| Rol | Kim ishlatadi | Nima qila oladi |
|---|---|---|
| `schoollms` | `pg_isready`, `pg_dump`, favqulodda kirish | hammasi (superuser) — **ilova ishlatmaydi** |
| `schoollms_owner` | `ConnectionStrings__Migrator` — faqat migratsiya | sxema egasi, DDL |
| `app_rw` | `ConnectionStrings__Default` — **ilova** | oddiy jadvallarda to'liq CRUD; moliyaviy jadvallarda faqat `SELECT`/`INSERT` |

`app_rw` **hech narsaga ega emas** — aynan shuning uchun `REVOKE` unga haqiqatan
ta'sir qiladi. Xato to'lov `reversal_of` bilan yangi qator qo'shib tuzatiladi,
tahrirlash bilan emas.

> **Eng xavfli xato:** `ConnectionStrings__Default` ni `schoollms` ga qaytarish.
> Hech qanday xato chiqmaydi, ilova xuddi shunday ishlayveradi — faqat himoya
> jimgina yo'q bo'ladi. Quyidagi "Tekshirish" bo'limi shuning uchun bor.

### Mavjud bazaga rollarni qo'llash

Lokal va serverdagi bazalarda allaqachon ma'lumot bor, ularda skript avtomatik
bajarilmaydi (postgres uni faqat BO'SH bazada ishga tushiradi). Bir marta qo'lda
qo'llang. Skript **idempotent** — qayta ishga tushirilsa xato bermaydi.

```bash
cd /opt/schoollms
git pull                                  # init-roles.sql shu yerda keladi

# 1) AVVAL BACKUP. Bu qadam ixtiyoriy emas.
docker exec wunderkind-database pg_dump -U schoollms -Fc schoollms \
    > ~/wunderkind_$(date +%F_%H%M)_rollardan_oldin.dump
ls -lh ~/wunderkind_*.dump                # 0 bayt bo'lsa — TO'XTANG

# 2) .env ga yangi parollarni qo'shing
printf 'DB_APP_PASSWORD=%s\n'      "$(openssl rand -base64 32)" >> .env
printf 'DB_MIGRATOR_PASSWORD=%s\n' "$(openssl rand -base64 32)" >> .env

# 3) Baza konteynerini yangi muhit o'zgaruvchilari bilan qayta yarating.
#    DIQQAT: `down -v` EMAS — `-v` bazani BUTUNLAY O'CHIRADI.
docker compose -f docker-compose.yml -f docker-compose.server.yml up -d database

# 4) Rollarni qo'llang. Faylni HOSTDAN uzatamiz (`-f -`), konteyner ichidagi
#    yo'ldan emas: macOS/Docker'da bitta faylning bind-mount'i host'da fayl
#    tahrirlanganda "eskirib" qoladi va `No such file or directory` beradi.
docker exec -i wunderkind-database \
    psql -U schoollms -d schoollms -v ON_ERROR_STOP=1 -f - < deploy/init-roles.sql

# 5) Ilovani yangi ulanish satrlari bilan qayta ko'taring
./deploy/deploy.sh
```

4-qadam oxirida jadval chiqadi. **`app_rw` qatorida `owns_in_public` = 0
bo'lishi SHART.** Boshqa qiymat bo'lsa — himoya ishlamaydi, to'xtang.

### Har migratsiyadan keyin — MAJBURIY qadam

Yangi jadval `ALTER DEFAULT PRIVILEGES` orqali `app_rw` ga **to'liq CRUD**
beradi. Ya'ni P1-04 `payments` jadvalini yaratgan paytda `app_rw` unda `DELETE`
huquqiga ega bo'ladi. Uni qaytarib olish uchun migratsiyadan keyin `init-roles.sql`
QAYTA ishga tushiriladi:

```bash
docker exec -i wunderkind-database \
    psql -U schoollms -d schoollms -v ON_ERROR_STOP=1 -f - < deploy/init-roles.sql
```

Bu deploy'ning ixtiyoriy emas, majburiy qadami. Skriptdagi ro'yxat (`payments`,
`payment_allocations`, `ledger_entries`, `access_events`, `point_transactions`)
hali mavjud bo'lmagan jadvallarni jimgina o'tkazib yuboradi — shuning uchun uni
istalgan vaqtda ishlatish xavfsiz.

> **P1-05 dan keyin yangilik: moliya jadvallari uchun bu qadam endi ZAXIRA.**
> `BillingCore` migratsiyasi GRANT/REVOKE ni O'ZI bilan olib keladi
> (`SchoolLms.Infrastructure/Migrations/Sql/billing_guards.sql`), ya'ni
> `payments` / `payment_allocations` / `ledger_entries` himoyasi jadvallar
> yaratilgan lahzada, bir tranzaksiya ichida yoqiladi. Qo'lda hech narsa
> qilmasangiz ham `app_rw` ularda UPDATE/DELETE qila olmaydi.
>
> `init-roles.sql` ni qayta ishga tushirish **baribir tavsiya etiladi**: u
> `access_events` va `point_transactions` (Faza 2 va 4) ni qamrab oladi,
> parollarni yangilaydi va oxirida `owns_in_public = 0` jadvalini chiqaradi —
> deploy dalili shu. Ya'ni ikki qulf: migratsiya unutilmaydigan qulf,
> `init-roles.sql` — keng qamrovli qulf.

Himoya haqiqatan o'rnatilganini bir buyruq bilan, ishlab turgan stack'ga
tegmasdan tekshirish mumkin (toza konteyner ko'taradi, o'zi tozalaydi):

```bash
./tools/verify-billing-guards.sh
```

U `deploy/init-roles.sql` → migratsiya yo'lini aynan takrorlaydi va
`payments` da UPDATE/DELETE 42501 berishini, taqsimot trigger'ini,
`approved_by <> created_by` tekshiruvini va seed'ni sinaydi.

### Migratsiyani alohida qadamga aylantirish

Hozir migratsiya konteyner startida avtomatik bajariladi (`Migrator` roli bilan).
Moliya moduli kelgach prodda buni ajratish tavsiya etiladi:

```yaml
# docker-compose.server.yml → backend → environment
Database__AutoMigrate: "false"
```

Shundan keyin tartib: **backup → migratsiyani qo'lda ishga tushirish →
init-roles.sql → ilovani ko'tarish**.
Rollback: o'zgaruvchini olib tashlash kifoya, qayta build SHART EMAS.

### Tekshirish — himoya haqiqatan ishlayaptimi?

```bash
# 1) Ilova QAYSI rol bilan ulangan? `app_rw` bo'lishi SHART.
docker exec wunderkind-database psql -U schoollms -d schoollms -c \
  "select usename, count(*) from pg_stat_activity where datname='schoollms' group by 1;"

# 2) app_rw hech narsaga ega emasmi? Javob 0 bo'lishi SHART.
docker exec wunderkind-database psql -U schoollms -d schoollms -c \
  "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
    where n.nspname='public' and c.relowner='app_rw'::regrole;"

# 3) To'lovni o'chirib ko'ring — 42501 bilan RAD ETILISHI shart.
#    (`payments` jadvali paydo bo'lgandan keyin, ya'ni P1-04 dan so'ng.)
DB_APP=$(grep '^DB_APP_PASSWORD=' /opt/schoollms/.env | cut -d= -f2-)
docker exec -e PGPASSWORD="$DB_APP" wunderkind-database \
  psql -U app_rw -d schoollms -h 127.0.0.1 --set=VERBOSITY=verbose \
  -c "update payments set amount = 1;"
# Kutilgan javob:  ERROR:  42501: permission denied for table payments
# Agar "UPDATE <son>" chiqsa — HIMOYA ISHLAMAYAPTI.
```

### Rollback

| Nima bo'ldi | Nima qilish kerak |
|---|---|
| Ilova bazaga ulana olmayapti | `.env` da `DB_APP_PASSWORD` to'g'rimi? `init-roles.sql` qayta ishga tushiring — u parolni `ALTER ROLE` bilan yangilaydi. |
| Migratsiya ishlamayapti | `DB_MIGRATOR_PASSWORD` tekshiring; `ConnectionStrings__Migrator` berilmasa ilova `Default` ga qaytadi, lekin `app_rw` da DDL huquqi yo'q — xato baland chiqadi. |
| Hammasini ortga qaytarish kerak | `docker-compose.yml` da `ConnectionStrings__Default` ni `Username=schoollms;Password=${DB_PASSWORD}` ga qaytaring va backend'ni qayta ko'taring. Rollar bazada qolaveradi — zarar qilmaydi. **Diqqat: bu moliyaviy himoyani o'chiradi.** |
| Rollarni butunlay olib tashlash | `REASSIGN OWNED BY schoollms_owner TO schoollms;` → `DROP OWNED BY app_rw;` → `DROP ROLE app_rw;` → `DROP ROLE schoollms_owner;` |

Eng ishonchli rollback — 1-qadamdagi backup'dan tiklash:

```bash
docker exec -i wunderkind-database pg_restore -U schoollms -d schoollms --clean \
    < ~/wunderkind_<sana>_rollardan_oldin.dump
```

---

## Keyingi yangilanishlar

```bash
ssh root@91.99.187.196 '/opt/schoollms/deploy/deploy.sh'
```

## Konteynerlar

| Konteyner | Vazifasi |
|---|---|
| `wunderkind-proxy` | 80/443 — Cloudflare oldidagi proksi, origin TLS |
| `wunderkind-backend` | .NET API + admin SPA + o'qituvchi PWA |
| `wunderkind-database` | PostgreSQL 17 |
| `wunderkind-backup` | Har kecha 02:00 (Toshkent) `pg_dump`, 7 kun saqlanadi |

Ilova va baza tashqariga chiqmaydi — faqat Caddy 80/443 da tinglaydi.

## Zaxira nusxani qo'lda olish

```bash
ssh root@91.99.187.196 'docker exec wunderkind-database pg_dump -U schoollms -Fc schoollms' > wunderkind_$(date +%F).dump
```
