# Pending wiring

Things Phase 1.B (P1-04 … P1-07) built but deliberately did **not** connect, because the
connecting file belongs to a later sequential task. Each entry says what to add, where, and
what breaks if it is skipped.

Delete an entry when it is done.

---

## For P1-15 — `Program.cs` DI

### 1. `LedgerService` is not registered — **DONE** (expense module, 2026-09-11)

`ILedgerService` → `LedgerService` is now registered in `Program.cs`, in the new
`// ---------- Moliya (billing) ----------` block, because `ExpenseService` cannot be
constructed without it. The entry is kept (not deleted) because items 9 below refer to
"entry 1 above"; nothing is left to do here.

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.ILedgerService,
                           SchoolLms.Application.Billing.LedgerService>();
```

### 2. The other five billing services have interfaces but no implementations yet

`IInvoiceService`, `ICashShiftService`, `IPaymentService`, `IDiscountService`,
`IReceiptService` are frozen in `SchoolLms.Application/Billing/IBillingServices.cs` (P1-06).
Phase 1.C writes the implementations; P1-15 registers them with the same one-line pattern.

### 3a. `FinanceReportQueries` is not in DI — and nothing breaks if it stays that way (P1-13)

`SchoolLms.Application/Billing/FinanceReportQueries.cs` (P1-13) is **not** registered, and
`FinanceReportsController` therefore constructs it itself from the request-scoped
`AppDbContext`:

```csharp
public class FinanceReportsController(AppDbContext db) : ControllerBase
{
    private readonly FinanceReportQueries _reports = new(db);
```

The class is stateless, read-only and has one dependency (`IAppDbContext`), so this works
today: **all four endpoints are live without any `Program.cs` change** — this entry is
optional tidying, not a fix.

If P1-15 wants it in DI for consistency with the other billing services, it is two lines:

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.FinanceReportQueries>();
// then in FinanceReportsController: (FinanceReportQueries reports) instead of (AppDbContext db)
```

**If skipped:** nothing. No interface, no hosted service, no startup validation depends on it.

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

### 4a. Four finance report endpoints are live but no screen calls them (P1-13)

All under `/api/admin/finance`, all `admin` + `superadmin` only (`FinanceAction.ViewBillingReports`);
a `cashier` gets **403**, so do not put them behind the cashier navigation.

| Endpoint | Query | Response |
|---|---|---|
| `GET /debtors` | `className`, `minDebt` (default 0.01), `onlyOverdue`, `includeArchived` | `DebtorRowDto[]` |
| `GET /pnl` | `from`, `to` (default: current month → today) | `ProfitLossDto` |
| `GET /cashflow` | `from`, `to` (default: last 12 months; max 120 months → 400) | `CashFlowDto` |
| `GET /collection-rate` | `from`, `to` (both optional) | `BillingMonthlyDto[]` |

`DebtorRowDto` and `BillingMonthlyDto` are the frozen shapes in `Dtos/BillingDtos.cs`.
`ProfitLossDto` / `CashFlowDto` live in `Billing/FinanceReportQueries.cs` — `BillingDtos.cs`
is frozen (P1-06) and P1-13 was not allowed to touch it. If a later task wants them beside
the other DTOs, moving them is a pure cut-and-paste.

**Semantics the UI must not re-invent:** in `collection-rate`, the month is the **invoice**
month (`invoices.period_month`), so `Collected` is the money that went to *that month's*
invoices whenever it was paid — which is why `Accrued − Collected` equals that month's slice
of the debtor report. "How much cash arrived in month M" is a different question and is
answered by `/cashflow` (`cash` + `bank` inflow).

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

## From P1-10 — cash shifts, gapless receipt numbers, Z-report

### 9. `CashShiftService` is not registered (P1-15, `Program.cs`)

`SchoolLms.Application/Billing/CashShiftService.cs` and
`SchoolLms.Server/Controllers/CashShiftsController.cs` exist and are tested, but nothing
resolves `ICashShiftService`. Add next to the other `AddScoped` calls:

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.ICashShiftService,
                           SchoolLms.Application.Billing.CashShiftService>();
```

`IAppDbContext` is already registered (`Program.cs:56`) and is the service's only
dependency, so the constructor resolves as-is.

**If skipped:** every `/api/cash/shifts/*` endpoint fails at request time with
`InvalidOperationException: Unable to resolve service`. Nothing fails at build time. The
RBAC gate still returns 401/403 correctly, which makes the failure look role-related —
check DI first. Until P1-15 lands, `CashShiftServiceTests` boots its own host with exactly
that one line (`fixture.Api.WithWebHostBuilder(...)`), so the endpoints are already
covered by tests.

### 10. `SchoolLms.Application.csproj` gained one `PackageReference` — **a shared file was touched**

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.*" />
```

Needed for `Database.ExecuteSqlRawAsync`: the gapless receipt number relies on
`pg_advisory_xact_lock`, which cannot be expressed in LINQ, and the Application project
referenced only the EF **core** package (`Relational` was present at runtime through the
Npgsql provider but invisible at compile time). No new runtime dependency, and still no
dependency on a database provider.

**Merge note for the orchestrator:** if another Phase 1.C task also edits that `ItemGroup`,
keep both lines — they are independent.

### 11. Frontend contract differs from the frozen stubs in three places (P1-16 / P1-20)

`schoollms.client/src/api/services/cashShifts.ts` was written in P1-06 against guessed
paths. The real routes follow `docs/TASKS.md` P1-10, which names them explicitly:

| Stub (P1-06) | Real route (P1-10) |
|---|---|
| `GET /cashier/shifts/current` | `GET /api/cash/shifts/current` |
| `POST /cashier/shifts/open` | `POST /api/cash/shifts/open` |
| `POST /cashier/shifts/{id}/close` | `POST /api/cash/shifts/{id}/close` |
| `GET /cashier/shifts/{id}/z-report` | `GET /api/cash/shifts/{id}/z-report` |
| `GET /admin/billing/shifts` | `GET /api/cash/shifts` |

Two behaviour notes for whoever swaps the stub bodies:

- **`current` returns `204 No Content`, not a JSON `null`.** The stub comment promises
  `null`; with axios a 204 gives `data === ''`, which is falsy but not `null`. Write
  `return res.status === 204 ? null : res.data`.
- **`GET /api/cash/shifts` silently scopes to the caller** when the caller is a cashier:
  a `cashierId` query parameter belonging to somebody else is overwritten with the
  caller's own id (SPEC §4.3 — a cashier sees no cross-cashier data). Admin and director
  get the unfiltered list. It is not a 403, so no error handling is needed.

Error bodies from this controller are `{ "code": "...", "message": "..." }`; the codes are
the constants in `CashShiftError` (`shift_already_open`, `not_your_shift`,
`invalid_counted_cash`, …). Branch on `code`, never on the message text.

### 12. Contract for P1-11 (`PaymentService`) — how to take a receipt number

```csharp
await using var tx = await db.BeginTransactionAsync(ct);
var receiptNo = await shifts.NextReceiptNoAsync(shift.Id, ct);   // takes the per-shift lock
// ... payments + payment_allocations + ledger, all on the same context ...
await tx.CommitAsync(ct);
```

- **The call throws without an open transaction.** `pg_advisory_xact_lock` is released at
  commit; outside a transaction it is released immediately and the number stops being
  gapless while the code still looks correct.
- **Ask for the number, then insert, then commit — in that order and in one transaction.**
  A rolled-back payment then leaves no gap, because the number was never used.
- A closed shift returns `CashShiftException(CashShiftError.NotOpen)` — map it to 409.

**Where a reversal row belongs.** A shift's Z-report and its `expected_cash` are both
derived from one set: the payments whose `cash_shift_id` is that shift, plus the `cash`
ledger rows whose `ref_id` is one of those payments. Put the reversal's `payments` row in
the shift that is **open at the moment of the reversal** (for an admin with no open shift,
the original's shift is the only sane fallback). Whichever is chosen, the two sides stay
consistent, because they read the same set — but the choice decides *which day's* report
shows the storno, so make it deliberately and write it down.

## For P1-15 / P1-11 — P1-12 (receipt: PDF + Telegram)

P1-12 added three new production files, one test file and one package reference
(`QuestPDF` in `SchoolLms.Application.csproj`). It touched **no** shared file:
`Program.cs`, `TelegramService.cs`, `BillingDtos.cs`, `IBillingServices.cs` and the Dockerfile
are all untouched. Everything below is what is deliberately left unconnected.

### 9. `ReceiptService` is not registered

`SchoolLms.Application/Billing/ReceiptService.cs` implements `IReceiptService` and is tested,
but nothing resolves it. Add next to the other billing `AddScoped` calls (see item 2):

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.IReceiptService,
                           SchoolLms.Application.Billing.ReceiptService>();
```

Three of its four constructor dependencies already resolve today: `IAppDbContext`
(`Program.cs:56`), `TelegramService` (`Program.cs:216`, singleton) and `ILogger<>`. The fourth,
`IPaymentService`, arrives with P1-11 and is registered by the same item 2 — so register the
two together, or the receipt endpoints fail on the missing payment service instead.

**If skipped:** `GET /api/receipts/{id}.pdf` and `POST /api/receipts/{id}/telegram` return 500
for anyone the RBAC filter lets through. 401 and 403 still behave correctly, because the
authorization filter runs before the controller is constructed — `ReceiptTests` asserts exactly
that, so the unwired state is covered and will not regress.

### 10. Nobody sends the receipt automatically yet — P1-11 has to call it

SPEC §4.7 wants the parent to hold a copy the school cannot alter. Today the copy is only sent
when a human presses "send" (`POST /api/receipts/{paymentId}/telegram`). The automatic path
belongs at the one moment a payment is accepted — in `PaymentService.AcceptAsync`, **after the
money transaction has committed**:

```csharp
// ... transaction committed, payment is final ...
await receipts.SendToGuardianAsync(payment.Id, ct);   // returns bool, NEVER throws
```

Two rules for whoever wires it:

1. **After the commit, never inside the transaction.** Rendering a PDF and waiting for
   Telegram inside an open transaction holds row locks on `payments` for seconds.
2. **Do not check the return value to decide anything about the payment.** `false` means
   "receipt not delivered", never "payment failed". The method already swallows and logs every
   exception precisely so that it cannot roll anything back.

**If skipped:** the money module works, but the fraud control in SPEC §4.7 only fires when the
cashier remembers to press a button.

### 11. `TelegramService.SendDocumentAsync` labels every file as `.docx`

`SchoolLms.Application/Services/TelegramService.cs:103` hard-codes

```csharp
new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document")
```

because it was written for contracts. Receipts go through the same method as `chek-1042.pdf`;
Telegram clients key off the file extension, so it arrives and opens, but the declared MIME
type is wrong. The fix is one optional parameter, and it is **not** made here because
`TelegramService.cs` is a shared file with five tasks in flight:

```csharp
public async Task<bool> SendDocumentAsync(
    long chatId, byte[] bytes, string fileName, string? caption = null,
    CancellationToken ct = default,
    string contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
```

then pass `"application/pdf"` from `ReceiptService.SendWithOneRetryAsync`. Default value keeps
`ContractsController` unchanged.

### 12. Optional: the image grew ~83 MB

QuestPDF ships native Skia for eight runtime identifiers and `dotnet publish` copies all of
them; the container uses `linux-x64` only. Adding `-r linux-x64 --self-contained false` to the
publish step in the `Dockerfile` removes the other seven. Not done here (the Dockerfile is not
this task's file), and not urgent — it is image size, not memory.

## From P1-26 — money-flow ring

### 9. Nothing is pending in `Program.cs` — deliberately

`GET /api/admin/finance/money-flow` works as soon as the build ships. `MoneyFlowController`
injects `IAppDbContext`, which is **already registered** (`Program.cs:56`), and
`SchoolLms.Application/Billing/MoneyFlowQueries.cs` is a `static` class with no state, so
there is no service to register.

This entry exists so that P1-15 does not go looking for one. If a later task turns the query
into an injected service, the line would be:

```csharp
builder.Services.AddScoped<SchoolLms.Application.Billing.MoneyFlowQueries>();
```

**Do not add it now** — a registration for a static class does not compile.

### 10. The page shows the empty state until P1-09 / P1-11 write to the ledger

`ledger_entries` is empty in the local stack (verified 2026-09-11: `select count(*)` = 0), so
`/admin/finance/money-flow` renders its "Bu davrda pul harakati yo'q" state. That is correct
behaviour, not a defect. The ring appears by itself once the accrual job (P1-09) and payment
intake (P1-11) start posting. No frontend change is needed when that happens.

### 11. New frontend dependency: `three`

`schoollms.client/package.json` gained `three@^0.185.1` (runtime) and `@types/three@^0.185.4`
(dev). Anyone rebasing onto this branch must re-run `npm ci`. Both are pinned to r185 —
three.js ships no type declarations of its own and gives no cross-minor API guarantee, so the
two versions must move together.

---

## Follow-up — reinstate connection retries correctly

`EnableRetryOnFailure` was removed from `Program.cs` (2026-09-11) because EF Core's retrying
execution strategy refuses user-initiated transactions, and the billing services use them in
five places:

- `InvoiceService.cs:262`, `:450`
- `PaymentService.cs:202`, `:352`
- `CashShiftService.cs:186`

**To restore retries:** wrap each of those transaction bodies in
`db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { ... })`, then re-enable
`EnableRetryOnFailure` in `Program.cs`. All five bodies are already idempotent on rollback
(nothing is committed before the final `SaveChanges`), and `CashShiftService`'s
`pg_advisory_xact_lock` releases automatically, so a retried block is safe.

**Why it was not caught by tests:** `PostgresFixture` and `ApiFactory` build the DbContext
without `EnableRetryOnFailure`, so the test configuration did not match production. Whoever
does this work should make the fixtures mirror `Program.cs` exactly — otherwise the next
configuration divergence surfaces in production again.

---

## From the expense module — `ExpenseService` + `ExpensesController` (2026-09-11)

New files: `SchoolLms.Application/Billing/ExpenseService.cs`,
`SchoolLms.Server/Controllers/ExpensesController.cs`,
`SchoolLms.Infrastructure/Migrations/20260911095512_ExpenseApprovalThreshold.cs`,
`SchoolLms.Tests/ExpensesTests.cs`. Wired in `Program.cs` (see item 1 above).
Routes: `GET /api/admin/expenses`, `GET /{id}`, `POST /`, `POST /{id}/approve`,
`POST /{id}/reverse` — **no `HttpPut`, no `HttpDelete`, no `HttpPatch`**, asserted by
reflection in `ExpensesTests.Controllerda_tahrirlash_va_ochirish_amallari_yoq`.

### A. **BLOCKER — `EnableRetryOnFailure` breaks every explicit money transaction in production**

This is **not** specific to expenses: it hits `PaymentService`, `InvoiceService`,
`CashShiftService` and `ExpenseService` alike, i.e. the whole money module.

`Program.cs:44` configures `npg.EnableRetryOnFailure(...)`. EF Core refuses
`Database.BeginTransaction*` under a retrying execution strategy, and
`IAppDbContext.BeginTransactionAsync` is exactly that call (`AppDbContext.cs:83`).
Verified empirically on 2026-09-11 against the test Postgres, with the production
`UseNpgsql` options copied verbatim:

```
InvalidOperationException: The configured execution strategy
'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions.
Use the execution strategy returned by 'DbContext.Database.CreateExecutionStrategy()'
to execute all the operations in the transaction as a retriable unit.
```

**Why no test catches it:** `SchoolLms.Tests/Fixtures/ApiFactory.cs:133` removes the
`AddDbContext` registration from `Program.cs` and re-adds it as
`.UseNpgsql(connectionString).UseSnakeCaseNamingConvention()` — **without** the retry
option. So every billing test exercises a DbContext that production does not use. Adding
the retry option there makes the money tests go red immediately; that redness is the bug,
not a test problem.

**Consequence if shipped as is:** `POST /api/admin/expenses` (below the threshold),
`POST /api/admin/expenses/{id}/approve` and every payment/invoice write return **500** on
the first call in production. Reads are unaffected, so a smoke test that only opens pages
looks healthy.

Two fixes, pick one — this is a one-place decision and must not be solved per service:

1. **Drop `EnableRetryOnFailure`** (one line in `Program.cs`). The money module was written
   around explicit transactions on purpose (SPEC §4.1), and a retried *implicit* save is not
   what protects it. Cost: a transient network blip surfaces as a 500 instead of being
   retried — which for a cash desk is arguably the honest answer.
2. **Keep it and run each money operation through the strategy**: expose
   `IExecutionStrategy CreateExecutionStrategy()` on `IAppDbContext` and wrap every
   `BeginTransactionAsync` block in `strategy.ExecuteAsync(...)`. Correct, and the EF-blessed
   answer, but it touches a frozen abstraction plus four services, and every future money
   path has to remember it.

Not fixed here: `Program.cs`'s DB options and `IAppDbContext` are shared decisions, and the
task that produced this module was explicitly scoped to *not* touch the retry setting.

### B. Reversing and approving an expense are **director-only**

`FinanceAction` is frozen (P1-06) and has no `ReverseExpense`, so both
`POST /{id}/approve` and `POST /{id}/reverse` use `FinanceAction.ApproveExpense`
(`superadmin` only). An `admin` can record an expense but neither approves nor reverses one;
`cashier`, `staff` and `teacher` get 403 at the class-level `[Authorize(Roles =
Roles.FinanceStaff)]` gate. If the school wants "any second admin may reverse", that is a new
`FinanceAction` + a row in `FinanceMatrix.Rules` + a line in the `CashierRoleTests` theory —
deliberately left to whoever owns that frozen file.

### C. No `audit_log` row yet — P1-14 owns it

`ExpenseService` writes no audit row, for the same reason `PaymentService` does not: SPEC
§4.6 coverage is P1-14's acceptance criterion ("every write through … and the expense path").
When it is added it must go **inside** `PostAsync`'s transaction, before `CommitAsync`,
otherwise a rolled-back expense leaves an audit entry for money that never moved.

### D. A `pending` expense can never be cancelled

An expense above the threshold that was entered by mistake stays in the director's pending
queue forever: it cannot be deleted (immutability) and it cannot be reversed (there is no
ledger batch to mirror — the endpoint answers `409 not_posted`). A `rejected` state needs a
column on `expenses`, and that entity is frozen (P1-04). Cheap to add with any later
migration: `rejected_at timestamptz`, `rejected_by uuid`, `rejected_reason text`, plus a
`POST /{id}/reject` guarded by `ApproveExpense`. Until then the queue is filtered by
`GET /api/admin/expenses?status=pending` and a stale row is visible noise, not a money error.

### E. The threshold is configurable, but not from the UI

`billing_settings.expense_approval_threshold` (`numeric(14,2)`, default **5 000 000**) is
read on every create. It is deliberately **not** added to the frozen `BillingSettingsDto` /
`UpdateBillingSettingsRequest` (`Dtos/BillingDtos.cs`), so today it changes with an `UPDATE`.
Whoever builds the finance-settings screen should add it there — it is an additive field,
which that file's header explicitly allows.

### F. `GET /api/admin/finance/money-flow` does not exist yet (P1-26)

The expense side of the ring is ready: every posted expense writes `debit expense:<category>`
/ `credit cash|bank`, and `Accounts.All` now carries all six expense nodes P1-26 asks for
(`salary`, `utilities`, `supplies`, `rent`, `repair`, `other`). `revenue:donation` is still
missing from `Accounts.All` — P1-26 lists it as an income node; adding it is one line in
`Accounts.cs` plus the list in `LedgerServiceTests`.
`ExpensesTests.Pul_aylanmasi_halqasi_balansda_qoladi` asserts the invariant that endpoint
must satisfy (income = hub = outflow, to the cent) directly on `ledger_entries`.

### G. No screen calls `/api/admin/expenses`

There is no expense page in `schoollms.client` and no entry in `navigation.ts`. The old
`FinanceController` screen still writes to `finance_transactions`, which does **not** reach
the ledger — so until the new screen exists, expenses entered through the old UI stay
invisible to P&L, cash-flow and the money-flow ring. P1-21 retires that path.

---

## For P1-20 / P1-14 — P1-18 (director finance dashboard)

### 13. Nothing has to be wired for the dashboard to work

P1-18 added five tabs **inside** the existing `FinancePage`, which is already routed
(`App.tsx:105`, `RequirePerm perm="finance"`) and already in the sidebar
(`navigation.ts:142`). `App.tsx` and `src/config/navigation.ts` were **not touched**.

New files, all additive:

```
src/api/services/financeReports.ts          typed client, real endpoints (no stubs)
src/pages/admin/finance/PnlTab.tsx
src/pages/admin/finance/CashFlowTab.tsx
src/pages/admin/finance/DebtorsTab.tsx
src/pages/admin/finance/ZReportTab.tsx
src/pages/admin/finance/VarianceTab.tsx
src/pages/admin/finance/VarianceBanner.tsx        non-dismissible counter (SPEC §4.6)
src/pages/admin/finance/CollectionRateCard.tsx
src/pages/admin/finance/ReportState.tsx           loading / error / empty, shared
src/pages/admin/finance/reportLabels.ts
src/pages/admin/finance/useVarianceWatch.ts
src/components/charts/CashFlowChart.tsx
```

**Optional for P1-20** — if the director should land on a report directly, add deep links
that preselect a tab. Today the tab lives in component state only; a `?tab=` query parameter
would be a three-line change in `FinancePage.tsx` (read `useSearchParams`, seed `useState`).
Not done here because it is not in the acceptance criteria and it invites a route discussion.

**Do not** add a second nav entry for these tabs — they are one page.

### 14. P1-14 owes the dashboard two endpoints; the client is already written against them

`VarianceTab` renders the "Sabab yozib hal qilish" button **only** when the flags endpoint
answers. Until then it shows the shift-derived list plus a visible note. The contract the
client assumes (`src/api/services/financeReports.ts`, `FinanceFlag`):

```
GET  /api/admin/finance/flags?unresolved=true   → FinanceFlag[]
POST /api/admin/finance/flags/{id}/resolve      → FinanceFlag      body: { reason }

FinanceFlag = {
  id, kind, detectedAt, message,
  refId?, amount?, resolvedAt?, resolvedReason?, resolvedByName?
}
kind ∈ shift_variance | quick_reversal | off_hours_payment | paid_without_allocation
```

`message` is rendered as-is, so it must arrive **in Uzbek** from the server. If P1-14 picks
different field names, the only file to change is `financeReports.ts` — nothing else reads
the shape.

**If skipped:** the counter keeps working off `cash_shifts.variance` (closed shifts with a
non-zero variance) and stays non-dismissible; only "resolve with a reason" is unavailable,
and the UI says so instead of pretending.

## From P1-19 — student / parent finance view

P1-19 added three new files and edited one page. It touched **no** shared file: `App.tsx`,
`src/config/navigation.ts`, `src/config/constants.ts`, `src/types/index.ts`,
`src/api/services/billing.ts`, `src/api/services/payments.ts`, `Program.cs`,
`Dtos/BillingDtos.cs` and `StudentPortalController.cs` are all untouched.

| New file | What it is |
|---|---|
| `SchoolLms.Server/Controllers/PortalFinanceController.cs` | `GET /api/student/billing`, `GET /api/student/receipts/{paymentId}.pdf` |
| `schoollms.client/src/api/services/portalFinance.ts` | typed client for the two endpoints above |
| `schoollms.client/src/pages/portal/FinanceView.tsx` | the screen; works standalone **and** embedded |

Edited: `schoollms.client/src/pages/admin/students/StudentDetailPage.tsx` — finance part only
(legacy `students.balance` badge removed from the profile header, new `Moliya` section added
after "Shaxsiy ma'lumotlar").

### 13. For P1-20 — the two routes

`FinanceView` is the page component for both portal routes. It takes no props there: the
server resolves the student from the JWT, so `/parent` and `/student` register identically.

```tsx
import { FinanceView } from '@/pages/portal/FinanceView'

<Route element={<ProtectedRoute role="parent" />}>
  <Route path="/parent" element={<AppLayout />}>
    <Route index element={<FinanceView />} />
  </Route>
</Route>

<Route element={<ProtectedRoute role="student" />}>
  <Route path="/student" element={<AppLayout />}>
    <Route index element={<FinanceView />} />

---

## From P1-16 — cashier workspace (frontend)

P1-16 added six new frontend files and **one new backend controller**. It touched **no**
shared file: `App.tsx`, `navigation.ts`, `constants.ts`, `ProtectedRoute.tsx`, `types/index.ts`,
`Program.cs`, `FinancePage.tsx` and the existing `api/services/*.ts` are all untouched.

### 13. For P1-20 — register the `/cashier` route

The component is exported as a named export:

```tsx
import { CashierPage } from '@/pages/cashier/CashierPage'
```

Add it to `App.tsx` **outside** the `/admin` tree — `ProtectedRoute role="admin"` allows
`admin | superadmin | staff` and would let a `staff` user in while keeping the `cashier`
out, which is the wrong way round on both counts:

```tsx
{/* Kassa — kassir, admin va direktor (SPEC §4.3) */}
<Route element={<ProtectedRoute roles={['cashier', 'admin', 'superadmin']} />}>
  <Route path="/cashier" element={<AppLayout />}>
    <Route index element={<CashierPage />} />
  </Route>
