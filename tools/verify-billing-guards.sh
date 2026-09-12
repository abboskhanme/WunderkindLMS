#!/usr/bin/env bash
# =============================================================================
#  SchoolLms — moliyaviy himoyani AMALIY tekshirish (SPEC §4.1, vazifa P1-05)
#
#      ./tools/verify-billing-guards.sh
#
#  Nima qiladi: TOZA postgres:17-alpine konteynerini ko'taradi, unga AYNAN
#  prodda ishlaydigan `deploy/init-roles.sql` ni initdb bosqichida qo'llaydi,
#  keyin `BillingCore` migratsiyasini `schoollms_owner` roli bilan yugurtiradi
#  va `app_rw` roli bilan quyidagilarni SINAYDI:
#
#      1. payments UPDATE      -> 42501 (permission denied)   SHART
#      2. payments DELETE      -> 42501                        SHART
#      3. payment_allocations / ledger_entries UPDATE+DELETE -> 42501
#      4. payments INSERT      -> ishlaydi (kassa ishlashi kerak)
#      5. ledger_entries INSERT -> ishlaydi (identity sequence huquqi bormi)
#      6. taqsimot trigger'i   -> 'Allocation exceeds payment amount'
#      7. discounts: approved_by = created_by -> check constraint rad etadi
#      8. seed: 5 toifa + billing_settings qatori
#      9. migratsiyani IKKINCHI marta qo'llash -> no-op
#     10. `migrations script` ichida FAQAT ruxsat berilgan DROP'lar bor
#
#  Nega alohida skript, `./tools/test.sh` emas: test harness'i (PostgresFixture)
#  ATAYLAB `app_rw` rolini o'zi yaratmaydi — aks holda u o'z grantlarini
#  tekshirib, soxta yashil berardi (docs/TESTING.md §4). Bu skript esa
#  haqiqiy deploy yo'lini (init-roles.sql -> migratsiya) aynan takrorlaydi.
#
#  Ishlab turgan stack'ga TEGMAYDI: boshqa konteyner, boshqa volume, port
#  ochilmaydi (SDK konteyneri `--network container:` orqali ulanadi).
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CID="wklms-verify-$$"
PG_SUPER_PW="verify_super_$$"
PG_OWNER_PW="verify_owner_$$"
PG_APP_PW="verify_app_$$"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"

FAILURES=0

cleanup() { docker rm -f "$CID" >/dev/null 2>&1 || true; }
trap cleanup EXIT

say()  { printf '\n\033[1m== %s\033[0m\n' "$1"; }
pass() { printf '  \033[32mOK\033[0m   %s\n' "$1"; }
fail() { printf '  \033[31mXATO\033[0m %s\n' "$1"; FAILURES=$((FAILURES + 1)); }

# psql'ni konteyner ichida ishlatish. $1 = rol, $2 = SQL.
psql_as() {
    local role="$1" sql="$2" pw
    case "$role" in
        schoollms)       pw="$PG_SUPER_PW" ;;
        schoollms_owner) pw="$PG_OWNER_PW" ;;
        app_rw)          pw="$PG_APP_PW" ;;
        *) echo "noma'lum rol: $role" >&2; return 1 ;;
    esac
    # `--set=VERBOSITY=verbose` SHART: usiz psql xato matnini chiqaradi-yu,
    # SQLSTATE kodini (42501) chiqarmaydi — tekshiruv esa aynan kodga tayanadi.
    docker exec -e PGPASSWORD="$pw" "$CID" \
        psql -h 127.0.0.1 -U "$role" -d schoollms -At --set=VERBOSITY=verbose -c "$sql"
}

# Xato KUTILAYOTGAN holat: SQLSTATE kodini qaytaradi (yoki muvaffaqiyat bo'lsa "NO_ERROR").
sqlstate_of() {
    local role="$1" sql="$2" out
    if out=$(psql_as "$role" "$sql" 2>&1); then
        echo "NO_ERROR"
    else
        # `--set=VERBOSITY=verbose` bo'lmasa kod chiqmaydi — xabardan qidiramiz.
        echo "$out" | grep -oE '\b(42501|23514|23505|23503)\b' | head -1 || echo "OTHER:$out"
    fi
}

expect_42501() {
    local label="$1" sql="$2" code
    code=$(sqlstate_of app_rw "$sql")
    if [ "$code" = "42501" ]; then pass "$label -> 42501"; else fail "$label -> KUTILGAN 42501, OLINGAN: $code"; fi
}

