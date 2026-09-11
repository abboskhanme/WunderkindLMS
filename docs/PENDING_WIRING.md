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