</Route>
```

Three things that must change with it, or the routes stay unreachable:

1. **`AuthProvider.tsx:12` blocks both roles from the web SPA**
   (`const WEB_BLOCKED_ROLES = ['student', 'parent']`). A `parent` login is rejected in
   `login()` *and* wiped in `readStoredUser()` / the `fetchMe` effect. Until that list is
   emptied, `/parent` and `/student` cannot be reached by the people they are for. The
   comment above it says the portal is mobile-only — SPEC §6 Phase 3 says "same screens as
   `/parent` and `/student` routes in the web SPA", so this is P1-20's call, not P1-19's.
2. **`homeByRole` (`navigation.ts:204-205`) points both roles at `/login`** — a logged-in
   parent hitting `/` would bounce back to the login page. Change to `/parent` and `/student`.
3. `navByRole.student` / `navByRole.parent` already carry one item each ("Bosh sahifa"),
   which is the right label once the route exists.

Nothing else is needed: `FinanceView` renders its own loading, empty, error and
no-debt states, and needs no permission key.

### 14. `PortalFinanceController` builds its services by hand (for P1-15)

Same pattern, and same reason, as `FinanceReportsController` (§3a above): the controller
takes `AppDbContext` and constructs `InvoiceService` / `ReceiptService` itself, so both
endpoints are live **without any `Program.cs` change**. Once P1-15 registers the billing
services, the two properties at the bottom of the file become constructor parameters:

```csharp
public sealed class PortalFinanceController(
    AppDbContext db, IInvoiceService invoices, IReceiptService receipts) : ControllerBase
