# Abonementlar (subscription plans) — SPEC

Client, 2026-09-28: *"abonimentlar mana shular bo'lishi kerak … shunchaki aboniment bo'lsin va uni
narxi bo'lsin, qaysi abonimentda nechta o'quvchi borligi ko'rinsin"* — with EduSchool's
"Moliya → Abonement" list as the reference (functionality only; our look stays ours).

## 1. Problem

We have `fee_categories` ("To'lov toifalari": tuition, bus, dormitory, meals, other) and
`student_subscriptions` (student + category + monthly amount). EduSchool's abonements
("5-11 sinflar 1 690 000", "Avtobus to'lovi 160 000", …) came over only as free text in
`student_subscriptions.detail` ("EduSchool: 5-11 sinflar 1 690 000"). There is no list of
abonements, no price per abonement, no "how many students are on it".

A category cannot simply become an abonement: `tuition` alone carries seven prices, and the category
**code** drives money logic (payment allocation order `PaymentService.CategoryRank`, revenue account
`Accounts.RevenueFor`, class-fee suggestion, reports). Splitting it would touch all of that.

## 2. Model

New table `subscription_plans` — an **abonement**:

| column | type | note |
|---|---|---|
| id | uuid | |
| name | varchar(160) | unique among ACTIVE plans (case-insensitive) |
| category_id | uuid → fee_categories (restrict) | the plan's *type* (O'qish / Avtobus / Yotoqxona / …) — accounting stays by category |
| monthly_amount | numeric(14,2) ≥ 0 | the price, per month (accrual is monthly) |
| is_active | bool | archived = no new subscriptions; existing ones continue |
| created_at | timestamptz | |

`student_subscriptions.plan_id uuid null → subscription_plans (restrict)`, indexed.

Invariants:
- a subscription with a plan has `category_id = plan.category_id` and `monthly_amount = plan.monthly_amount`
  at the time it is written (the amount is copied — invoices keep history);
- the existing overlap rule stays: one live subscription per (student, category).

## 3. Price change = "hammaga, keyingi oydan" (client choice)

`PUT /plans/{id}` with a new price, `next = first day of next month`:
- live subscriptions of the plan (`ends_on is null or ends_on >= next`) that started **before** `next`
  are ended on `next - 1 day` and a new subscription (same student/plan/detail, new amount) starts on `next`;
- subscriptions that start on/after `next` just get the new amount;
- past months and written invoices are untouched. Audit per plan: "narx X → Y, N ta o'quvchi, D dan".

## 4. Backfill (migration)

