# Pending wiring

Things Phase 1.B (P1-04 … P1-07) built but deliberately did **not** connect, because the
connecting file belongs to a later sequential task. Each entry says what to add, where, and
what breaks if it is skipped.

Delete an entry when it is done.

---

## For P1-15 — `Program.cs` DI

### 1. `LedgerService` is not registered

`SchoolLms.Application/Billing/LedgerService.cs` exists and is tested, but nothing resolves
`ILedgerService`. Add next to the other `AddScoped` calls (around line 210):

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.ILedgerService,
                           SchoolLms.Application.Billing.LedgerService>();
```

`IAppDbContext` is already registered (`Program.cs:56`), so the constructor resolves as-is.

**If skipped:** any controller injecting `ILedgerService` fails at request time with
`InvalidOperationException: Unable to resolve service`. Nothing fails at build time.

### 2. The other five billing services have interfaces but no implementations yet

`IInvoiceService`, `ICashShiftService`, `IPaymentService`, `IDiscountService`,
`IReceiptService` are frozen in `SchoolLms.Application/Billing/IBillingServices.cs` (P1-06).
Phase 1.C writes the implementations; P1-15 registers them with the same one-line pattern.

### 3. Accrual hosted service

`TuitionAccrualService` (`Program.cs:203`) still drives the **legacy** `MonthlyCharge`
accrual. Its per-category replacement is `IInvoiceService.AccrueDueAsync` (P1-09). Both may
run side by side until P1-21 retires the legacy path — they write to different tables and do
not interfere.

### 9. `PaymentService` (P1-11) is written but not registered

`SchoolLms.Application/Billing/PaymentService.cs` implements `IPaymentService`. It needs
**three** registrations to resolve — the other two are entry 1 above and P1-10's shift service:

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.IPaymentService,
                           SchoolLms.Application.Billing.PaymentService>();
```

Its constructor is `(IAppDbContext, ICashShiftService, ILedgerService)`. `ICashShiftService`
is P1-10's; until that one is registered too, `POST /api/cash/payments` fails at request time
with `Unable to resolve service for type ICashShiftService`. Nothing fails at build time.

`PaymentsTests` registers all three itself on a second host, so the tests stay green before
P1-15 lands — see the comment at the top of `SchoolLms.Tests/PaymentsTests.cs`.

---

## For P1-20 — routes, navigation, permissions

### 4. `cashier` has a placeholder navigation entry

Adding `'cashier'` to the TS `Role` union (P1-06) made three `Record<Role, …>` maps in
`schoollms.client/src/config/navigation.ts` incomplete, which breaks `npm run build`. Minimal
placeholders were added so the build stays green:

- `navByRole.cashier` — a single item, `{ label: 'Kassa', to: '/cashier', icon: Wallet }`
- `homeByRole.cashier` — `'/cashier'`
- `roleLabels.cashier` — `'Kassir'`

**The `/cashier` route itself does not exist yet.** A cashier logging in today lands on a
404. P1-16 builds the workspace, P1-20 registers the route and replaces the placeholder nav.

### 5. Cashier accounts cannot be created from the UI

`StaffController` only creates users with `role = "staff"`. There is no endpoint or screen
that issues a `cashier` account yet, so the first one has to be made with
`tools/create_user.py --role cashier`. P1-20 (or whoever owns the staff screen) should add
the role choice.

---

## For P1-22 — financial security suite

### 6. The test harness still cannot see the `REVOKE`

`PostgresFixture` does not create `app_rw`, so the migration's grant block skips itself and
`AppRwIsOwnerFallback` stays `true`. The exact change needed — and the trap that would turn
the test into a false green — is written up in `docs/TESTING.md` §4, under
"Update after P1-05".

Until then the production path is covered by `./tools/verify-billing-guards.sh`
(20 checks, all passing as of 2026-09-11).

---

## For Phase 1.C owners — contract notes

### 7. Use `AppClock.NowInstant` for every `timestamptz` column

Not `DateTime.UtcNow`, and **not** `new DateTimeOffset(AppClock.Now, …)`: Npgsql rejects a
`DateTimeOffset` whose offset is not zero with
`"only offset 0 (UTC) is supported"`. `AppClock.NowInstant` returns the instant in UTC, and
`AppClock.LocalDateOf(...)` converts back to a Tashkent calendar day for shift and Z-report
grouping.

### 8. Wrap multi-table money writes in `IAppDbContext.BeginTransactionAsync`

`LedgerService.PostAsync` calls `SaveChangesAsync` itself. A payment (P1-11) writes
`payments`, `payment_allocations`, invoice status **and** ledger rows; without an explicit
transaction those are two separate commits, and a crash in between leaves a payment with no
ledger entry.

---

## Added by P1-08 (fee catalog, subscriptions, discounts)

### 9. For P1-15 — the two P1-08 services are not registered