```

`AppDbContext` is still needed for the ownership checks. **If skipped:** nothing.

### 15. The parent → child lookup exists twice

`PortalFinanceController.ResolveAsync` repeats the rule in
`StudentPortalController.TargetAsync`: a `parent` is matched to a student by comparing the
digits of their login (`users.email`, which holds a phone number) against
`students.parent_phone`. Two copies of an authorisation rule is one copy too many — but
extracting it means editing a 1 300-line controller that other tasks are using, and the
rule changes anyway when SPEC §3.2's guardian many-to-many arrives.

Whoever lands that schema change should collapse both into one helper. Today the rule also
means **a parent with two children sees only the first match** — acceptable while the
schema has a single `parent_phone` column, and exactly the thing SPEC §6 Phase 3 ("a
guardian with two children can switch between them") will fix.

### 16. No screen sends the parent's receipt anywhere new

`GET /api/student/receipts/{paymentId}.pdf` is a read: it renders the same PDF as
`/api/receipts/{id}.pdf` and never touches Telegram. Automatic delivery is still entry §10
of the P1-12 section above (`PaymentService.AcceptAsync`).

---

## For P1-20 — P1-17 (admin billing catalog) routes and navigation

P1-17 built four pages and deliberately touched **neither** `App.tsx` **nor**
`config/navigation.ts` (three other frontend tasks were in flight on the same files).
Every page is a plain named export and guards its own role, so it is safe to mount as-is.

### 13. Four routes to register in `App.tsx`

```tsx
import { CategoriesPage }    from '@/pages/admin/billing/CategoriesPage'
import { SubscriptionsPage } from '@/pages/admin/billing/SubscriptionsPage'
import { DiscountsPage }     from '@/pages/admin/billing/DiscountsPage'
import { ExpensesPage }      from '@/pages/admin/billing/ExpensesPage'
```

| Path | Element | Notes |
|---|---|---|
| `/admin/billing/categories` | `<CategoriesPage />` | fee categories reference data |
| `/admin/billing/subscriptions` | `<SubscriptionsPage />` | per-student, per-category |
| `/admin/billing/discounts` | `<DiscountsPage />` | + permanent approval queue |
| `/admin/billing/expenses` | `<ExpensesPage />` | + approval queue, storno only |

All four go **inside the existing `admin` `ProtectedRoute` branch**, next to
`/admin/finance`. Do not put them behind the cashier branch.

### 14. Navigation — one registration covers desktop and mobile

There is no separate mobile nav component: `Sidebar.tsx` is the mobile drawer as well, and
`CommandPalette.tsx` reads the same `navByRole`. So `config/navigation.ts` is the only file
to edit.

The current `Moliya` entry is a leaf; turn it into a group (same shape as `O'quvchilar`):

