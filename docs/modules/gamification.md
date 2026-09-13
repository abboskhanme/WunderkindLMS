# Gamification — module specification

**Status.** Specification only. No code, no migration, no entity. The schema is
assembled centrally by the orchestrator (see `/tmp/wk-agent-brief.md`); five
agents writing migrations in parallel would collide in
`SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`.

**Source of truth for the target.** The client's live tenant
`wunderkind.eduschool.uz`, read **offline** from
`.eduschool-bundle/all.js` (8.1 MB, main chunk + 64 lazy chunks) and
`.eduschool-bundle/edu-menu.json`. Nothing was clicked, submitted or changed.
Every claim marked *(bundle)* is a literal string from that file with the byte
offset given. Every claim marked *(assumption)* is mine, with the reasoning
attached, and appears again in §10.

**Reference implementation.** This module is built as a copy of the **Billing**
module's shape, file for file. When a question is not answered here, the answer
is whatever `Billing` does:

| Concern | Read this file |
|---|---|
| Append-only ledger, the single writer, batch integrity, reversal | `SchoolLms.Application/Billing/LedgerService.cs` |
| Derived balance, never stored | `SchoolLms.Application/Billing/StudentBalanceQuery.cs` |
| Concurrency under an advisory lock | `SchoolLms.Application/Billing/CashShiftService.cs` (`LockSql`, line 92) |
| EF model, check constraints, indexes | `SchoolLms.Infrastructure/Data/BillingModel.cs` |
| Raw SQL: triggers + `GRANT`/`REVOKE` | `SchoolLms.Infrastructure/Migrations/Sql/billing_guards.sql` |
| Controller shape, two-layer RBAC | `SchoolLms.Server/Controllers/BillingCatalogController.cs` |
| Domain entities in their own file, `Entities.cs` untouched | `SchoolLms.Domain/Billing.cs` |
| Role matrix as data, not `if` chains | `SchoolLms.Server/Controllers/FinanceRoleAttribute.cs` |

---

## 1. Goal

Give every child a coin balance they earn automatically from the journal we
already have, spend in a shop and bid with in auctions, so that effort in class
turns into something visible the same day. Give teachers a weekly coin budget
they hand out with a named reason, and give the school a leaderboard, a set of
limits and an audit trail.

**A coin balance is money.** It is earned, held, spent and transferred between
children and the school. Everything this project already decided about cash in
`docs/SPEC.md` §4 applies here without exception: the ledger is append-only, a
mistake is corrected by a reversal, and the balance is **derived, never
stored**. `deploy/init-roles.sql` line 177 already lists `point_transactions` in
the `REVOKE UPDATE, DELETE` set — that decision was taken in Phase 1 and this
module is where it becomes real.

---

## 2. What EduSchool actually has (and where our SPEC is thinner)

### 2.1 The thirteen menu keys are twelve screens

*(bundle @ 7183244 and @ 7199576)* — the sidebar is built by two functions:
`nwt(coinAdvancedMode)` for moderators and `Nj(gamification, sidebarSettings,
coinAdvancedMode)` for teachers.

```js
r = { title:"Sabablar", path:"/gamification/reasons",
      translate: e ? "GAMIFICATION_ALL.CRITERIA" : "GAMIFICATION_ALL.REASONS",
      group:"CATALOG", role:"getCoinReason" }
```

**`CRITERIA` and `REASONS` are the same screen.** One route
(`/gamification/reasons`), one permission (`getCoinReason`), one component
(`ReasonsPage`); only the i18n key flips with the `coinAdvancedMode` tenant
setting. The task brief's table lists both and therefore over-counts: there are
**twelve** gamification screens plus one detail route.

Likewise `class-rating` is **not** a gamification route. It is `/class-rating`
under `ANALITICS_ALL.CLASS_RATING`, permission
`getClassSeasonalMarkAnalitics` — a seasonal-grade ranking of classes
(`edu-menu.json`). We still ship a coin-based class leaderboard, but as part of
our own Rating screen, not as a copy of theirs.

### 2.2 Two operating modes and a kill switch

*(bundle @ 1974391, @ 1981294, @ 7207102)*

| Tenant setting | Effect |
|---|---|
| `gamification` (default `true`) | Master switch. `false` hides the whole section, the `coins` column in the students and employees lists, the `coin-history` tab on the student card, and the two journal columns. |
| `coinAdvancedMode` (default `false`) | `false` → **shop-only mode**: Categories, Products, Reasons, Orders, Coin history (5 screens). `true` → **full economy**: all 12. Turning it off force-clears `coinPassiveEnabled`. |
| `coinPassiveEnabled` | Only visible when `coinAdvancedMode` is on. Automatic (journal-driven) earning. |
| `coinsWidget` | Shows the coin widget in the parent/student mobile app. |
| `COIN_UNIT` (`/coin-type`, finance settings tab, role `coinTypeGet`) | The **name** of the unit, localised `{uz, ru, en}` — the school may call it "coin", "yulduz", "ball". *(bundle @ 1817912, @ 3330065)* |

### 2.3 Staff hold coins too

*(bundle @ 1205557)* The employees list has a `coins` column and a row action
`employees.giveCoin` gated on `assignCoinToTeacher`. The employee form has a
per-branch, teacher-only checkbox `autoCoinEnabled`. The settings page has a
whole "teacherAutomation" panel *(bundle @ 1988814)*:

```
teacherAutoCoinEnabled      bool     master switch
teacherAutoCoinDayOfWeek    enum     which weekday the grant runs
teacherAutoCoinTime         time     HH:mm, step 60s
teacherAutoCoinCoefficient  number   >= 0, decimals allowed
teacherAutoCoinResetToZero  bool     unspent allowance is wiped at the next grant
```

This is a **teacher wallet**: the school tops teachers up weekly, teachers hand
coins to children out of their own balance, and the remainder expires. Our
`docs/SPEC.md` §3.8 has no staff side at all.

### 2.4 Where EduSchool is richer than `docs/SPEC.md` §3.8 / §6 Phase 4

Phase 4 in our SPEC is three bullets and 2 weeks. The gap, item by item:

| # | EduSchool has | SPEC §3.8 has | Evidence |
|---|---|---|---|
| 1 | Coin holders are **students *and* staff** | students only | `/coins/employee`, `/coins/branch`, `/coins/student` @ 3337453 |
| 2 | **Weekly teacher allowance** with weekday, time, coefficient, reset | nothing | @ 1988400 |
| 3 | **Directions** — colour-tagged earning categories | nothing | `/coin-direction*` @ 3336319; `byDirection[].direction.color` @ 853731 |
| 4 | **Levels** — thresholds with a name | nothing | `/coin-level*` @ 3336395 |
| 5 | **Grade → coin table** — the automatic earning rule, editable | one sentence, "automatic awards from attendance and assessments" | `/grade-coin-rate` @ 3336455 |
| 6 | **Reason limits** — a reason can run out | nothing | `/coin-reason/{id}/limit-status` @ 3336527 |
| 7 | **Freeze / unfreeze / compliance** | nothing | `/coins/freeze/{status,list,export,compliance,unfreeze}` @ 3336717 |
| 8 | A **shop** (categories → products → orders) **separate from auctions** | `reward_orders.item_id` points at `auction_items` — the two are conflated | `/category`, `/product`, `/product-order/*` @ 3336080 |
| 9 | Auctions have **lots**, **controllers**, **eligibility**, and per-lot **settle / handover / cancel**; plus an auction **protocol** page | a flat `auction_items` with a `stock` column | @ 3336856…3337324; route `auctions/:auctionId` → `AuctionProtocol` @ 1113400 |
| 10 | **Analytics**: top students, weekly-by-branch, summary, type breakdown, a dashboard pie, and a per-student coin profile (earned / penalised / cancelled, by kind, by direction) | "leaderboard" | @ 3336573; @ 853731; @ 382000 |
| 11 | `bonus` / `penalty` as a **typed, coloured** first-class distinction | `point_reasons.delta` may be negative; no typing | `{bonus:{color:"#16a34a"}, penalty:{color:"#dc2626"}}` @ 382511 |
| 12 | **Rating export** to file | nothing | `/coins/rating/export` @ 3337369 |
| 13 | 33 fine-grained RBAC permissions for this module alone | one implied section | @ 2291870 |

**Where our plan is stronger, and must stay that way.** EduSchool stores the
balance as a mutable column — `student.coins` and `employee.coins` are rendered
straight from the row *(bundle @ 872619, @ 1205557)*. That is the exact defect
this project already removed from money in migration `RetireLegacyFinance`
(`students.balance` deleted; see the comment block in `SchoolLms.Domain/Entities.cs`
around line 110). We do not reintroduce it for coins.

### 2.5 Their RBAC vocabulary (kept for the mapping table in §5.1)

*(bundle @ 2291870, verbatim)*

```
getGamification
getCategory      editCategory      deleteCategory
getProduct       editProduct       deleteProduct
getProductOrder  editProductOrder  deleteProductOrder
getCoinReason    editCoinReason    deleteCoinReason
getCoinDirection editCoinDirection deleteCoinDirection
getCoinLevel     editCoinLevel     deleteCoinLevel
getGradeCoinTable                  editGradeCoinTable
getCoinFreeze    manageCoinFreeze
getCoinAuction   editCoinAuction   deleteCoinAuction
getCoinRating    exportCoinRating
getCoinsHistory
assignCoinToBranch  assignCoinToTeacher  assignCoinToStudent  assignCoinReason
```

### 2.6 Their API surface (kept for the mapping table in §6)

*(bundle @ 3336080–3337500, verbatim path literals)*

```
/category  /category/pagin
/product   /product/pagin
/product-order/pagin  /product-order/{id}  /product-order/cancel  /product-order/complate
/coin-reason  /coin-reason/pagin  /coin-reason/{id}/limit-status
/coin-direction  /coin-direction/pagin  /coin-direction/all
/coin-level      /coin-level/pagin      /coin-level/all
/grade-coin-rate
/coins/students/bulk  /coins/student  /coins/employee  /coins/branch
/coins/my-balance  /coins/pagin
/coins/student/{id}/summary
/coins/analytics/top-students  /coins/analytics/weekly-by-branch
/coins/analytics/summary       /coins/analytics/type-breakdown
/coins/rating  /coins/rating/export
/coins/freeze/status  /coins/freeze/list  /coins/freeze/export
/coins/freeze/compliance  /coins/freeze/unfreeze
/coin-auction  /coin-auction/pagin  /coin-auction/{id}  /coin-auction/summary
/coin-auction/eligibility  /coin-auction/controllers
/coin-auction/lot-products /coin-auction/lot-categories
/coin-auction/{id}/lot
/coin-auction/{id}/lot/{lotId}/settle
/coin-auction/{id}/lot/{lotId}/handover
/coin-auction/{id}/lot/{lotId}/cancel
/coin-auction/{id}/finish
/coin-auction/{id}/participants/list
/coin-type
```

---

## 3. The coin ledger

This section is the one that must not be re-litigated by an implementer.

### 3.1 Shape

Table name is **`point_transactions`** and is not negotiable: `docs/SPEC.md`
§3.8 names it, and `deploy/init-roles.sql` line 177 already revokes
`UPDATE, DELETE` on it. Rename it and the protection silently stops applying —
with no error, which is the worst failure mode available here.

