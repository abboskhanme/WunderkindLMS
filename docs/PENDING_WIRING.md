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