```ts
{
  label: 'Moliya',
  to: '/admin/finance',
  icon: Wallet,
  perm: 'finance',
  children: [
    { label: 'Umumiy',            to: '/admin/finance', end: true },
    { label: "To'lov toifalari",  to: '/admin/billing/categories',    roles: ['admin', 'superadmin'] },
    { label: 'Obunalar',          to: '/admin/billing/subscriptions', roles: ['admin', 'superadmin'] },
    { label: 'Chegirmalar',       to: '/admin/billing/discounts',     roles: ['admin', 'superadmin'] },
    { label: 'Chiqimlar',         to: '/admin/billing/expenses',      roles: ['admin', 'superadmin'] },
  ],
},
```

`roles: ['admin', 'superadmin']` is not decoration. `perm: 'finance'` alone would show these
four to a `staff` user who was granted the finance permission, and the server answers
`403` for `staff` (`Roles.FinanceStaff = admin, superadmin`). `cashier` uses its own
`navByRole.cashier` list and never sees the admin menu at all.

**If skipped:** the four pages exist, compile and are reachable by typed URL, but nothing
links to them.

---

## For P1-13 — expense endpoints P1-17 already calls

`expenses` is fully populated in the database (5 rows, 4 approved) but
`/api/admin/billing/expenses` returns `404 {"message":"API endpoint topilmadi"}` — there is
no `ExpensesController`. `ExpensesPage.tsx` calls the real URLs anyway and renders a
distinct "hali serverga ulanmagan" state for a bare 404, so the screen starts working the
moment the controller lands. No frontend change will be needed.

