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