# -----------------------------------------------------------------------------
say "1/6  Toza Postgres 17 + deploy/init-roles.sql (initdb bosqichida)"
# -----------------------------------------------------------------------------
docker run -d --name "$CID" \
    -e POSTGRES_USER=schoollms \
    -e POSTGRES_PASSWORD="$PG_SUPER_PW" \
    -e POSTGRES_DB=schoollms \
    -e APP_DB_PASSWORD="$PG_APP_PW" \
    -e MIGRATOR_DB_PASSWORD="$PG_OWNER_PW" \
    -e POSTGRES_INITDB_ARGS="--encoding=UTF8 --locale=C" \
    -v "$ROOT/deploy/init-roles.sql":/docker-entrypoint-initdb.d/10-init-roles.sql:ro \
    postgres:17-alpine >/dev/null

printf '  Postgres kutilmoqda'
for _ in $(seq 1 60); do
    if docker exec "$CID" pg_isready -U schoollms -d schoollms >/dev/null 2>&1; then break; fi
    printf '.'; sleep 1
done
echo

# init-roles.sql initdb'da bajarilganini tasdiqlaymiz: `app_rw` hech narsaga EGA EMAS.
OWNS=$(psql_as schoollms "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='public' and c.relowner='app_rw'::regrole;" || echo ERR)
if [ "$OWNS" = "0" ]; then pass "app_rw hech narsaga ega emas (owns_in_public = 0)"
else fail "app_rw $OWNS ta obyektga EGA — REVOKE unga ta'sir qilmaydi"; fi

# -----------------------------------------------------------------------------
say "2/6  Migratsiya (schoollms_owner roli bilan)"
# -----------------------------------------------------------------------------
OWNER_CS="Host=127.0.0.1;Port=5432;Database=schoollms;Username=schoollms_owner;Password=$PG_OWNER_PW"

run_ef() {
    docker run --rm --network "container:$CID" \
        -v "$ROOT":/src -w /src \
        -v wklms-test-nuget:/nuget -e NUGET_PACKAGES=/nuget \
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        "$SDK_IMAGE" bash -c "
            export PATH=\"\$PATH:/root/.dotnet/tools\"
            dotnet tool install --global dotnet-ef --version '10.*' >/dev/null 2>&1 || true
            dotnet build SchoolLms.Server/SchoolLms.Server.csproj -p:BuildSpa=false -v q --nologo >/dev/null
            $1"
}

run_ef "dotnet ef database update \
        --project SchoolLms.Infrastructure/SchoolLms.Infrastructure.csproj \
        --startup-project SchoolLms.Server/SchoolLms.Server.csproj \
        --no-build --connection '$OWNER_CS'" >/dev/null
pass "dotnet ef database update — bo'sh bazada muvaffaqiyatli"

APPLIED=$(psql_as schoollms "select count(*) from \"__EFMigrationsHistory\" where migration_id like '%BillingCore';" || echo ERR)
[ "$APPLIED" = "1" ] && pass "BillingCore qo'llangan" || fail "BillingCore qo'llanmadi"

# -----------------------------------------------------------------------------
say "3/6  Seed idempotent va to'liq"
# -----------------------------------------------------------------------------
CATS=$(psql_as schoollms "select string_agg(code, ',' order by code) from fee_categories;" || echo ERR)
[ "$CATS" = "bus,dormitory,meals,other,tuition" ] \
    && pass "5 toifa seed qilindi: $CATS" || fail "toifalar noto'g'ri: '$CATS'"

SETTINGS=$(psql_as schoollms "select payment_due_day || '/' || overdue_after_day from billing_settings;" || echo ERR)
[ "$SETTINGS" = "10/15" ] \
    && pass "billing_settings = $SETTINGS (payment_due_day/overdue_after_day)" \
    || fail "billing_settings noto'g'ri: '$SETTINGS'"

# -----------------------------------------------------------------------------
say "4/6  Test ma'lumoti (owner roli bilan — kassa hali yo'q)"
# -----------------------------------------------------------------------------
psql_as schoollms_owner "
insert into users (id, full_name, role, email, avatar_url, password_hash, position, permissions)
values ('u-cashier', 'Verify kassir', 'cashier', 'verify.cashier', null, 'x', '', '{}'),
       ('u-director', 'Verify direktor', 'superadmin', 'verify.director', null, 'x', '', '{}');
-- P1-21: balance va discount_pct/amount/note ustunlari o'chirilgan; qoldiq hisoblanadi.
insert into students (id, full_name, last_name, first_name, middle_name, birth_date, address, gender,
                      parent_full_name, parent_last_name, parent_first_name, parent_middle_name,
                      parent_phone, class_name, enrollment_date,
                      sub_group, is_archived, archived_with_class, device_user_id)
