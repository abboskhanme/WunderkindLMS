# Tasks — Phase 1 (Money: billing + cash desk)

**Source of truth:** `docs/SPEC.md` §3.7, §4, §6 (Phase 1), §7, §8
**Planned:** 2026-09-11 · **Budget:** 3 weeks · 1 backend-dev + 1 frontend-dev
**Total:** 25 tasks · 240 h of work · **critical path 112 h** · 17 of 25 parallelizable

> Rule for every agent working this file: the **ID** is the reference key. Other documents,
> commits and PRs cite `P1-xx`, never a task title.

---

## 1. Current state audit

### 1.1 What the finance module does today

A single flat cash book plus a stored student balance. Everything is one category ("tuition"),
every row is mutable, and there is no cashier, no shift, no receipt and no ledger.

| Concern | Where it lives today | Lines |
|---|---|---|
| Money movement (income + expense, one flat table) | `SchoolLms.Domain/Entities.cs` — `FinanceTransaction` | 500–520 |
| Monthly accrual row (one per student per month, single category) | `SchoolLms.Domain/Entities.cs` — `MonthlyCharge` | 485–498 |
| Stored mutable balance, discount on the student row | `SchoolLms.Domain/Entities.cs` — `Student.Balance`, `.DiscountPct`, `.DiscountAmount`, `.DiscountNote` | 105, 112, 114, 116 |
| Class price | `SchoolLms.Domain/Entities.cs` — `SchoolClass.MonthlyFee` | 217 |
| Accrual arithmetic + month helpers | `SchoolLms.Application/Services/TuitionService.cs` | 21–118 |
| Accrual background job (startup + every 12 h) | `SchoolLms.Application/Services/TuitionAccrualService.cs` | 12–32 |
| Per-student month-by-month view + FIFO payment spreading | `SchoolLms.Application/Services/StudentLedger.cs` | 18–88 |
| Teacher salary ledger (reads the same flat table) | `SchoolLms.Application/Services/SalaryLedger.cs` | 47–55 |
| Teacher salary arithmetic | `SchoolLms.Application/Services/TeacherSalaryCalc.cs` | whole file |
| Admin finance API (CRUD + reports + accrue) | `SchoolLms.Server/Controllers/FinanceController.cs` | 32–290 |
| Payment intake (the only "take money" endpoint) | `SchoolLms.Server/Controllers/StudentsController.cs` — `AddPayment` | 643–678 |
| Salary payout | `SchoolLms.Server/Controllers/TeachersController.cs` | 285–309 |
| Audit trail for money | `SchoolLms.Application/Services/AuditService.cs` | 19–59 |
| Access gate (`staff` + `finance` permission) | `SchoolLms.Server/Controllers/AdminPermAttribute.cs` | 29–46 |
| Admin UI | `schoollms.client/src/pages/admin/finance/FinancePage.tsx` (703), `TransactionFormModal.tsx` (379), `TeacherSalaryDetailModal.tsx` (181) | — |
| Frontend API client | `schoollms.client/src/api/services/finance.ts` | 68–264 |
| Frontend types | `schoollms.client/src/types/index.ts` | 348, 402, 424 |

### 1.2 The four holes, measured

1. **Everything is editable.** `FinanceController.Update` (78–111) and `Delete` (199–213) rewrite and
   remove money rows outright. Audit records the act, but the row is gone. This is exactly the fraud
   the client described.
2. **Single category.** `MonthlyCharge` has one `Amount`; there is no bus, dormitory or meals line, and
   no way to split a payment across them (SPEC §3.7 requires it).
3. **Balance is a stored mutable number.** `Student.Balance` is mutated in 6 places
   (`TuitionService.cs:111`, `StudentsController.cs:138,281,296,652`, `ClassesController.cs:98`) and is
   the only source of debt. Any missed update silently corrupts the debt figure.
4. **No cashier identity at all.** `Roles.cs` has no `cashier`; the cash desk is a `staff` user with the
   `finance` permission key (`constants.ts:87`). `AdminPermAttribute` line 42 lets **any** staff user
   read **any** section. There is no shift, no receipt number, no counted cash, no variance.

### 1.3 What survives into the new model

| Existing asset | Verdict |
|---|---|
| `TuitionService.ChargeFor` / `DiscountFor` (21–46) — discount arithmetic | **Keep the arithmetic**, move it into `DiscountService`, add a category dimension |
| `TuitionService.NextMonth` / `MonthRange` (48–67) | **Keep as-is**, reused by the invoice accrual job |
| `TuitionService.AcademicYearStartMonthAsync` (74–80) | **Keep as-is** |
| `StudentLedger` FIFO allocation loop (44–63) | **Keep the algorithm**, but it becomes a *suggestion* for the cashier's split screen; the authoritative split is `payment_allocations` rows |
| `AuditService.Record` (25–46) | **Keep**, extend with `before`/`after` as real `jsonb` (SPEC §4.6) |
| `TelegramService.SendDocumentAsync` (`TelegramService.cs:93`) | **Keep** — receipt delivery to the guardian rides on it, no new integration needed |
| `TelegramRegistration` (`Entities.cs:816`) | **Keep** — already maps student → guardian chat id |
| `AppClock` (`AppClock.cs`) | **Keep** — the single clock, mandatory for `received_at` / shift boundaries |
| `TeacherSalaryCalc` (whole file) | **Keep untouched**; only its *payout* row moves from `finance_transactions` to `expenses` + ledger |
| `TuitionAccrualService` hosted job shape | **Keep the shape**, swap the body for the per-category invoice accrual |

### 1.4 What is replaced

| Existing | Replaced by (SPEC §3.7) |
|---|---|
| `MonthlyCharge` | `fee_categories` + `student_subscriptions` + `discounts` + `invoices` |
| `FinanceTransaction` (income, tuition) | `payments` + `payment_allocations` + `ledger_entries` |
| `FinanceTransaction` (expense, *) | `expenses` + `ledger_entries` |
| `Student.Balance`, `.DiscountPct`, `.DiscountAmount`, `.DiscountNote` | derived: `sum(invoices.amount - invoices.discount) - sum(payment_allocations.amount)`; discounts move to the `discounts` table with `approved_by` |
| `SchoolClass.MonthlyFee` as the price source | `student_subscriptions.monthly_amount` (class fee stays as the *default* when a subscription is created) |
| `FinanceController` PUT + DELETE | reversal-only (`payments.reversal_of`), enforced by `REVOKE` |
| `staff` + `finance` permission for the cash desk | first-class `cashier` role (SPEC §3.1) |

### 1.5 Phase-0 residue that Phase 1 inherits

These are real and they block §4.1. They are handled by **P1-02**.

- `SchoolLms.Server/SchoolLms.Server.csproj:22` still references `Microsoft.EntityFrameworkCore.SqlServer`.
- All four projects are `net8.0`; SPEC §2.1 says the .NET 10 upgrade happens during the Postgres work. It did not.
- `docker-compose.yml:37` connects the app as `schoollms`, which **owns** the database. `REVOKE UPDATE, DELETE`
  on a table has no effect against its owner — **§4.1 is currently unimplementable without a second role.**
- Ids are `text` (`InitialPostgres.cs:19` etc.), not `uuid`; dates are `text`, not `date`. Phase 0 ported
  the old shape to Postgres rather than the clean schema of SPEC §3. **New billing tables use real
  `uuid` / `date` / `timestamptz` / `numeric(14,2)` types and FK to `students(id)` as `text`.** Mixing
  is acceptable and cheap; converting the whole 53-entity schema is not Phase 1 work.