```
point_transactions
  id              bigserial   PK
  holder_kind     text        not null   'student' | 'staff'
  holder_id       text        not null   students.id | teachers.id  (both text today)
  delta           integer     not null   check (delta <> 0); + = credit, − = debit
  kind            text        not null   closed list, §3.2
  type            text        not null   'bonus' | 'penalty'  (display/reporting only)
  reason_id       uuid        null       -> coin_reasons(id)      on delete restrict
  reason_name     text        null       SNAPSHOT of the reason name at write time
  direction_id    uuid        null       -> coin_directions(id)   on delete restrict
  direction_name  text        null       SNAPSHOT
  ref_type        text        null       'journal' | 'auction_bid' | 'product_order'
                                          | 'award_batch' | 'allowance' | null
  ref_id          text        null       id of that thing (text: our ids are mixed)
  source_key      text        null       stable identity of the cause, §3.4
  source_seq      integer     not null default 1
  memo            text        null       free comment, shown to the child
  created_by      text        not null   -> users(id); from the JWT, never the body
  created_at      timestamptz not null
  reversal_of     bigint      null       -> point_transactions(id)
```

Indexes:

- `(holder_kind, holder_id, id)` — the balance and the history feed.
- `(created_at)` — analytics windows.
- `(reason_id)` where `reason_id is not null` — limit counting.
- `(direction_id)` where `direction_id is not null` — coin profile by direction.
- unique `(reversal_of)` where `reversal_of is not null` — one reversal per row
  (this is the `payments.reversal_of` pattern from P1-05).
- unique `(source_key, source_seq)` where `source_key is not null` — §3.4.
- `(ref_type, ref_id)` where `ref_id is not null`.

Check constraints (in the EF model, not raw SQL — see the comment at the top of
`BillingModel.cs` for why: model-level constraints land in the snapshot and a
later `--autogenerate` will not silently drop them):

```
ck_point_transactions_holder_kind   holder_kind in ('student','staff')
ck_point_transactions_type          type in ('bonus','penalty')
ck_point_transactions_kind          kind in (<the 12 values of §3.2>)
ck_point_transactions_delta         delta <> 0
ck_point_transactions_sign          (type = 'bonus'  and delta > 0)
                                 or (type = 'penalty' and delta < 0)
                                 or kind in ('auction_hold','auction_release',
                                             'purchase','refund','expiry','reversal')
ck_point_transactions_reversal      reversal_of is null or source_key is null
```

**Why `integer`, not `numeric`.** A coin is a counting unit a nine-year-old has
to be able to add up in their head. Fractions would buy nothing and would import
the whole rounding-error class of bug that `LedgerService` spends forty lines
defending against. Whole coins only, everywhere, including prices and rates.

**Why `timestamptz` (`DateTimeOffset`), not `DateTime`.** The same reason as
`SchoolLms.Domain/Billing.cs` states in its header: `AppDbContext.OnModelCreating`
ends with a loop that forces **every** `DateTime` property to
`timestamp without time zone`. An auction window and an award timestamp must be
reconstructible from another timezone, so gamification timestamps are
`DateTimeOffset`, which that loop does not touch. Write them with
`AppClock.Instant` (UTC offset zero — Npgsql rejects anything else).

**Why snapshot `reason_name` / `direction_name`.** Precedent inside this
repository: `DisciplinePoint` already copies `ReasonName` and `Points` so that
editing or deleting a reason cannot rewrite history
(`SchoolLms.Domain/Entities.cs` line ~389). A child who asks "why did I lose
five coins in October" must get October's answer, not today's.

### 3.2 `kind` — the closed list

Every row says, in one machine-readable word, why the coins moved. This is the
column the "why did I get this coin" screen renders from.

| `kind` | Sign | Written by | Meaning shown to the child |
|---|---|---|---|
| `grade` | + / − | automatic (journal) | "5 baho — Matematika" |
| `homework` | + / − | automatic (journal) | "Uy vazifasi bajarildi" |
| `behavior` | + / − | automatic (journal / discipline) | "Xulq-atvor" |
| `attendance` | + / − | automatic (journal) | "Sababsiz kelmadi" |
| `manual` | + / − | staff award | "O'qituvchi berdi: Faol ishtirok" |
| `allowance` | + | weekly job | teacher wallet top-up (staff only) |
| `expiry` | − | weekly job | unspent allowance wiped (staff only) |
| `auction_hold` | − | bid placed | "Auksion: Velosiped — band qilindi" |
| `auction_release` | + | outbid / cancelled | "Auksion: coinlar qaytarildi" |
| `purchase` | − | shop order placed | "Do'kon: Ruchka" |
| `refund` | + | shop order cancelled | "Do'kon: buyurtma bekor qilindi" |
| `reversal` | ± | admin correction | "Tuzatish" |

There is **no `auction_settle` kind**. The `auction_hold` row *is* the payment:
when the bid wins, the hold simply stays. Posting a second row at settlement
would either double-charge or require rewriting the hold, and the ledger cannot
be rewritten. The history line for a winning hold reads
"Auksion: {lot} — yutdingiz".

### 3.3 What may write to it

**Exactly one class: `CoinLedgerService`** (`SchoolLms.Application/Gamification/CoinLedgerService.cs`).

This is the `LedgerService` rule transplanted verbatim, and for the same reason
stated in that file's header: if coin writes go through the generic repository,
"what moved the balance" becomes unanswerable and a corrupted balance becomes
untraceable.

- `ICoinLedgerService` exposes `PostAsync`, `ReverseAsync`, and nothing else.
  There is **no** `Update`, no `Delete`, no `SetBalance`.
- The implementation never calls `Update()` or `Remove()`.
- `app_rw` has `SELECT, INSERT` on `point_transactions` and nothing more
  (SQLSTATE `42501` on `UPDATE`/`DELETE`). Enforced twice: in the module's own
  migration SQL (`Migrations/Sql/gamification_guards.sql`, copying
  `billing_guards.sql` §2 exactly) **and** in `deploy/init-roles.sql`, which
  already lists the table. The migration copy is the primary: `ALTER DEFAULT
  PRIVILEGES` hands every freshly created table full CRUD, and re-running
  `init-roles.sql` is a manual step that produces no error when forgotten.

Legitimate callers of `CoinLedgerService`, and no others:

| Caller | Posts |
|---|---|
| `CoinAutoAwardService` (hooked into `JournalService`) | `grade`, `homework`, `behavior`, `attendance` |
| `CoinAwardService` (staff "Coin berish") | `manual` |
| `TeacherAllowanceJob` (hosted service) | `allowance`, `expiry` |
| `AuctionBidService` | `auction_hold`, `auction_release` |
| `AuctionSettlementService` | `auction_release` (cancel path only) |
| `ProductOrderService` | `purchase`, `refund` |
| `CoinCorrectionService` (admin only) | `reversal` |

Every posting carries `created_by` taken from `ClaimTypes.NameIdentifier` on the
server. If the request body contains an actor field, the request is **rejected**
(`400`), not silently overridden — `docs/SPEC.md` §4.4.

### 3.4 Idempotency: how automatic awards avoid double-paying

An automatic award is caused by a row in the journal, and journal rows get
edited. Editing a grade from 5 to 3 must move the coins, not add more.

- `source_key` is the **stable** identity of the cause:
  `journal:{journalEntryId}:grade`, `journal:{journalEntryId}:homework`, …
- `source_seq` is the attempt number, starting at 1.
- `unique (source_key, source_seq) where source_key is not null`.

Award flow, all inside the caller's transaction:

1. `SELECT pg_advisory_xact_lock(hashtextextended('coin_source:' || :sourceKey, 0))`.
   The prefix matters: the 64-bit advisory space is database-wide and
   `billing_guards.sql` and `CashShiftService` already hash into it
   (`docs/ASSUMPTIONS.md` 2026-09-11). A collision would not corrupt anything but
   would make two unrelated operations wait on each other with no explanation.
2. Read the live row for that `source_key` (`max(source_seq)`, not reversed).
3. If it exists and its `delta` equals what we would post now — **do nothing**
   and return it. A journal re-save that changed nothing must not churn the
   ledger.
4. If it exists and differs — post a `reversal` row against it, then post the
   new award with `source_seq = max + 1`.
5. If it does not exist — post with `source_seq = 1`.

**Why an advisory lock and not insert-retry.** Same three reasons recorded for
`NextReceiptNoAsync` in `docs/ASSUMPTIONS.md` (2026-09-11): the retry would
surface in a different file from the decision, it degrades to O(n²) under a
burst (a teacher saving a 30-student journal grid in one submit is exactly such
a burst), and `SELECT … FOR UPDATE` is unavailable because `app_rw` deliberately
lacks `UPDATE` on the table it would need to lock.

### 3.5 The balance is derived

`CoinBalanceQuery` (`SchoolLms.Application/Gamification/CoinBalanceQuery.cs`),
modelled on `StudentBalanceQuery` down to the `ForAsync` / `ForManyAsync` pair
and the "do not call in a loop" warning.

```
available(holder) = SUM(delta)                       over all rows for that holder
held(holder)      = SUM(-delta) WHERE kind = 'auction_hold'
                                  AND ref_id IN (active bids)
lifetime_earned   = SUM(delta) WHERE delta > 0 AND kind IN
                    ('grade','homework','behavior','attendance','manual','allowance')
lifetime_spent    = SUM(-delta) WHERE delta < 0 AND kind IN
                    ('purchase','auction_hold')  -- only holds that became payments
```

- A reversal row carries the **opposite delta** and is included in the sum. So
  `available` is a plain `SUM` with no exclusion join.

  This is deliberately *different* from `StudentBalanceQuery`, which must exclude
  both the reversal and the reversed payment. The difference is forced by the
  data: `payments.amount` is always positive and a reversal is also positive, so
  they cannot cancel by addition. `point_transactions.delta` is signed, so they
  can. And a plain `SUM` cannot be got wrong by a caller who forgets the
  exclusion — which is precisely the silent-underreporting trap
  `StudentBalanceQuery` documents at length. The reversal also stays visible in
  the child's history, which a correction should be.
- `available` is already net of holds, because a hold is a real negative row. A
  child therefore cannot bid coins they have promised elsewhere. The Mini App
  shows both numbers: **"Balans 120 · Auksionda band 50"**.
- The **level** is computed from `lifetime_earned`, never from `available`.
  Spending must not demote a child; a level that goes down when you buy a pencil
  teaches the opposite of what the module is for.
- `available` may never go negative. `CoinLedgerService.PostAsync` re-reads the
  balance inside the transaction, under an advisory lock on
  `'coin_holder:' || holder_kind || ':' || holder_id`, and refuses a debit that
  would cross zero (`CoinRuleException.Insufficient`). A *penalty* is the one
  exception: a manual penalty may take a child to zero but not below, and the
  service clamps it and records the clamp in `memo`.

### 3.6 How a mistaken award is undone

There is exactly one way, and it is not editing.

```
POST /api/admin/gamification/coins/{txnId}/reverse
body: { reason: string }          reason is mandatory, min 3 chars
```

- Inserts one new row: `delta = -original.delta`, `kind = 'reversal'`,
  `type` flipped, `reversal_of = original.id`, `memo = reason`,
  `created_by` from the JWT.
- The original row is **never touched**. Both lines appear in the child's
  history; the reversed line is struck through and the correction is captured
  under it.
- A reversal may only target a row whose own `reversal_of IS NULL`, and a row may
  be reversed at most once. The second rule is the partial unique index; the
  first needs to see another row, so it is a trigger in
  `Migrations/Sql/gamification_guards.sql` — the same reason
  `check_allocation_total()` is a trigger and not a `CHECK`.
- **Who.** `admin` or `superadmin` only. A teacher cannot reverse their own
  award, exactly as a cashier cannot reverse a payment (`docs/SPEC.md` §4.3).
  A teacher who made a mistake asks an admin; the request is a normal
  conversation, not a screen.
