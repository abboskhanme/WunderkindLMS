#!/usr/bin/env bash
#
# Zaxira nusxasini S3-mos bulut omboriga yuklaydi (Cloudflare R2, Backblaze B2, AWS S3).
# Serverda kuniga bir marta cron orqali ishlatiladi.
#
# NEGA KERAK: `wunderkind-backup` konteyneri kunlik nusxani serverning O'ZIDA saqlaydi.
# Server yo'qolsa — nusxa ham yo'qoladi. Bu skript uni tashqariga chiqaradi.
#
# ── SOZLASH ────────────────────────────────────────────────────────────────
# Serverdagi .env ga quyidagilarni qo'shing (qiymatlarni bulut panelidan olasiz):
#
#   S3_ENDPOINT=https://<account-id>.r2.cloudflarestorage.com
#   S3_BUCKET=wunderkind-backups
#   S3_ACCESS_KEY=...
#   S3_SECRET_KEY=...
#   S3_REGION=auto                 # R2 uchun "auto", AWS uchun masalan "eu-central-1"
#
# Keyin cron ga qo'shing (har kuni 03:00 Toshkent = 22:00 UTC):
#
#   crontab -e
#   0 22 * * * /opt/schoollms/deploy/backup-offsite.sh >> /var/log/wunderkind-backup.log 2>&1
#
# Cloudflare R2 tavsiya etiladi: 10 GB bepul, chiqish trafigi bepul, allaqachon
# Cloudflare ishlatilyapti. Panel: Cloudflare → R2 → Create bucket → API token.
# ───────────────────────────────────────────────────────────────────────────

set -euo pipefail

cd "$(dirname "$0")/.."
[ -f .env ] && set -a && . ./.env && set +a

: "${S3_ENDPOINT:?.env da S3_ENDPOINT yo'q — skript boshidagi izohga qarang}"
: "${S3_BUCKET:?.env da S3_BUCKET yo'q}"
: "${S3_ACCESS_KEY:?.env da S3_ACCESS_KEY yo'q}"
: "${S3_SECRET_KEY:?.env da S3_SECRET_KEY yo'q}"
S3_REGION="${S3_REGION:-auto}"
KEEP_DAYS="${OFFSITE_KEEP_DAYS:-90}"

STAMP=$(date -u +%Y%m%d_%H%M)
TMP="/tmp/wunderkind_${STAMP}.dump"

echo "[$(date -u +%FT%TZ)] zaxira olinmoqda..."
docker exec wunderkind-database pg_dump -U schoollms --format=custom schoollms > "$TMP"

if [ ! -s "$TMP" ]; then
  echo "!! zaxira bo'sh — baza javob bermadi"
  rm -f "$TMP"
  exit 1
fi
echo "[$(date -u +%FT%TZ)] hajmi: $(du -h "$TMP" | cut -f1)"

# AWS CLI konteyner ichida ishlatiladi — serverga hech narsa o'rnatilmaydi.
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" \
  -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
  -e AWS_DEFAULT_REGION="$S3_REGION" \
  -v "$TMP:/backup.dump:ro" \
  amazon/aws-cli:latest \
  s3 cp /backup.dump "s3://${S3_BUCKET}/wunderkind_${STAMP}.dump" \
  --endpoint-url "$S3_ENDPOINT"

rm -f "$TMP"
echo "[$(date -u +%FT%TZ)] yuklandi: s3://${S3_BUCKET}/wunderkind_${STAMP}.dump"

# Bulutdagi eski nusxalarni tozalash
CUTOFF=$(date -u -d "-${KEEP_DAYS} days" +%Y%m%d 2>/dev/null || date -u -v-"${KEEP_DAYS}"d +%Y%m%d)
docker run --rm \
  -e AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY" \
  -e AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY" \
  -e AWS_DEFAULT_REGION="$S3_REGION" \
  amazon/aws-cli:latest \
  s3 ls "s3://${S3_BUCKET}/" --endpoint-url "$S3_ENDPOINT" 2>/dev/null \
  | awk '{print $4}' | grep '^wunderkind_' || true \
  | while read -r key; do
      d=$(echo "$key" | sed 's/wunderkind_\([0-9]\{8\}\)_.*/\1/')
      [ "$d" -lt "$CUTOFF" ] 2>/dev/null && echo "  eski nusxa o'chirilmoqda: $key"
    done

echo "[$(date -u +%FT%TZ)] tayyor"