- There is **no test project** anywhere in `SchoolLms.slnx`. SPEC §7 makes tests a mandatory Phase 1
  deliverable, so the harness itself is a task (**P1-03**).

---

## 2. Task board

Legend: `[P]` parallelizable · `[S]` sequential · `★` on the critical path

### Phase 1.A — Day one [parallel, max 3] · start all three on day 1
- [ ] P1-01 Client decision pack — **external wait, start first** — plan-manager — `[P]`
- [ ] P1-02 ★ Split DB roles (`owner` / `app_rw`) + REVOKE bootstrap + Phase-0 residue — ship-devops — `[P]`
- [ ] P1-03 `SchoolLms.Tests` project + Postgres integration harness — test-backend — `[P]`

### Phase 1.B — Schema and frozen contracts [sequential, orchestrator] · shared files, one owner
- [ ] P1-04 ★ Billing entities + `cashier` role + DbContext registration — backend-dev — `[S]`
- [ ] P1-05 ★ EF migration `BillingCore` + database guards + seed — backend-dev — `[S]`
- [ ] P1-06 ★ Frozen contracts: DTOs, TS types, API client stubs, service interfaces, auth attribute — backend-dev — `[S]`
- [ ] P1-07 ★ `LedgerService` — the only code permitted to write money — backend-dev — `[S]`

### Phase 1.C — Billing slices [parallel, max 5] · disjoint new files only
- [ ] P1-08 Fee catalog, subscriptions, discounts (service + controller) — backend-dev — `[P]`
- [ ] P1-09 Invoices + per-category monthly accrual job — backend-dev — `[P]`
- [ ] P1-10 Cash shifts, gapless receipt numbers, Z-report — backend-dev — `[P]`
- [ ] P1-11 ★ Payment intake, split allocation, reversal — backend-dev — `[P]`
- [ ] P1-12 Receipt: PDF render + Telegram delivery — backend-dev — `[P]`

### Phase 1.D — Reports and detection [parallel, max 2] · may overlap Phase 1.C once P1-07 lands
- [ ] P1-13 Debtor report, income dynamics, first P&L / Cash Flow — backend-dev — `[P]`
- [ ] P1-14 Billing audit (`before`/`after` jsonb) + nightly anomaly scan — backend-dev — `[P]`

### Phase 1.E — Backend wiring [sequential, orchestrator]
- [ ] P1-15 ★ `Program.cs`: DI, hosted services, `app_rw` connection string — backend-dev — `[S]`

### Phase 1.F — Frontend [parallel, max 4] · one page tree each
- [ ] P1-16 Cashier workspace — frontend-dev — `[P]`
- [ ] P1-17 Admin billing catalog (categories, subscriptions, discounts) — frontend-dev — `[P]`
- [ ] P1-18 ★ Director finance dashboard (rebuild of `FinancePage`) — frontend-dev — `[P]`
- [ ] P1-19 Student / parent finance view — frontend-dev — `[P]`

### Phase 1.G — Frontend wiring [sequential, orchestrator] · route + nav + permission registry
- [ ] P1-20 ★ `App.tsx` routes, `navigation.ts`, `constants.ts` perms, cashier landing — frontend-dev — `[S]`

### Phase 1.H — Legacy retirement [sequential] · touches 12 call sites across the backend
- [ ] P1-21 ★ Retire `MonthlyCharge` / `FinanceTransaction` / `Student.Balance` + demo data reset — backend-dev — `[S]`

### Phase 1.I — Tests [parallel, max 3]
- [ ] P1-22 ★ Financial security suite: REVOKE, immutability, RBAC matrix, identity — test-backend — `[P]`
- [ ] P1-23 Billing arithmetic unit tests — test-backend — `[P]`
- [ ] P1-24 Cash shift, receipt integrity, Z-report, variance tests — test-backend — `[P]`

### Phase 1.J — Gate [sequential]
- [ ] P1-25 ★ Phase 1 acceptance walkthrough + `qa-security` pass — qa-review — `[S]`

### Blocked
- P1-09 partial — `due_on` rule and grace period unknown → waiting on client Q6 (P1-01)
- P1-08 partial — discount approval threshold unknown → waiting on client Q5 (P1-01)
- P1-12 partial — PDF-only vs thermal printer unknown → waiting on client Q3 (P1-01)
- P1-21 partial — opening debt per student as of go-live → waiting on client Q17 (P1-01)

---

## 3. Task detail

---

### P1-01 — Client decision pack
**Owner:** plan-manager · **Depends on:** — · **Parallelizable:** yes · **Estimate:** 2 h work, **3–14 days wait**

Send the client the question list in §6 of this document as one message, numbered, each with a
recommended default so silence still produces a decision. Record every answer in `docs/SPEC.md` §8
and every unanswered-but-assumed item in `docs/ASSUMPTIONS.md`.

**Files touched:** `docs/SPEC.md`, `docs/ASSUMPTIONS.md`

**Acceptance criteria**
- All 11 questions in §6 have either a written client answer or a logged default in `docs/ASSUMPTIONS.md`.
- Q3, Q5, Q6, Q17 answers exist before P1-09 / P1-08 / P1-12 / P1-21 reach their blocked step.
- Q17 (opening debt per student) arrives as a file (Excel/CSV) with columns `student, category, amount as of date`.

> **This task starts on day one and waits in parallel with everything else.** Nothing downstream
> stops while it waits — every blocked item has a working default.

---

### P1-02 — Split DB roles + REVOKE bootstrap + Phase-0 residue
**Owner:** ship-devops · **Depends on:** — · **Parallelizable:** yes · **Estimate:** 6 h · **★ critical path**

SPEC §4.1 is the whole point of Phase 1 and it **cannot work today**: the app connects as the database
owner, and `REVOKE` is a no-op against an owner. Create two roles, move the app to the weak one, keep
migrations on the strong one.

**Files touched**
- `docker-compose.yml`, `docker-compose.local.yml`, `docker-compose.server.yml`
- `deploy/README.md`, new `deploy/init-roles.sql`
- `SchoolLms.Server/appsettings*.json` (add `ConnectionStrings:Migrator`)
- `SchoolLms.Server/SchoolLms.Server.csproj` (drop the `Microsoft.EntityFrameworkCore.SqlServer` reference, line 22)

**Acceptance criteria**
- `schoollms_owner` owns the schema; `app_rw` is `LOGIN`, `NOINHERIT`, owns nothing.
- `psql -U app_rw -c "update payments set amount = 1"` fails with SQLSTATE **42501** (integration-tested in P1-22).
- `db.Database.Migrate()` runs on `ConnectionStrings:Migrator`; all request-path DbContexts use `ConnectionStrings:Default` = `app_rw`.
- `dotnet build` produces zero references to `Microsoft.EntityFrameworkCore.SqlServer` (`dotnet list package | grep -c SqlServer` returns 0).
- A written go/no-go note on the .NET 10 upgrade (SPEC §2.1) lands in `docs/ASSUMPTIONS.md` — **recommendation: defer to after Phase 1**, because a runtime upgrade in the same three weeks as the money module doubles the blast radius of any failure.

---

### P1-03 — Test project + Postgres integration harness
**Owner:** test-backend · **Depends on:** — · **Parallelizable:** yes · **Estimate:** 6 h

There is no test project in the solution. SPEC §7 makes billing tests mandatory, so the harness is
built before there is anything to test.

**Files touched:** new `SchoolLms.Tests/SchoolLms.Tests.csproj`, `SchoolLms.Tests/Fixtures/PostgresFixture.cs`, `SchoolLms.Tests/Fixtures/ApiFactory.cs`, `SchoolLms.slnx`