### 15. Four endpoints, frozen in `src/api/services/expenses.ts`

| Method | Path | Body | Response |
|---|---|---|---|
| `GET`  | `/admin/billing/expenses` | query: `from`, `to`, `category` | `ExpenseDto[]` |
| `POST` | `/admin/billing/expenses` | `{ onDate, category, amount, note? }` | `ExpenseDto` |
| `POST` | `/admin/billing/expenses/{id}/approve` | — | `ExpenseDto` |
| `POST` | `/admin/billing/expenses/{id}/reverse` | `{ reason }` | `ExpenseDto` |

`ExpenseDto` is already frozen in `Dtos/BillingDtos.cs`. Guards: class-level
`[Authorize(Roles = Roles.FinanceStaff)]`, then `[FinanceRole(FinanceAction.RecordExpense)]`
on create, `[FinanceRole(FinanceAction.ApproveExpense)]` on approve.

**There is no `DELETE` and no `PUT`, and there must not be** (SPEC §4.1). A wrong expense is
corrected by `reverse`, with a mandatory reason. The page has no delete control at all.
**There is also no `reject`**: an expense is a fact that already happened, so the two
outcomes are *approve* and *storno*, not *approve* and *deny*.

### 16. Two decisions the UI made because the DTO could not answer

1. **The approval threshold is a frontend constant.** `EXPENSE_APPROVAL_THRESHOLD =
   5_000_000` lives in `src/api/services/expenses.ts`. SPEC §4.5 says "above N so'm" but N is
   not exposed anywhere; the seed data implies 5 000 000 (the 4 600 000 row is unapproved,
   every row above 7 400 000 is approved). If P1-13 makes it a school setting, return it in
   the settings payload and delete the constant.
2. **`ExpenseDto` has no `status`.** `expenseState()` derives it from three facts:
   `reversedBy` → `reversed`, `approvedByName` → `approved`, `amount > threshold` →
   `pending`, otherwise `recorded`. If the backend later returns a real status, that one
   function is the only place to change.

The optional fields `createdById`, `approvedById`, `reversalOf`, `reversedBy` and
`reversalReason` are already declared on `ExpenseRecord`; the UI uses them when present and
degrades cleanly when absent.

---

## For whoever adds `CreatedById` to the billing DTOs

`DiscountDto` and `ExpenseDto` carry `CreatedByName` but no id, so P1-17 cannot compare the
creator to the logged-in user by id. Dual control (SPEC §4.5) is enforced in the UI by
comparing **normalised full names** — see `isOwnRecord()` in
`src/pages/admin/billing/access.ts`.

That comparison fails **closed**: two staff members with the same full name hide the approve
button from each other rather than showing it to the wrong person, and the server's
`self_approval` check (verified live: `403 self_approval`) is untouched either way.

Adding an optional `CreatedById` to both DTOs is a non-breaking change by the rules at the
top of `BillingDtos.cs`. The frontend needs **no** change when it appears: `DiscountRecord`
and `ExpenseRecord` already declare `createdById?: string`, and `isOwnRecord()` prefers the
id whenever it is present.

`ProtectedRoute` currently takes a single `role` and hard-codes the one multi-role case
(`role="admin"` → `admin | superadmin | staff`). P1-20 owns that file; the smallest change
that serves both callers is an optional `roles?: Role[]` prop checked before `role`.
Whatever shape is chosen, the acceptance criterion is the same three roles.

`RootRedirect` already sends a cashier to `/cashier` (`homeByRole.cashier`), and
`navByRole.cashier` already holds exactly one item — **no navigation change is needed**,
only the route.

**The page does not depend on the route being nested in `AppLayout`.** It renders its own
`<h1>Kassa</h1>` header and works standalone, so a full-screen cash-desk layout is also an
option if the sidebar is judged to be noise for this role.

### 14. The page's own role check is a second line, not the first

`CashierPage` refuses to draw any control for a user outside
`cashier | admin | superadmin` (it shows "Kassa bo'limi sizga ochiq emas"). That is a
fallback for a mis-registered route — **it is not the guard**. The route guard in P1-20 and
the `[FinanceRole]` gate on the server are the real ones.

### 15. `GET /api/cash/students?q=` is new — and it needed no DI