values ('s-1', 'Verify o''quvchi', 'Verify', 'O''quvchi', '', '2015-01-01', '', 'male',
        'Ota Ona', 'Ota', 'Ona', '', '+998900000000', '1-A', '2026-09-01',
        0, false, false, '');
insert into cash_shifts (id, cashier_id, opened_at, opening_float, status)
values ('11111111-1111-1111-1111-111111111111', 'u-cashier', now(), 0, 'open');
insert into invoices (id, student_id, category_id, period_month, amount, discount, due_on, status, created_at)
values ('22222222-2222-2222-2222-222222222222', 's-1',
        '00000000-0000-0000-0000-0000000000c1', date '2026-09-01', 1000000, 0, date '2026-09-10', 'open', now());
insert into payments (id, receipt_no, student_id, amount, method, cash_shift_id, cashier_id, received_at)
values ('33333333-3333-3333-3333-333333333333', 1, 's-1', 500000, 'cash',
        '11111111-1111-1111-1111-111111111111', 'u-cashier', now());
" >/dev/null
pass "smena, hisob-faktura va to'lov yaratildi"

# -----------------------------------------------------------------------------
say "5/6  SPEC §4.1 — app_rw moliyaviy tarixni O'ZGARTIRA OLMAYDI"
# -----------------------------------------------------------------------------
expect_42501 "payments UPDATE"            "update payments set amount = 1;"
expect_42501 "payments DELETE"            "delete from payments;"
expect_42501 "payment_allocations UPDATE" "update payment_allocations set amount = 1;"
expect_42501 "payment_allocations DELETE" "delete from payment_allocations;"
expect_42501 "ledger_entries UPDATE"      "update ledger_entries set amount = 1;"
expect_42501 "ledger_entries DELETE"      "delete from ledger_entries;"

# Qator omon qoldimi (huquq yo'qligi — xato, jim o'tkazib yuborish emas)?
LEFT=$(psql_as schoollms "select count(*) from payments;" || echo ERR)
[ "$LEFT" = "1" ] && pass "to'lov qatori omon qoldi" || fail "to'lov qatori YO'QOLDI (count=$LEFT)"

# ...lekin kassa ISHLASHI kerak: INSERT ochiq.
if psql_as app_rw "
insert into payments (id, receipt_no, student_id, amount, method, cash_shift_id, cashier_id, received_at)
values (gen_random_uuid(), 2, 's-1', 100000, 'card',
        '11111111-1111-1111-1111-111111111111', 'u-cashier', now());" >/dev/null 2>&1
then pass "payments INSERT — app_rw uchun ochiq (kassa ishlaydi)"
else fail "payments INSERT app_rw uchun YOPIQ — kassa ishlamaydi"; fi

if psql_as app_rw "
insert into ledger_entries (entry_date, account, direction, amount, ref_type, ref_id, memo, created_by, created_at)
values (current_date, 'cash', 'debit', 100000, 'payment', null, 'verify', 'u-cashier', now());" >/dev/null 2>&1
then pass "ledger_entries INSERT — identity ketma-ketligiga huquq bor"
else fail "ledger_entries INSERT ishlamadi (identity sequence huquqi?)"; fi