**Acceptance criteria**
- `dotnet test` runs green from a clean clone with Docker available.
- `PostgresFixture` starts a throwaway Postgres 17 container, applies migrations as the owner role, and exposes **two** connection strings (owner and `app_rw`) — P1-22 needs both.
- `ApiFactory` issues a JWT for any of `superadmin | admin | cashier | staff | teacher` in one line.
- Test run under 90 s on the developer machine.

---

### P1-04 — Billing entities + `cashier` role + DbContext registration
**Owner:** backend-dev · **Depends on:** P1-02 · **Parallelizable:** no · **Estimate:** 10 h · **★ critical path**

**Deliberate decision:** the new entities go into a **new file**, `SchoolLms.Domain/Billing.cs`, not into
the 1077-line `Entities.cs`. `Entities.cs` is the single worst merge-conflict surface in this repo and
four agents will be writing billing code at once in Phase 1.C.

**Files touched**
- new `SchoolLms.Domain/Billing.cs` — `FeeCategory`, `StudentSubscription`, `Discount`, `Invoice`, `Payment`, `PaymentAllocation`, `CashShift`, `Expense`, `LedgerEntry`
- `SchoolLms.Domain/Roles.cs` — add `Cashier = "cashier"`, add `CashierOrAdmin`, `FinanceStaff` groupings
- `SchoolLms.Application/Abstractions/IAppDbContext.cs` — 9 new `DbSet`s
- `SchoolLms.Infrastructure/Data/AppDbContext.cs` — `DbSet`s + `OnModelCreating` (precision, unique indexes, FKs)

**Acceptance criteria**
- Every money property maps to `numeric(14,2)` (`HasPrecision(14,2)`), verified in the generated snapshot — **not** the legacy `(18,2)`.
- `unique (student_id, category_id, period_month)` on `invoices`, `unique (cash_shift_id, receipt_no)` on `payments` present in the model snapshot.
- `payment_allocations.payment_id` and `.invoice_id` are `DeleteBehavior.Restrict`.
- `Roles.Cashier` compiles into `AuthController` token issuance without a new `if` chain (role is data, not a branch).
- `dotnet build` green; **no existing entity modified** (`git diff --stat SchoolLms.Domain/Entities.cs` is empty).

---

### P1-05 — Migration `BillingCore` + database guards + seed
**Owner:** backend-dev · **Depends on:** P1-04 · **Parallelizable:** no · **Estimate:** 12 h · **★ critical path**

Additive only. The legacy tables stay alive until P1-21, so the app keeps working throughout.

**Files touched:** new `SchoolLms.Infrastructure/Migrations/<ts>_BillingCore.cs`, `AppDbContextModelSnapshot.cs`, new `SchoolLms.Infrastructure/Migrations/Sql/billing_guards.sql`

The migration contains raw SQL that EF cannot express:
- `check_allocation_total()` trigger, verbatim from SPEC §3.7
- `cash_shifts.variance` as `generated always as (counted_cash - expected_cash) stored`
- `check (approved_by is null or approved_by <> created_by)` on `discounts` and `expenses` (SPEC §4.5)
- `REVOKE update, delete on payments, payment_allocations, ledger_entries from app_rw`
- `grant select, insert on ...` to `app_rw` for the same tables
- `create index ledger_entries_date_account on ledger_entries (entry_date, account)`

**Acceptance criteria**
- `dotnet ef database update` from an empty database succeeds; `dotnet ef migrations script` is read line by line and contains **zero `DROP`** statements (global rule: autogenerate emits spurious drops).
- Re-running `upgrade head` on an already-migrated database is a no-op.
- Inserting allocations totalling more than `payments.amount` raises `Allocation exceeds payment amount`.
- `insert into discounts (created_by, approved_by) values (X, X)` is rejected by the check constraint.
- Seed creates the five categories from SPEC §3.7 (`tuition`, `bus`, `dormitory`, `meals`, `other`) and is idempotent.

---

### P1-06 — Frozen contracts: DTOs, TS types, API client stubs, service interfaces, auth attribute
**Owner:** backend-dev (frontend-dev reviews) · **Depends on:** P1-04 · **Parallelizable:** no · **Estimate:** 10 h · **★ critical path**

This task exists **solely so Phase 1.C and Phase 1.F can be parallel.** Every shared-surface file is
edited here, once, by one person. After this task no parallel agent touches these files.

**Files touched**
- `SchoolLms.Application/Dtos/Dtos.cs` — billing DTOs (shared file, 527+ lines)
- `schoollms.client/src/types/index.ts` — matching TS interfaces (shared file)
- new `schoollms.client/src/api/services/billing.ts`, `cashShifts.ts`, `payments.ts` — typed stubs that throw `NotImplemented`
- new `SchoolLms.Application/Billing/IBillingServices.cs` — `ILedgerService`, `IInvoiceService`, `ICashShiftService`, `IPaymentService`, `IDiscountService`, `IReceiptService`
- new `SchoolLms.Server/Controllers/FinanceRoleAttribute.cs` — the §4.3 role gate

**Acceptance criteria**
- The six service interfaces are signature-complete; Phase 1.C tasks only add implementations, never change a signature. A signature change after this task requires a note in `docs/ASSUMPTIONS.md` and a ping to every 1.C owner.
- `npm run build` passes with the new types and stub clients wired but unused.
- `FinanceRoleAttribute` encodes the §4.3 table as data (a `record` per action), not as scattered `if`s, so P1-22 can assert the whole matrix in a loop.
- `cashier_id`, `created_by`, `approved_by` are **absent from every request DTO** — grep proves it. They come from JWT claims only (SPEC §4.4).

---

### P1-07 — `LedgerService` — the only code permitted to write money
**Owner:** backend-dev · **Depends on:** P1-05, P1-06 · **Parallelizable:** no · **Estimate:** 10 h · **★ critical path**

SPEC §2.2: financial writes never go through a generic repository. This is that chokepoint.

**Files touched:** new `SchoolLms.Application/Billing/LedgerService.cs`, new `SchoolLms.Application/Billing/Accounts.cs`

**Acceptance criteria**
- `PostAsync(entries, actorId, ct)` accepts a balanced set only; an unbalanced set (debits ≠ credits) throws before any `SaveChanges`.
- `ReverseAsync(entryId, reason, approverId)` inserts mirrored entries with `reversal_of` set; it never updates the original.
- The service takes `IAppDbContext` and exposes **no** method that updates or deletes a ledger row.
- Account codes are a closed list in `Accounts.cs` (`cash`, `bank`, `receivable`, `revenue:tuition`, `revenue:bus`, `revenue:dormitory`, `revenue:meals`, `revenue:other`, `expense:salary`, `expense:other`); an unknown code throws.
- A unit test posts a 500 000 payment and asserts exactly two rows: `debit cash 500000`, `credit receivable 500000`.

---

### P1-08 — Fee catalog, subscriptions, discounts
**Owner:** backend-dev · **Depends on:** P1-06, P1-07 · **Parallelizable:** yes · **Estimate:** 10 h

**Files touched:** new `SchoolLms.Application/Billing/SubscriptionService.cs`, `DiscountService.cs`, new `SchoolLms.Server/Controllers/BillingCatalogController.cs`