`SchoolLms.Server/Controllers/CashierStudentsController.cs` (new file) exists because a
`cashier` gets **403** from `GET /api/admin/students`: that controller sits behind
`[AdminPerm("students")]`, which admits only `admin | superadmin | staff`. Letting the
cashier through that gate would hand them the whole student CRUD, including the
login/password export.

- Route: `GET /api/cash/students?q=<kamida 2 belgi>`, max 25 rows, archived students excluded.
- Guard: `[FinanceRole(FinanceAction.AcceptPayment)]` — the existing SPEC §4.3 row, no new rule.
- Returns `CashierStudentDto(Id, FullName, ClassName, ParentFullName, ParentPhone)`. **No balance** —
  the only source of a debt figure stays `suggest-allocation`.
- Constructor takes `AppDbContext`, already registered (`Program.cs:56`), so **P1-15 has
  nothing to add for it**. Verified on a build with zero billing DI: the endpoint answers 200
  while every `/api/cash/shifts/*` route on the same build 500s.

Measured RBAC on a throwaway stack: `cashier` 200 · `admin` 200 · `staff` **403** ·
anonymous **401**.

### 16. `api/services/cashier.ts` duplicates two frozen stubs on purpose

`payments.ts` and `cashShifts.ts` (P1-06) still throw `notImplemented(...)` and point at
guessed paths (§11 above). Both belong to other Phase 1.F agents, so P1-16 wrote its own
client at `src/api/services/cashier.ts` against the **real** routes. Signatures were kept
identical to the stubs, so once those are wired the new file can become a set of
one-line re-exports. Whoever consolidates should keep three things that live only in the
new file and are not obvious:

- `getCurrentShift()` must branch on `res.status === 204`; axios gives `data === ''`, not `null`.
- `PROBE_AMOUNT = 0.01` — `suggest-allocation` returns `[]` for `amount <= 0`, so listing a
  student's open invoices before any amount is typed needs a positive probe. Only `remaining`
  is read in that call; `suggested` is ignored.
- `financeErrorCode()` / `financeErrorMessage()` read the `{ code, message }` shape that all
  four money controllers return. Branch on `code`, never on the text.

### 17. `cashTotal` is deliberately not rendered while a shift is open

`CashShiftDto` carries `cashTotal` on an **open** shift (measured: `1200000.00` before close).
Showing it in the shift bar tells the cashier what the drawer should contain, which is exactly
what SPEC §4.2 is written to prevent. `ShiftBar` therefore shows only the open time and the
receipt count. `expectedCash` / `variance` are `null` until the close call returns, so the
server does not leak them either — that is what makes the two-phase close dialog honest rather
than decorative. **Do not "improve" the shift bar by adding the total.**
- [2026-09-12] `DiscountService.ChargeFor` keeps its own copy of the discount arithmetic instead of delegating to `DiscountMath`, which `DiscountMath`'s own doc comment warns against. The three copies (`TuitionService`, `DiscountMath`, `DiscountService`) agree today — P1-23 pins all three against the same 40 pairs plus 5 000 random inputs — but nothing except those tests enforces it. Collapse to one implementation when `TuitionService` is retired.
- [2026-09-12] `InvoiceQuery` / `PaymentQuery` `MaxRows` / `MaxListRows` caps are untested. Not P1-23 scope; worth a boundary test before the pagination is exposed to the UI.

## From P1-14 — billing audit + nightly anomaly scan

P1-14 wired itself: `IAnomalyService` and the hosted `AnomalyScanService` are registered in
`Program.cs`. Nothing is left unresolvable at request time. What follows is deliberately
**not** done.

### 18. No screen renders the flags counter yet (P1-18)

`GET /api/admin/finance/flags?unresolved=true` returns `AnomalyFlagsDto`
(`Unresolved`, `Total`, `UnresolvedAmount`, `ByKind[]`, `Items[]`). SPEC §4.6 wants that
counter on the director's dashboard, un-dismissable, resolvable only with a typed reason.

| Endpoint | Method | Body | Notes |
|---|---|---|---|
| `/api/admin/finance/flags` | GET | — | `unresolved`, `kind`, `limit` (1..500, default 200) |
| `/api/admin/finance/flags/{id}/resolve` | POST | `{ "reason": "..." }` | empty/whitespace → **400 `reason_required`**; already closed → **409 `flag_already_resolved`** |
| `/api/admin/finance/flags/scan` | POST | — | idempotent, safe to double-click |

All three are `admin` + `superadmin` (`FinanceAction.ViewVarianceReport`); a cashier gets 403.
Error bodies are the usual `{ code, message }`.

`VarianceBanner.tsx` / `useVarianceWatch.ts` (P1-18) currently derive their own variance view
from the shift list. They should read this endpoint instead — it is the only source that
carries a *resolution* and therefore the only one that can stop showing a variance the
director has already explained. **Counters must come from `Unresolved`, never from
`Items.length`**: the list is filtered and capped, the counter is not.

`UnresolvedAmount` is the sum of **absolute** values, so a 50 000 shortfall and a 50 000
overage add up to 100 000 rather than cancelling out.

### 19. `audit_logs` is still fully writable by `app_rw`

The P1-14 migration revokes `UPDATE, DELETE, TRUNCATE` on `finance_anomaly_flags` but leaves
`audit_logs` alone. Nothing in the codebase updates or deletes an audit row
(`grep -rn "AuditLogs.Remove\|AuditLogs.Update"` is empty), so the same treatment would cost
nothing:

```sql
GRANT SELECT, INSERT ON public.audit_logs TO app_rw;
REVOKE UPDATE, DELETE, TRUNCATE ON public.audit_logs FROM app_rw;
```

Not done here because `audit_logs` is shared with the legacy `finance_transactions` path that
P1-21 is retiring in parallel, and an unrequested revoke on a shared table during a
parallel-agent phase is how a Sunday gets ruined. **This belongs to P1-22's security suite** —
an append-only audit log is worth more than an append-only flag table.

### 20. Cashier working hours are a constant, not a setting

`AnomalySettings.WorkDayStart` / `WorkDayEnd` (08:00–20:00 Tashkent) drive the third §4.6
condition. Making them configurable is two columns on `billing_settings`, one migration and
one read in `AnomalyService.OffHoursPaymentAsync`. Deliberately deferred: `users` has no
schedule column, and a per-cashier schedule is a feature nobody has asked for. Same file also
holds `FastReversalWindow` (24 h), `ScanLookback` (90 days) and `NightlyRunAt` (03:00).

### 21. The scan is single-instance by assumption

`AnomalyScanService` runs in-process on every replica. With one container that is correct;
with two, both would scan at 03:00. Nothing breaks — the unique `(kind, ref_id)` index makes
the second run a no-op and the service retries once on 23505 — but the second replica does the
work for nothing. If the deployment ever scales out, take a `pg_advisory_lock` at the top of
`ScanAsync` (the pattern is already in `CashShiftService.LockShiftAsync`).

### 13. Fan progresi bayram kunlarini rejadan chiqarmaydi (backend, seeder emas)

