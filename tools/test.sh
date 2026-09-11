#!/usr/bin/env bash
# SchoolLms — backend testlarini ishga tushirish (mahalliy .NET SDK TALAB QILINMAYDI).
#
#   ./tools/test.sh                 # hammasi
#   ./tools/test.sh -v n            # batafsil chiqish
#   ./tools/test.sh --filter HarnessTests
#
# Qanday ishlaydi: SDK konteyneri ichida `dotnet test` yuradi; testlar esa Docker soketi
# orqali O'ZI uchun postgres:17-alpine konteynerini ko'taradi (Testcontainers).
# Batafsil: docs/TESTING.md
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# NuGet keshi uchun doimiy volume — ikkinchi yurishdan boshlab restore ~1 s.
docker volume create wklms-test-nuget >/dev/null

exec docker run --rm -i \
  -v "$ROOT":/src -w /src \
  -v wklms-test-nuget:/nuget -e NUGET_PACKAGES=/nuget \
  `# Sibling-konteyner rejimi: testlar Postgres'ni XOST demonida ko'taradi.` \
  -v /var/run/docker.sock:/var/run/docker.sock \
  `# Ko'tarilgan Postgres porti XOSTda ochiladi — SDK konteyneri unga shu nom orqali yetadi.` \
  --add-host host.docker.internal:host-gateway \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test SchoolLms.Tests/SchoolLms.Tests.csproj "$@"