**Acceptance criteria**
- `POST /api/admin/billing/subscriptions` creates a subscription; a second active subscription for the same `(student, category)` with overlapping dates is rejected with 409.
- When a student's first subscription is created and the category is `tuition`, the default `monthly_amount` is pre-filled from `SchoolClass.MonthlyFee` (continuity with today's behaviour).
- `POST /api/admin/billing/discounts` with `percent` or `amount` above the configured threshold returns `409 approval_required` and stores the row with `approved_by = null`, `status` pending.
- `POST /api/admin/billing/discounts/{id}/approve` by the **same** user who created it returns 403 (dual control, §4.5) — and additionally fails at the DB check constraint if it somehow gets through.
- A `cashier` calling any write endpoint here gets 403 (§4.3 row "Change monthly fee", "Grant a discount").
- The discount arithmetic is the existing `TuitionService.ChargeFor` (percent first, then amount, floor 0), moved not rewritten — verified by P1-23 against the same inputs as today.

**Blocked step:** the threshold value needs client Q5. Default until then: **20 % or 500 000 so'm**, logged in `docs/ASSUMPTIONS.md`.

---

### P1-09 — Invoices + per-category monthly accrual
**Owner:** backend-dev · **Depends on:** P1-06, P1-07 · **Parallelizable:** yes · **Estimate:** 12 h

Replaces `TuitionService.AccrueMonth`. One invoice per student **per category** per month.

**Files touched:** new `SchoolLms.Application/Billing/InvoiceService.cs`, new `SchoolLms.Application/Billing/BillingAccrualService.cs` (hosted), reuses `TuitionService.MonthRange` / `NextMonth`

**Acceptance criteria**
- Accrual for a month is **idempotent**: running it twice produces the same row count (guaranteed by `unique (student_id, category_id, period_month)`, not by an `already` set in memory).
- A student with `tuition` + `bus` subscriptions produces exactly 2 invoices for the month.
- Archived students (`Student.IsArchived`) get no invoice.
- A subscription starting mid-month produces an invoice per the Q12 rule (default: **full month**, logged).
- Every invoice insert posts `debit receivable / credit revenue:<category>` through `LedgerService` — an invoice with no matching ledger pair is impossible.
- `status` moves `open → partial → paid` from allocation totals only; nothing sets it directly.
- The job runs on startup and every 12 h, same shape as `TuitionAccrualService`, and logs the month list it wrote.

**Blocked step:** `due_on` day and grace period need client Q6. Default: **due on the 10th, overdue after the 15th**, logged.

---

### P1-10 — Cash shifts, gapless receipt numbers, Z-report
**Owner:** backend-dev · **Depends on:** P1-06, P1-07 · **Parallelizable:** yes · **Estimate:** 12 h

**Files touched:** new `SchoolLms.Application/Billing/CashShiftService.cs`, new `SchoolLms.Server/Controllers/CashShiftsController.cs`

**Acceptance criteria**
- `POST /api/cash/shifts/open` by a cashier who already has an open shift returns 409 (§4.2).
- `POST /api/cash/shifts/{id}/close` requires `countedCash` in the body; without it → 400.
- `expected_cash` is computed from `ledger_entries` (`debit cash` within the shift) at close time, never supplied by the client.
- `variance` is never written by the application (it is a generated column); an attempt to set it fails.
- Closing another cashier's shift returns 403; closing as admin/superadmin is allowed and records `closed_by`.
- `NextReceiptNoAsync(shiftId)` is gapless per shift under 50 concurrent callers — proven by the concurrency test in P1-24, using either a per-shift advisory lock or insert-retry on the unique violation.
- `GET /api/cash/shifts/{id}/z-report` returns opening float, receipts grouped by `method`, expected, counted, variance.

**Blocked step:** opening float policy needs client Q11. Default: **0**, cashier may enter a float, logged.

---

### P1-11 — Payment intake, split allocation, reversal
**Owner:** backend-dev · **Depends on:** P1-06, P1-07 · **Parallelizable:** yes · **Estimate:** 14 h · **★ critical path**

The task the client actually paid for.

**Files touched:** new `SchoolLms.Application/Billing/PaymentService.cs`, new `SchoolLms.Server/Controllers/PaymentsController.cs`

**Acceptance criteria**
- `POST /api/cash/payments` with `{ studentId, amount, method, allocations: [{invoiceId, amount}] }` writes, **in one transaction**: 1 `payments` row, N `payment_allocations` rows, 2 `ledger_entries` rows (`debit cash|bank / credit receivable`), and updates the touched invoices' `status`.
- `cash_shift_id` comes from the caller's open shift; no open shift → **409 `no_open_shift`**, and no money row is written.
- `cashier_id` is taken from the JWT. If the request body contains `cashierId` the request is rejected with **400**, not silently ignored (§4.4).
- Allocations totalling more than `amount` → 400 from the service **and** blocked by the DB trigger if the service is bypassed (both paths tested in P1-22).
- Unallocated remainder is allowed and shows as student credit; it is never written to a mutable balance column.
- **There is no `PUT` and no `DELETE` on this controller.** `grep -c "HttpPut\|HttpDelete" PaymentsController.cs` returns 0.
- `POST /api/admin/payments/{id}/reverse` requires `admin` or `superadmin`, requires a non-empty `reason`, and inserts a new payment row with `reversal_of` set plus mirrored ledger entries. A cashier gets 403.
- Reversing an already-reversed payment returns 409.
- A split across `tuition` + `bus` in one payment produces one receipt number and two allocation rows — this is the SPEC §6 "done when" sentence, tested end to end.

---

### P1-12 — Receipt: PDF render + Telegram delivery
**Owner:** backend-dev · **Depends on:** P1-06 · **Parallelizable:** yes · **Estimate:** 12 h

**Files touched:** new `SchoolLms.Application/Billing/ReceiptService.cs`, new `SchoolLms.Application/Billing/ReceiptDocument.cs`, `SchoolLms.Application/SchoolLms.Application.csproj` (PDF package), new `SchoolLms.Server/Controllers/ReceiptsController.cs`

**Acceptance criteria**
- `GET /api/receipts/{paymentId}.pdf` returns a PDF containing: school name, receipt no, date/time (`AppClock`), student, **each allocated category on its own line**, method, total, cashier name.
- The same receipt is pushed to the guardian's Telegram chat via the existing `TelegramService.SendDocumentAsync`, resolved through `TelegramRegistration.StudentId → ChatId`. Delivery failure is logged and retried once; **it never rolls back the payment**.
- A guardian with no linked Telegram produces a warning log, not an error response.
- Rendering a receipt for a reversed payment stamps it `BEKOR QILINGAN` (Uzbek, client-facing) with the reversal date.
- PDF engine decision recorded in `docs/ASSUMPTIONS.md`. **Recommendation: QuestPDF Community** — free under 1 M USD annual revenue, which this school is. If a commercial licence turns out to be required, that is money spent → stop and ask the user.

**Blocked step:** thermal printer vs PDF-only needs client Q3. Default: **PDF + Telegram only**; an ESC/POS path is a Phase 5 add-on, not Phase 1.

---

### P1-13 — Debtor report, income dynamics, first P&L / Cash Flow
**Owner:** backend-dev · **Depends on:** P1-07 · **Parallelizable:** yes · **Estimate:** 10 h

**Files touched:** new `SchoolLms.Application/Billing/FinanceReportQueries.cs`, new `SchoolLms.Server/Controllers/FinanceReportsController.cs`

**Acceptance criteria**
- `GET /api/admin/finance/debtors` returns one row per student with per-category breakdown, computed **only** from `invoices` and `payment_allocations` — the query contains no reference to `students.balance`.
- `GET /api/admin/finance/pnl?from&to` sums `ledger_entries` by account prefix (`revenue:*` vs `expense:*`); revenue − expense equals the same figure computed independently in the test.
- `GET /api/admin/finance/cashflow?from&to` reports `cash` and `bank` account movement per month.
- `GET /api/admin/finance/collection-rate` returns invoiced vs collected per month.
- A `cashier` calling any of these gets 403 (§4.3 "See variance report across cashiers").
- All four endpoints answer in under 500 ms against 500 students × 10 months of seed data (SPEC §7).