- Reversing a **hold** is forbidden (`409`). Holds are undone by cancelling the
  bid or the auction, which is what releases them properly.
- Reversing a `purchase` is forbidden (`409`). Cancel the order instead; that
  posts a `refund` and returns the stock.
- Every reversal also writes an `audit_log` row in the same `SaveChanges`, as
  `LedgerService.ReverseAsync` does.

---

## 4. Data model

New domain file: **`SchoolLms.Domain/Gamification.cs`**.
`SchoolLms.Domain/Entities.cs` is **not touched** — acceptance criterion:
`git diff --stat SchoolLms.Domain/Entities.cs` is empty. Same rule, same reason
as `Billing.cs` (that file is 1077 lines and the repository's worst merge
conflict).

New EF configuration file: **`SchoolLms.Infrastructure/Data/GamificationModel.cs`**,
called once from `AppDbContext.OnModelCreating`.

Type conventions, copied from `Billing.cs`:

- entity PK → `uuid` (`Guid`); ledger PK → `bigserial` (`long`)
- FK to `students`, `teachers`, `users` → **`text`**, because those tables still
  have `text` primary keys after Phase 0. This is a deliberate mix, and it buys a
  real foreign key rather than a loose string.
- dates → `date` (`DateOnly`); instants → `timestamptz` (`DateTimeOffset`)
- coins → `integer`

There is no soft delete anywhere in this module. Catalogue rows carry
`is_active`; history rows are never removed. Currency does not appear — coins
are not money and are never converted to money (§10, Q7).

### 4.1 `coin_settings` — one row

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `00000000-0000-0000-0000-0000000000c1` | singleton, seeded |
| `is_enabled` | bool | no | `false` | master switch (EduSchool `gamification`) |
| `advanced_mode` | bool | no | `false` | shop-only vs full economy |
| `passive_earning_enabled` | bool | no | `false` | journal-driven awards; forced `false` when `advanced_mode` is off |
| `unit_name` | text | no | `'coin'` | what the school calls it, Uzbek |
| `teacher_allowance_enabled` | bool | no | `false` | |
| `teacher_allowance_dow` | smallint | no | `1` | 1 = Monday … 7 = Sunday |
| `teacher_allowance_time` | time | no | `08:00` | school-local (Asia/Tashkent) |
| `teacher_allowance_coefficient` | numeric(6,2) | no | `1.00` | see §7.5 |
| `teacher_allowance_reset` | bool | no | `true` | wipe the remainder at the next grant |
| `auction_min_increment` | integer | no | `1` | default step for new lots |
| `auction_anti_snipe_seconds` | integer | no | `60` | 0 disables |
| `rating_default_period` | text | no | `'month'` | `week` \| `month` \| `quarter` \| `year` \| `all` |
| `updated_by` | text | yes | | users.id |
| `updated_at` | timestamptz | no | | |

Checks: `teacher_allowance_dow between 1 and 7`,
`teacher_allowance_coefficient >= 0`, `auction_min_increment >= 1`,
`auction_anti_snipe_seconds between 0 and 900`,
`rating_default_period in ('week','month','quarter','year','all')`,
`advanced_mode or not passive_earning_enabled`.

Why a separate table rather than columns on `SchoolMeta`: identical to the
reasoning in `Billing.cs` for `BillingSettings` — `SchoolMeta` lives in
`Entities.cs` and is already a 40+ column dumping ground.

### 4.2 `coin_directions` — Yo'nalishlar

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique, Uzbek ("Akademik", "Sport", "Ijodkorlik") |
| `color` | text | no | `#RRGGBB`, check `~ '^#[0-9A-Fa-f]{6}$'` |
| `sort` | smallint | no | default 0 |
| `is_active` | bool | no | default true |

A direction is the colour a coin was earned in. It is attached to a *reason* and
to a *grade-coin rate*, and is copied onto the ledger row at write time. It
exists so a child sees "60 akademik, 20 sport" rather than one undifferentiated
number *(bundle @ 853731, `byDirection[].direction.color`)*.

### 4.3 `coin_levels` — Darajalar

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique ("Bronza", "Kumush", "Oltin") |
| `min_coins` | integer | no | unique, `>= 0`; one row must have `0` |
| `icon_url` | text | yes | `/uploads/...` |
| `color` | text | yes | `#RRGGBB` |
| `sort` | smallint | no | default 0 |

The level of a holder is derived: the row with the greatest `min_coins <=
lifetime_earned`. No column anywhere stores a level. A migration seeds one row
`("Boshlang'ich", 0)` so the query always returns something.

### 4.4 `coin_reasons` — Sabablar / Cheklovlar

The screen EduSchool labels `REASONS` in simple mode and `CRITERIA` in advanced
mode (§2.1). The extra columns are the "criteria" half.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique |
| `type` | text | no | `bonus` \| `penalty` |
| `default_coins` | integer | no | `> 0`; the sign comes from `type` |
| `min_coins` | integer | yes | if set, the awarder may choose within a range |
| `max_coins` | integer | yes | `max >= min`; if both null, `default_coins` is fixed |
| `direction_id` | uuid | yes | → `coin_directions` (restrict) |
| `requires_comment` | bool | no | default false |
| `daily_limit_per_actor` | integer | yes | how many times one member of staff may use it per day |
| `daily_limit_per_holder` | integer | yes | how many times one child may receive it per day |
| `weekly_limit_per_holder` | integer | yes | |
| `cooldown_hours` | integer | yes | minimum gap between two uses on the same child |
| `allowed_roles` | text[] | no | default `{'teacher','admin','superadmin'}` |
| `is_active` | bool | no | default true |
| `sort` | smallint | no | default 0 |

Checks: `type in ('bonus','penalty')`, `default_coins > 0`,
`min_coins is null or min_coins > 0`,
`max_coins is null or min_coins is null or max_coins >= min_coins`,
`cooldown_hours is null or cooldown_hours between 1 and 720`, and every
`*_limit_*` column `is null or > 0`.

All limits are counted **from `point_transactions`**, never from a counter
column. A counter is a second source of truth and it will drift.

### 4.5 `grade_coin_rates` — Baho-coin jadvali

The rule that turns the journal into coins. One row = one automatic trigger.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `trigger` | text | no | `grade` \| `homework` \| `behavior` \| `mastery` \| `attendance` |
| `match_value` | smallint | yes | see the table below |
| `absence_reason_id` | text | yes | → `absence_reasons(id)` (restrict); only for `attendance` |
| `mastery_from` | smallint | yes | only for `mastery`, 0..100 |
| `mastery_to` | smallint | yes | only for `mastery`, `>= mastery_from` |
| `coins` | integer | no | signed, `<> 0` |
| `direction_id` | uuid | yes | → `coin_directions` |
| `label` | text | yes | overrides the generated history line |
| `is_active` | bool | no | default true |

What `match_value` means per trigger — this mapping comes from our own
`JournalEntry` (`SchoolLms.Domain/Entities.cs` line 260), not from EduSchool:

| `trigger` | `match_value` | Journal source |
|---|---|---|
| `grade` | 1..5 | `JournalEntry.Grade` |
| `homework` | 1 = done, 2 = not done | `JournalEntry.Homework` (0 = unset, never fires) |
| `behavior` | 1 = good, 2 = bad | `JournalEntry.Behavior` (0 = unset, never fires) |
| `mastery` | null; uses `mastery_from`/`mastery_to` | `JournalEntry.Mastery` (0..100) |
| `attendance` | null; uses `absence_reason_id` | `JournalEntry.ReasonId` |

Uniqueness (one rule per trigger point) — a plain unique tuple is wrong here
because three of the five columns are null most of the time, so:

```sql
create unique index ux_grade_coin_rates_trigger
  on grade_coin_rates (
    trigger,
    coalesce(match_value, -1),
    coalesce(absence_reason_id, ''),
    coalesce(mastery_from, -1)
  );
```

Checks: `trigger in ('grade','homework','behavior','mastery','attendance')`,
`coins <> 0`, `mastery_to is null or mastery_from is null or mastery_to >= mastery_from`,
`(trigger = 'attendance') = (absence_reason_id is not null)`,
`(trigger = 'mastery') = (mastery_from is not null)`.

Seeded, active, and matching this school's practice (grades 1..5 exist in
`JournalEntry`; `QuarterGrade` uses 2..5):

| trigger | match | coins |
|---|---|---|
| grade | 5 | +10 |
| grade | 4 | +5 |
| grade | 3 | +1 |
| grade | 2 | −5 |
| homework | 1 (done) | +2 |
| homework | 2 (not done) | −3 |
| behavior | 1 (good) | +3 |
| behavior | 2 (bad) | −5 |

Nothing is seeded for `attendance` or `mastery`: the absence-reason list is
school-specific and `Mastery` is not filled in consistently today.

### 4.6 `point_transactions`

Defined in full in §3.1. Append-only.

### 4.7 `coin_freezes` — Cheklovlar (Freeze)

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `holder_kind` | text | no | `student` \| `staff` |
| `holder_id` | text | no | |
| `scope` | text | no | `spend` \| `earn` \| `all` |
| `reason` | text | no | mandatory, min 3 chars, shown to the holder |
| `starts_at` | timestamptz | no | |
| `ends_at` | timestamptz | yes | null = until released by hand |
| `created_by` | text | no | users.id |
| `created_at` | timestamptz | no | |
| `released_by` | text | yes | |
| `released_at` | timestamptz | yes | |
| `release_reason` | text | yes | mandatory when releasing |

- unique `(holder_kind, holder_id)` where `released_at is null` — at most one
  open freeze per holder. Nothing is ever deleted; a lifted freeze keeps its row.
- checks: `scope in ('spend','earn','all')`,
  `ends_at is null or ends_at > starts_at`,
  `released_at is null or release_reason is not null`.

Effect, enforced in `CoinLedgerService.PostAsync`:

| Active scope | Blocked |
|---|---|
| `spend` | `purchase`, `auction_hold` → `409 coin_frozen` |
| `earn` | `manual`, `grade`, `homework`, `behavior`, `attendance`, `allowance` |
| `all` | both |

`refund`, `auction_release`, `expiry` and `reversal` are **never** blocked. A
freeze must not be able to trap coins that the school owes back.

An automatic award that lands on an `earn`-frozen holder is **skipped, not
queued**, and one line is written to `audit_log`. Queueing would mean the child
gets a surprise payout weeks later with no memory of the lesson that earned it.

### 4.8 `coin_categories` and `coin_products` — the shop

`coin_categories`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `name` | text | no | unique |
| `image_url` | text | yes | |
| `sort` | smallint | no | default 0 |
| `is_active` | bool | no | default true |

`coin_products`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `category_id` | uuid | no | → `coin_categories` (restrict) |
| `name` | text | no | |
| `description` | text | yes | |
| `image_url` | text | yes | |
| `price_coins` | integer | no | `> 0` |
| `stock` | integer | yes | null = unlimited; `>= 0` |
| `per_student_limit` | integer | yes | lifetime cap per child; `> 0` |
| `min_level_id` | uuid | yes | → `coin_levels`; hides the product below that level |
| `visible_from` | timestamptz | yes | |
| `visible_to` | timestamptz | yes | `> visible_from` |
| `is_active` | bool | no | default true |
| `sort` | smallint | no | default 0 |

`stock` is a mutable column and stays mutable — it is inventory, not history.
`app_rw` keeps full CRUD on `coin_products`.

