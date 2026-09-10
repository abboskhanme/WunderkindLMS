# Deploy

Server: `91.99.187.196` · Domen: `https://lms.wunderkindedu.uz` (Cloudflare)

## Birinchi marta o'rnatish

```bash
ssh root@91.99.187.196
git clone https://github.com/<akkaunt>/WunderkindLMS.git /opt/schoollms
cd /opt/schoollms
cp .env.example .env && nano .env      # DB_PASSWORD va JWT_KEY ni to'ldiring
./deploy/deploy.sh
```

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

## Keyingi yangilanishlar

```bash
ssh root@91.99.187.196 '/opt/schoollms/deploy/deploy.sh'
```

## Konteynerlar

| Konteyner | Vazifasi |
|---|---|
| `schoollms-caddy` | 80/443 — Cloudflare oldidagi proksi, origin TLS |
| `schoollms-app` | .NET API + admin SPA + o'qituvchi PWA |
| `schoollms-db` | PostgreSQL 17 |
| `schoollms-backup` | Har kecha 02:00 (Toshkent) `pg_dump`, 7 kun saqlanadi |

Ilova va baza tashqariga chiqmaydi — faqat Caddy 80/443 da tinglaydi.

## Zaxira nusxani qo'lda olish

```bash
ssh root@91.99.187.196 'docker exec schoollms-db pg_dump -U schoollms -Fc schoollms' > wunderkind_$(date +%F).dump
```