---

### P1-26 — Money-flow visualisation (client request, 2026-09-11)

**Owner:** build-frontend + backend-dev · **Depends on:** P1-07 · **Parallelizable:** yes · **Estimate:** 14 h

A new sub-section of the finance module showing the **whole school's money movement** —
turnover, income, expenses — as a continuously rotating 3D ring with particles flowing
along the links. The client supplied an Alphabet quarterly Sankey diagram as the reference
for the *data structure*; the requested *presentation* is radial and animated, not a
static Sankey.

**Data shape** — a directed graph derived entirely from `ledger_entries`:

```
income sources            →  total turnover  →  outflows
  revenue:tuition                                 expense:salary
  revenue:bus                                     expense:utilities
  revenue:dormitory            (hub node)         expense:supplies
  revenue:meals                                   expense:rent
  revenue:donation                                expense:repair
  revenue:other                                   expense:other
                                                  net:retained   (turnover − expenses)
```

**Files touched**
- new `SchoolLms.Application/Billing/MoneyFlowQueries.cs`
- new `SchoolLms.Server/Controllers/MoneyFlowController.cs`
- new `schoollms.client/src/pages/admin/finance/MoneyFlowPage.tsx`
- new `schoollms.client/src/components/charts/MoneyFlowRing.tsx`
- `schoollms.client/src/App.tsx`, `src/config/navigation.ts` (route + menu entry)

**Acceptance criteria**
- `GET /api/admin/finance/money-flow?from&to` returns `{ nodes: [{id, label, kind, value}], links: [{source, target, value}] }`, computed **only** from `ledger_entries` — no reference to `students.balance` or `finance_transactions`.
- Node `kind` is one of `income | hub | expense | net`; the UI colours by kind, never by hard-coded id.
- Sum of income link values equals the hub value equals the sum of outgoing link values, to the cent. A mismatch is a bug, not a rounding note — asserted in a test.
- `cashier` → 403; `admin` / `superadmin` → 200 (§4.3).
- The ring renders at 60 fps on an integrated GPU with 7 income nodes, 6 expense nodes and ~2 000 particles; it degrades to a static ring (no particles) when `prefers-reduced-motion` is set.
- Empty period (no ledger rows) shows an explicit empty state, not a blank canvas.
- The 3D library is lazy-loaded: the finance page bundle must not grow for users who never open this sub-section.

**Presentation requirements (client, non-negotiable)**
- Nodes sit on a **ring in 3D space**, income on one arc, expenses on the opposite arc, turnover hub at the centre.
- Links are curved tubes; **particles travel along them continuously**, density and speed proportional to the amount.
- The whole scene **rotates slowly and continuously** without user input.
- Amounts are labelled in so'm with thousands separators; hovering a node or link shows the exact figure.

---

### P1-14 — Billing audit + nightly anomaly scan
**Owner:** backend-dev · **Depends on:** P1-07 · **Parallelizable:** yes · **Estimate:** 8 h

**Files touched:** `SchoolLms.Application/Services/AuditService.cs` (extend, do not rewrite), new `SchoolLms.Application/Billing/AnomalyScanService.cs` (hosted), new `SchoolLms.Application/Billing/Anomaly.cs`

**Acceptance criteria**
- Every write through `LedgerService`, `PaymentService`, `DiscountService` and the expense path lands an `audit_log` row with `before`/`after` serialized as **jsonb** (§4.6).
- The nightly job flags all four SPEC §4.6 conditions: shift closed with non-zero variance; reversal within 24 h of the original; payment recorded outside the cashier's normal hours; invoice marked `paid` with no allocations.
- Each flag is a persisted row with a `resolved_at` / `resolved_reason` pair; **resolving requires a written reason** and the flag cannot be deleted.
- `GET /api/admin/finance/flags?unresolved=true` returns the counter the director's dashboard renders.
- Seeding a deliberate 50 000 variance produces exactly one flag on the next scan.

---

### P1-15 — `Program.cs` wiring
**Owner:** backend-dev · **Depends on:** P1-08, P1-09, P1-10, P1-11, P1-12, P1-13, P1-14 · **Parallelizable:** no · **Estimate:** 4 h · **★ critical path**

Its own phase because `Program.cs` is a single shared file and every Phase 1.C/1.D task wants a line in it.

**Files touched:** `SchoolLms.Server/Program.cs` only

**Acceptance criteria**
- All seven new services registered `Scoped`; `BillingAccrualService` and `AnomalyScanService` registered as hosted services.
- `TuitionAccrualService` registration (line 172) removed — the old accrual no longer runs alongside the new one. Running both would double-bill.
- `ConnectionStrings:Default` (runtime) is `app_rw`; the startup `db.Database.Migrate()` block uses `ConnectionStrings:Migrator`.
- App starts clean against a fresh database; `/api/health` returns 200.

---

### P1-16 — Cashier workspace
**Owner:** frontend-dev · **Depends on:** P1-06 · **Parallelizable:** yes · **Estimate:** 16 h

A dedicated, deliberately narrow screen. The cashier sees a search box, a student's open invoices,
an amount field, a split editor and a print button. Nothing else.

**Files touched:** new `schoollms.client/src/pages/cashier/CashierPage.tsx`, `ShiftBar.tsx`, `PaymentSplitModal.tsx`, `ReceiptPreview.tsx`

**Acceptance criteria**
- The page refuses to render the payment form when no shift is open; it shows "Smenani oching" and the open-shift button instead.
- The split editor pre-fills allocations oldest-invoice-first (the existing `StudentLedger` FIFO logic, ported to TS) and lets the cashier override any line; the remainder counter never goes negative.
- There is **no edit and no delete control anywhere on this page** — `grep -i "delete\|o'chir" pages/cashier/` returns nothing outside a comment.
- Close-shift dialog requires counted cash before the button enables, and shows the computed variance **after** submission, never before (so the cashier cannot tune the count to the expected figure).
- `npm run build` passes; the page renders in Uzbek.

---

### P1-17 — Admin billing catalog
**Owner:** frontend-dev · **Depends on:** P1-06 · **Parallelizable:** yes · **Estimate:** 12 h

**Files touched:** new `schoollms.client/src/pages/admin/billing/CategoriesPage.tsx`, `SubscriptionsPage.tsx`, `DiscountsPage.tsx`