### 4.9 `coin_product_orders` — Buyurtmalar

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `product_id` | uuid | no | → `coin_products` (restrict) |
| `student_id` | text | no | → `students` (restrict — an order is history) |
| `product_name` | text | no | SNAPSHOT |
| `price_coins` | integer | no | SNAPSHOT, `> 0` |
| `qty` | smallint | no | `>= 1`, default 1 |
| `total_coins` | integer | no | `= price_coins * qty`, generated column |
| `status` | text | no | `pending` \| `completed` \| `cancelled` |
| `debit_txn_id` | bigint | no | → `point_transactions` — the `purchase` row |
| `refund_txn_id` | bigint | yes | → `point_transactions` — the `refund` row |
| `created_at` | timestamptz | no | |
| `decided_by` | text | yes | users.id — who completed or cancelled |
| `decided_at` | timestamptz | yes | |
| `cancel_reason` | text | yes | mandatory when `cancelled` |

Checks: `status in ('pending','completed','cancelled')`,
`(status = 'cancelled') = (cancel_reason is not null)`,
`(status = 'cancelled') = (refund_txn_id is not null)`,
`status = 'pending' or decided_at is not null`.

Index: `(student_id, created_at desc)`, `(status)` for the queue screen.

### 4.10 Auctions

`coin_auctions` — the event

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `title` | text | no | |
| `description` | text | yes | |
| `image_url` | text | yes | |
| `status` | text | no | `draft` \| `scheduled` \| `open` \| `closed` \| `settled` \| `cancelled` |
| `opens_at` | timestamptz | no | |
| `closes_at` | timestamptz | no | `> opens_at` |
| `min_increment` | integer | no | `>= 1`, default from settings |
| `anti_snipe_seconds` | integer | no | `0..900`, default from settings |
| `elig_min_level_id` | uuid | yes | → `coin_levels` |
| `elig_min_balance` | integer | yes | `>= 0` |
| `elig_class_ids` | text[] | yes | null = every class |
| `elig_grade_min` | smallint | yes | 0..11 |
| `elig_grade_max` | smallint | yes | `>= elig_grade_min` |
| `created_by` | text | no | |
| `created_at` | timestamptz | no | |
| `settled_at` | timestamptz | yes | |
| `cancel_reason` | text | yes | |

`coin_auction_lots` — the item

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `auction_id` | uuid | no | → `coin_auctions` (cascade) |
| `product_id` | uuid | yes | → `coin_products` (restrict); null = a one-off prize |
| `title` | text | no | snapshot / free text |
| `image_url` | text | yes | |
| `start_price` | integer | no | `>= 0` |
| `min_increment` | integer | no | `>= 1` |
| `status` | text | no | `pending` \| `open` \| `sold` \| `unsold` \| `cancelled` |
| `sort` | smallint | no | lots are auctioned in this order |
| `closes_at` | timestamptz | no | per-lot; seeded from the auction, extended by anti-snipe |
| `extended_count` | smallint | no | default 0 — how many times anti-snipe fired |
| `winner_student_id` | text | yes | → `students` |
| `winning_bid_id` | bigint | yes | → `coin_auction_bids` |
| `settled_at` | timestamptz | yes | |
| `handed_over_at` | timestamptz | yes | |
| `handed_over_by` | text | yes | |
| `cancel_reason` | text | yes | |

Checks: `status in (…)`, `start_price >= 0`, `min_increment >= 1`,
`(status = 'sold') = (winner_student_id is not null)`,
`handed_over_at is null or status = 'sold'`,
`(status = 'cancelled') = (cancel_reason is not null)`.

`coin_auction_bids`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | bigserial | no | |
| `lot_id` | uuid | no | → `coin_auction_lots` (cascade) |
| `student_id` | text | no | → `students` (restrict) |
| `amount` | integer | no | `> 0` |
| `status` | text | no | `active` \| `outbid` \| `superseded` \| `won` \| `lost` \| `cancelled` |
| `hold_txn_id` | bigint | no | → `point_transactions` — the `auction_hold` row |
| `release_txn_id` | bigint | yes | → `point_transactions` — the `auction_release` row |
| `placed_at` | timestamptz | no | |

- unique `(lot_id)` where `status = 'active' and amount = (max)` is not
  expressible; instead: unique `(lot_id, student_id)` where `status = 'active'`
  — a student has at most one live bid per lot. The top bid is a query
  (`order by amount desc, id asc limit 1`).
- index `(lot_id, amount desc, id)`.
- checks: `status in (…)`,
  `(status in ('outbid','superseded','lost','cancelled')) = (release_txn_id is not null)`.

`coin_auction_controllers`

| Column | Type | Null | Notes |
|---|---|---|---|
| `auction_id` | uuid | no | PK part, → `coin_auctions` (cascade) |
| `teacher_id` | text | no | PK part, → `teachers` (cascade) |
| `assigned_by` | text | no | |
| `assigned_at` | timestamptz | no | |

A controller may open, close, settle, hand over and cancel lots of **their**
auction without holding the admin role. `admin`/`superadmin` may always.

### 4.11 `teacher_coin_settings`

| Column | Type | Null | Notes |
|---|---|---|---|
| `teacher_id` | text | no | PK, → `teachers` (cascade) |
| `allowance_enabled` | bool | no | default false |
| `allowance_override` | integer | yes | fixed weekly coins; overrides the coefficient formula |
| `updated_by` | text | no | |
| `updated_at` | timestamptz | no | |

A separate table rather than two columns on `Teacher` — the `Entities.cs`
no-touch rule (§4 preamble). A teacher with no row here has no allowance.

There is deliberately **no** `teacher_coin_allowances` table. "This week's
allowance" is the most recent `allowance` row in the ledger; "what expired" is
the `expiry` row next to it. A second table would be a second truth.

### 4.12 Grants

`Migrations/Sql/gamification_guards.sql`, structured exactly like
`billing_guards.sql`:

```sql
-- Mutable reference and operational data — full CRUD.
GRANT SELECT, INSERT, UPDATE, DELETE ON
  public.coin_settings, public.coin_directions, public.coin_levels,
  public.coin_reasons, public.grade_coin_rates, public.coin_freezes,
  public.coin_categories, public.coin_products, public.coin_product_orders,
  public.coin_auctions, public.coin_auction_lots, public.coin_auction_bids,
  public.coin_auction_controllers, public.teacher_coin_settings
TO app_rw;

-- APPEND-ONLY. A mistake is fixed by inserting a reversal.
GRANT  SELECT, INSERT            ON public.point_transactions TO app_rw;
REVOKE UPDATE, DELETE, TRUNCATE  ON public.point_transactions FROM app_rw;
```

Wrapped in the same `DO $guards$ … IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE
rolname = 'app_rw') THEN RAISE NOTICE … RETURN;` block, because the test harness
applies migrations without `app_rw`.

`deploy/init-roles.sql` §5 already contains `('point_transactions')`. No change
is required there, and none should be made — but the deploy step "re-run
`init-roles.sql` after the migration" stays mandatory.

---

## 5. RBAC

### 5.1 Mapping EduSchool's 33 permissions onto ours

Our system does not have EduSchool's per-verb permission tree. It has coarse
section keys checked by `AdminPermAttribute` (read is always open to staff,
writes need the claim) plus a fine-grained action matrix for money
(`FinanceRoleAttribute` + `FinanceMatrix`). Coins are money-like, so we use both
layers, exactly as Billing does.

**One new staff permission key: `gamification`.** Added to
`adminPermissions` in `schoollms.client/src/config/constants.ts`
(label: "Gamifikatsiya"). That is the coarse gate.

**One new teacher permission key: `coins`.** Added to
`SchoolLms.Domain/TeacherPermissions.cs` and to `teacherPermissions` in
`constants.ts` (label: "Coin berish").

**A new action matrix,** `SchoolLms.Server/Controllers/CoinRoleAttribute.cs`
with `CoinMatrix.Rules` — a table, not an `if` chain, so one test can walk every
row (this is what P1-22 does for `FinanceMatrix`).

