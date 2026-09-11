#!/usr/bin/env bash
#
# Serverdagi bazadan zaxira olib, SHU MASHINAGA tushiradi.
#
#   ./deploy/backup-pull.sh              # ./backups/ ga saqlaydi
#   ./deploy/backup-pull.sh /yo-l/papka  # boshqa papkaga
#
# Nega kerak: serverdagi kunlik zaxira o'sha serverning diskida turadi. Server
# yo'qolsa (disk buzilishi, akkaunt bloklanishi) zaxira ham yo'qoladi. Bu skript
# nusxani serverdan TASHQARIGA chiqaradi.
#
# Tiklash:
#   docker exec -i wunderkind-database pg_restore -U schoollms -d schoollms \
#       --clean --if-exists < backups/<fayl>.dump

set -euo pipefail

SERVER="${SCHOOLLMS_SERVER:-root@91.99.187.196}"
OUT_DIR="${1:-$(cd "$(dirname "$0")/.." && pwd)/backups}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-30}"

mkdir -p "$OUT_DIR"
STAMP=$(date -u +%Y%m%d_%H%M)
FILE="$OUT_DIR/wunderkind_${STAMP}.dump"

echo "── Serverda zaxira olinmoqda ──"
ssh -o BatchMode=yes "$SERVER" \
  'docker exec wunderkind-database pg_dump -U schoollms --format=custom schoollms' > "$FILE"

SIZE=$(du -h "$FILE" | cut -f1)
if [ ! -s "$FILE" ]; then
  echo "!! Zaxira bo'sh — server yoki baza javob bermadi"
  rm -f "$FILE"
  exit 1
fi

echo "── Yuklab olindi: $FILE  ($SIZE) ──"

# Eski nusxalarni tozalash
find "$OUT_DIR" -name 'wunderkind_*.dump' -type f -mtime +"$KEEP_DAYS" -delete 2>/dev/null || true
COUNT=$(find "$OUT_DIR" -name 'wunderkind_*.dump' -type f | wc -l | tr -d ' ')
echo "── Saqlanayotgan nusxalar: $COUNT ta (oxirgi $KEEP_DAYS kun) ──"