# -----------------------------------------------------------------------------
say "6/6  Baza invariantlari"
# -----------------------------------------------------------------------------
# Taqsimot trigger'i: 500 000 lik to'lovga 600 000 taqsimlab bo'lmaydi.
ALLOC_OUT=$(psql_as app_rw "
insert into payment_allocations (id, payment_id, invoice_id, amount)
values (gen_random_uuid(), '33333333-3333-3333-3333-333333333333',
        '22222222-2222-2222-2222-222222222222', 600000);" 2>&1 || true)
if echo "$ALLOC_OUT" | grep -q 'Allocation exceeds payment amount'
then pass "trigger: 'Allocation exceeds payment amount'"
else fail "trigger ishlamadi. Javob: $ALLOC_OUT"; fi

# ...lekin to'g'ri taqsimot o'tishi kerak.
if psql_as app_rw "
insert into payment_allocations (id, payment_id, invoice_id, amount)
values (gen_random_uuid(), '33333333-3333-3333-3333-333333333333',
        '22222222-2222-2222-2222-222222222222', 500000);" >/dev/null 2>&1
then pass "to'g'ri taqsimot (500000 <= 500000) o'tdi"
else fail "to'g'ri taqsimot rad etildi — trigger juda qattiq"; fi

# SPEC §4.5 — o'zini o'zi tasdiqlash.
DISC_OUT=$(psql_as app_rw "
insert into discounts (id, student_id, category_id, percent, amount, reason, starts_on,
                       status, created_by, approved_by, created_at)
values (gen_random_uuid(), 's-1', null, 10, 0, 'test', date '2026-09-01',
        'approved', 'u-director', 'u-director', now());" 2>&1 || true)
if echo "$DISC_OUT" | grep -q 'ck_discounts_approver_differs'
then pass "discounts: approved_by = created_by rad etildi (ikki qavatli nazorat)"
else fail "o'zini o'zi tasdiqlash O'TIB KETDI. Javob: $DISC_OUT"; fi

# Bir kassirda ikkita ochiq smena bo'lmasin.
SHIFT_OUT=$(psql_as app_rw "
insert into cash_shifts (id, cashier_id, opened_at, opening_float, status)
values (gen_random_uuid(), 'u-cashier', now(), 0, 'open');" 2>&1 || true)
if echo "$SHIFT_OUT" | grep -q 'ux_cash_shifts_one_open_per_cashier'
then pass "ikkinchi ochiq smena rad etildi"
else fail "kassir ikkita smena ocha oldi. Javob: $SHIFT_OUT"; fi

# Migratsiyani ikkinchi marta qo'llash — no-op.
run_ef "dotnet ef database update \
        --project SchoolLms.Infrastructure/SchoolLms.Infrastructure.csproj \
        --startup-project SchoolLms.Server/SchoolLms.Server.csproj \
        --no-build --connection '$OWNER_CS'" >/dev/null
AFTER=$(psql_as schoollms "select count(*) from fee_categories;" || echo ERR)
[ "$AFTER" = "5" ] && pass "qayta 'upgrade head' — no-op (toifalar hali ham 5 ta)" \
                   || fail "qayta migratsiya seed'ni takrorladi: $AFTER ta toifa"

# `migrations script` ichida FAQAT KUTILGAN DROP'lar bo'lsin.
#
#  Ilgari bu tekshiruv "birorta ham DROP bo'lmasin" edi. P1-21
#  (`RetireLegacyFinance`) eski moliya yo'lini ATAYLAB o'chiradi, ya'ni endi
#  DROP bor. Tekshiruvni butunlay olib tashlash tuzoqni yo'q qilardi —
#  autogenerate chiqargan "ortiqcha" DROP jimgina o'tib ketardi. Shuning uchun
#  qoida qat'iyroq qilindi: chiqishdagi DROP'lar to'plami quyidagiga AYNAN
#  teng bo'lishi kerak. Yangi DROP paydo bo'lsa — skript yiqiladi va uni
#  qo'lda ko'rib chiqish kerak (docs/TASKS.md §4.1).
#  DIQQAT: ustun o'chirish `ALTER TABLE ... DROP COLUMN` ko'rinishida keladi,
#  ya'ni `^DROP ` filtri uni TUTMAYDI. Shuning uchun ikkala shakl ham
#  qidiriladi — aks holda oltita o'chirishning to'rttasi ko'rinmay qolardi.
EXPECTED_DROPS="alter table students drop column balance;
alter table students drop column discount_amount;
alter table students drop column discount_note;
alter table students drop column discount_pct;
drop table finance_transactions;
drop table monthly_charges;"

SCRIPT_SQL=$(run_ef "dotnet ef migrations script --idempotent \
        --project SchoolLms.Infrastructure/SchoolLms.Infrastructure.csproj \
        --startup-project SchoolLms.Server/SchoolLms.Server.csproj \
        --no-build")

ACTUAL_DROPS=$(printf '%s\n' "$SCRIPT_SQL" \
    | grep -iE '^[[:space:]]*(DROP |ALTER TABLE .* DROP )' \
    | tr 'A-Z' 'a-z' | sed 's/^[[:space:]]*//; s/[[:space:]]\{1,\}/ /g' \
    | sort -u)

[ "$ACTUAL_DROPS" = "$(printf '%s' "$EXPECTED_DROPS" | sort -u)" ] \
    && pass "'migrations script' ichida faqat kutilgan DROP'lar (P1-21)" \
    || fail "'migrations script' ichidagi DROP'lar ro'yxati o'zgardi — QO'LDA TEKSHIRING:
$ACTUAL_DROPS"

# -----------------------------------------------------------------------------
echo
if [ "$FAILURES" -eq 0 ]; then
    printf '\033[32mHAMMASI O'"'"'TDI — SPEC §4.1 himoyasi ishlayapti.\033[0m\n'
else
    printf '\033[31m%s ta TEKSHIRUV YIQILDI — himoya to'"'"'liq emas.\033[0m\n' "$FAILURES"
fi
exit "$FAILURES"