| Action (`gamification:<verb>`) | teacher | staff + `gamification` | admin | superadmin | controller of the auction |
|---|---|---|---|---|---|
| `gamification:read` (all list screens) | ✅ | ✅ | ✅ | ✅ | ✅ |
| `gamification:catalog.write` (directions, levels, reasons, rates, categories, products) | ⛔ | ✅ | ✅ | ✅ | ⛔ |
| `gamification:award` (manual bonus/penalty) | ✅ *from own wallet* | ✅ *from school* | ✅ | ✅ | ⛔ |
| `gamification:award.bulk` | ✅ | ✅ | ✅ | ✅ | ⛔ |
| `gamification:reverse` | ⛔ | ⛔ | ✅ | ✅ | ⛔ |
| `gamification:order.decide` (complete / cancel) | ⛔ | ✅ | ✅ | ✅ | ⛔ |
| `gamification:auction.write` (create, edit, add lots) | ⛔ | ⛔ | ✅ | ✅ | ⛔ |
| `gamification:auction.run` (open, close, settle, handover, cancel a lot) | ⛔ | ⛔ | ✅ | ✅ | ✅ |
| `gamification:freeze` (freeze / unfreeze) | ⛔ | ⛔ | ✅ | ✅ | ⛔ |
| `gamification:settings` | ⛔ | ⛔ | ⛔ | ✅ | ⛔ |
| `gamification:rating.export` | ⛔ | ✅ | ✅ | ✅ | ⛔ |
| `coins:self` (my balance, my history — Mini App) | ✅ | — | — | — | — |
| `coins:child` (my child's balance and history) | parent/student role | — | — | — | — |

`gamification:settings` is superadmin-only because turning `passive_earning_enabled`
off silently stops every child earning, and turning the coefficient up multiplies
every teacher's budget. Those are director decisions.

### 5.2 Rules that are not role checks

- **Server-derived actor.** `created_by`, `awarded_by`, `decided_by`,
  `released_by` come from the JWT. A body field with any of those names is a
  `400`.
- **A teacher awards from their own wallet.** If `teacher_coin_settings.allowance_enabled`
  is true for them, `manual` awards debit their staff balance and credit the
  child's, as two rows in one batch. If it is false, the award is created from
  the school (one row, credit only) and requires `staff + gamification` or above.
  This is the whole point of the allowance: a teacher with a budget cannot print
  coins.
- **Ownership beats role for children's data.** A parent asking for a child that
  is not theirs gets `404`, not `403` — the existence of the child is itself
  information. Use `GuardianAccess` (`SchoolLms.Server/Controllers/TelegramParentController.cs`).

---

## 6. API contract

Base: `/api/admin/gamification` (admin panel), `/api/teacher/coins` (teacher web
panel), `/api/tg/parent/...` and `/api/tg/teacher/coins` (Mini App).

**Pagination and filtering** follow the existing admin conventions in this
repository, not EduSchool's `POST /pagin`: `?page=1&size=20&search=&sort=field&dir=asc|desc`,
response `{ items: [...], total: n, page, size }`. Date filters are
`?from=YYYY-MM-DD&to=YYYY-MM-DD` inclusive, interpreted in Asia/Tashkent by
`AppClock`.

**Errors** are the `BillingRuleException` shape reused as `CoinRuleException`:
`{ code, message }`, HTTP `409` for a rule violation, `400` for a malformed
request, `404` for "not yours / not found".

Codes: `coins_insufficient`, `coin_frozen`, `reason_limit_reached`,
`reason_cooldown`, `product_out_of_stock`, `product_limit_reached`,
`auction_not_open`, `bid_too_low`, `not_eligible`, `lot_already_settled`,
`already_reversed`, `cannot_reverse_hold`, `wallet_insufficient`.

### 6.1 Settings

| Method | Path | Body / Query | Response | Permission |
|---|---|---|---|---|
| GET | `/settings` | — | `CoinSettingsDto` | `gamification:read` |
| PUT | `/settings` | full `CoinSettingsDto` minus audit fields | `CoinSettingsDto` | `gamification:settings` |

### 6.2 Catalogue (Directions, Levels, Reasons, Grade-coin, Categories, Products)

Uniform CRUD, one shape for all six. `<res>` ∈ `directions`, `levels`,
`reasons`, `grade-coin-rates`, `categories`, `products`.

| Method | Path | Notes | Permission |
|---|---|---|---|
| GET | `/<res>` | paginated; `?activeOnly=true` | `gamification:read` |
| GET | `/<res>/all` | unpaginated, active only — for dropdowns | `gamification:read` |
| POST | `/<res>` | `409` on a unique clash | `gamification:catalog.write` |
| PUT | `/<res>/{id}` | | `gamification:catalog.write` |
| DELETE | `/<res>/{id}` | **soft**: sets `is_active = false` if the row is referenced by any ledger row or order; hard-deletes only when unreferenced. Response says which happened. | `gamification:catalog.write` |

`GET /reasons/{id}/limit-status` → EduSchool's `/coin-reason/{id}/limit-status`.

```
GET /reasons/{id}/limit-status?studentId=<optional>
200 {
  reasonId, name, type, defaultCoins, minCoins, maxCoins,
  actorRemainingToday:   int | null,   // null = no limit
  holderRemainingToday:  int | null,
  holderRemainingWeek:   int | null,
  cooldownUntil:         iso | null,
  blocked:               bool,
  blockedCode:           'reason_limit_reached' | 'reason_cooldown' | null
}
```

The award form calls this on reason-select and disables the submit button with
the reason spelled out, rather than letting the child watch a teacher get a red
toast.

### 6.3 Awarding

```
POST /coins/award
{
  holderKind: 'student' | 'staff',
  holderIds:  string[],          // 1..200; the bulk case is the same endpoint
  reasonId:   uuid,
  coins:      int | null,        // null = the reason's defaultCoins
  comment:    string | null      // mandatory if reason.requiresComment
}
201 {
  posted:  [{ holderId, txnId, delta, balanceAfter }],
  skipped: [{ holderId, code, message }]     // frozen, limit reached, …
}
```

- Permission `gamification:award` (single) / `gamification:award.bulk` (>1).
- **Partial success is the contract.** Awarding a class of 30 where two children
  are frozen must award 28 and say so. An all-or-nothing batch would make the
  teacher hunt for the bad row.
- One transaction per holder, not one for the batch: an advisory lock on the
  actor's daily counter plus one on each holder, held for the whole batch, would
  serialise the school.
- The whole call takes one advisory lock on `'coin_actor:' || actorId` so two
  browser tabs cannot both pass the actor's daily limit.

```
POST /coins/{txnId}/reverse      { reason }        → 201 { txnId, reversalId }
GET  /coins                      paginated ledger feed
GET  /coins/holders/{kind}/{id}/summary            → CoinProfileDto
```

`GET /coins` filters: `holderKind`, `holderId`, `classId`, `kind`, `type`,
`reasonId`, `directionId`, `createdBy`, `from`, `to`, `minCoins`, `maxCoins`.
This is the **Coin tarixi** screen. Also `GET /coins/export` (XLSX) —
`gamification:rating.export`.

`CoinProfileDto` mirrors EduSchool's `/coins/student/{id}/summary`
*(bundle @ 853731)*:

```
{
  available, held, lifetimeEarned, lifetimePenalised, lifetimeCancelled,
  earnedCount,
  level: { id, name, minCoins, iconUrl, color } | null,
  nextLevel: { id, name, minCoins, remaining } | null,
  byKind:      [{ kind, coins, count }],
  byDirection: [{ directionId, name, color, coins }]
}
```

### 6.4 Freeze

| Method | Path | Body | Permission |
|---|---|---|---|
| GET | `/freeze` | `?holderKind=&search=&active=true` | `gamification:read` |
| GET | `/freeze/status?holderKind=&holderId=` | → `{ frozen, scope, reason, endsAt }` | `gamification:read` |
| POST | `/freeze` | `{ holderKind, holderId, scope, reason, endsAt? }` | `gamification:freeze` |
| POST | `/freeze/{id}/release` | `{ reason }` | `gamification:freeze` |
| GET | `/freeze/export` | XLSX | `gamification:rating.export` |
| GET | `/freeze/compliance` | `?from=&to=` — see below | `gamification:read` |

`/freeze/compliance` — the one endpoint whose EduSchool meaning I could not
recover from the bundle (only the path literal survives). Our definition, chosen
because it is the only reading the surrounding data supports: **per teacher for
the period — allowance granted, coins awarded, coins expired, distribution rate
(%) , number of distinct children rewarded, and current freeze state.** It is
the report that makes `teacher_allowance_reset` meaningful. Flagged in §10 Q4.

### 6.5 Shop

| Method | Path | Body | Permission |
|---|---|---|---|
| GET | `/orders` | paginated; `?status=&studentId=&classId=&from=&to=` | `gamification:read` |
| POST | `/orders/{id}/complete` | — | `gamification:order.decide` |
| POST | `/orders/{id}/cancel` | `{ reason }` | `gamification:order.decide` |
| GET | `/orders/export` | XLSX | `gamification:rating.export` |

Order creation is not an admin endpoint — the child places the order. See §8.

### 6.6 Auctions

| Method | Path | Body / Notes | Permission |
|---|---|---|---|
| GET | `/auctions` | paginated; `?status=&from=&to=` | `gamification:read` |
| POST | `/auctions` | title, window, increments, eligibility | `gamification:auction.write` |
| GET | `/auctions/{id}` | auction + lots + controllers + eligibility | `gamification:read` |
| PUT | `/auctions/{id}` | only while `draft` or `scheduled` | `gamification:auction.write` |
| DELETE | `/auctions/{id}` | only while `draft` | `gamification:auction.write` |
| POST | `/auctions/{id}/controllers` | `{ teacherIds: [] }` (replaces the set) | `gamification:auction.write` |
| POST | `/auctions/{id}/lots` | `{ productId? , title, imageUrl?, startPrice, minIncrement?, sort }` | `gamification:auction.write` |
| PUT | `/auctions/{id}/lots/{lotId}` | only while `pending` | `gamification:auction.write` |
| DELETE | `/auctions/{id}/lots/{lotId}` | only while `pending`, no bids | `gamification:auction.write` |
| POST | `/auctions/{id}/open` | `draft`/`scheduled` → `open` | `gamification:auction.run` |
| POST | `/auctions/{id}/finish` | closes and settles **every** open lot | `gamification:auction.run` |
| POST | `/auctions/{id}/lots/{lotId}/settle` | only after `closes_at` | `gamification:auction.run` |
| POST | `/auctions/{id}/lots/{lotId}/handover` | `sold` → records delivery | `gamification:auction.run` |
| POST | `/auctions/{id}/lots/{lotId}/cancel` | `{ reason }`; releases the winner's hold | `gamification:auction.run` |
| GET | `/auctions/{id}/lots/{lotId}/bids` | the bid ladder, newest first | `gamification:read` |
| GET | `/auctions/{id}/participants` | distinct bidders + their totals | `gamification:read` |
| GET | `/auctions/{id}/protocol` | the settlement record, §7.4 | `gamification:read` |
| GET | `/auctions/{id}/protocol.xlsx` | export | `gamification:rating.export` |

Bidding is **not** an admin endpoint. See §8.

### 6.7 Rating and analytics

| Method | Path | Notes |
|---|---|---|
| GET | `/rating/students` | `?period=&from=&to=&classId=&directionId=&page=&size=` |
| GET | `/rating/classes` | average coins earned **per student** in the class |
| GET | `/rating/export` | XLSX of the current filter |
| GET | `/analytics/summary` | totals: earned, penalised, spent, in circulation, active holders |
| GET | `/analytics/type-breakdown` | `[{ type:'bonus'|'penalty', total }]` — the dashboard pie |
| GET | `/analytics/top-students` | `?limit=10` |
| GET | `/analytics/weekly` | coins earned per ISO week, last 12 weeks |

Class rating is **per student**, not per class total. A class of 30 would
otherwise always beat a class of 20 and the leaderboard would teach nothing.

### 6.8 Teacher web panel

`/api/teacher/coins` — `[Authorize(Roles = "teacher")]` + teacher permission
`coins`.

| Method | Path | Notes |
|---|---|---|
| GET | `/wallet` | `{ available, allowanceThisWeek, spentThisWeek, expiresAt, resetEnabled }` |
| GET | `/reasons` | active reasons the teacher's role is allowed to use |
| GET | `/classes/{classId}/students` | roster + each child's `available` and level |
| POST | `/award` | same body and response as §6.3, holderKind fixed to `student` |
| GET | `/history` | the teacher's own awards, paginated |

---

## 7. Business rules

### 7.1 Automatic earning — the exact trigger

Hooked into the existing journal write path (`SchoolLms.Application/Services/JournalService.cs`),
inside the **same transaction** as the journal save.

Why synchronous and not a background scan: a background scan makes the child's
balance lag the lesson by minutes, which is precisely what makes a gamification
feature feel broken to a nine-year-old; and the journal write is already a
transaction, so awarding inside it means a grade can never exist without its
coin. The cost — the journal save now depends on the coin service — is bounded
by making every failure mode of the coin service non-fatal except a genuine bug
(see the skip rules below).

For each `JournalEntry` being inserted or updated, and for each of the five
triggers:

1. Skip everything if `coin_settings.is_enabled` is false, or
   `passive_earning_enabled` is false.
2. Resolve the matching `grade_coin_rates` row (active only). No match → no
   award, no error. A `0` in `JournalEntry.Homework` / `.Behavior` means "not
   marked" and never matches.
3. Build `source_key = 'journal:' || journalEntry.Id || ':' || trigger`.
4. Run the idempotency flow of §3.4. Delete of a journal entry → reverse the live
   award for every `source_key` with that entry id.
5. Skip (and audit) if the child has an active `earn` or `all` freeze.
6. Post with `kind = trigger`, `type` from the sign of `coins`,
   `direction_id`/`direction_name` from the rate,
   `reason_name = rate.label ?? '<generated>'`, `ref_type = 'journal'`,
   `ref_id = journalEntry.Id`, `memo = '<subject> · <date>'`.

The generated history line — this is the "a child must be able to see why"
requirement, made concrete:

| trigger | Uzbek line |
|---|---|
| grade | `"{grade} baho — {subject}"` |
| homework | `"Uy vazifasi bajarildi — {subject}"` / `"Uy vazifasi bajarilmadi — {subject}"` |
| behavior | `"Xulq-atvor: yaxshi — {subject}"` / `"Xulq-atvor: yomon — {subject}"` |
| mastery | `"O'zlashtirish {mastery}% — {subject}"` |
| attendance | `"{absenceReason.Name} — {subject}"` |

Grade-coin rates are **not applied retroactively**. Changing a rate changes what
future journal entries earn; it never rewrites yesterday. A silent retroactive
recompute would move balances children had already spent.

Note on the neighbouring module: `DisciplineReason` / `DisciplinePoint`
(`SchoolLms.Domain/Entities.cs` lines 377–405, screens `/admin/discipline`)
already exist and are the behaviour system, tracked separately at 100 base
points. Coins and discipline points are **two different currencies and stay
separate.** The bridge is one-directional and optional: a `grade_coin_rates` row
with `trigger = 'behavior'` reads `JournalEntry.Behavior`, not
`DisciplinePoint`. Wiring `DisciplinePoint` into coins is out of scope and is
Q6 in §10.

### 7.2 Manual awarding, limits and the teacher wallet

`CoinAwardService.AwardAsync`, per holder, in one transaction:

1. `pg_advisory_xact_lock('coin_actor:' || actorId)` — taken once for the whole
   batch, before the loop.
2. Validate the reason: active; `actorRole ∈ reason.allowed_roles`;
   `coins` within `[min_coins, max_coins]` or equal to `default_coins`;
   `comment` present if `requires_comment`.
3. Limits, all counted from `point_transactions`:
   - `daily_limit_per_actor`: rows with this `reason_id` and `created_by = actor`
     since today 00:00 Asia/Tashkent.
   - `daily_limit_per_holder`, `weekly_limit_per_holder`: same, keyed on
     `holder_id`. The week starts Monday.
   - `cooldown_hours`: the newest row with this `reason_id` + `holder_id`.
4. Freeze check (§4.7).
5. If the actor is a teacher with `allowance_enabled`:
   `pg_advisory_xact_lock('coin_holder:staff:' || teacherId)`, check the wallet
   covers the total, then post **two rows in one batch**: `-coins` on the
   teacher, `+coins` on the child, both `kind = 'manual'`, both sharing
   `ref_type = 'award_batch'` and the same `ref_id`. A penalty runs the other
   way and does **not** refund the teacher — a penalty destroys coins, it does
   not earn the teacher anything.
6. Otherwise post one row crediting (or debiting) the child from the school.
7. `audit_log` row in the same `SaveChanges`.

### 7.3 Auctions — states and the two-bids-at-once problem

**Auction states**

```
draft ──(open)──▶ scheduled ──(opens_at reached)──▶ open
                                                     │
                                       (finish │ every lot closed)
                                                     ▼
                                                  closed ──(all lots settled)──▶ settled
draft │ scheduled │ open ──(cancel, reason)──▶ cancelled
```

**Lot states**

```
pending ──(auction opens)──▶ open ──(closes_at, ≥1 bid)──▶ sold ──(handover)──▶ sold + handed_over_at
                                 └─(closes_at, 0 bids)──▶ unsold
open │ sold ──(cancel, reason)──▶ cancelled     (releases the hold)
```

`settled` and `handed_over` are terminal. A cancelled lot cannot be reopened; the
school creates a new lot.

**Placing a bid** — `AuctionBidService.PlaceAsync`, one transaction:

1. `SELECT pg_advisory_xact_lock(hashtextextended('auction_lot:' || lotId::text, 0))`.

   This is the same mechanism, for the same reason, as gapless receipt numbering
   in `CashShiftService` (`docs/SPEC.md` §4.7). Without it, two transactions each
   read the same "current highest bid" under `READ COMMITTED`, both pass the
   `amount > highest` check, and both commit — producing two winning bids of the
   same amount and a lot that cannot be settled. `SELECT … FOR UPDATE` on the lot
   is not an option for the same privilege reason recorded in
   `docs/ASSUMPTIONS.md`: the lock is also needed on the code path that touches
   `point_transactions`, where `app_rw` has no `UPDATE`.
2. Re-read the lot **after** taking the lock. Require `status = 'open'` and
   `now() < closes_at`, else `409 auction_not_open`.
3. Eligibility: level, minimum balance, class, grade range → `409 not_eligible`.
4. Freeze check (`spend` scope) → `409 coin_frozen`.
5. `minNext = max(start_price, highestActive.amount + lot.min_increment)`.
   `amount >= minNext` else `409 bid_too_low` with `minNext` in the payload.
6. `available >= amount` else `409 coins_insufficient`.
7. If this student already has an `active` bid on this lot: mark it `superseded`,
   post `auction_release` for its hold, link `release_txn_id`.
8. If another student holds the top bid: mark it `outbid`, post
   `auction_release`, link `release_txn_id`.

   **Releasing on outbid, not at settlement,** is a deliberate choice. It means a
   child who is outbid can immediately re-bid or go and spend their coins, and
   the school is never holding coins from thirty children for a single bicycle.
   The cost is more ledger rows; rows are cheap and the history reads honestly.
9. Post the new hold: `delta = -amount`, `kind = 'auction_hold'`,
   `ref_type = 'auction_bid'`, `ref_id = <new bid id>`.
   Insert order: the bid row first (to get its id), then the hold, then set
   `hold_txn_id`. Both inside the transaction, so a crash leaves neither.
10. Anti-snipe: if `anti_snipe_seconds > 0` and
    `closes_at - now() < anti_snipe_seconds`, set
    `closes_at = now() + anti_snipe_seconds` and `extended_count += 1`.
    Cap `extended_count` at 20 — an unbounded extension is a lot that never ends.
11. Commit. The lock releases at commit; nothing to unlock by hand.

**Settlement** — `AuctionSettlementService.SettleLotAsync`, one transaction,
same advisory lock:

1. Require `status = 'open'` and `now() >= closes_at`.
2. Top `active` bid → winner. `winner_student_id`, `winning_bid_id`, bid →
   `won`, lot → `sold`, `settled_at = now()`. **No coin row is posted** — the
   winner's `auction_hold` already debited them and now becomes the payment
   (§3.2).
3. Any remaining `active` bid (there should be none, but belt and braces) →
   `lost`, post `auction_release`.
4. No bids → `unsold`.
5. If the lot came from a product with finite `stock`, decrement it by 1.
6. `audit_log`.

**Who runs it.** A hosted service, `AuctionCloseJob`, polls every 30 seconds for
lots where `status = 'open' AND closes_at <= now()` and settles them. The manual
`POST …/settle` runs the identical service method and exists for retry and for a
controller who wants to close early after the window has passed. A lot can never
be left open because a controller went home.

Concurrency between the job and the button is handled by the same advisory lock
plus the `status = 'open'` re-check inside it. The loser of the race sees the lot
already `sold` and returns `409 lot_already_settled`.

Note: `coin_auctions` and `coin_auction_lots` **are** mutable (`closes_at`,
`status`, `winner_*`). `app_rw` keeps `UPDATE` on them. Only
`point_transactions` is append-only. Do not "helpfully" extend the `REVOKE` to
the auction tables — anti-snipe and settlement both need `UPDATE`.

### 7.4 The auction protocol page (`/auctions/:auctionId`)

EduSchool routes `auctions/:auctionId` to a component literally named
`AuctionProtocol` *(bundle @ 1113400)*. Ours is the settlement record, printable:

- auction header: title, window, controllers, eligibility rules as sentences
- per lot: image, title, start price, number of bidders, number of bids,
  winning amount, winner's name and class, settled-at, handed-over-at
- the bid ladder per lot, newest first, with each bidder's name, amount and time
- a footer total: coins committed, coins returned, coins collected
- an export button (`/protocol.xlsx`)

This screen is the answer to "who got the bicycle and why", and it must be
readable by a parent standing at a desk.

### 7.5 The weekly teacher allowance

`TeacherAllowanceJob`, a hosted service. Every minute it checks whether the
school-local time has crossed `teacher_allowance_dow` +
`teacher_allowance_time`, and if so runs once (guarded by an advisory lock on
`'coin_allowance:' || isoWeek` so a restart cannot double-grant).

For each teacher with `teacher_coin_settings.allowance_enabled`:

1. `amount = allowance_override ?? round(coefficient × <basis>)`.
2. If `teacher_allowance_reset` and the teacher's `available > 0`: post
   `expiry` for `-available` first, memo "Sarflanmagan haftalik coin".
3. Post `allowance` for `+amount`, `ref_type = 'allowance'`,
   `ref_id = '<iso-week>'`, `source_key = 'allowance:' || teacherId || ':' || isoWeek`,
   which makes a double run a no-op by §3.4.
4. Nothing is granted to a teacher with an active `earn` freeze.

**`<basis>`** — the multiplicand behind `teacherAutoCoinCoefficient`. The bundle
gives the field but not the formula. Our definition: **the number of distinct
students the teacher taught in the previous seven days**, from
`ScheduleLesson` / `JournalEntry`. Rationale: the budget should scale with how
many children the teacher can actually reward, so a teacher of two classes is
not given the same purse as a teacher of eight. Flagged as Q3 in §10; the
`allowance_override` column exists precisely so the school can bypass the formula
while we wait for an answer.

### 7.6 Shop orders

`ProductOrderService.PlaceAsync`, one transaction:

1. `pg_advisory_xact_lock('coin_product:' || productId)`.
2. Product active, inside its visibility window, child at or above `min_level_id`.
3. `stock is null or stock >= qty` else `409 product_out_of_stock`.
4. `per_student_limit`: count this child's non-cancelled orders of this product,
   else `409 product_limit_reached`.
5. Freeze check (`spend`).
6. Post `purchase` for `-(price × qty)`; `available` check inside
   `CoinLedgerService` under the holder lock.
7. Insert the order `pending` with snapshots and `debit_txn_id`.
8. Decrement `stock` (unless null).

`Complete` → status `completed`, `decided_by/at`. No coin movement; the child
already paid.
`Cancel` → post `refund` for `+total`, restore `stock`, status `cancelled`,
`cancel_reason` mandatory. Cancel is allowed from `pending` and from `completed`
(a returned pencil), but never twice — `refund_txn_id is null` is the guard.

### 7.7 Rounding, timezone, edge cases — stated so nobody has to guess

| Question | Answer |
|---|---|
| Fractional coins | Do not exist. `integer` everywhere. `coefficient` is `numeric(6,2)` but the product is `round()`ed to a whole coin at the point of posting, half away from zero. |
| Negative balance | Impossible for `purchase` and `auction_hold`. A `penalty` clamps at zero and records the clamp in `memo`. |
| Timezone | Every window, limit and period boundary is Asia/Tashkent via `AppClock`. Stored as `timestamptz` with UTC offset (`AppClock.Instant`) — Npgsql rejects non-zero offsets. |
| Week boundary | ISO week, Monday 00:00 Asia/Tashkent. |
| Archived student | `Student.IsArchived = true` → excluded from rating, cannot bid, cannot order, cannot receive automatic awards. History and balance are preserved and still visible on the card. |
| Deleted student | `on delete restrict` from `point_transactions`. A student with coin history cannot be hard-deleted, only archived. |
| Deleted reason / direction | `restrict`. Soft-deactivate instead; the ledger keeps the snapshot name. |
| Currency | None. Coins are never priced in so'm and are never convertible. |
| Soft delete | Only `is_active` on catalogue tables. No `deleted_at` anywhere. |
| Class change mid-year | The balance belongs to the child, not the class. Rating for a period uses the child's class **as of the query**, and says so in the export header. |
| Academic year rollover | Balances carry over. Resetting every child to zero each September is Q5 in §10 (recommendation: do not). |

---

## 8. UI

### 8.1 Admin panel (`/admin/gamification/*`)

Twelve screens plus one detail route, registered in
`schoollms.client/src/App.tsx` behind `<RequirePerm perm="gamification">`, and
in `schoollms.client/src/config/navigation.ts` as one collapsible section
"Gamifikatsiya" with icon `Coins`.

The section, and each child inside it, is hidden when
`coin_settings.is_enabled` is false. The seven advanced screens are hidden when
`advanced_mode` is false — same rule as EduSchool, same two settings.

| # | Screen | Route | Advanced only | Contains |
|---|---|---|---|---|
| 1 | Kategoriyalar | `/admin/gamification/categories` | no | table + create/edit drawer: name, image, sort, active |
| 2 | Mahsulotlar | `/admin/gamification/products` | no | card grid + drawer: category, name, description, image, price, stock, per-student limit, min level, visibility window, active |
| 3 | Buyurtmalar | `/admin/gamification/product-orders` | no | queue: student, class, product, coins, status chip, created; row actions **Yakunlash** / **Bekor qilish (sabab bilan)**; filters by status/class/date; export |
| 4 | Sabablar | `/admin/gamification/reasons` | no | table + drawer: name, bonus/penalty toggle, default/min/max coins, direction, requires-comment, the four limit fields, cooldown, allowed roles, active. The limit fields are hidden unless `advanced_mode`. |
| 5 | Coin tarixi | `/admin/gamification/coin-history` | no | the ledger feed. Columns: date, holder (student or staff, links to the card), class, ± coins, type chip (green/red), kind, reason, direction chip (its colour), comment, awarded by. Filters as §6.3. Row action **Bekor qilish** for admin only. Reversed rows struck through with the correction linked. |
| 6 | Yo'nalishlar | `/admin/gamification/directions` | yes | table + colour picker |
| 7 | Darajalar | `/admin/gamification/levels` | yes | table sorted by `min_coins`, icon upload, live preview of the badge |
| 8 | Baho - coin jadvali | `/admin/gamification/grade-coin` | yes | five sections (grade, homework, behavior, mastery, attendance), inline-editable coin values, a direction dropdown per row, an active toggle per row, and one banner: **"O'zgartirish faqat yangi baholarga ta'sir qiladi"** |
| 9 | Coin berish | `/admin/gamification/award` | yes | class picker → student table with checkboxes, current balance and level per child; reason dropdown; coin input pre-filled and range-bounded; comment; a live limit banner from `/reasons/{id}/limit-status`; result panel listing posted and skipped |
| 10 | Auksionlar | `/admin/gamification/auctions` | yes | list with status chip and countdown; **Yangi auksion** wizard (details → eligibility → lots → controllers); row click → protocol |
| 10a | Auksion protokoli | `/admin/gamification/auctions/:auctionId` | yes | §7.4 |
| 11 | Cheklovlar (Freeze) | `/admin/gamification/freeze` | yes | two tabs: **Muzlatilganlar** (active freezes, with **Ochish** + reason) and **Nazorat** (the compliance table of §6.4); freeze dialog: holder search, scope, reason, optional end date; export |
| 12 | Coin reytingi | `/admin/gamification/rating` | yes | two tabs: **O'quvchilar** (rank, avatar, name, class, level badge, earned in period, balance) and **Sinflar** (average earned per student); period selector; class and direction filters; export |

**What each permission hides**

| Condition | Hidden |
|---|---|
| `coin_settings.is_enabled = false` | the whole nav section; the two journal columns; the student card's coin tab; the students-list and teachers-list `coins` columns |
| `advanced_mode = false` | screens 6–12 (nav and route both — a hidden nav item with a live route is a bug waiting to be bookmarked) |
| staff without `gamification` | all write buttons on every screen (read stays open, per `AdminPermAttribute`) |
| not `admin`/`superadmin` | **Bekor qilish** on screen 5; the whole of screen 11; the auction create/edit wizard |
| not `superadmin` | the Sozlamalar → Gamifikatsiya panel |
| not a controller of that auction and not admin | Open / Finish / Settle / Handover / Cancel on screens 10 and 10a |

### 8.2 Additions to existing admin screens

Additive only — no existing screen is rewritten.

- **Journal grid** (`/admin/journal`, `/teacher/journal`): two extra columns
  before the date columns, exactly as EduSchool *(bundle @ 951000)* — **Coin**
  (the child's `available`) and **Coin berish** (a button opening the award
  drawer for that one child). Both hidden when gamification is off or the actor
  lacks `gamification:award`.
- **Students list**: a `Coin` column and a row action **Coin berish**.
- **Teachers list**: a `Coin` column, a row action **Coin berish**, and a
  **Haftalik coin** toggle writing `teacher_coin_settings.allowance_enabled`.
- **Student card** (`StudentDetailPage.tsx`): a **Coin** tab — the profile card
  of §6.3 (`CoinProfileDto`) above the child's own filtered coin history.
- **Teacher card**: the same tab for the staff wallet.
- **Admin dashboard**: one tile, **Coin aylanmasi** — a bonus/penalty pie for the
  selected period, clicking a segment navigating to coin history pre-filtered by
  type. This is EduSchool's `dashboard.coins_share_title` *(bundle @ 382511)*.
- **Sozlamalar**: a **Gamifikatsiya** panel holding every field of
  `coin_settings` (§4.1), superadmin only.

### 8.3 Telegram Mini App

The Mini App is where this module earns its keep. It is already live for parents
and teachers (`schoollms.client/src/pages/miniapp/ui-tg/`), so we are adding a
tab to each panel, not building an app.

#### Parent / student panel — a sixth tab, **"Coin"**

Registered in `screens/ParentPanel.jsx` `TABS` (icon `Coins`, label `Coin`),
after `finance`, and only present when `coin_settings.is_enabled`. New file
`screens/parent/CoinTab.jsx`; new calls added to `lib/parentApi.js` (never
`api.get()` inline — that file's header states the rule).

The tab is four stacked blocks, in this order, and the order is the point:

1. **Balance card.** The number, large, in the school's `unit_name`.
   Under it: `Auksionda band: 50` (only when non-zero) and the level badge with a
   progress bar — `Kumush · Oltingacha 40 coin`.
2. **"Nega?" — the last 20 movements.** One row each: date, the ± amount in
   green or red, the generated line from §7.1 ("5 baho — Matematika",
   "Auksion: Velosiped", "Do'kon: Ruchka"), and who gave it. A reversed row is
   struck through with "Bekor qilindi" beneath it. **Tapping a row expands the
   full reason and comment.** This block is the single most important thing in
   the module: a coin nobody can explain is a coin nobody trusts.
3. **Reyting.** Two chips, *Sinfim* / *Maktab*. Top 10 with the child's own row
   pinned at the bottom if outside it, showing rank, name, level badge and coins
   earned this month. The child's own row is highlighted.
4. **Do'kon va Auksion.** Two chips.
   - *Do'kon*: a two-column product grid; each card shows image, name, price,
     and a **Buyurtma** button that is disabled with the reason written on it
     when unaffordable, out of stock, over the limit or below the level. Confirm
     dialog states the price and the balance after. Below the grid,
     *Buyurtmalarim* with status chips.
   - *Auksion*: open lots with a live countdown, current top bid and who holds
     it (first name + class, never a full name), and my own bid if any. The
     **Ko'tarish** button pre-fills `minNext` from the server. Losing the top
     spot shows a toast, "Sizdan oshib ketishdi" — and the coins are already
     back, because holds release on outbid (§7.3).

Refresh policy: the tab polls `GET /api/tg/parent/children/{studentId}/coins/live`
every 5 seconds **only while the Auksion chip is open**, and otherwise refetches
on focus. No SignalR: a 500-pupil school does not need a socket, and the Mini App
already survives token expiry by re-authenticating on 401.

Mini App endpoints (parent surface, `[Authorize(Roles = "parent")]`, ownership
via `GuardianAccess`, `404` on a foreign child):

```
GET  /api/tg/parent/children/{studentId}/coins            → balance + level + last 20
GET  /api/tg/parent/children/{studentId}/coins/history    → paginated
GET  /api/tg/parent/children/{studentId}/coins/rating     → ?scope=class|school
GET  /api/tg/parent/children/{studentId}/shop             → visible products for this child
POST /api/tg/parent/children/{studentId}/shop/orders      → { productId, qty }
GET  /api/tg/parent/children/{studentId}/orders
GET  /api/tg/parent/children/{studentId}/auctions         → open lots + my bids
POST /api/tg/parent/children/{studentId}/auctions/{lotId}/bid → { amount }
GET  /api/tg/parent/children/{studentId}/coins/live       → { available, held, lots:[{lotId, top, minNext, closesAt}] }
```

`GET /coins` on this surface is EduSchool's `/coins/my-balance`.

**Who may spend?** Both the child (role `student`) and the guardian (role
`parent`) reach the same panel today — `App.jsx` routes both roles to
`ParentPanel`. Ordering and bidding are therefore available to both, and every
order and bid records `created_by` so the history says which. Q2 in §10.

#### Teacher panel — a sixth tab, **"Coin"**

Registered in `screens/TeacherPanel.jsx` `TABS` with `perm: 'coins'` — that
array already filters by the teacher's permissions, so no new mechanism is
needed. New file `screens/teacher/CoinTab.jsx`, calls added to
`lib/teacherApi.js`.

1. **Hamyonim.** Remaining / granted this week / spent, and the expiry date when
   `teacher_allowance_reset` is on: *"Yakshanba 23:59 da kuyadi"*. A teacher who
   can see the deadline actually spends the budget, which is the behaviour the
   setting exists to produce.
2. **Coin berish.** Class picker → student list with checkboxes (each row: name,
   balance, level) → reason chips (green for bonus, red for penalty) → coin
   stepper bounded by the reason's range → optional comment. Under the reason,
   the live limit line: *"Bugun bu sabab bilan yana 3 marta"*. Submit shows the
   per-child result of §6.3, listing anyone skipped and why.
3. **Tarixim.** The teacher's own awards, newest first, with the child's name and
   the reason. No reverse button — that is admin-only (§3.6) and the screen says
   so rather than hiding the fact.

Mini App endpoints (teacher surface, `[Authorize(Roles = "teacher")]` + `coins`):
the same five as §6.8, under `/api/tg/teacher/coins`.

---

## 9. Modules and phases

Estimates are developer-days for one competent developer per unit. B = backend,
F = frontend. Units in the same phase with **Parallel: yes** may run in different
worktrees at the same time.

### 9.0 Shared files — SEQUENTIAL, never parallel

These are touched by more than one unit. One agent, one commit, one at a time.
Any unit that needs a change here writes it into `docs/PENDING_WIRING.md` and
the orchestrator applies it.

| File | What changes | When |
|---|---|---|
| `SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | **one** migration for the whole module | G0 only |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | 14 `DbSet<>` lines + one `GamificationModel.Apply(b)` | G0 only |
| `SchoolLms.Application/Abstractions/IAppDbContext.cs` | the same 14 `DbSet<>` declarations | G0 only |
| `deploy/init-roles.sql` | nothing (`point_transactions` is already listed) — verify only | G0 |
| `SchoolLms.Server/Program.cs` | DI for 8 services + 2 hosted services | after G1, G6, G9 |
| `SchoolLms.Domain/TeacherPermissions.cs` | add `Coins = "coins"` to the list and to `All` | G11 |
| `schoollms.client/src/config/constants.ts` | `adminPermissions` += `gamification`; `teacherPermissions` += `coins` | G2 |
| `schoollms.client/src/config/navigation.ts` | the "Gamifikatsiya" section | G2 |
| `schoollms.client/src/App.tsx` | 13 routes | G2, G5, G6 |
| `miniapp/ui-tg/src/screens/ParentPanel.jsx` | one `TABS` entry | G10 |
| `miniapp/ui-tg/src/screens/TeacherPanel.jsx` | one `TABS` entry | G11 |
| `SchoolLms.Domain/Entities.cs` | **nothing. Ever.** | — |

### 9.1 Build order

| Unit | Name | Files it touches | Depends on | Parallel? | B | F |
|---|---|---|---|---|---|---|
| **G0** | Schema foundation | `Domain/Gamification.cs`, `Infrastructure/Data/GamificationModel.cs`, one migration, `Migrations/Sql/gamification_guards.sql`, + the shared files above | — | **no** — it is the schema | 3 | — |
| **G1** | Coin ledger | `Application/Gamification/{ICoinLedgerService,CoinLedgerService,CoinBalanceQuery,CoinRuleException}.cs` | G0 | no (everything depends on it) | 3 | — |
| **G2** | Catalogue + settings + nav | `Server/Controllers/GamificationCatalogController.cs`, `CoinRoleAttribute.cs`, 6 admin pages, nav, routes, perms | G0 | yes with G3 | 3 | 4 |
| **G3** | Automatic earning | `Application/Gamification/CoinAutoAwardService.cs`, hook in `Services/JournalService.cs`, grade-coin screen | G1, G2 (rates table) | yes with G4 | 3 | 1 |
| **G4** | Manual award + limits + history | `CoinAwardService.cs`, `GamificationCoinsController.cs`, Coin berish + Coin tarixi pages, journal columns, student/teacher card tabs | G1, G2 | yes with G3 | 3 | 4 |
| **G5** | Shop | `ProductOrderService.cs`, `GamificationShopController.cs`, Buyurtmalar page | G1, G2 | yes with G6, G7 | 2 | 2 |
| **G6** | Auctions | `AuctionService.cs`, `AuctionBidService.cs`, `AuctionSettlementService.cs`, `AuctionCloseJob.cs`, `GamificationAuctionsController.cs`, Auksionlar + protocol pages | G1, G2, G5 (products as lots) | yes with G5, G7 | 5 | 4 |
| **G7** | Freeze + compliance | `CoinFreezeService.cs`, freeze endpoints, Cheklovlar page | G1, G2 | yes with G5, G6 | 2 | 2 |
| **G8** | Rating + analytics + exports | `CoinRatingQuery.cs`, `CoinAnalyticsQuery.cs`, Coin reytingi page, dashboard tile, XLSX via the existing `Services/ExcelExport.cs` | G1, G4 | yes with G9 | 2 | 3 |
| **G9** | Teacher wallet + weekly job | `TeacherAllowanceJob.cs`, `TeacherCoinSettings` endpoints, teachers-list toggle | G1, G4 | yes with G8 | 3 | 1 |
| **G10** | Mini App — student Coin tab | `miniapp/.../parent/CoinTab.jsx`, `lib/parentApi.js`, `Server/Controllers/TelegramParentController.cs` (additive actions) | G1, G4, G5, G6, G8 | yes with G11 | 2 | 4 |
| **G11** | Mini App — teacher Coin tab | `miniapp/.../teacher/CoinTab.jsx`, `lib/teacherApi.js`, `TelegramTeacherController.cs`, `TeacherPermissions.cs` | G1, G4, G9 | yes with G10 | 1 | 3 |
| **G12** | Tests | `SchoolLms.Tests/Gamification/*` | all | no | 5 | — |

**Naming collision to avoid.** `SchoolLms.Application/Services/RatingService.cs`
already exists and is the *academic* rating (average grade + attendance). The
coin leaderboard must be `CoinRatingQuery`, in the `Gamification` namespace.
Likewise `StudentLedger.cs` is the *money* ledger view — the coin equivalent is
`CoinProfileQuery`.

### 9.2 Effort and what fits in Phase 4's two weeks

| | Backend days | Frontend days |
|---|---|---|
| Total | **37** | **28** |

With one backend and one frontend developer running in parallel that is roughly
**five working weeks**, not the two `docs/SPEC.md` §6 Phase 4 budgets.

The two-week slice, if the budget is fixed, is **G0 → G1 → G2 → G3 → G4 → G10**
(19 B-days, 13 F-days): coins earned automatically from the journal, awarded by
hand with reasons and limits, a full history, and the student's balance,
"why?" list and leaderboard in the Mini App. That is the demo. Shop, auctions,
freeze, rating exports and the teacher wallet (G5–G9, G11) become Phase 4b.

I recommend saying that plainly rather than shipping twelve half-screens.

---

## 10. Open questions

Each carries my recommended decision. **If nobody answers, the recommendation
stands** and gets logged in `docs/ASSUMPTIONS.md`.

**Q1 — Auctions or a fixed-price shop?** (This is `docs/SPEC.md` §8 Q8, still
open.)
→ **Both, because EduSchool has both and they are different products.** The shop
(`/category`, `/product`, `/product-order`) is the everyday sink that keeps
coins circulating; the auction (`/coin-auction`) is the occasional event for the
one bicycle. Building only the shop would leave the module without its
centrepiece; building only auctions gives a child nothing to do on a normal
Tuesday.

**Q2 — May a parent spend the child's coins?**
→ **Yes, and the ledger records who.** `App.jsx` already routes both `student`
and `parent` roles to the same panel, so blocking the parent means a second
panel. Coins are earned by the child but a seven-year-old will hand the phone to
a parent anyway; recording `created_by` makes the truth visible instead of
pretending. Revisit if the school objects.

**Q3 — What does `teacherAutoCoinCoefficient` multiply?**
→ **The number of distinct students the teacher taught in the previous seven
days.** The budget should scale with how many children the teacher can reward.
`teacher_coin_settings.allowance_override` exists so the school can set a flat
number per teacher without waiting for this answer. Ask the client; this is the
one formula I could not recover from the bundle.

**Q4 — What is `/coins/freeze/compliance`?**
→ **A per-teacher weekly distribution report** (granted / awarded / expired /
distribution rate / children reached / freeze state), as specified in §6.4. Only
the path literal survives in the bundle. This reading is the only one the
surrounding fields (`teacherAutoCoinResetToZero`, `autoCoinEnabled`) support.

**Q5 — Do balances reset at the start of an academic year?**
→ **No.** A child who saved all year and lost it in September will not save
again. The `AcademicYearController` rollover leaves `point_transactions`
untouched. If the school wants a fresh leaderboard, that is what the *period*
filter on the rating screen is for.

**Q6 — Should the existing behaviour module (`DisciplinePoint`) feed coins?**
→ **Not in this phase.** Discipline points are a 100-point life bar that goes
down; coins are a currency that goes up and is spent. Merging them makes both
harder to explain to a child. The `grade_coin_rates` row with
`trigger = 'behavior'` already covers the journal's good/bad marks, which is the
90% case. Revisit once both modules have run for a term.

**Q7 — Can coins ever be exchanged for money or for a discount on tuition?**
→ **No, and the schema must make it impossible.** There is no currency column,
no rate, and no endpoint that touches both `point_transactions` and
`ledger_entries`. The moment coins convert to so'm they become a financial
liability, they need to appear in the P&L, and every control in
`docs/SPEC.md` §4 has to be extended to cover them. If the client wants this, it
is a separate project with an accountant in the room.

**Q8 — Do parents get a Telegram notification when their child earns or spends?**
→ **Spends only, and only above a threshold.** A push per grade would be
thirty messages a day. Recommendation: notify on `purchase`, on winning an
auction lot, and on any `penalty` of 10 coins or more. Reuses the existing
`TelegramService` / `FcmService`. Not in the G0–G12 estimate; add half a day to
G10 if approved.

**Q9 — Multi-branch.** EduSchool has `/coins/branch` and
`/coins/analytics/weekly-by-branch`; we removed the Control Plane and are
single-school (`docs/EDUSCHOOL-INVENTORY.md` §4).
→ **Ignore the branch dimension.** No `branch_id` on any gamification table. If a
second branch ever arrives it is a foundation change across the whole schema,
not a gamification one, and adding a dead column now would only make that
migration look done when it is not.

**Q10 — Does the coin unit need a plural / declension in Uzbek?**
→ **No.** `unit_name` is a single string appended after the number
("120 coin"). Uzbek does not decline the counted noun after a numeral.

---

## 11. Definition of Done

Machine-checked, in this order. "It renders" is not done.

**Schema and safety**

- [ ] `dotnet ef database update` applies the single gamification migration on an
      empty database and on a database restored from production.
- [ ] The migration's `Up()` contains **no** `DROP`. (Read it. Autogenerate emits
      spurious drops — the project rule in `CLAUDE.md`.)
- [ ] `git diff --stat SchoolLms.Domain/Entities.cs` is empty.
- [ ] As `app_rw`: `UPDATE point_transactions SET delta = 1` → SQLSTATE **42501**,
      and the row survives. `DELETE` → 42501. As `schoollms_owner`: both succeed.
      (Integration test, mirroring P1-22.)
- [ ] `deploy/init-roles.sql` re-run after the migration reports
      `owns_in_public = 0` for `app_rw`.
- [ ] Every new table appears in `gamification_guards.sql` with the right grant.

**Ledger correctness**

- [ ] Nothing outside `CoinLedgerService` writes `point_transactions`. Proven by
      a test that greps the compiled call graph, or failing that a reviewed
      `grep -rn "PointTransactions.Add"` returning exactly one file.
- [ ] Balance = `SUM(delta)`; a reversal returns the balance to its prior value,
      and both rows remain visible.
- [ ] A row can be reversed at most once (`23505`), and a reversal cannot be
      reversed (trigger raises).
- [ ] No sequence of API calls can drive `available` below zero. Property test:
      1000 random purchase/bid/award operations against one child, assert the
      invariant after each.

**Concurrency** (the tests that would catch a rewrite)

- [ ] 50 parallel `POST /bid` on one lot with valid increasing amounts → exactly
      one `active` bid remains, holds and releases balance exactly, and the sum
      of that student cohort's coins is unchanged from before the auction.
      With the advisory lock stubbed to a no-op, this test must **fail**.
- [ ] 20 parallel saves of the same journal entry → exactly one live award per
      `source_key`, `unique (source_key, source_seq)` never violated.
- [ ] The settlement job and a manual `POST /settle` racing on the same lot →
      one wins, the other gets `409 lot_already_settled`, no double release.
- [ ] Two parallel purchases of the last unit in stock → one `201`, one
      `409 product_out_of_stock`, `stock` lands at 0 and never negative.

**RBAC** (mandatory for every new endpoint — `CLAUDE.md`)

- [ ] A test walks every row of `CoinMatrix.Rules` and asserts the allowed roles
      get 2xx and every other role gets 403.
- [ ] A teacher calling `POST /coins/{id}/reverse` → 403.
- [ ] A parent calling any `/api/tg/parent/children/{otherChildId}/coins*` → 404.
- [ ] A body containing `createdBy` / `awardedBy` → 400.
- [ ] Staff with `gamification` can read every list and write nothing outside
      `catalog.write` and `order.decide`.

**Behaviour**

- [ ] Setting a grade of 5 in the journal credits the seeded 10 coins, visible in
      the Mini App within one refresh.
- [ ] Changing that 5 to a 3 leaves the balance at +1 net, with three visible
      rows: +10, −10 (reversal), +1.
- [ ] Deleting the journal entry leaves the balance at 0 with the history intact.
- [ ] A frozen child receives no automatic award and one `audit_log` line.
- [ ] Turning `is_enabled` off hides the nav section, both journal columns and
      the Mini App tab, and returns 404 from every gamification route.
- [ ] Turning `advanced_mode` off hides screens 6–12 in nav **and** route.

**Build**

- [ ] `dotnet test` passes (`SchoolLms.Tests`).
- [ ] `npm run build` passes in `schoollms.client`.
- [ ] `npm run build` passes in `schoollms.client/src/pages/miniapp/ui-tg`.
- [ ] Every existing test still passes; none was deleted or weakened.

**Documentation**

- [ ] Every decision taken while building that is not in this file is one line in
      `docs/ASSUMPTIONS.md`.
- [ ] Anything a later agent must wire (DI, routes, nav) is in
      `docs/PENDING_WIRING.md`.
- [ ] `docs/SPEC.md` §3.8 is replaced by a pointer to this file — that sketch is
      now superseded and leaving both would give two answers to the same
      question.