**Acceptance criteria**
- A student's subscriptions are listed per category with amount, detail (bus route / room), start and end.
- Creating a discount above the threshold shows the "needs a second approver" state and the row appears in a pending queue.
- The approve button is hidden for the user who created the discount.
- A user with role `cashier` never reaches these pages (guarded in P1-20, asserted here by the page's own role check).

---

### P1-18 — Director finance dashboard
**Owner:** frontend-dev · **Depends on:** P1-06 · **Parallelizable:** yes · **Estimate:** 14 h · **★ critical path**

Rebuild of `FinancePage.tsx` (703 lines) on the new endpoints. Work additively: build the new tabs
alongside, delete the old transaction CRUD tab **in P1-21**, not here.

**Files touched:** `schoollms.client/src/pages/admin/finance/FinancePage.tsx`, new `ZReportTab.tsx`, `VarianceTab.tsx`, `DebtorsTab.tsx`, `PnlTab.tsx`

**Acceptance criteria**
- Unresolved-variance counter is visible on load and **cannot be dismissed** — only resolved with a typed reason (§4.6).
- Z-report tab lists today's shifts per cashier with expected / counted / variance, variance rows highlighted.
- Debtors tab shows per-category debt and sorts by total debt descending.
- The transaction "Tahrirlash" and "O'chirish" buttons (current `FinancePage.tsx`) are gone from the payments view; a reversal button in their place, visible only to admin/superadmin.
- `npm run build` passes.

---

### P1-19 — Student / parent finance view
**Owner:** frontend-dev · **Depends on:** P1-06 · **Parallelizable:** yes · **Estimate:** 10 h

**Files touched:** `schoollms.client/src/pages/admin/students/StudentDetailPage.tsx` (finance tab), new `schoollms.client/src/pages/portal/FinanceView.tsx`

**Acceptance criteria**
- Invoices are listed per month **per category**, each with amount, discount, paid and remaining.
- Each payment row links to its receipt PDF.
- A reversed payment renders struck through with its reversal reason visible.
- The parent-facing copy is in Uzbek and shows no internal ids.

---

### P1-20 — Route, nav and permission registry
**Owner:** frontend-dev · **Depends on:** P1-16, P1-17, P1-18, P1-19 · **Parallelizable:** no · **Estimate:** 4 h · **★ critical path**

Its own sequential phase: four shared registry files that every Phase 1.F task would otherwise fight over.

**Files touched:** `schoollms.client/src/App.tsx`, `src/config/navigation.ts`, `src/config/constants.ts`, `src/components/auth/ProtectedRoute.tsx`, `src/context/AuthProvider.tsx`

**Acceptance criteria**
- `/cashier` route exists and is reachable **only** by role `cashier`, `admin`, `superadmin`.
- A user with role `cashier` who logs in lands on `/cashier`, not `/admin` (`RootRedirect`).
- `adminPermissions` in `constants.ts` gains `billing` and `cashShifts`; the `finance` key keeps its meaning for the director view.
- Nav shows the cashier exactly one item.
- `npm run build` passes and no route is registered twice.

---

### P1-21 — Retire the legacy finance path + demo data reset
**Owner:** backend-dev · **Depends on:** P1-15, P1-20 · **Parallelizable:** no · **Estimate:** 12 h · **★ critical path**

The single most dangerous task in the phase: 12 call sites across 9 files. Sequential, one owner, one commit.

**Files touched**
- `SchoolLms.Server/Controllers/FinanceController.cs` — delete `Update` (78–111), `Delete` (199–213), `Create` (49–76), `Accrue` (250–267); keep the read endpoints until P1-18 replaces them
- `SchoolLms.Server/Controllers/StudentsController.cs` — `AddPayment` (643–678) → 410 Gone pointing at `/api/cash/payments`; accrual blocks at 120–141 and 258–300 deleted; 328 charge cleanup deleted
- `SchoolLms.Server/Controllers/ClassesController.cs:75–105` — fee-change re-accrual deleted (subscriptions own the price now)
- `SchoolLms.Server/Controllers/TeachersController.cs:285–320` — salary payout → `expenses` + `expense:salary` ledger entry
- `SchoolLms.Server/Controllers/SalaryRatesController.cs:101`, `SchoolLms.Application/Services/SalaryLedger.cs:47` — read `expenses`, not `finance_transactions`
- `SchoolLms.Server/Controllers/MessagesController.cs:132,172,202,243` — `{balans}` / `{qarz}` tokens via a new `StudentBalanceQuery`
- `SchoolLms.Server/Controllers/AcademicYearController.cs:224–225,263–264,325–327` — archive/reset over the new tables
- `SchoolLms.Server/Controllers/StudentPortalController.cs:681,917–922`, `TeacherPortalController.cs:390`, `AttendanceController.cs:21`, `Analytics.cs:18`, `StudentProfileBuilder.cs:142` — `Balance` → `StudentBalanceQuery`
- `SchoolLms.Application/Services/StudentLedger.cs` — rebuilt on invoices
- new migration `<ts>_RetireLegacyFinance.cs` — drop `monthly_charges`, `finance_transactions`, `students.balance`, `students.discount_pct`, `students.discount_amount`, `students.discount_note`
- `tools/seed_demo.py` — seed subscriptions, invoices, payments, shifts

**Acceptance criteria**
- `grep -rn "FinanceTransactions\|MonthlyCharges\|\.Balance" --include=*.cs SchoolLms.*` returns **zero** hits outside `Migrations/`.
- The drop migration is read line by line before it is applied; the migration script is pasted into the PR.
- `dotnet build` green, `dotnet test` green, `npm run build` green.
- Demo money data is wiped and reseeded; every screen that showed a balance still shows the same number, now derived.
- **No data migration is written** — see §4.

---

### P1-22 — Financial security suite
**Owner:** test-backend · **Depends on:** P1-11, P1-15 · **Parallelizable:** yes · **Estimate:** 8 h · **★ critical path**

This suite is the deliverable that makes SPEC §4 real rather than documented.

**Files touched:** new `SchoolLms.Tests/Security/LedgerImmutabilityTests.cs`, `RbacMatrixTests.cs`, `IdentityBindingTests.cs`

**Acceptance criteria**
- Connected as `app_rw`, each of `update payments`, `delete from payments`, `update payment_allocations`, `delete from payment_allocations`, `update ledger_entries`, `delete from ledger_entries` raises **42501**. Six explicit assertions, not a loop over a list that could silently be empty.
- Connected as the owner role, the same statements succeed — proving the test is actually exercising the grant and not a broken connection.
- The §4.3 table is asserted **row by row**: 8 actions × 3 roles = 24 assertions with the expected status code.
- Posting a payment with `cashierId` in the body returns 400; the stored `cashier_id` always equals the JWT subject.
- Bypassing `PaymentService` and inserting over-allocated rows directly still fails on the trigger.
- A cashier attempting `POST /reverse` gets 403; an admin reversing their own reversal gets 409.

---

### P1-23 — Billing arithmetic unit tests
**Owner:** test-backend · **Depends on:** P1-08, P1-09 · **Parallelizable:** yes · **Estimate:** 8 h

**Files touched:** new `SchoolLms.Tests/Billing/DiscountMathTests.cs`, `AllocationTests.cs`, `AccrualTests.cs`

**Acceptance criteria**
- Discount: percent-then-amount order, 100 % discount → 0, over-100 % → 0 (never negative), rounding to 2 decimals — the same input/expected pairs the current `TuitionService` satisfies, so the port is provably behaviour-preserving.
- Allocation: FIFO across three open invoices; exact-fit; overpayment leaves credit; underpayment marks `partial`.
- Accrual: two categories → two invoices; run twice → still two; archived student → zero; subscription ending mid-month → per the Q12 rule.
- Ledger: every generated invoice and payment pair balances (`sum(debit) == sum(credit)`) for a 200-operation randomised scenario.

---

### P1-24 — Cash shift and receipt integrity tests
**Owner:** test-backend · **Depends on:** P1-10 · **Parallelizable:** yes · **Estimate:** 8 h

**Files touched:** new `SchoolLms.Tests/Billing/CashShiftTests.cs`, `ReceiptNumberingTests.cs`

**Acceptance criteria**
- 50 concurrent payments into one shift produce receipt numbers `1..50` with **no gaps and no duplicates**.
- Second open shift for the same cashier → 409.
- Closing another cashier's shift → 403.
- `variance` equals `counted − expected` and cannot be written directly (an explicit `UPDATE` attempt fails).
- Z-report totals equal the sum of that shift's payments grouped by method.

---

### P1-25 — Phase 1 acceptance gate
**Owner:** qa-review (+ qa-security) · **Depends on:** P1-21, P1-22, P1-23, P1-24 · **Parallelizable:** no · **Estimate:** 8 h · **★ critical path**

**Acceptance criteria** — the SPEC §6 Phase 1 sentence, executed manually and recorded:
- A cashier opens a shift, takes one payment split across `tuition` + `bus`, prints the receipt, and the guardian's Telegram receives the PDF.
- The same cashier then **cannot** alter or delete that payment: no UI control, no endpoint, and a direct SQL attempt as `app_rw` fails.
- The director's dashboard shows that day's variance.
- `dotnet test` green · `npm run build` green · `dotnet ef database update` from empty green.
- `qa-security` sign-off on §4.1–§4.7.
- `docs/SPEC.md` §8 updated with every client answer received.

---

## 4. Migration strategy

**The decisive fact: there is no real data.** `monthly_charges` and `finance_transactions` contain only
demo rows generated by `tools/seed_demo.py`. That removes the entire class of work this phase would
otherwise carry — no backfill, no reconciliation, no dual-write window, no rollback data plan.

**Consequence: the legacy money data is deleted, not migrated.**

### 4.1 Two migrations, in this order

1. **`BillingCore` (P1-05) — purely additive.** Creates the 9 new tables, the trigger, the generated
   column, the check constraints and the `REVOKE` grants. Touches nothing that exists. The application
   keeps running on the old path throughout Phase 1.C–1.F. This is what makes the whole phase safe to
   build incrementally.
2. **`RetireLegacyFinance` (P1-21) — destructive, single commit, after everything else is green.**
   Drops `monthly_charges`, `finance_transactions`, `students.balance`, `students.discount_pct`,
   `students.discount_amount`, `students.discount_note`.

Never merge these two. The additive one ships early and often; the destructive one ships once, after
the test suite passes.

### 4.2 What happens to each legacy artefact

| Legacy | Fate |
|---|---|
| `monthly_charges` rows | deleted; re-created as `invoices` by the accrual job on first run |
| `finance_transactions` (income) | deleted; the cashier re-enters nothing — there is nothing real to re-enter |
| `finance_transactions` (expense, salary) | deleted; future payouts go to `expenses` + `expense:salary` ledger |
| `students.balance` | column dropped; replaced by derived `StudentBalanceQuery` (12 read sites migrated in P1-21) |
| `students.discount_*` | column dropped; replaced by `discounts` rows with `created_by` / `approved_by` |
| `SchoolClass.MonthlyFee` | **kept** — it becomes the default amount offered when a `tuition` subscription is created |
| `tools/seed_demo.py` | rewritten to seed subscriptions → invoices → shifts → payments |

### 4.3 Opening balances at go-live

If the school wants real debt carried in on day one, it arrives as a one-off import, **not** as a
migration from the old tables: one `invoices` row per student per category with
`period_month = <go-live month>` and `memo = 'opening balance'`, posted through `LedgerService` like
any other invoice. This needs client Q17 (a file). If no file arrives, the system starts from zero and
that is a legitimate outcome — log it in `docs/ASSUMPTIONS.md`.

### 4.4 Rollback

Up to and including P1-20, rollback is `git revert` — the legacy path still works. After P1-21, rollback
is a restore from the nightly `pg_dump` (`docker-compose.yml` backup service). **Take a manual dump
immediately before applying `RetireLegacyFinance`,** and confirm SPEC §7's off-site copy exists first.

---

## 5. Dependency graph and critical path

### 5.1 Graph (text)

```
DAY 1, all three start together
  P1-01 client pack ....................... [external wait 3-14 days] ──┐
  P1-02 db roles + residue ★                                            │
  P1-03 test harness                                                    │
                                                                        │
P1-02 ──► P1-04 entities ★ ──► P1-05 migration ★ ──┬──► P1-07 ledger ★ ─┤
                              └──► P1-06 contracts ★ ┘                  │
                                                                        │
P1-06,P1-07 ──┬──► P1-08 catalog ........... [needs Q5] ◄───────────────┤
              ├──► P1-09 invoices+accrual .. [needs Q6] ◄───────────────┤
              ├──► P1-10 cash shifts ....... [needs Q11] ◄──────────────┤
              ├──► P1-11 payments ★                                     │
              └──► P1-12 receipts .......... [needs Q3] ◄───────────────┘
P1-07 ────────┬──► P1-13 reports
              └──► P1-14 audit + anomaly

P1-08..P1-14 ──► P1-15 Program.cs ★  [SEQUENTIAL — shared file]

P1-06 ──┬──► P1-16 cashier UI
        ├──► P1-17 billing catalog UI
        ├──► P1-18 director dashboard ★
        └──► P1-19 student/parent UI

P1-16..P1-19 ──► P1-20 routes+nav+perms ★  [SEQUENTIAL — shared registries]

P1-15 + P1-20 ──► P1-21 legacy retirement ★  [SEQUENTIAL — 12 call sites]

P1-11,P1-15 ──► P1-22 security suite ★
P1-08,P1-09 ──► P1-23 arithmetic tests
P1-10 ───────► P1-24 shift/receipt tests

P1-21 + P1-22 + P1-23 + P1-24 ──► P1-25 gate ★
```

### 5.2 Critical path — 112 h

```
P1-02 (6) → P1-04 (10) → P1-05 (12) → P1-06 (10) → P1-07 (10) → P1-11 (14)
      → P1-15 (4) → P1-18 (14) → P1-20 (4) → P1-21 (12) → P1-22 (8) → P1-25 (8)
```

**112 hours ≈ 14 working days ≈ 2.8 calendar weeks.**

Total work is 240 h against a 240 h budget (2 devs × 3 weeks × 40 h). **The plan has zero slack.**
Any one task slipping a day slips the phase. The three places to buy time, in order:

1. **P1-18** (14 h, director dashboard) can ship with Z-report + variance only; P&L and Cash Flow are
   SPEC Phase 5 items that merely *start* here. Saves ~6 h off the critical path.
2. **P1-21** can keep `students.balance` as a shadow column and drop it in Phase 2. Saves ~5 h and
   removes the riskiest step from the phase, at the cost of one lingering dead column.
3. **P1-12** Telegram delivery can be deferred a week behind the PDF endpoint — but note this weakens
   §4.7, the cheapest fraud control in the whole design. Deferring it is a security decision, not a
   scheduling one.

### 5.3 Parallelism

| Phase | Tasks | Mode | Peak agents |
|---|---|---|---|
| 1.A | P1-01..P1-03 | parallel | 3 |
| 1.B | P1-04..P1-07 | **sequential** | 1 |
| 1.C | P1-08..P1-12 | parallel | 5 |
| 1.D | P1-13, P1-14 | parallel (may overlap 1.C) | 2 |
| 1.E | P1-15 | **sequential** | 1 |
| 1.F | P1-16..P1-19 | parallel | 4 |
| 1.G | P1-20 | **sequential** | 1 |
| 1.H | P1-21 | **sequential** | 1 |
| 1.I | P1-22..P1-24 | parallel | 3 |
| 1.J | P1-25 | **sequential** | 1 |

**17 of 25 tasks are parallelizable. 8 are sequential, and every one of them is sequential for the
same reason: it edits a file that more than one agent would otherwise touch.**

Shared files, each owned by exactly one sequential task:

| Shared file | Owned by |
|---|---|
| `SchoolLms.Domain/Entities.cs`, `Roles.cs`, `AppDbContext.cs`, `IAppDbContext.cs` | P1-04 |
| `SchoolLms.Infrastructure/Migrations/` (the EF migration head) | P1-05, then P1-21 |
| `SchoolLms.Application/Dtos/Dtos.cs`, `schoollms.client/src/types/index.ts` | P1-06 |
| `SchoolLms.Server/Program.cs` | P1-15 |
| `App.tsx`, `navigation.ts`, `constants.ts`, `ProtectedRoute.tsx` | P1-20 |
| the 12 legacy call sites | P1-21 |

---

## 6. Risks

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | **`REVOKE` does nothing today** — the app connects as the database owner (`docker-compose.yml:37`). Ship as-is and §4.1 is decoration. | The client's stated fraud stays possible | **P1-02 on day one.** P1-22 asserts SQLSTATE 42501 as `app_rw` **and** success as owner, so a misconfigured role cannot pass silently. |
| R2 | Two migrations in one phase, the second destructive, both authored by EF autogenerate — which reliably emits spurious `DROP`s. | Silent data or column loss | Read every generated migration line by line (global rule). Paste the script into the PR. Manual `pg_dump` before `RetireLegacyFinance`. Never merge the additive and destructive migrations. |
| R3 | `Entities.cs` is a single 1077-line file and five agents work billing simultaneously in Phase 1.C. | Merge conflicts, lost entity definitions | New entities go in a **new** `SchoolLms.Domain/Billing.cs`; P1-04 is sequential and `Entities.cs` must show an empty diff. |
| R4 | Gapless receipt numbering under concurrency. | Duplicate or skipped receipt numbers — an auditor treats a gap as a deleted receipt | `unique (cash_shift_id, receipt_no)` plus a per-shift advisory lock or insert-retry. P1-24 proves it with 50 concurrent writers. |
| R5 | `Student.Balance` is read in 12 places across 9 files. Dropping it breaks message templates, the parent portal, the teacher portal, attendance and analytics. | Broken screens discovered in production | P1-21 is sequential with a `StudentBalanceQuery` shim; the acceptance criterion is a `grep` returning zero hits, not "looks fine". |
| R6 | Old accrual (`TuitionAccrualService`) and new accrual (`BillingAccrualService`) both registered during the overlap. | **Every student billed twice** | P1-15 explicitly removes `Program.cs:172`. P1-23 asserts idempotency via the DB unique index rather than in-memory bookkeeping. |
| R7 | Zero slack: 240 h of work against a 240 h budget. | A one-day slip is a phase slip | Three named descope levers in §5.2, decided at the P1-15 checkpoint — **not** at the end. |
| R8 | New billing tables use `uuid`/`date`/`timestamptz`; the surrounding 53 entities use `text` for both ids and dates, and existing code does `string.Compare` on dates (`FinanceController.cs:38`, `SalaryLedger.cs:49`). | Off-by-one-month bugs at period boundaries, invisible in tests that use round dates | New billing code uses `DateOnly`/`DateTimeOffset` exclusively; conversion happens only in DTO mapping. Period-boundary cases (1st, last day, month rollover at 23:59 Tashkent) are explicit test cases in P1-23. |
| R9 | PDF library licensing. QuestPDF Community is free under 1 M USD revenue — but that is a licence condition, not a guarantee. | Legal exposure, or an unplanned purchase | P1-12 records the decision in `docs/ASSUMPTIONS.md`. If a commercial licence is needed, **stop and ask the user** — that is money spent. |
| R10 | Hiding the delete button is mistaken for removing the capability. | A cashier with a REST client deletes a payment | The endpoint must not exist: `grep -c "HttpPut\|HttpDelete" PaymentsController.cs` = 0 is an acceptance criterion, and the DB grant is the real guarantee. Two independent layers, both tested. |
| R11 | Phase 0 is declared "nearly done" but the .NET 10 upgrade never happened and a SQL Server package reference remains. | SPEC drift; an upgrade landing mid-Phase-1 doubles the blast radius | P1-02 removes the dead reference and records an explicit **defer the .NET 10 upgrade to after Phase 1** decision. |
| R12 | Client answers to Q3/Q5/Q6/Q11/Q17 arrive late or never. | Blocked tasks, or guesses baked into the schema | Every blocked step ships with a named default, logged in `docs/ASSUMPTIONS.md`. All defaults are configuration values, not schema — changing one later is an `UPDATE`, not a migration. |
| R13 | No off-site backup yet (SPEC §7, §9). P1-21 is the first irreversible step against the only copy. | Server loss during the destructive migration = total loss | Confirm the off-site copy exists **before** P1-21 runs. If it does not, that is a hard stop — escalate to the user. |

---

## 7. Open questions for the client

### 7.1 From SPEC §8, Phase 1 relevant

| SPEC # | Question | Blocks | Default if unanswered |
|---|---|---|---|
| 3 | **Receipts** — thermal printer (ESC/POS) at the cash desk, or PDF + Telegram only? | P1-12 | PDF + Telegram only; ESC/POS becomes a Phase 5 add-on |
| 4 | **Bus and dormitory pricing** — flat rate, or per route / per room type? | P1-08 | Flat rate per student, stored in `student_subscriptions.monthly_amount` with the route/room in `detail` — this shape supports both, so the answer changes data entry, not schema |
| 5 | **Discount approval threshold** — above what percentage or amount is a second approver required? | P1-08 | 20 % or 500 000 so'm, whichever is hit first |
| 6 | **Payment due date** — which day of the month, and what grace period before "overdue"? | P1-09 | Due on the 10th, overdue after the 15th |
| 2 | **Telegram bot** — existing bot and token, or register a new one? Phase 1 needs it for receipt delivery (§4.7), earlier than SPEC says. | P1-12 | Use the token already stored in `SchoolMeta`; if empty, receipts are PDF-only and §4.7 is not satisfied |
| 9 | **Go-live date** — which academic period must the system run for? | P1-21, opening balances | Not assumable. **This one genuinely needs an answer** — it sets whether opening balances are needed at all |

### 7.2 New, raised by this plan

| # | Question | Blocks | Default |
|---|---|---|---|
| 10 | **Reversal policy** — may a cashier's error be reversed within the same shift by the admin, or only next day with director approval? Also: does a reversal reopen the invoice, or create a credit? | P1-11 | Admin may reverse any time with a reason; reversal reopens the invoice |
| 11 | **Opening float** — does the cash desk start each day with a float, and who sets the amount? | P1-10 | 0; the cashier may enter a float when opening |
| 12 | **Part-month enrolment and withdrawal** — pro-rata by day, or full month charged? Applies separately to tuition and to bus. | P1-09 | Full month for both |
| 13 | **Payment methods actually in use** — cash, card terminal, bank transfer? If a card terminal settles into the school account, card payments must **not** count toward `expected_cash` at shift close. | P1-10, P1-11 | All three supported; only `cash` counts toward `expected_cash` |
| 14 | **One payment, two children** — can a guardian pay for two children in a single receipt? SPEC §3.7 gives `payments` a single `student_id`, so today the answer is no: two payments, two receipts. | P1-11 (**schema impact**) | One payment per child. **If the client says yes, this must be decided before P1-05** — it moves `student_id` off `payments` and onto the allocation, and that is a schema change, not a feature. |
| 15 | **Advance payment for a full year** — create invoices for future months immediately, or hold the money as unallocated credit and allocate as each month accrues? | P1-11 | Unallocated credit, allocated automatically by the accrual job |
| 16 | **Expense categories** — the list the school actually uses, and which of them need a second approver (§4.5). | P1-14 | Carry over today's list (`salary`, `utilities`, `supplies`, `rent`, `other`); dual control above 5 000 000 so'm |
| 17 | **Opening debt at go-live** — does existing student debt need to be carried in? If yes, supply a file: `student, category, amount, as-of date`. | P1-21 | Start from zero |

**Q14 is the one to chase first.** It is the only question in this list whose answer can change the
schema, and the schema freezes at P1-05 — three working days in.