For every distinct `(category_id, detail, monthly_amount)` whose detail starts with `EduSchool: `, create
a plan named `detail` minus the prefix, active, and set `plan_id` on those subscriptions. Generic SQL —
no school-specific names in the migration. Plans EduSchool has but we have no subscriptions for
(bus, dormitory, evening lessons) are added from the UI (or by us, with the client's OK).

## 5. API (`/api/admin/billing`, `Roles.FinanceStaff`; writes `FinanceAction.ManageSubscriptions`)

- `GET plans?activeOnly=` → `[{ id, name, categoryId, categoryName, categoryCode, monthlyAmount, isActive, studentCount }]`
  — `studentCount` = distinct students with a subscription of the plan live TODAY.
- `POST plans` `{ name, categoryId, monthlyAmount }` → plan.
- `PUT plans/{id}` `{ name, monthlyAmount, isActive }` → `{ plan, repriced: n, effectiveFrom }`.
  The type (category) is fixed once students are on it.
- `CreateSubscriptionRequest` gets optional `planId`: when given, category and amount come from the plan
  (request amount ignored); an archived plan → 409 `plan_inactive`.
- `UpdateSubscriptionRequest`: a planned subscription's amount can only stay or equal the plan price
  (same rule as the category's fixed price today).
- `StudentSubscriptionDto` gets `planId`, `planName`.

## 6. Screens

- Moliya sozlamalari: **"Abonementlar"** replaces "To'lov toifalari" in the side menu. Table: №, Nomi,
  Turi, Oylik narxi, Bog'langan o'quvchilar, Holati, Amallar (tahrirlash, arxivlash/tiklash);
  "+ Abonement". On a price change the form says how many students move to the new price and from which date.
- Obuna formasi: pick an **abonement** (active, grouped by type) — price shown, not editable.
- Obunalar: the abonement name shows in each row; filter by abonement.
- "To'lov toifalari" page stays reachable at its route (rarely needed: five base types).

## 7. Schedule on the abonement (client, 2026-09-28)

`subscription_plans.days_per_week int null` — null = **"Butun oy"**, 1..7 = **"Haftasiga N kun"**.
The price stays monthly; the schedule only counts a month's **lesson days** (for §9):
- "Butun oy" → every calendar day of the month;
- "Haftasiga N kun" → days whose weekday is one of the first N of the week (Mon … ; 5 = Mon–Fri,
  6 = Mon–Sat, 7 = all), minus school holidays (`holidays`).
A subscription with no abonement counts as "Butun oy". EduSchool-backfilled plans start as "Butun oy";
the school sets the real schedule (e.g. tuition "Haftasiga 6 kun").

## 8. Discount catalog — "Chegirma turlari" (client: *"chegirma nomlari bo'lsin miqdori bo'lsin"*)

`discount_types`: `name` (unique among active), `amount` > 0 so'm, `category_id` null = every abonement
type, `is_active`, `created_at`. **Amount only** (client, 2026-09-28: *"chegirma foizda emas summada
bo'lsin"*) — the `percent` column exists for the shared discount math but is always 0 for a catalog type.

The Chegirmalar settings page shows only this catalog (client: *"o'quvchi chegirmalar qismi kerakmas"*);
the old per-student request list and its approval queue left the UI. Rows already requested stay in the
database with their old rule (pending = no effect).

**Attaching to a student needs no approval** (client choice). `discounts.discount_type_id uuid null`;
a catalog discount is written `status = approved` with `approved_by = null` — the check
`ck_discounts_approved_has_approver` becomes `status <> 'approved' or approved_by is not null or
discount_type_id is not null`, and accrual picks `approved and (approved_by is not null or
discount_type_id is not null)`. Hand-written discounts keep the director approval as today.

One discount per invoice stays the rule (a category-specific one beats a global one, then the latest).

Attaching from a month that is **already invoiced**: each open invoice of that student from the start
month on (matching category, not void) gets the difference `DiscountFor(gross) − its current discount`
if positive, capped at what is still unpaid — via an invoice adjustment (§10). Later months get it at accrual.

Changing a catalog type's percent/amount updates the attached, still-running discounts in place
(= from the next accrual; written invoices unchanged), like an abonement price.

Ending: `POST discounts/{id}/end { endsOn }` — no new month after `endsOn` gets it.

## 9. Sickness discount — "Kasallik chegirmasi" (client choices)

In the cashier's payment form: button → month + number of sick days → for **every** open invoice of
the student for that month (all abonement types): `daily = invoice gross ÷ lesson days of the month
(§7, by the subscription's abonement)`, `deduction = min(daily × days, unpaid remaining)`,
days ≤ lesson days. Put by the **cashier**, no approval; recorded (who, when, days, per invoice).
A fully paid month gets nothing (no refund here). The modal shows only the **total** to deduct,
not the per-invoice split (client: *"jami ayriladiganni ko'rsatsin, qaysindan qancha ayrilishi
kerakmas"*).

### 9.1 Cancelling one (client: *"adashib ketsa bekor qilish button ham bo'lsin"* — on the green notice, *"mana bu yerda"*)

One application = one adjustment **group** (`group_id`, a row per invoice of the month). The payment
form's green notice "Kasallik chegirmasi qo'llandi: …" carries "Bekor qilish" (inline "Ha, bekor
qilish / Yo'q"); it is built from the server's list of the student's standing ones from the last
30 days, so it survives a page reload. The sickness modal itself has no cancel. Cancelling is
whole-group, by whoever takes payments (the same cashier may undo their own), and never edits a row:
per row a reversal row (`reversal_of` = original, unique), `invoices.discount −= amount`, status
refreshed, ledger **debit `receivable`, credit `revenue:<category>`**. The debt only grows, so it is
allowed on a month paid since. Refused: already cancelled (409 `sick_discount_already_cancelled`),
invoice voided since (409 `invoice_void`).

A catalog discount attached later counts the invoice's discount **without** the standing sick part
(`target − (discount − sick net)`), so a sickness discount never swallows it.

## 10. Invoice adjustment — how a written month is reduced

`invoice_adjustments` (append-only; UPDATE/DELETE revoked from `app_rw`): `invoice_id`, `kind`
(`sick` | `discount`), `amount > 0`, `sick_days null`, `note`, `group_id` (one action),
`reversal_of null` (§9.1), `created_by`, `created_at`.
Applying one: `invoices.discount += amount` (stays ≤ amount), status refreshed, and a ledger batch
`RefType = invoice_adjustment`: **debit `revenue:<category>`, credit `receivable`** — accrual posted
revenue net of discount, so the adjustment reduces both by the same sum. Nothing is ever deducted
below what is already paid (allocations are append-only).

## 10.1 Start date vs. first billed month — `accrue_from` (client, 2026-09-29: *"abonimentlar sentabrdan boshlanishi kerak, eduschool bila bir xil bo'lsin"*)

`student_subscriptions.accrue_from` (nullable, `>= starts_on`): the subscription stands from
`starts_on` (shown, counted as "Amalda", used by dormitory attendance), but accrual writes invoices
only for months from `accrue_from`. Null = from `starts_on`, i.e. the old behaviour.

Why: the bus / dormitory / evening abonements taken over from EduSchool were first opened from
1 October, because the imported September "O'qish" invoice already carries the lumped September
charge (tuition + bus/dorm). Migration `SubscriptionAccrueFrom` moves them to `starts_on =
2026-09-01, accrue_from = 2026-10-01` — only for students who do have a September invoice (the rest
have no September money anywhere and stay on 1 October). Screens show "Hisob: 1-oktabrdan" under the
start date. A plan re-price carries `accrue_from` into the new tail only when it is still ahead.

## 10.2 Student leaves mid-month — "Chiqib ketdi" (client, 2026-10-06)

A month already invoiced for a student who left must not stay as debt, must not be "paid" with
invented cash, and must not block archiving. From the **last day studied** (≤ today):

- **leave month:** `keep = payable × studied lesson days ÷ lesson days of the month`; lesson days
  follow the abonement schedule, "Butun oy" counts the school week Mon–Sat, holidays excluded;
- **later months:** keep 0;
- **write-off** = payable − keep → `invoice_adjustments` row, kind `withdrawal` (ledger: debit
  revenue, credit receivable — no cash row);
- **paid beyond keep** → negative `payment_allocations` rows, kind `release` (append-only; the
  money goes back to the payment's unallocated part = advance), then `AdvanceAllocator` lets it
  close the student's other debts first; what is left is refundable;
- the selected subscriptions end on the last day (one not started yet: ends on its start with
  `accrue_from` past it), so accrual never writes those months again.

One action = one `student_withdrawals` row (group id of its adjustments and releases, previous
subscription state in `details`). Cancelling within 30 days reverses the adjustments, restores the
subscriptions (refused if one was changed since) and lets the advance pay the months back; skipped
months are accrued. Release rows are hidden where a payment is shown as it was taken (receipt
reprint, Z-report, cash day, cash statement, payment details, journal categories).

API (`Roles.FinanceStaff`, writes `ManageSubscriptions`): `GET|POST
/api/admin/billing/students/{id}/withdrawals`, `POST …/preview`, `POST …/{withdrawalId}/cancel`.
UI: student profile ⋮ → "Chiqib ketdi — abonement"; Abonement tab shows standing ones with cancel.

## 11. API

- `GET/POST discount-types`, `PUT discount-types/{id}` (`{ name, percent, amount, isActive }` →
  `{ discountType, updated }`) — read: finance staff; write: `ManageSubscriptions`.
  `DiscountType = { id, name, percent, amount, categoryId|null, categoryName|null, isActive, studentCount }`.
- `POST discounts/attach` `{ studentId, discountTypeId, startsOn, endsOn? }` →
  `{ discount, adjustedInvoices, adjustedTotal }`; `POST discounts/{id}/end` `{ endsOn }` → discount.
  `DiscountDto` gains `discountTypeId`, `discountTypeName`.
- Cashier (`/api/cash`, `AcceptPayment`): `GET sick-discount/preview?studentId&month=yyyy-MM&days`,
  `POST sick-discount` `{ studentId, month, days, note? }` → `{ month, days, total,
  lines: [{ invoiceId, categoryName, planName, gross, remaining, lessonDays, daily, deduction }],
  groupId }` (`groupId` null on a preview).
- `GET sick-discount?studentId` → standing ones, newest first: `[{ groupId, month, days, total, note,
  createdAt, createdByName }]`; `POST sick-discount/{groupId}/cancel` → `{ groupId, month, total }`.
- `SubscriptionPlan` gains `daysPerWeek: number | null` (create/update send it; null = "Butun oy").
- `StudentSubscription` gains `accrueFrom: string | null`.
