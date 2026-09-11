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