Topilgan joy: `SchoolLms.Application/Services/SubjectProgressService.cs` —
`ClassSlotsAsync` (o'quvchi/admin ko'rinishi) va `ForTeacherAsync` (o'qituvchi ilovasi).

Ikkalasi ham chorak haftalaridagi HAR bir jadval katagini `Planned` va `ExpectedByToday`
ga qo'shadi, bayram kunlarini esa tashlab yubormaydi. Jurnal buni boshqacha qiladi:
`JournalService.ComputeColumnsAsync` `db.Holidays` ni chetlab o'tadi, ya'ni bayram kuniga
ustun umuman chiqmaydi va o'qituvchi u darsni "o'tildi" deb belgilay olmaydi.

Natija: bayram tushgan hafta kunida dars beradigan o'qituvchida `conducted` doimo
`expectedByToday` dan kam bo'ladi va `ProgressScreen` qizil "Rejadan orqada" bannerini
ko'rsatadi; progress 100% ga hech qachon yetmaydi. Mahalliy demoda o'lchandi
(1-chorakda ikki bayram — 2026-09-01 va 2026-10-01):

```
karimovadilnoza  50/260 = 19%
  1-A Matematika  planned=26 conducted=5 expectedByToday=5  ok
  1-B Matematika  planned=26 conducted=5 expectedByToday=6  ORQADA   <- 2026-09-01 (Mustaqillik kuni)
  2-A Matematika  planned=26 conducted=5 expectedByToday=6  ORQADA
  ...
```

Tuzatish o'lchami: har ikkala metodda `var holidays = (await db.Holidays.Select(h => h.Date)
.ToListAsync()).ToHashSet();` va sana tekshiruviga `|| holidays.Contains(date)` qo'shish —
`ComputeColumnsAsync` dagi bilan bir xil mantiq.

Bu yerda QILINMADI: `SubjectProgressService` ni o'zgartirish o'quvchi portali va admin
hisobotlaridagi raqamlarni ham o'zgartiradi, bu esa demo ma'lumot vazifasidan tashqarida.
Seeder tomonidan "yopib qo'yish" (bayram kuniga ham `conducted=true` yozish) ataylab
qilinmadi — u jurnalda ko'rinmaydigan, yolg'on dars yozuvi bo'lardi.


## From P1-21 — legacy finance retired (2026-09-12)

Nothing here is *unwired*: P1-21 is the sequential task and it wired its own changes.
These are the four notes another agent needs so nothing is re-discovered the hard way.

### 21.1 `AuditService.cs` lost one method — the file is owned by P1-14

`AuditService.Snapshot(FinanceTransaction)` was deleted, because the `FinanceTransaction`
entity no longer exists and the file would not compile. Nothing else in the file was
touched, and `EntityFinanceTransaction` was **kept** (older `audit_logs` rows carry that
string and the audit screen filters on it).

If P1-14 needs a `before`/`after` snapshot for money, take it from the DTO the service
already returns (`ExpenseDto`, `PaymentDto`) — there is no mutable money entity left to
snapshot, which is the point of SPEC §4.1.

### 21.2 New endpoint: `POST /api/admin/billing/accrual/run`

`BillingCatalogController`, `[FinanceRole(FinanceAction.ManageSubscriptions)]` (admin +
director). Optional `?month=yyyy-MM`; without it, every unbilled month. Idempotent.

It exists because `BillingAccrualService` only ticks at startup and every 12 h, so a
freshly seeded database had subscriptions and no invoices — the demo reset in
`tools/seed_demo.py` and `tools/seed_billing.py` both depend on it. **The actor is the JWT
subject**, unlike the background job, which falls back to the first director.

### 21.3 New column: `expenses.teacher_id`

Nullable, FK → `teachers.id` (RESTRICT), with `ck_expenses_teacher_only_salary`
(`teacher_id is null or category = 'salary'`) and an index on `(teacher_id, on_date)`.
`CreateExpenseRequest`, `ExpenseQuery` and `ExpenseDto` carry it; `ExpensesController`
accepts `teacherId` in the body and as a query filter, and rejects `teacherName`
(server-derived).

Read it through `SalaryPaymentQuery` (`SchoolLms.Application/Billing/`), never directly:
it also applies the "posted and not reversed" rule, without which a pending or reversed
expense would count as salary paid.

### 21.4 Two derived read-models replace the dropped columns

| Dropped | Read it with |
|---|---|
| `students.balance` | `StudentBalanceQuery.ForAsync` / `.ForManyAsync` (bulk — use it for lists) |
| `finance_transactions` (salary) | `SalaryPaymentQuery.ForTeacherAsync` / `.ForAllAsync` |

Both are plain classes over `IAppDbContext`, constructed at the call site like
`FinanceReportQueries` — **no `Program.cs` registration needed**. Both are N+1-free by
construction; if you find yourself calling `ForAsync` inside a loop, the bulk method is
what you want.

### 21.5 Frontend surfaces that disappeared with the endpoints

`TransactionFormModal.tsx`, `PaymentModal.tsx`, `api/mock/finance.ts` and the
`overview`/`students` tabs of `FinancePage` were deleted; `api/services/finance.ts` keeps
only `getSalaryReport`. Payment intake lives at `/cashier`, expenses at
Moliya → Chiqimlar, debts at the Qarzdorlar tab.

---

## From Phase 3 — Telegram Mini App backend (2026-09-12)

Everything below is **wired**: the controllers are registered by `MapControllers`, the DbSets are
on `AppDbContext`, the migration is in the folder and the rate-limit policy is in `Program.cs`.
This section exists for the two frontend agents working in `schoollms.client/src/pages/miniapp/`
and for whoever finishes retiring `students.parent_phone`.

### T3.1 The endpoint contract

Base URL `/api`. Every response is a Pydantic-equivalent C# record from
`SchoolLms.Application/Dtos/` — no bare dictionaries. Errors are the usual `{ code, message }`
or `{ message }`.

#### Session — `/api/tg` (`TelegramAuthController`)

| Verb | Path | Auth | Request | Response |
|---|---|---|---|---|
| POST | `/api/tg/auth` | anonymous, 20/min per IP | `TgAuthRequest { initData }` | `TgAuthResponse` |
| POST | `/api/tg/link` | anonymous, 20/min per IP | `TgLinkRequest { initData, code }` | `TgAuthResponse` |
| GET | `/api/tg/me` | JWT `parent` \| `teacher` | — | `TgProfileDto` |
| DELETE | `/api/tg/link` | JWT (any role) | — | `204` |

`TgAuthResponse.status` is the only thing the shell should branch on:

* `"ok"` → `token` + `user` are set. `token` is **the same JWT `/api/auth/login` issues**, so every
  existing endpoint works with it unchanged. Store it and send `Authorization: Bearer <token>`.
* `"unlinked"` → signature was valid, this Telegram id is not linked to anyone. `telegram`
  carries `{ id, displayName, username }` for the "ask the school for a code" screen; send the
  code to `POST /api/tg/link` together with the **same** `initData`.

Failure modes: `401 invalid_init_data` (forged/expired — do **not** retry automatically),
`401 account_blocked` (archived teacher/student), `503 telegram_not_configured` (no bot token in
`SchoolMeta`), `400 invalid_code`, `409 telegram_already_linked`, `409 user_already_linked`,
`429` (rate limit).

#### Parent — `/api/tg/parent` (`TelegramParentController`), role `parent`

| Verb | Path | Query | Response |
|---|---|---|---|
| GET | `/children` | — | `TgChildDto[]` |
| GET | `/children/{studentId}/overview` | — | `TgChildOverviewDto` |
| GET | `/children/{studentId}/attendance` | `quarter?` | `StudentAttendanceFullDto` |
| GET | `/children/{studentId}/grades` | — | `StudentReportDto` |
| GET | `/children/{studentId}/schedule` | `quarter?`, `week?` | `StudentLessonDto[]` |
| GET | `/children/{studentId}/finance` | — | `PortalFinanceDto` |
| GET | `/children/{studentId}/receipts/{paymentId}.pdf` | — | `application/pdf` |
| GET | `/children/{studentId}/announcements` | — | `BroadcastDto[]` |
| GET | `/children/{studentId}/pickup` | — | `PickupRequestDto \| null` |
| POST | `/children/{studentId}/pickup` | — | `PickupRequestDto` |

A `studentId` the caller is not a guardian of returns **404**, never 403 — the existence of
another family's child is itself information. `PortalFinanceDto` is the same shape
`GET /api/student/billing` returns (`PortalFinanceController.ToPortal`, now `internal`).
`StudentLessonDto.day` is `0 = Monday … 5 = Saturday`; "today" is a client-side filter on that
field. Debt on `TgChildDto` is a **positive** number (`0` = no debt); a credit balance shows as `0`
there and in full on the finance screen.

#### Teacher — `/api/tg/teacher` (`TelegramTeacherController`), role `teacher`

| Verb | Path | Query | Response |
|---|---|---|---|
| GET | `/today` | — | `TgTeacherTodayDto` |
| GET | `/roster` | `classId`, `subjectId`, `quarter`, `date?`, `period` | `TgRosterDto` |
| GET | `/journal/recent` | `limit?` (1..100, default 30) | `TgJournalRecentDto[]` |
| GET | `/chat/unread` | — | `TgChatUnreadDto[]` |
| POST | `/chat/{channel}/read` | — | `204` |

`/roster` is the **read** side of one-tap attendance; writing is the existing
`PUT /api/teacher/journal` (`SetJournalEntryRequest`), which already refuses a future date and
checks that the teacher actually teaches that class+subject. Do not add a second write path.
Missing `TeacherPermissions` give **403** on `/roster`, `/journal/recent`, `/chat/*`; on `/today`
the schedule and unread count degrade to empty instead of failing the whole screen.

#### Admin — `/api/admin/telegram` and `/api/admin/guardians`, `[AdminPerm("app")]`

| Verb | Path | Request | Response |
|---|---|---|---|
| POST | `/api/admin/telegram/link-codes` | `IssueLinkCodeRequest { userId }` | `LinkCodeDto` |
| GET | `/api/admin/telegram/links` | `search?` | `TelegramLinkDto[]` |
| DELETE | `/api/admin/telegram/links/{telegramUserId}` | — | `204` |
| GET | `/api/admin/guardians` | `search?` | `GuardianDto[]` |
| GET | `/api/admin/guardians/by-student/{studentId}` | — | `GuardianDto[]` |
| POST | `/api/admin/guardians` | `SaveGuardianRequest` | `GuardianDto` |
| PUT | `/api/admin/guardians/{id}` | `SaveGuardianRequest` | `GuardianDto` |
| POST | `/api/admin/guardians/{id}/children` | `AttachChildRequest` | `GuardianDto` |
| DELETE | `/api/admin/guardians/{id}/children/{studentId}` | — | `204` |
| POST | `/api/admin/guardians/{id}/account` | `CreateGuardianAccountRequest` | `CredentialsDto` |

`LinkCodeDto.code` is shown **once** — only its SHA-256 is stored. Issuing a new code kills the
previous unused one for that user.

### T3.2 Endpoints deliberately NOT duplicated under `/api/tg`

The Mini App token is an ordinary JWT, so these already work as-is and adding a `/api/tg/...`
alias would mean two surfaces to keep in step. **Call them directly.**

| Need | Existing endpoint | Role |
|---|---|---|
| Canteen menu (day / range) | `GET /api/student/canteen/{date}`, `GET /api/student/canteen?start=&end=` | `parent` |
| Quarters, lesson times, absence reasons | `GET /api/student/meta` · `GET /api/teacher/meta` | `parent` / `teacher` |
| School name, holidays | `GET /api/student/school`, `/holidays` (and `/api/teacher/...`) | both |
| Teacher salary summary | `GET /api/teacher/salary?from=&to=` → `SalaryLedgerDto` | `teacher` |
| Teacher classes / full week schedule | `GET /api/teacher/classes`, `GET /api/teacher/schedule` | `teacher` |
| Write attendance / grade | `PUT /api/teacher/journal` | `teacher` |
| Accept a pickup | `POST /api/teacher/pickups/{id}/accept` | `teacher` |

### T3.3 `students.parent_phone` is still the source of truth — follow-up

`guardians` / `student_guardians` (SPEC §3.2) are populated two ways and **both read from
`parent_phone`**:

* the migration backfills every existing row (`Migrations/Sql/guardians_backfill.sql`);
* `GuardianSync.EnsureAsync` / `.EnsureManyAsync` mirror it on student create, update and Excel
  import (`StudentsController`).

The mirror is **one-way**. Editing a guardian in `/api/admin/guardians` does not write back to
`students.parent_phone`, so after such an edit the two disagree for that family until the student
row is saved again. That is deliberate — P1-21 has just finished one large retirement and a second
one was explicitly out of scope — but it is the thing to finish next:

1. Move `StudentPortalController.TargetAsync` and `PortalFinanceController.ResolveAsync` onto
   `GuardianAccess` (this also closes §15 above: today they are two copies of one rule, and both
   still show a two-child parent only the first child).
2. Move the student create/edit form's parent fields onto the guardian editor.
3. Drop `students.parent_phone`, `parent_full_name`, `parent_last_name`, `parent_first_name`,
   `parent_middle_name`, `parent_passport_url` — **12+ readers**: `TelegramBotService`,
   `MessagesController.Personalize`, `ExcelImport`/`ExcelExport`, `ContractService`,
   `ParentsController`, `StudentsController`, `StudentProfileBuilder`, `StudentReportBuilder`,
   `CashierStudentDto`, the student list screen, the student card screen, the import template.

Until then, treat `parent_phone` as the input and `guardians` as the index built over it.

### T3.4 No Mini App navigation entry in the admin SPA

Two new admin screens have endpoints and no UI: **Vasiylar** (`/api/admin/guardians`) and
**Telegram bog'lanishlari** (`/api/admin/telegram/links` + the code issuer). Both sit naturally
under the existing "Ilova" section, which is why they are gated by `[AdminPerm("app")]`. Nothing
breaks without them — but until the code issuer exists in the UI, **the only way to link a live
Telegram account is `curl`**, and the council demo depends on the seeded links instead
(`tools/seed_demo.py`, section 18).

### T3.5 `chat_reads` is teacher-only so far

`POST /api/tg/teacher/chat/{channel}/read` is the only writer. The admin chat screen and the
student/parent chat still have no read state, so their unread counts are still "compare against
the last message you sent". The table is not teacher-specific — `(user_id, channel)` — so those
screens can start using it without a migration.
