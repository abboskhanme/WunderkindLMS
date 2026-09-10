#!/usr/bin/env bash
# Serverda GitHub'dan yangilash va qayta ishga tushirish.
#
#   ssh root@91.99.187.196 '/opt/schoollms/deploy/deploy.sh'
#
# Hamma narsa Docker ichida quriladi — serverga .NET SDK ham, Node ham o'rnatilmagan.
set -euo pipefail

cd /opt/schoollms

echo "── GitHub'dan yangilanish ──"
git fetch --prune origin
git reset --hard origin/master          # server nusxasi har doim GitHub bilan bir xil

echo "── Qurish va ishga tushirish ──"
docker compose -f docker-compose.yml -f docker-compose.server.yml up -d --build

echo "── Eski obrazlarni tozalash ──"
docker image prune -f >/dev/null

echo "── Holat ──"
docker compose -f docker-compose.yml -f docker-compose.server.yml ps \
  --format "  {{.Name}}: {{.Status}}"

for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w '%{http_code}' http://localhost/api/health || true)
  [ "$code" = "200" ] && { echo "  sog'liq: OK"; exit 0; }
  sleep 2
done
echo "  !! ilova 60 soniyada javob bermadi — loglarni ko'ring:"
echo "     docker compose -f docker-compose.yml -f docker-compose.server.yml logs app --tail=50"
exit 1