`BillingCatalogController` injects `ISubscriptionService` and `IDiscountService`; neither is
in `Program.cs`. Add next to the other `AddScoped` calls (around line 210):

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.ISubscriptionService,
                           SchoolLms.Application.Billing.SubscriptionService>();
builder.Services.AddScoped<SchoolLms.Application.Billing.IDiscountService,
                           SchoolLms.Application.Billing.DiscountService>();
```

Both constructors take `(IAppDbContext, AuditService)`; both are already registered
(`Program.cs:56` and `:223`), so nothing else is needed.

`ISubscriptionService` lives in `SchoolLms.Application/Billing/SubscriptionService.cs`, not in
the frozen `IBillingServices.cs` — P1-06 froze five interfaces and subscriptions was not one
of them, and adding a type to that file would have collided with the four other Phase 1.C
agents working in parallel.

**If skipped:** every `/api/admin/billing/*` endpoint returns **500** at request time
(`Unable to resolve service`). The RBAC denials (401/403) still work, because the
authorization filter runs before the controller is constructed — which is exactly why
`BillingCatalogTests` can test permissions today but has to register the two services in a
derived `WebApplicationFactory` to test the happy path. Once this entry is done, that helper
(`BillingCatalogTests.WiredApi()`) can be deleted and replaced with
`fixture.Api.ClientAsAsync(...)`.

### 10. For the next migration owner — no database guard against overlapping subscriptions

`SubscriptionService` rejects a second subscription for the same `(student_id, category_id)`
whose date range overlaps an existing one (`409 subscription_overlap`). **Only the
application enforces this.** The database equivalent is

```sql
create extension if not exists btree_gist;
alter table student_subscriptions add constraint ex_student_subscriptions_no_overlap
  exclude using gist (
    student_id with =, category_id with =,
    daterange(starts_on, coalesce(ends_on, 'infinity'::date), '[]') with &&
  );
```

It was **not** added here because P1-08 owns no migration (P1-05's file is closed and Phase
1.C runs in parallel), and because `btree_gist` on a `text` column needs checking against the
production image first. Until it lands, two concurrent requests can still create overlapping
rows — the accrual job (P1-09) would then see two prices for one month and take both.
Cheap to add with any later migration.

### 11. For Phase 1.C owners — reuse `BillingFaultAttribute` for error responses

`SchoolLms.Server/Controllers/BillingCatalogController.cs` also declares
`BillingFaultAttribute` (an `IExceptionFilter`) and `BillingErrorDto`. Services throw
`BillingRuleException` (`SchoolLms.Application/Billing/BillingRuleException.cs`) with a fault
kind, the filter maps it to 400/403/404/409 and returns `{ "code": …, "message": … }` — the
shape the whole frontend already reads (`err.response.data.message`).

Put `[BillingFault]` on the P1-09…P1-12 controllers too rather than inventing a second error
shape. The filter deliberately catches **only** `BillingRuleException`: catching
`InvalidOperationException` would turn a missing DI registration into a 409 and hide a broken
deployment behind what looks like a normal business response.

## From P1-09 — invoices and per-category accrual

### 9. `InvoiceService` and `BillingAccrualService` are not registered

`SchoolLms.Application/Billing/InvoiceService.cs` and `BillingAccrualService.cs` exist and are
tested, but nothing resolves them. For P1-15, next to the other `AddScoped` calls:

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.IInvoiceService,
                           SchoolLms.Application.Billing.InvoiceService>();
builder.Services.AddHostedService<SchoolLms.Application.Billing.BillingAccrualService>();
```

`InvoiceService` needs `IAppDbContext` (already registered, `Program.cs:56`) **and**
`ILedgerService` (entry 1 above) — registering the accrual job without `ILedgerService`
makes the job throw on its first tick, in the background, where nobody looks.

**Delete `builder.Services.AddHostedService<…TuitionAccrualService>()` (`Program.cs:210`) in the
same commit.** The two jobs write to different tables (`monthly_charges` vs `invoices`) and do
not collide technically, but a student would be billed twice — once in each model — and the
director's dashboard would show both. This is the one line in P1-15 that costs money if it is
forgotten. `TuitionAccrualService.cs` itself stays untouched; P1-21 deletes the file.

**If skipped:** nothing is billed at all. No invoice is ever created, so the cash desk (P1-11)
has nothing to allocate a payment to and the debtor report (P1-13) is empty. Nothing fails at
build time, and nothing fails at startup either — it is simply silent.

### 10. The accrual job runs as the director, because there is no system user

`ledger_entries.created_by` is `not null` and references `users(id)`, so an unattended job still
has to name a person (SPEC §4.4). `BillingAccrualService.ResolveActorAsync` picks the first
`superadmin` (ordered by id), falling back to `admin`; with neither present it logs a warning and
does nothing that tick.

If a real `system` account is wanted — and it would read better in the ledger — it needs a
migration (seeded row, empty `password_hash` so it cannot log in) plus one line in
`ResolveActorAsync`. That is P1-15's or P1-21's call, not something P1-09 may add: the migration
chain is a shared file.

**Consequence today:** on a database with no admin yet, the first accrual tick is skipped. There
are no subscriptions on such a database either, so nothing is lost — the next tick catches up.

### 11. P1-08: `DiscountService.ChargeFor` must delegate to `DiscountMath`

The discount formula (percent first, then the flat amount, floor 0, round to 2) now lives in
`DiscountMath` at the bottom of `InvoiceService.cs` — accrual calls it directly, without DI,
because P1-08 and P1-09 were written in parallel and injecting `IDiscountService` would have made
the accrual unbuildable until P1-08 landed.

`IDiscountService.ChargeFor` / `DiscountFor` (P1-08) should be two-line delegations to it. **Two
copies of a money formula is the thing to avoid here**: they agree the day they are written and
drift the day one of them is fixed, and the only person who notices is the parent holding a
receipt. P1-23 asserts both against the legacy `TuitionService` values.

### 12. `InvoiceQuery` has no paging — `ListAsync` caps at 2000 rows

`InvoiceQuery` was frozen in P1-06 without `Page` / `PageSize`, and an unfiltered list of
1000 students × 3 categories × 12 months is 36 000 rows. `InvoiceService.ListAsync` therefore
orders newest month first and takes `InvoiceService.MaxListRows` (2000).

Whoever builds the invoices endpoint (P1-13 / P1-18) should add paging fields to `InvoiceQuery`
— an additive change to the record, which is explicitly allowed (`BillingDtos.cs` header) — and
drop the cap. Until then, a screen that shows every invoice of a large school will silently show
only the most recent ones.

## From P1-11 — payments (contract notes for other Phase 1 tasks)

### 10. For P1-10 — how a reversal maps onto a shift

A storno row is written into the **approver's own open shift**, not into the shift of the
payment being reversed. Reason: the money leaves today's drawer, and adding a row to an
already closed shift would retroactively falsify a Z-report whose `variance` is a stored
generated column and cannot be corrected. Consequence: `POST .../reverse` returns
**409 `no_open_shift`** when the admin has no shift open.

This matters for `expected_cash`. The two mirrored ledger rows produced by
`LedgerService.ReverseAsync` carry `ref_type = 'reversal'` and `ref_id = the ORIGINAL
payment id` (P1-07's batch rule), while the storno **payment** row belongs to the approver's
shift. So a shift's cash is:

```
opening_float
  + Σ debit  cash  from ledger rows whose ref_id is a payment in THIS shift
  − Σ credit cash  from ledger rows whose ref_id is the ORIGINAL of a storno payment
                     in THIS shift   (i.e. join payments p on p.reversal_of = ledger.ref_id)
```

Joining `ledger.ref_id = payments.id` alone would subtract the reversal from the *original*
(possibly closed) shift — a silent, and permanent, off-by-one-shift error.

### 11. For P1-14 — where the audit hook goes

`PaymentService.AcceptAsync` and `ReverseAsync` each run inside one
`IAppDbContext.BeginTransactionAsync` and commit at the end. An `audit_log` row must be
written **inside** that transaction (before `CommitAsync`), otherwise a rolled-back payment
leaves an audit entry for money that was never taken. The service deliberately writes no
audit row today — that is P1-14's acceptance criterion, not P1-11's.

### 12. For P1-09 / P1-13 — how "paid" is computed

Allocations are immutable, so a reversal cannot delete them. The single definition of a
paid amount is in `PaymentService.EffectiveAllocations()`:

> an allocation counts only if its payment is **not itself a storno** (`reversal_of is null`)
> **and has not been reversed** (no payment row points at it).

A storno row carries **no** allocation rows of its own. Any other query that sums
`payment_allocations` (debtor report, invoice `paid`/`remaining`, student card) must apply
the same two filters or it will report reversed money as collected. `invoices.status` is
already maintained by `PaymentService` on both paths and is safe to read directly.

### 13. For P1-16 / P1-12 — routes

Every payment action answers on **two** paths: the one named in `docs/TASKS.md` P1-11 and
the one the frozen P1-06 client stub calls. `schoollms.client/src/api/services/payments.ts`
needs no path change — uncommenting the axios line is enough. The pairs are
`/api/cash/payments` = `/api/cashier/payments`,
`/api/cash/payments/suggest-allocation` = `/api/cashier/payments/suggest-allocation`,
`/api/admin/payments/{id}/reverse` = `/api/admin/billing/payments/{id}/reverse`.
Reads live at `GET /api/billing/payments` and `GET /api/billing/payments/{id}`, which leaves
`GET /api/billing/payments/{id}/receipt.pdf` (P1-12) free — different segment count, no
route conflict.
