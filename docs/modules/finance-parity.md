# Finance (Moliya) — screen-level parity with EduSchool

**Status:** discovery, documentation only. No code, no migration, no entity, no page.
**Date:** 2026-09-17 · **Author:** discovery analyst (Phase 0 of `docs/PLAN-STUDENTS-FINANCE.md`)
**Audience:** the build agents who implement from this file. They cannot see the conversation that
produced it; everything needed is here or is named here by exact path.

**Read first:** `docs/modules/existing-module-gaps.md` §3 and §7.3–7.4 (settled decisions this file
does not re-open), `docs/modules/hr.md` (payroll, owns *Ish haqi*), `docs/SPEC.md` §4 (append-only
ledger).

Contents: §0 method and one correction · §1 summary · §2 screen by screen · §3 schema changes,
consolidated · §4 build slices · §5 open questions.

---

## 0. Method, evidence, and one correction to earlier docs

### 0.1 Labels

| Label | Meaning |
|---|---|
| **[bundle]** | Read out of `/Users/abboskhan/Documents/Projects/WunderkindLMS/.eduschool-bundle/all.js` (7 427 lines, 8.1 MB), `edu-menu.json` or `edu-roles.json`. Field names, endpoint paths and i18n keys are quoted verbatim. |
| **[inferred]** | Not literally in the bundle; deduced from names plus a sibling that is. The reasoning is given. |
| **[ours]** | Read out of this repository at commit `7e95329` (master, 2026-09-17). |
| **[unrecoverable]** | Referenced by the bundle but never captured, or server-driven. Recorded, not guessed. |
| **[decision]** | Ours, with the reason; cites the settled decision where one exists. |
| **[defect]** | A bug in our code found while comparing; verified by reading, not by running. |

**No request reached the live tenant.** Every EduSchool fact came from the files on disk (`CLAUDE.md`,
read-only rule).

### 0.2 How the bundle was read

`all.js` is the entry chunk `index-BgjLwN0j.js` (lines 3 503–7 427) with about 70 lazy chunks
concatenated in front of it (lines 1–3 502). A lazy chunk imports its API constants from the entry
chunk under a two-letter alias (`import{…,jS as Le,kl as Re}from"./index-BgjLwN0j.js"`); every
alias below was resolved against the entry chunk's final `export{…}` and its string constants
(`EPe="/cashboxes"`, `cLt="fin-map/cashboxes"`, …). **i18n values are not in the bundle** (fetched
at runtime), so labels below are i18n **keys**; the Uzbek our screens render is ours to choose.

### 0.3 Which Finance chunks the bundle contains

The router (offset ≈ 8 121 000) maps each `FINANCE_ALL` path to a lazy component. Chunks were matched
to screens by what they render (endpoints, i18n key families, `data-testid`s):

| Menu key | Route | Router role | Chunk | In bundle? (all.js line) |
|---|---|---|---|---|
| `FINANCE_ALL.CASH` "Moliya" | `/cash/*` | `cashboxGet` | `index-CqqCIv49.js` + `CashInfo-BHtu6zGY.js` | page **yes** (2 483–2 496); `CashInfo` drawer **no** |
| `FINANCE_ALL.DEBTORS` | `/debtors` | `debtors` | `index-DoHUgxuf.js` | **yes** (3 397); columns server-driven |
| `FINANCE_ALL.MONTHLY_SALARY` "Ish haqi" | `/salary/*` | `_id` (menu `getEmployeeSalaryData`) | `index-rEx_5p1H.js` | **yes** (3 457) |
| — create a salary run | `/salary/monthly-salary` | `_id` | `index-BkU2-dlJ.js`, `FlexSalaryDetailsDialog`, `TeacherLessonDetailsModal` | **no** |
| `FINANCE_ALL.FINANCIAL_REPORTS` | `/financial-reports` | `getFinanceReport` | `index-sbnpRYQP.js` | **yes** (3 458–3 497) |
| `FINANCE_ALL.PNL_REPORTS` | `/pnl-reports` | `getFinanceReport` | `index-B2ZETHkv.js` | **yes** (176) |
| `FINANCE_ALL.PNL_EXPECTATION` "P&L 2.0" (beta) | `/pnl-expectation` | `getFinanceReport` | `expectation-CJsrxdEN.js` | **yes** (170) |
| `FINANCE_ALL.CASHFLOW` "Pul oqimi" | `/cashflow` | `getFinanceReport` | `index-Dd69IViC.js` | **yes** (3 164) |
| `FINANCE_ALL.FINANCIAL_ANALYTICS` (beta) | `/financial-analytics` | `financialAnalytics` | `index-DUmn5ZdU.js` | **yes** (2 515–3 160) |
| `FINANCE_ALL.TRANSACTIONS` | `/transactions` | `getTransactions` | inline in entry chunk (`Uxt`) | **yes** |
| `FINANCE_ALL.SUBSCRIPTION_TRANSACTIONS` | `/subscription-transactions` | `getSubscriptionTransactions` | inline in entry chunk (`E4t`) | **yes** |
| `FINANCE_ALL.BONUS` | `/bonus` | `getBonus` | `index-hdn8zlsO.js` | **yes** (3 455) |
| `FINANCE_ALL.PENALTY` "Jarima" | `/penalty` | `getPenalty` | `index-Cqb7ly97.js` | **yes** (2 482) |
| `FINANCE_ALL.SUBSCRIPTION_TRANSACTIONS_PIVOT` | `/subscription-transactions-pivot` | `getSubscriptionTransactions` | `index-YRWmIM9x.js` | **yes** (3 429) |
| `SETTINGS_ALL.FINANCE_SETTINGS` — every catalogue the Finance forms pick from | `/finance-settings` | `FinanceSettings` | `index-DSOxJJQb.js` | **yes** (2 498–2 513) |
| subscription plan detail | `/aboniment/:id` | `subscriptionGet` | `index-4tl3PuWY.js` | **no** |
| discount detail | `/discount/:id` | `getDiscount` | `index-C1TbDQds.js` | **no** |

### 0.4 Correction — `fin-map` is *Moliya analitikasi*, not *Moliya*

`existing-module-gaps.md` §3.2 and `PLAN-STUDENTS-FINANCE.md` §1 map the `fin-map/*` dashboard to
`FINANCE_ALL.CASH` (`/cash`). **The bundle says otherwise [bundle]:**

- `/cash` renders the **cashbox workspace**: cashbox cards, a per-cashbox transaction table and the
  income / expense / transfer / exchange drawers. It calls `/cashboxes`, `/cashbox/transaction/pagin`,
  `/cashbox/transaction/total`, `/cashbox/income`, `/cashbox/expense`, `/cashbox/exchange` — never
  `fin-map/*`.
- `fin-map/*` is called only by the chunk at lines 2 515–3 160, whose context hook throws
  `"useFinanceAnalytics must be used within a FinanceAnalyticsProvider"` and whose title key is
  `financial_analytics.title` — i.e. `/financial-analytics`, permission `financialAnalytics`.

So our **Kassa kuni** (`CashDayPage.tsx`) is the counterpart of *Moliya analitikasi*, and *Moliya*
itself is the cash desk — whose counterparts are our `/cashier` page plus *Chiqimlar*. The §3.2
decision (a read-only daily dashboard over the ledger) is unaffected; only the menu mapping moves.

### 0.5 Two more corrections

- **`docs/modules/hr.md` §9 says "HR-1 ~118 h", but its own task table (HR-01…HR-19) sums to
  206 h** (12+3+10+6+10+10+24+12+20+6+2+22+20+14+3+4+12+12+4). HR-2 (46 h) and HR-3 (24 h) add up.
  Plan with 206 h until `hr.md` is re-estimated.
- **`hr.md` §2.7** says both Bonus and Jarima pick from `transaction-types?type=vouncher`. The bundle:
  Bonus uses `type=vouncher`, **Jarima uses `type=penalty`**, and the Jarima form carries an image.

---

## 1. Summary

Verdicts: `have` · `partial` · `missing` · `declined` (a single school does not need it — settled) ·
`refused (§4)` (breaks `SPEC.md` §4 — settled). Hours = backend + frontend to close every **P0 and
P1** gap on that row; P2 in brackets. Gap ids refer to §2.

| # | EduSchool screen | EduSchool, in one line **[bundle]** | Ours **[ours]** | Verdict | Hours P0+P1 (P2) |
|---|---|---|---|---|---|
| 2.1 | **Moliya** (`/cash`) | cashbox cards, per-cashbox table, income / expense / transfer / exchange drawers, allocation of a payment to unpaid charges, subscription attach/cancel with refund preview, receipt auto-print | `/cashier` (shift bar, student search, split allocation, receipt PDF/Telegram) + `/admin/billing/expenses` + Z-report | **partial** — and **creating or approving an expense from the UI fails** (F1.01–F1.02) | **79** (12) |
| 2.2 | **Qarzdorlar bilan ishlash** | debtor list with month filter, search, export, status + promised-date action, status catalogue | `DebtorsTab.tsx` + debtor workflow (shipped 2026-09-17) | **partial** — month filter missing; one status bug | **6** (11) |
| 2.3 | **Ish haqi** | salary runs: list, per-employee fix/flex/bonus/fine/paid, cancel whole or part, lesson details, xlsx | salary report tab + hourly salary calculator + salary as an expense | **partial** — payroll is `hr.md` HR-1; **staff can post salary payments** (F3.05) | **221** (0) — 206 of it is HR-1 |
| 2.4 | **Moliya hisobotlari** | KPI in/out/remainder with % change, daily chart, breakdown by type or method with drill-down, discount summary | P&L tab (by account), cash-flow tab | **partial** | **25** (15) |
| 2.5 | **Moliya hisobotlari (P&L)** | year × month matrix, start/end balance, drill-down to lines, dividends, xlsx | `PnlTab.tsx` (one period, by account, CSV) | **partial** | **22** (7) |
| 2.6 | **P&L 2.0** | plan vs fact revenue model, change journal, planned-expense templates | none | **missing** — deferred by `existing-module-gaps.md` §3.6 | 0 (**78**) |
| 2.7 | **Pul oqimi** | operating / investing / financing by category, month or year, drill-down, xlsx | `CashFlowTab.tsx` (cash/bank totals by month, chart, CSV) | **partial** | **16** (4) |
| 2.8 | **Moliya analitikasi** (`fin-map`) | per-method balances, calendar with in/out/balance, day drill, journal, 12-month chart, top-5 | `CashDayPage.tsx` + calendar, `CashFlowTab` chart, `MoneyFlowPage.tsx` | **partial** | **1** (15) |
| 2.9 | **Tranzaksiyalar** | every cashbox transaction, filters, add income/expense, export | **none** — `GET /api/billing/payments` exists, no screen | **missing** | **36** (1) |
| 2.10 | **Abonement tranzaksiyalari** | every subscription charge and return, to-be-paid / paid, period | **none** — `InvoiceService.ListAsync` / `VoidAsync` have no endpoint | **missing** | **24** (5) |
| 2.11 | **Bonus** | one-off credit to a student or employee, cancel | none | **missing** | **12** (7) |
| 2.12 | **Jarima** | one-off charge to a student or employee with a photo, cancel | none | **missing** | **11** (6) |
| 2.13 | **Qarzdorlik oyma-oy** | student × subscription × month pivot, filters, export | `ArrearsPage.tsx` (shipped 2026-09-16) | **have** | 0 (20) |
| 2.14 | Finance settings (reachable catalogues) | transaction types, payment methods, plans, discounts, currency, planned expenses, tax, debtor statuses | categories, subscriptions, discounts pages; no billing-settings screen | **partial** | **7** (11) |
| 2.15 | Cross-cutting | xlsx everywhere, per-user columns | CSV on some screens; money reads open to staff | **partial** | **7** (1) |
| — | Cashboxes, transfers, exchange, currency, editable type tree / methods, branch views | — | one desk, `cash` + `bank` accounts | **declined** | — |
| — | Cancel a transaction, back-dating, stored `beforeAmount/afterAmount`, delete-all, flag-cancel of bonus/fine/salary | — | reversal (storno), derived balances | **refused (§4)** | — |

**Totals:** P0 **69 h** · P1 **398 h** (206 of it HR-1) · P2 **193 h**.

### 1.1 The P0 list — the school cannot run its money daily without these

| Id | Gap | Hours BE+FE |
|---|---|---|
| F1.01 | **[defect]** "Yangi chiqim" always fails — the client sends no `method`, the server requires one | 0+2 |
| F1.02 | **[defect]** approving a pending expense always fails (no body); client guesses status from a hard-coded 5 M threshold; double approval not locked | 1+2 |
| F1.03 | a cash expense does not lower the shift's expected cash → every cash expense shows up as a shortage at close | 6+2 |
| F3.05 | **[defect, access]** `POST /api/admin/teachers/{id}/salary-payments` is guarded only by `AdminPerm("teachers")`; staff can post salary money and change pay rates; bonus changes are not audited | 3+1 |
| F9.01 | transaction journal — one list of every payment, storno and expense, filters, receipt-number search, totals | 12+12 |
| F9.02 | reverse a payment from the UI (the endpoint exists, no screen calls it) | 0+6 |
| F10.01 | invoice register — every accrued charge, filterable | 4+10 |
| F10.02 | void a wrong invoice from the UI (the service exists, no endpoint; it also refuses stornoed invoices and throws unmapped exceptions) | 5+3 |

---

## 2. Screen by screen

### 2.0 What we do not copy, and the equivalent

Settled in `existing-module-gaps.md` §3.3, §3.4.4, §7.3, §7.4 and `SPEC.md` §4. Not re-opened; listed so
a build agent meeting one of these controls in §2 knows the answer.

| EduSchool control **[bundle]** | Where | Verdict | Our equivalent **[ours]** |
|---|---|---|---|
| Several named cashboxes, *main* cashbox, responsible employee, online cashbox (`/cashbox`, `/cashbox-main`, `/cashbox/responsible`) | 2.1 | **declined** | the cash **shift**; money lives in two accounts, `cash` and `bank` |
| Transfer between cashboxes or branches with `waiting → accepted \| rejected` | 2.1, 2.9 | **declined** | none needed; cash leaving the drawer is gap **F1.04** (a handover, not a cashbox transfer) |
| Currency exchange between payment methods (`/cashbox/exchange`), currency catalogue (`/money-type`) | 2.1, 2.14 | **declined** | so'm only |
| Editable transaction-type tree, editable payment methods | 2.14 | **declined** | closed `Accounts.cs` and `PaymentMethod` |
| **Cancel** a transaction (`PUT /cashbox/cancel`, journal delete icon) | 2.1, 2.8 | **refused (§4)** | **storno** — `POST /api/admin/billing/payments/{id}/reverse`, `POST /api/admin/expenses/{id}/reverse` |
| **Back-dated** transaction (`actualDate`, permission `actualDateTransaction`) | 2.1 | **refused (§4)** | a payment is dated by the server inside an open shift. (An expense's `on_date` may be in the past by design — `ExpenseService.cs:252-262` — because an expense records when money was spent, and it does not enter a shift's arithmetic until F1.03.) |
| **Stored running balance** `beforeAmount` / `afterAmount` (subscription rows, bonus/fine rows, salary lines, employee transactions) | 2.3, 2.10, 2.11 | **refused (§4)** | derived per request (`StudentBalanceQuery.cs`); where a screen needs "before / after", compute it in the query, never store it |
| **Delete all subscription transactions** (`/student/subscription/delete-all`) | 2.10 | **refused (§4)** | void invoices, reverse payments |
| **Undo last subscription cancellation** (`/student/subscription/cancel-return`) | 2.10 | **refused as a mutation** | start a new subscription from the cancellation date; the cancellation stays in history |
| Cancel a bonus / fine by flagging it (`/bonus-cancel`, `/penalty-cancel`) | 2.11 | **refused as a flag** | append a reversing row (F11.01) |
| Cancel a salary run, whole or per employee (`/salary/cancel`) | 2.3 | **refused as a flag** | reverse the payroll document / payroll payment (`hr.md` §3.2, §6.6) |
| Edit a transaction's comment (`/cashbox/transaction/comment`) | 2.1 | **refused** — an UPDATE on `payments` | the note is fixed at receipt time; corrections go in the storno reason |
| Cashbox balance visible to the cashier during a shift | 2.1 | **refused** — our shift count is blind on purpose (`ShiftBar.tsx:31-34`: the count is made without seeing the expected figure) | admin/director see it on Kassa kuni |
| Catalogue discount picked at the till (`discountId` in the income drawer) | 2.1 | **declined** — every discount needs the director (`SPEC.md` §8.1 Q5) | discount request → approval (`DiscountsPage.tsx`) |
| Attach / cancel a subscription from the till | 2.1 | **declined** for the cashier (`SPEC.md` §4.3) | admin on *Obunalar*; ending with preview is F1.06 |
| SMS to the parents of selected payers | 2.1 | **declined** — Telegram is the only channel, and a debtor broadcast already exists (`POST /api/admin/messages/broadcast`, `OnlyDebtors`) | — |
| `SYSTEM_SUBSCRIPTION` tab (paying EduSchool itself) | 2.14 | **not applicable** | — |
| Branch selector, "all branches" totals | 2.6, 2.8 | **declined** — single branch | school totals |
| Assigned moderator (student curator) filter | 2.1, 2.9 | **declined here** — a Students-module attribute; see the Students parity document (`docs/modules/students-parity.md`, written in parallel) | — |

---

### 2.1 Moliya — the cash desk (`FINANCE_ALL.CASH`, `/cash`)

#### 2.1.1 EduSchool **[bundle]**

**Layout.** Header: title `FINANCE.CASH.TITLE`, date-range picker (default today 00:00 → 23:59),
*Filter* toggle, *Export* (permission `exportCashboxTransaction`). Left ≈ 30 %: cashbox cards. Right
≈ 70 %: the selected cashbox's balances and transaction table.

**Filters** (collapsible, written to the URL):

| Filter | Param | Type / source |
|---|---|---|
| Student | `studentId` | async select `students/pagin` |
| Class | `classId` | async select `/class/pagin`, label `grade-letter` |
| Payment method | `paymentMethodId` | select `payment-methods` |
| Transaction method | `type` | `payIn` · `payOut` · `transfer` |
| Transaction type | `transactionTypeId` | select `transaction-types?type=payIn\|payOut` |
| Assigned moderator | `assignedModeratorId` | moderators; only when setting `assignedModeratorEnabled` |
| Date range | `fromDate`, `toDate` | header picker |

**Cashbox cards** (`GET /cashboxes`): title `FINANCE.CASH.CASHES`, an eye toggle that masks every
amount as `... so'm`, a `+` button. Card: balance, name, branch, responsible employee. The active
card adds *Edit*, *make main* (confirm → `POST /cashbox-main {cashboxId}`) and four buttons:
`INCOME` (payIn), `EXPENSE` (payOut), `MOVING` (transfer, permission `cashboxTransfer`), `EXCHANGE`
(also gated by `cashboxTransfer`). Cashbox form: `name`, `isOnlinePayment`; on edit with
`cashboxEdit`: responsible employee (`PUT /cashbox/responsible`), `isIncomeCashbox`,
`isExpenseCashbox` for integration cashboxes; `DELETE /cashbox/{id}`.

**Right column:** one card per payment method with the cashbox balance in it; period totals payIn ↑ /
payOut ↓ from `GET /cashbox/transaction/total?cashboxId&fromDate&toDate`; search
`FINANCE.CASH.SEARCH_BY_CHECK_NUMBER` (500 ms debounce); a bulk button fed with the students of the
selected rows (component from `index-I7lil56t.js`, which loads `SmsTemplates` — **[inferred]** SMS to
those students). Table `GET /cashbox/transaction/pagin?cashboxId&fromDate&toDate&search&page&limit`;
rows selectable only when not a transfer, not cancelled, and with a student:

| # | Column key | Source | Notes |
|---|---|---|---|
| 1 | `TABLE.DATE` | `createdAt` | `DD.MM.YYYY \| HH:mm` |
| 2 | `TABLE.WHO` | `student.fullName` or employee name | link to the card |
| 3 | `TABLE.CONTRACT_NUMBER` | `student.contractNumber` | not sortable |
| 4 | `TABLE.AMOUNT` | `amount` | masked when hidden |
| 5 | `TABLE.TRANSACTION_TYPE` | `transactionType.name` or `type` | |
| 6 | `TABLE.STATE` | `state` | transfers: `waiting` (+ accept / reject / cancel icons), `accepted`, `cancelled`, `rejected`; others: `cancelled` |
| 7 | `TABLE.PAYMENT_METHOD` | `paymentMethod.name` | |
| 8 | `TABLE.CASHIER` | cashier name | `TABLE.ONLINE_PAYMENT` when null |
| 9 | `TABLE.COMMENT` | `comment` | |
| 10 | `TABLE.REASON` | `reasonInfo.name` or `reason` | not sortable |
| 11 | `TABLE.TYPE` | `type` | payIn green / payOut red / transfer icon |

No sort configured. Row click → `CashInfo` drawer **[unrecoverable]**; the endpoints it can reach
are `PUT /cashbox/cancel {_id}` (`cashboxCancel`) and `/cashbox/transaction/comment`.

**Export** modal `general.select_export_version`: *old* `/cashbox/transaction/export?…`, *new*
`/cashbox/transaction/export-v2?…` (params `page, limit, cashboxId, studentId, classId,
paymentMethodId, type, fromDate, toDate`); file columns **[unrecoverable]**.

**Income / expense drawer** (`POST /cashbox/income` · `POST /cashbox/expense`):

| Field | Type | Rule / default | Shown when |
|---|---|---|---|
| `transactionTypeId` | select `transaction-types?type=payIn\|payOut` (object) | carries `hasImpactOn` (`student`\|`employee`\|`other`), `isPrePayment`, `isSalary` | always |
| `employeeId` | select `employees/pagin` (name + phone) | shows `GET /employees/balance/{id}` when `canSeeStudentBalance` and type `isPrePayment` or `isSalary`; role chip | `hasImpactOn = employee` |
| `assignedModeratorId` | select moderators | optional, narrows the student picker | setting on, income to a student |
| `studentId` | select `/students/pagin?noArchive=true` | shows balance (`canSeeStudentBalance`) and the subscription (name, price, months) | `hasImpactOn = student` |
| *Attach / update subscription* | button → modals below | — | student type, `setStudentSubscription` |
| `paymentMethodId` | `payment-methods?isActive=true&showOnIncomeExpence=true` | — | always |
| `forMonthes[]` | rows of `amount` (≥ 0, required) + month picker (required, no month twice); "+ add" | default one row, current month | **every expense, and income not to a student** |
| `amount` | number ≥ 0, required | disabled = Σ `forMonthes.amount` when rows are used | always |
| *Debtor subscriptions* | allocation modal (below) | required before submit when unpaid charges exist and amount > 0 | student income, `GET /student/unpaid-subscription-transactions/{id}` non-empty |
| `discountId` | `/discount/pagin` | optional | student income, setting `onTimePrivilegeEnabled` off |
| `actualDate` | date | today | permission `actualDateTransaction` |
| `comment` | multiline | optional | always |
| `fileUrls[]` | image uploads (`POST /upload-file`) with thumbnails | optional | expense only |

Client guard: an expense or transfer may not exceed that method's balance in the cashbox
(`"Mablag' yetarli emas"`). Body: form + `cashboxId` + `forMonthes[{amount,month,year}]` +
`unpaids[{transactionId, subscriptionId, amount}]`. On success a receipt renders from `GET /receipt`
settings (header, footer, logo, number, amount, cashbox, customer, comment, type, method, branch,
date, discount, cashier, parent-app QR, `templateType: bankSlip` with bank details) and prints
automatically when `autoPrint` is on.

**Allocation modal** (`DEBTOR_SUBSCRIPTIONS_MODAL_TITLE`, 920 px): entered amount; table checkbox ·
subscription · `toBePaid` · allocated (+ *partial*) · period; the button pre-fills oldest-first;
footer total allocated / remaining; save refused unless 0 < allocated ≤ amount.

**Transfer drawer** (`POST /cashbox/payment-transfer` · `POST /cashbox/branch-transfer`):
`isBranchTransfer`, `toCashboxId` (`/cashboxes-main`) or `toBranchCashboxId` (`/cashboxes-branch`),
`paymentMethodId` (`showOnTransfer=true`), `amount`, `actualDate`, `comment`.
**Exchange drawer** (`POST /cashbox/exchange {_id}`): `fromAmount`, `fromPaymentMethodId`, `toAmount`
(defaults to `fromAmount`), `toPaymentMethodId`, live difference, `actualDate`, `comment`.

**Subscription modals** (also on the student card):
- *Attach* — `subscriptionId` (`/subscription/pagin?state=active`), `activatesAt`, `endsAt` →
  `POST /student/subscription {subscriptionId, studentIds[], activatesAt, endsAt,
  existStudentSubsciptionEdit:'onlyFreeStudents'}`.
- *Cancel* — header: student, subscription, charging range, price for the whole period; radio
  `completeReSubscription` | `reSubscriptionFromChoosenDate` (+ `activatesAt`); each change calls
  `POST /student/subscription/cancel/preview` → `remainingPrice`, `returningPrice`, `totalPaid`,
  `returnForPeriod`, `transactionIds`; editable `remainingPrice` (`SERVICE_PROVIDED_AMOUNT`) and
  `returningPrice` (`WRITEOFF_AMOUNT`), each 0 ≤ x ≤ `totalPaid`, one recomputes the other, both
  locked without `updateAbonimentAmount`; `comment` → `POST /student/subscription/cancel`.

**Sidebar badge:** the `/cash` menu item shows `GET /finance/expense/reminder/unseen-count` (planned
expense reminders, §2.6).

**Permissions:** `cashboxGet`, `cashboxEdit`, `cashboxDelete`, `cashboxCancel`, `cashboxTransfer`,
`cashboxExchange`, `cashboxBranch`, `cashboxGetWithIntegrationPayment`, `transactionsMakeIncome`,
`transactionsMakeExpense`, `exportCashboxTransaction`, `actualDateTransaction`,
`canSeeStudentBalance`, `setStudentSubscription`, `cancelStudentSubscription`, `updateAbonimentAmount`.

#### 2.1.2 Ours **[ours]**

- **`/cashier`** — `schoollms.client/src/pages/cashier/CashierPage.tsx` (roles cashier, admin,
  superadmin; **no admin menu entry points here**). Without an open shift nothing but the shift bar
  renders. Search "O'quvchini qidirish" (≥ 2 chars, `GET /api/cash/students?q`: name, parent,
  phone; archived excluded). Student card: class, parent, phone, "Jami qarz". "Ochiq
  hisob-fakturalar" (category, period, remaining). Form: "Summa (so'm)" > 0, "To'lov usuli" (Naqd /
  Karta / Bank o'tkazmasi / Onlayn), "Izoh"; "Butun qarzni qo'yish". `PaymentSplitModal.tsx`:
  Toifa · Davr · Qarz qoldig'i · Yo'naltiriladi (server FIFO `GET /api/cash/payments/suggest-allocation`),
  remainder becomes an advance → `POST /api/cash/payments`. `ReceiptPreview.tsx`: PDF
  (`GET /api/receipts/{id}.pdf`) and **manual** "Telegramga yuborish".
- **`ShiftBar.tsx`** — open with "Ochilish qoldig'i"; close with "Sanalgan naqd" (blind: expected is
  shown only after submit); no running totals by design.
- **`/admin/billing/expenses`** — `ExpensesPage.tsx` + `ExpenseFormModal.tsx`: filters Sanadan /
  Sanagacha / Toifa; pending queue (built on the client, only inside the date filter); columns Sana,
  Toifa, Summa, Izoh, Kim yozdi, Kim tasdiqladi, Holat, Amal; storno with reason. Form: Sana (≤ today),
  Toifa (salary, utilities, supplies, rent, repair, other), Summa > 0, Izoh — **no method, no teacher,
  no attachment**.
- **Server:** `PaymentService.cs` (accept, storno with two-man rule and open-shift requirement),
  `ExpenseService.cs` (threshold `billing_settings.expense_approval_threshold`, approval, storno),
  `CashShiftService.cs` (`ExpectedCashAsync` counts only the shift's payments and stornos),
  `ReceiptService.cs`. Subscriptions are ended on *Obunalar* (`EndSubscriptionModal.tsx`, end date
  only).

#### 2.1.3 Gaps

| Id | What is missing | Files it touches (ours) | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F1.01 | **[defect]** "Yangi chiqim" fails: `ExpenseInput` in `api/services/expenses.ts:92-97` has no `method`; `ExpenseService.RequireMethod` (`:711-716`) throws `invalid_method`. Add "To'lov usuli" (Naqd · Karta · O'tkazma · Onlayn, default Naqd) and send it. | `schoollms.client/src/api/services/expenses.ts`, `pages/admin/billing/ExpenseFormModal.tsx` | no | 0 | 2 | **P0** |
| F1.02 | **[defect]** "Tasdiqlash" fails: `approveExpense` posts no body, `ExpensesController.Approve` (`:177-190`) needs `{method}` — add a method picker to the approval. Use the server's `Status` instead of the client's hard-coded 5 000 000 threshold (`expenses.ts:43,75-79`). Serialise approval (advisory lock on the expense id) so a double click cannot post twice. | `expenses.ts`, `ExpensesPage.tsx`, `SchoolLms.Application/Billing/ExpenseService.cs` | no | 1 | 2 | **P0** |
| F1.03 | **Cash expenses leave the drawer.** EduSchool's payOut lowers the cashbox balance; ours posts `credit cash` but `CashShiftService.ExpectedCashAsync` (`:528-565`) ignores expenses, so every cash expense becomes a false shortage and a `shift_variance` flag. A cash expense (method `cash`) is attached to the **recorder's open shift** (or, when pending, the **approver's** open shift at approval) — 409 `no_open_shift` otherwise, the same rule payment storno already uses — and `ExpectedCashAsync` and the Z-report subtract that shift's cash expenses. A storno of a cash expense puts the money back into the **reverser's** open shift (refused with 409 `no_open_shift` otherwise, mirroring payment storno); the shift is derived from the reverser's shift window at the mirror lines' `created_at`, so no column is needed on the reversal. Closed shifts are never recomputed. Z-report gains a "Chiqimlar" line. | `ExpenseService.cs`, `CashShiftService.cs`, `ExpensesController.cs`, `ZReportTab.tsx`, `ExpenseFormModal.tsx` (hint) | **yes** — `expenses.cash_shift_id` | 6 | 2 | **P0** · Q3 |
| F1.04 | **Cash handover** — money leaving the drawer that is not an expense: deposit to the bank, or hand-over to the director's safe. Today the `cash` account only grows and the next shift's opening float is typed by hand, so Kassa kuni's closing cash drifts from reality. Not EduSchool's cashbox transfer (declined): no `cashbox_id`, only our two accounts. From an open shift: amount, destination `bank` \| `safe`, note; `bank` posts `debit bank / credit cash`, `safe` posts nothing (still school cash) but lowers the shift's expected cash; reversal by a mirror row; audit. | new `SchoolLms.Application/Billing/CashHandoverService.cs`, new `SchoolLms.Server/Controllers/CashHandoversController.cs`, `CashShiftService.cs`, `ShiftBar.tsx`, `ZReportTab.tsx`, new `api/services/cashHandovers.ts` | **yes** — `cash_handovers` (**financial, REVOKE**) | 10 | 5 | P1 · Q1 |
| F1.05 | **Refund of an advance** to a parent (EduSchool: `returningPrice` + payOut to a student). Today only a full storno exists, which also undoes allocated money. Admin requests (student, amount ≤ current advance, method, reason); **director approves** (≠ requester); on approval posts `debit receivable / credit cash\|bank`, cash refunds need the approver's open shift; `StudentBalanceQuery` advance = Σ effective payments − allocations − Σ posted, non-reversed refunds; receipt-like PDF optional (P2). | new `StudentRefundService.cs`, new `StudentRefundsController.cs`, `StudentBalanceQuery.cs`, `FinanceReportQueries.cs` is **not** touched (debt ignores advance), `CashShiftService.cs` (expected cash), new `pages/admin/billing/RefundsPage.tsx` or a modal on the invoice register | **yes** — `student_refunds` (**financial, REVOKE + column grant**) | 12 | 8 | P1 · Q2 |
| F1.06 | **Ending a subscription with a preview.** EduSchool previews what was served vs written off; ours only sets `EndsOn`, and invoices already accrued for later months stay as debt. `POST /api/admin/billing/subscriptions/{id}/end/preview {endsOn}` → invoices with `period_month` after the end month: amount, paid, remaining, voidable (no effective allocation) or "storno first"; `POST …/end {endsOn, voidInvoiceIds[], reason}` voids them in the same transaction through `InvoiceService.VoidAsync`. A partial last month is **not prorated** (our rule); a discount request covers a goodwill reduction. | `SubscriptionService.cs`, `BillingCatalogController.cs`, `EndSubscriptionModal.tsx`, `api/services/billingCatalog.ts` | no | 6 | 5 | P1 |
| F1.07 | **Receipt to Telegram automatically** after a payment is accepted — `SPEC.md` §4.7 ("receipts … are also sent to the guardian's Telegram account"); today only on a click. Fire-and-forget after commit; the manual button stays for re-send. | `PaymentService.cs` or `PaymentsController.cs` (after `AcceptAsync`), `ReceiptService.cs` | no | 2 | 0 | P1 |
| F1.08 | **Photos / scans on an expense** (EduSchool `fileUrls[]`). Reuse `POST /api/admin/uploads` (`UploadsController.cs`); attach on create, show thumbnails in the list; no delete (evidence). | `ExpenseService.cs`, `ExpensesController.cs` (`POST {id}/attachments`, GET), `ExpenseFormModal.tsx`, `ExpensesPage.tsx` | **yes** — `expense_attachments` | 5 | 4 | P1 |
| F1.09 | **Salary expense to a named teacher from *Chiqimlar*.** The service already accepts `TeacherId` only for `salary`; the form has no picker. When Toifa = "Oylik maosh", show a teacher select and that teacher's month *hisoblangan / berilgan / qoldiq* (`GET /api/admin/teachers/{id}/salary-ledger`); add an "O'qituvchi" column. (EduSchool shows the employee's balance for salary / pre-payment types.) | `ExpenseFormModal.tsx`, `ExpensesPage.tsx`, `expenses.ts` | no | 0 | 5 | P1 |
| F1.10 | **Concurrent allocation** — two cashiers can over-allocate the same invoice (`PaymentService.cs:208-216`, no lock). Take `pg_advisory_xact_lock` per invoice id in `AcceptAsync` before the remaining check. | `PaymentService.cs` | no | 3 | 0 | P1 |
| F1.11 | Menu entry **"Kassa"** under *Moliya* for admin / superadmin (EduSchool's *Moliya* is the admin's own screen; `/cashier` already admits them). | `schoollms.client/src/config/navigation.ts` (orchestrator) | no | 0 | 0.5 | P1 |
| F1.12 | Receipt settings: header / footer text, logo, parent-app QR, auto-print on accept (EduSchool `GET /receipt`). | `ReceiptDocument.cs`, `ReceiptService.cs`, billing-settings screen (F14.01) | **yes** — 4 columns on `billing_settings` | 4 | 4 | P2 |
| F1.13 | An expense for a *month* (EduSchool `forMonthes[]`, e.g. September rent paid in October). | `ExpenseService.cs`, `ExpenseFormModal.tsx`, P&L matrix F5.01 reads it | **yes** — `expenses.period_month` | 2 | 2 | P2 |

---

### 2.2 Qarzdorlar bilan ishlash (`FINANCE_ALL.DEBTORS`, `/debtors`)

#### 2.2.1 EduSchool **[bundle]**

- Title `debtors.page_title`; **search** box; **export** when `debtorsExport` →
  `GET /debtor/students/export` (same filters).
- Data `GET /debtor/students`. **Columns are server-driven** (`POST /table-settings/get
  {page_name:"debtor_pagin"}`) **[unrecoverable]**. The client overrides one column,
  `column_balance_currency`, red and bold, value `monthlyDebt ?? balance` — the chosen month's debt,
  or the total balance when no month is chosen.
- Header filters: **month** (`month=YYYY-MM`, clearable), **class** (`/class/pagin`), **group**
  (`/groups/pagin`).
- Row action `debtors.add_action` (flag `debtors`) → modal: subtitle `fullName · grade-letter · phone`;
  `statusId` **required** (`/debtor-action-statuses?isActive=true`, hint `debtors.status_hint`);
  `expectedPaymentDate` optional (helper `current_payment_date: DD.MM.YYYY` or `payment_date_hint`);
  `comment` optional → `POST /debtor/action {studentId, statusId, comment?, expectedPaymentDate?, month?}`
  (`month` = the page's month filter).
- Status catalogue (Finance settings, add/edit need `debtorStatusEdit`): `name` required, `color`,
  `isActive` → `POST|PUT /debtor-action-status`, `DELETE /debtor-action-status {_id}`.
- Student card tab `debt-history` (`GET /debtor/actions?studentId`): `month` (MMMM YYYY),
  `balanceAtAction` (red), `employee.fullName`, `status.name` (chip in its colour), `comment`,
  `expectedPaymentDate` (`old → new`, old struck through when `paymentDateChanged`).
- Permissions `debtors`, `debtorsExport`, `debtorStatusEdit`.

#### 2.2.2 Ours **[ours]**

`schoollms.client/src/pages/admin/finance/DebtorsTab.tsx` — a tab of `FinancePage` (admin,
superadmin). Sources `GET /api/admin/finance/debtors` (`FinanceReportQueries.DebtorsAsync`) merged
with `GET /api/admin/finance/debtors/workflow`. Filters: search "O'quvchi yoki telefon" (client),
class (client), "Faqat muddati o'tganlar" (server), "Arxivdagilar ham" (server, default on). KPIs:
Jami qarz, Qarzdorlar, Muddati o'tgan qarz, Buzilgan va'da; per-category cards; embedded
`CollectionRateCard`. Columns: O'quvchi, Sinf, Telefon, one per category, Jami qarz, Kechikish, Eng
eski oy, Holat (coloured), Oxirgi amal, Va'da (red when broken), action; footer totals; CSV.
`DebtorActionModal.tsx`: "Nima qilindi" (required, ≤ 2000), "Holat" (default "O'zgartirilmasin"),
"Va'da qilingan to'lov sanasi" (± 366 days) + history + soft delete. `DebtorStatusesPage.tsx`
(`/admin/finance/debtor-statuses`): name, colour, position, hint, retire / restore. Broken promises
become an anomaly kind (`BrokenPromiseScan.cs`).

#### 2.2.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F2.01 | **[defect]** a comment-only action ("O'zgartirilmasin", `status_id = null` = *unchanged* per `Debtors.cs:99-103`) blanks the list's Holat: `DebtorWorkflowService.RowsAsync` (`:412-422`) takes the latest action's `StatusId`. Take the latest action **with** a non-null status. | `SchoolLms.Application/Billing/DebtorWorkflowService.cs`, `SchoolLms.Tests/DebtorWorkflowTests.cs` (regression) | no | 1 | 0 | P1 |
| F2.02 | **Month filter** — only students owing for a chosen invoice month, amount = that month's remaining (EduSchool `month`, `monthlyDebt`). `DebtorReportQuery.Month` → filter invoices by `period_month`, reuse `EffectiveAllocations`. | `FinanceReportQueries.cs`, `FinanceReportsController.cs`, `DebtorsTab.tsx`, `api/services/financeReports.ts` | no | 3 | 2 | P1 |
| F2.03 | The action records which month it concerns (EduSchool sends `month`). | `DebtorWorkflowService.cs`, `DebtorActionModal.tsx`, `debtorWorkflow.ts` | **yes** — `debtor_actions.period_month` | 1 | 1 | P2 |
| F2.04 | Group filter. | after `PLAN` §4 step B (`study_group_members`) | no | 1 | 1 | P2 |
| F2.05 | Server xlsx export with the same filters (ours: client CSV of visible rows). | `FinanceReportsController.cs` (`debtors/export`), `ExcelExport.cs` via F0.01 | no | 2 | 1 | P2 |
| F2.06 | Debt history on the student card with "debt at the time" and "promised date changed from → to" — both **derived** (ledger as of `created_at`; previous action's `promised_on`), never stored. | `DebtorWorkflowService.cs` (`ActionsAsync` DTO); the card tab is owned by `students-parity.md` | no | 3 | 0 | P2 |
| F2.07 | Own menu entry "Qarzdorlar bilan ishlash" (EduSchool has it top-level; ours is a tab). A route rendering `DebtorsTab` standalone. | `App.tsx`, `navigation.ts` (orchestrator) | no | 0 | 1 | P2 |

---

### 2.3 Ish haqi (`FINANCE_ALL.MONTHLY_SALARY`, `/salary`)

#### 2.3.1 EduSchool **[bundle]**

- List `GET /salary/pagin` (title `general.monthly_salary`, *add* → `/salary/monthly-salary`,
  create page **[unrecoverable]**). Columns `TABLE.DATE` (`fromDate – toDate`),
  `TABLE.EMPLOYEE_COUNT` (`employeeSalaryParams.length`), `TABLE.ALL_PRICE` (`totalSalaryAmount`),
  cancel icon (`cancelEmployeeSalary` → confirm `toast_messages.cancelSalary` →
  `POST /salary/cancel {_id}`).
- Row click → 1 200 px modal (title = date range), select-all checkbox grid: checkbox ·
  `general.employee` · `fix_salary` (`fixSalary`) · `flex_salary` (`flexSalary`) · `bonus` · `punish` ·
  `current_balanse` (`beforeAmount`) · `salary` · `balanse_from_salary` (`afterAmount`) ·
  `salary.lesson_details.title` (icon → per-teacher lesson breakdown, **[unrecoverable]**).
- With rows ticked: *Oylikni bekor qilish* → `POST /salary/cancel {_id, employeeIds[]}`. Export is
  **client-side** xlsx (sheet `OylikMaoshTarixi`, file `OylikMaoshTarixi_YYYYMMDD_YYYYMMDD.xlsx`, bold
  header on `#D9E1F2`, the nine columns + date range; ticked rows only when any are ticked).
- Employee card tab *transactions* (`GET /employees/transactions?employeeId`): transaction type (or
  `monthlySalaryCancelled`), method, state, amount, `beforeAmount`/`afterAmount` (only with
  `canSeeStudentBalance`), comment; row action *lesson details*.
- Permissions `getEmployeeSalaryData`, `createEmployeeSalary`, `cancelEmployeeSalary`,
  `getEmployeeSalary`.

`hr.md` §2.8 already classifies this as the legacy payroll that EduSchool's own HR module replaced.

#### 2.3.2 Ours **[ours]**

- `FinancePage.tsx` tab "O'qituvchilar" (`GET /api/admin/finance/salary-report?from&to`): O'qituvchi,
  Oylik, Hisoblangan, Berilgan, Qoldiq, Tarix; KPIs; CSV; row → `finance/TeacherSalaryDetailModal.tsx`
  (per-month expected / paid / remaining).
- `pages/admin/schedule/SalaryCalcPage.tsx` (`/admin/teachers/salary`): hourly rate per teacher
  category, lessons × rate − absences + bonus %, bulk bonus.
- Salary is paid as an expense `category = salary` with `teacher_id`; `SalaryPaymentQuery.cs`,
  `SalaryLedger.cs`. **No payroll documents, no non-teacher salaries, no run cancel, no HR code at all.**

#### 2.3.3 Mapping onto `hr.md`

| EduSchool *Ish haqi* element | `hr.md` | Covered? |
|---|---|---|
| Run list (period, employees, total) | `payroll_documents` list, HR-13 | yes |
| Per-employee line fix / flex / bonus / fine / paid | `payroll_lines` (`card_paid + cash_paid` fixed part, lesson amount, `bonus_amount`, `penalty_amount`, `net`), HR-09 / HR-13 | yes |
| `beforeAmount` / `afterAmount` | — | **no** → F3.02 (derived, never stored) |
| Cancel whole run | reverse a posted document (§3.2) | yes |
| Cancel for selected employees | reverse the individual `payroll_payments` (§6.6); an un-accrual for a subset = reverse the document and re-post without them | equivalent |
| Lesson details per teacher | hourly payroll row drawer (§6.4, HR-13) | yes |
| xlsx export | HR-23 (in HR-2) | pulled forward → F3.04 |
| Manual bonus / fine feeding the line | — (`hr.md` only has rule-based `hr_rules`) | **no** → F3.03 + §2.11 |
| Pay a line from the till | `payroll_payments` with `cash_shift_id` | yes |

#### 2.3.4 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F3.01 | **Payroll spine** — `hr.md` HR-01…HR-19 as specified. Blocking client questions: `hr.md` §11 Q1 (gross formula) and Q6 (card/cash tax base). | per `hr.md` §9 | **yes** — `hr.md` §4.2 (22 tables; `payroll_payments` and `hr_timesheet_corrections` append-only) | 147 | 59 | P1 |
| F3.02 | Per employee on a run: *owed before · accrued · paid · owed after*, **derived** from posted `payroll_lines.net` minus non-reversed `payroll_payments` (EduSchool stores `beforeAmount/afterAmount` — refused). | HR-09 `PayrollService`, HR-13 detail page | no | 3 | 2 | P1 |
| F3.03 | Manual one-off bonus / fine (§2.11 F11.01) summed into `bonus_amount` / `penalty_amount` at fill; posted payroll locks them. | HR-09 fill | via F11.01 | 3 | 0 | P1 |
| F3.04 | Run xlsx export (pull HR-23's payroll part into HR-1). | HR-23 | no | 3 | 0 | P1 |
| F3.05 | **[defect, access]** `POST /api/admin/teachers/{id}/salary-payments` (`TeachersController.cs:302`) is guarded only by `[AdminPerm("teachers")]` — staff with `teachers` post salary money past `FinanceAction.RecordExpense`; `SalaryCalcPage` lets the same staff change rates; `SalaryRatesController.SetBonus` / `SetBonusBulk` write no audit row. Add `[Authorize(Roles = Roles.FinanceStaff)]` + `[FinanceRole(FinanceAction.RecordExpense)]` to the salary write, admin/superadmin on rate and bonus writes, audit both bonus writes, and an in-page role check on `SalaryCalcPage`. | `TeachersController.cs`, `SalaryRatesController.cs`, `pages/admin/schedule/SalaryCalcPage.tsx`, `SchoolLms.Tests/Security/*` | no | 3 | 1 | **P0** |

---

### 2.4 Moliya hisobotlari (`FINANCE_ALL.FINANCIAL_REPORTS`, `/financial-reports`)

#### 2.4.1 EduSchool **[bundle]**

Header: title `financial_reports.title`; **export** (`exportTransactionAnalytics`) →
`GET /transaction/analytics/export?cashboxId&studentId&paymentMethodId&type&fromDate&toDate`;
filters **cashbox**, **payment method**, **date range** (default current month, not clearable).
Common query `cashboxId?, paymentMethodId?, fromDate, toDate`.

| Widget | Endpoint | What it shows |
|---|---|---|
| KPI *Kirim* | `GET transaction/analytics` → `payIn {totalPayIn, percent}` | amount, % change with ↑/↓, mini doughnut of the % |
| KPI *Chiqim* | same → `payOut {totalPayOut, percent}` | same |
| KPI `remainder` | same → `residue {amount, percent}` | same |
| `graphic` | `GET transaction/analytics/graph` → `[{_id:'payIn'\|'payOut', data:[{date, amount}]}]` | daily payIn vs payOut, **line ⇄ bar**, x `DD.MM`, y K/M/B |
| `by_transaction` | `GET transaction/analytics/cycle?filter=transaction` → `[{_id, total, data:[{transaction{name,color}, amount}]}]` | doughnut, radio payIn/payOut, centre total + "N ta tranzaksiya" |
| Income breakdown | `GET transaction/analytics/income?filter=transaction\|payment` | radio by type / by method; rows name · % bar · amount; parent rows expand; a type leaf drills into `GET transaction/analytics/type?type=payIn&transactionTypeId=` → Date · Who · Method · Amount · Comment, row → CashInfo |
| Expense breakdown | `GET transaction/analytics/expense?filter=…` | mirror, red |
| `discount_summary` | `GET transaction/discount/total` → `{totalAmount, totalCount, data:[{discountId, discount{name,amount}, totalAmount, totalCount}]}` | tiles total / count / average; a card per discount (share %, name, uses, value, total, bar) → `/discount/{id}?fromDate&toDate` |

Permissions `getFinanceReport`, `exportTransactionAnalytics`.

#### 2.4.2 Ours **[ours]**

No single dashboard. `PnlTab.tsx` gives revenue and expense **by account** with share %; `CashFlowTab.tsx`
gives cash/bank in/out by month with a chart; `MoneyFlowPage.tsx` a 3D ring. No % change, no daily
chart, no by-method breakdown, no drill-down, no discount summary, no xlsx.

#### 2.4.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F4.01 | **"Moliya hisobotlari" page**: KPI *Kirim* (Σ debit to `cash`+`bank` from payments), *Chiqim* (Σ credit), *Qoldiq* (in − out), each with % change against the previous period of equal length; daily in/out chart (line ⇄ bar); filters date range + payment method. Stornos net out on their own day (the Kassa kuni rule, `CashDayQueries.cs:43-56`). | new `SchoolLms.Application/Billing/FinanceDashboardQueries.cs`, new `SchoolLms.Server/Controllers/FinanceStatementsController.cs` (`GET /api/admin/finance/dashboard`), new `pages/admin/finance/FinancialReportsPage.tsx`, new `api/services/financeStatements.ts` | no | 6 | 8 | P1 |
| F4.02 | Income and expense **breakdown by account or by payment method**, % bar per row, **drill-down** to the transactions behind a row (Date · Who · Method · Amount · Note) using the journal endpoint F9.01 with `account` / `method` / date filters. | same files | no | 5 | 6 | P1 |
| F4.03 | Doughnut by account with total and count. | `FinancialReportsPage.tsx` | no | 1 | 3 | P2 |
| F4.04 | Discount summary for the period: total `invoices.discount`, number of discounted invoices, average; one card per discount reason (per discount type once F14.02 exists). | `FinanceDashboardQueries.cs` | no | 4 | 4 | P2 |
| F4.05 | xlsx export of the period's rows. | via F9.03 | no | 2 | 1 | P2 |

Cashbox filter: declined (§2.0).

---

### 2.5 Moliya hisobotlari (P&L) (`FINANCE_ALL.PNL_REPORTS`, `/pnl-reports`)

#### 2.5.1 EduSchool **[bundle]**

- Filter **year** only; export `GET reports/pnl/export?year=` (file `P&L-{year}`).
- Data `GET /reports/pnl/parent-child?year=`, two shapes:
  - **Monthly matrix** (`rows`, `months[]`): columns = months; rows in order `startBalance[]`,
    **`totalIncome[]`** (collapsible) + one row per income category, **`totalExpense[]`**
    (collapsible) + one row per expense category **with child rows** (`children[].transaction.name`,
    `amount[]`), `profit[]`, `dividends[]`, `endBalance[]`.
  - **Single period** (`revenue.total`, `revenue.byCategory[]` with expandable `children[]`, the same
    for `expenses`, `netProfit`).
- Every amount cell → dialog `pnl_reports.detailsTitle` listing
  `GET /reports/pnl/details?year&month?&categoryId&type=income|expense`: `details_date` ·
  `details_source` · `details_person` · `details_amount` · `details_type`, numbered.
- Permission `getFinanceReport`.

#### 2.5.2 Ours **[ours]**

`PnlTab.tsx`: period from `FinancePage` (default 1 January → today); `GET /api/admin/finance/pnl`
(`ProfitLossAsync`, ledger `revenue:*` and `expense:*` in `[from, to]`); KPIs Daromad / Xarajat / Sof
natija; two tables Toifa · Summa · Ulush with footer; "Davr yakuni"; CSV. No months, no balances,
no drill-down.

#### 2.5.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F5.01 | **Year × month matrix** (12 columns + total): revenue rows per `revenue:*`, expense rows per `expense:*`, totals collapsible, profit row. Same sign rule as `ProfitLossAsync`, which is **not** modified. | new `SchoolLms.Application/Billing/FinanceStatementsQueries.cs` (`ProfitLossMatrixAsync`), `FinanceStatementsController.cs` (`GET pnl/matrix?year`), `PnlTab.tsx` (mode toggle *Davr* / *Yil*), `financeStatements.ts` | no | 5 | 6 | P1 |
| F5.02 | Start / end balance rows (`cash` + `bank` at month start and end; reuse the opening rule of `CashFlowAsync`). | same | no | 2 | 1 | P1 |
| F5.03 | **Drill-down** from any cell to its ledger lines: date · source (payment / invoice / expense / reversal, with receipt №) · person (student / teacher / author) · amount. | `FinanceStatementsQueries.cs` (`LedgerDetailsAsync`), controller `GET ledger/details?account&from&to`, new `pages/admin/finance/LedgerDetailsModal.tsx` | no | 4 | 4 | P1 |
| F5.04 | Child rows under an expense category (EduSchool parent/child types). | — | — | — | — | **declined** — `Accounts.cs` is closed and flat (§3.3) |
| F5.05 | Dividends / owner withdrawals row. Needs an equity account in `Accounts.cs`. | `Accounts.cs`, `ExpenseService.cs` or a new movement | no table; one account code | 3 | 1 | P2 · Q7 |
| F5.06 | xlsx export. | controller + F0.01 | no | 2 | 1 | P2 |

---

### 2.6 Moliya hisobotlari (P&L) 2.0 (`FINANCE_ALL.PNL_EXPECTATION`, `/pnl-expectation`, beta)

#### 2.6.1 EduSchool **[bundle]**

A plan-versus-fact revenue model. Header: branch (`allBranches` + each), year, month, `asOf` date.
Tabs `pnl_reports.tabs.*`:

| Tab | Endpoint | Content |
|---|---|---|
| `expectation` | `GET /reports/pnl/expectation?year&month&asOf&branchId` | KPI tiles `students` (paid / partial), `net` (expected net, collection rate for the period, collected / outstanding), `discount` (actual vs planned rate), `collected` (cash basis), `expense`, `profit`; a **plan · fact · diff** table grouped *students* (`row_studentsCount`, `row_admitted`, `row_departed`, `row_studentsExpected`, `row_studentsPaid`), *revenue* (`row_expectedRevenue`, `row_grossExpected`, `row_discountNegative`, `row_discountRate`, `row_netExpected`, `row_perStudentNet`), *changes* (one row per change kind, `row_changeTotal`, `row_unexplained`), *cash* (`row_collectedForPeriod`, `row_collectionRateForPeriod`, `row_outstandingForPeriod`, `row_collected`), *expense* (`row_expenseTotal` + categories), *result* (`row_profit`, `row_margin`, `row_profitPerStudent`); a bridge chart opening plan → current; discount table `discCol_type · discCol_students · discCol_plan · discCol_fact · discCol_diff`; drill-downs `GET …/expectation/students?kind=` |
| `dynamics` | `GET /reports/pnl/expectation/daily?year&month` | daily chart with forecast and "today" marker |
| `changes` | `GET /reports/pnl/expectation/changes?year&month&asOf&kind&page&limit` | immutable journal `colDate · colEvent · colStudent · colGross · colDiscount · colNetEffect · colAuthor`; kinds `studentJoined`, `studentLeft`, `discountAdded`, `discountRemoved`, `discountChanged`, `tariffChanged` |
| `yearly` | `GET /reports/pnl/expectation/yearly?year` | KPI year net / discount / expense / profit / best month / worst month; month × (plan · fact · diff) for gross, discount, net, expense, profit, margin %, discount rate %; mode fact / plan |
| `planned` | `GET /reports/pnl/expectation/planned-expense?year&month` | KPI plan / accrued vs plan / unplanned + rate / corrected / not accrued; table `colName · colBranchPlan · colTotalPlan · colAccrued · colDiff · colState`, legend over / under / unplanned / corrected |

**Planned-expense templates** (Finance settings `planned-expense`, `GET|POST|PUT|DELETE
/finance/expense/template`, `expenseTemplateGet|Edit|Delete`): `name` required, `transactionTypeId`
(payOut), `amount`, `frequency` (`monthly` only), `status` (`active`\|`inactive`), `startDate`,
`endDate` (≥ start, optional), `reminderDay` (optional). Reminders feed the `/cash` badge.

#### 2.6.2 Ours **[ours]**

None. Nearest inputs: `CollectionRateAsync` (accrued vs collected per month), active
`student_subscriptions`, approved `discounts`, `audit_log` (subscription and discount changes).

#### 2.6.3 Gaps — all P2, the §3.6 deferral stands unless the client lifts it (Q6)

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F6.01 | Planned-expense templates + monthly reminder on the director's dashboard (Telegram to the director, not SMS). | new `ExpenseTemplateService.cs`, controller, settings page | **yes** — `expense_templates` | 8 | 8 | P2 |
| F6.02 | Month *expectation* plan · fact · diff: students, gross from active subscriptions, discount, net, collected, outstanding, expense plan vs posted, profit. | new `RevenueExpectationQueries.cs`, page | no | 16 | 14 | P2 |
| F6.03 | Change journal derived from `student_subscriptions`, `discounts`, student archive dates and `audit_log` — no new table. | same | no | 8 | 6 | P2 |
| F6.04 | Daily dynamics. | same | no | 3 | 4 | P2 |
| F6.05 | Yearly plan / fact table with best / worst month. | same | no | 5 | 6 | P2 |

---

### 2.7 Pul oqimi (`FINANCE_ALL.CASHFLOW`, `/cashflow`)

#### 2.7.1 EduSchool **[bundle]**

- Filters **year** (default current), **month** (optional). Export `GET reports/cash-flow/export?year&month?`.
- Data `GET reports/cash-flow?year&month?`:
  - **Month chosen** — `rowLabel · amount`; sections `operating`, `investing`, `financing`, each:
    `cashIn` total + one row per category, `cashOut` total + rows, `net`; last row `netCashFlow`.
  - **Year only** — `months[]` columns; `startBalances[]`; the three sections with `totalIn[]`,
    `in[{category, id, data[]}]`, `totalOut[]`, `out[]`, `net[]`; `endBalances[]`.
- A category amount with an `id` → dialog `cashflow.detailsTitle` listing
  `GET /cashbox/transaction/pagin?transactionTypeId&fromDate&toDate`.
- The operating / investing / financing classification is not on the transaction-type form →
  server-side **[unrecoverable]**. Permission `getFinanceReport`.

#### 2.7.2 Ours **[ours]**

`CashFlowTab.tsx` (`GET /api/admin/finance/cashflow`, `CashFlowAsync`, ≤ 120 months): view Jami / Kassa
(naqd) / Bank; KPIs opening / in / out / closing; Recharts chart (bars in/out, line balance); monthly
table Oy · Boshlang'ich · Kirim · Chiqim · Sof · Yakuniy; per-account cards; CSV. **In and out are
single totals — no categories.**

#### 2.7.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F7.01 | **In / out by category**, month and year modes: cash-in split by the revenue account of the invoices each payment was allocated to (unallocated → "Avans"), cash-out by expense account (and refunds / handovers once F1.04–F1.05 exist); stornos net within their category. | `FinanceStatementsQueries.cs` (`CashFlowStatementAsync`), `FinanceStatementsController.cs`, `CashFlowTab.tsx` | no | 6 | 6 | P1 |
| F7.02 | Operating / investing / financing sections — **[decision]** a static map in code next to `Accounts.cs` (every current account is operating); no editable field. | same | no | 1 | 1 | P2 |
| F7.03 | Drill-down from a category cell to its transactions (journal F9.01 filtered). | `CashFlowTab.tsx` | no | 1 | 3 | P1 |
| F7.04 | xlsx export. | controller + F0.01 | no | 2 | 0 | P2 |

---

### 2.8 Moliya analitikasi (`FINANCE_ALL.FINANCIAL_ANALYTICS`, `/financial-analytics`, beta) — `fin-map`

#### 2.8.1 EduSchool **[bundle]**

**Left panel** (collapsible) — `GET fin-map/cashboxes` → `{total:{total, payments[{_id,name,amount}]},
branchs[{_id, branch{name}, total, payments[]}]}`: a "total of all branches" card with one line per
payment method, then one card per branch. A method line jumps to the journal tab filtered by it.

**Tabs** `calendar` · `journal` · `money-flow`, plus a branch selector.
- *Calendar* — **year**, **month**; toggle calendar / bar. Calendar (`GET fin-map/calendar?year&month`
  → `{year,month,day,payIn,payOut,balance}`; opening balance `GET /fin-map/last-day`): Monday-first,
  each day `+payIn`, `−payOut`, closing `balance`. Bar chart: daily closing balance, negatives red.
  A selected day opens two sub-tabs: `cashbox_balance` (`GET fin-map/daily-cash-balance?date` →
  `paymentMethod · payIn · payOut · afterAmount` + total row `allPayIn · allPayOut ·
  allAfterAmount`) and `daily_transactions` (`GET fin-map/daily-transaction?date&paymentMethodId?` →
  `date · amount (±) · branch · cashbox`).
- *Journal* — date range (default current month); export `GET fin-map/daily-transaction/export?…`;
  table `GET /fin-map/daily-transaction-pagin`: `TABLE.DATE` · `TABLE.AMOUNT` (±, coloured) ·
  `TABLE.TRANSACTION_TYPE` · `TABLE.PAYMENT_METHOD` · `financial_reports.cashbox` · cancel icon
  (hidden when cancelled) → confirm → `PUT /cashbox/cancel {_id}`.
- *Money-flow* — chart / table toggle; PDF export (jsPDF + html2canvas). Chart: `GET /fin-map/monthly-chart`
  → `[{month, payIn, payOut, balance}]` bars + balance line; doughnuts
  `top_5_income_distribution` / `top_5_expense_distribution` from `GET fin-map/top-five?fromDate&toDate`.
  Table (`GET fin-map/transaction-type` + `GET /fin-map/monthly-last-amount`): month columns, rows
  *Oy boshidagi qoldiq*, *Oy oxiridagi qoldiq*, *Sof pul oqimi*, *Tushumlar* (expandable by type),
  *Chiqimlar* (expandable by type).

Permission `financialAnalytics`.

#### 2.8.2 Ours **[ours]**

`CashDayPage.tsx` + `CashDayCalendar.tsx` (admin, superadmin): date, "Bugun", "Yangilash";
`GET /api/admin/finance/cash-day?date` and `…/calendar?month`. Sections: open-shift cards (float,
cash so far, "Javonda bo'lishi kerak", non-cash); KPIs Kun boshidagi qoldiq / Kirim / Chiqim / Kun
oxiridagi qoldiq; "Hisoblar kesimida" (cash, bank: Kun boshi · Kirim · Chiqim · Sof · Kun oxiri);
monthly calendar (net per day, month footer); "Turlar bo'yicha"; "Toifalar bo'yicha" (incl. advance);
top 5 of the day; "Kun harakatlari" (Vaqt · Nima · Hisob + method · Chek · Kim · Summa). No export.
`CashFlowTab.tsx` already has the 12-month in/out bars with a balance line; `MoneyFlowPage.tsx` shows
sources and uses for a range.

#### 2.8.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F8.01 | Journal tab / link: from Kassa kuni open the transaction journal (F9.01) pre-filtered to the day or the month. | `CashDayPage.tsx` | no | 0 | 1 | P1 |
| F8.02 | Day inflow **by payment method** (Naqd · Karta · O'tkazma · Onlayn) next to the cash / bank split. Outflow by method is not stored for expenses — show outflow by account only. | `CashDayQueries.cs`, `CashDayPage.tsx`, `api/services/cashDay.ts` | no | 3 | 2 | P2 |
| F8.03 | Calendar cell shows `+kirim`, `−chiqim` and closing (ours: net only; `days[]` already carries all three). | `CashDayCalendar.tsx` | no | 0 | 2 | P2 |
| F8.04 | Top-5 income and expense **accounts for a range** as doughnuts. | `MoneyFlowPage.tsx` (data exists in `MoneyFlowDto`) | no | 0 | 3 | P2 |
| F8.05 | Print / PDF of a report screen (print stylesheet, not a PDF library). | `CashDayPage.tsx`, `FinancialReportsPage.tsx` | no | 0 | 3 | P2 |
| F8.06 | Daily closing-balance bar chart. | `CashDayCalendar.tsx` | no | 0 | 2 | P2 |
| — | Per-method *balances* in a left panel | — | — | — | — | **declined** — only cash vs bank are balances in our ledger; card, transfer and online all settle to `bank` (`Accounts.SettlementFor`) |
| — | Monthly cash table by type with opening / closing | — | — | — | — | covered by F7.01 |

---

### 2.9 Tranzaksiyalar (`FINANCE_ALL.TRANSACTIONS`, `/transactions`)

#### 2.9.1 EduSchool **[bundle]**

- Title `general.invoices`; `GET /cashbox/transaction/pagin` over all cashboxes; default range the
  current month; **no search**; header export (`exportCashboxTransaction` → `/cashbox/transaction/export-list`)
  plus the old/new version modal once a cashbox is chosen (params `page, limit, cashboxId, studentId,
  paymentMethodId, type, state, cashierId, fromDate, toDate`).
- Header: checkbox **`dashboard.first_payment_student_count`** (`isFirstPayment=true`); *Filter*
  toggle (state kept in sessionStorage); **Kirim** / **Chiqim** buttons.
- Filters `cashboxId`, `state` (`accepted` · `rejected` · `cancelled` · `waiting`), `studentId`,
  `cashierId`, `assignedModeratorId` (setting-gated).
- Columns: `TABLE.WHO` (link) · `TABLE.AMOUNT` · `TABLE.TRANSACTION_TYPE` · `TABLE.PAYMENT_METHOD` ·
  `LABELS.ROLE_ALL.CASHBOX` · `TABLE.CASHIER` (or `ONLINE_PAYMENT`) · `TABLE.COMMENT` ·
  `TABLE.REASON` · `TABLE.STATE` · `TABLE.TYPE` (icon) · `TABLE.DATE`.
- Add drawer `FORMDRAWER.add_transaction (Kirim|Chiqim)` → `POST /cashbox/income|expense`: `amount`,
  `comment`, `transactionTypeId`, `cashboxId`, `paymentMethodId`, `assignedModeratorId`, `studentId`.
- Permission `getTransactions`.

#### 2.9.2 Ours **[ours]**

**No screen.** `GET /api/billing/payments?studentId&cashierId&cashShiftId&from&to&method&onlyReversals`
exists (`PaymentsController.cs:161`, newest first, max 1 000, payments only) and
`POST /api/admin/billing/payments/{id}/reverse` exists (`:196`); `api/services/payments.ts` is an
unused stub. Expenses have their own list. The only cross-type view is one day's movements on Kassa kuni.

#### 2.9.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F9.01 | **Transaction journal** "Tranzaksiyalar" — one server-paged list of every money movement: payments, payment stornos, expenses (posted, pending, reversed), and later refunds (F1.05) and handovers (F1.04). Columns: Sana-vaqt · Kim (student / teacher link) · Chek № · Summa (signed, storno amber) · Turi (To'lov / Storno / Chiqim / Qaytarish / Inkassatsiya) · Toifa (category / account) · To'lov usuli · Kassir / yozgan · Izoh · Holat (Faol / Storno qilingan / Tasdiq kutmoqda). Filters: date range (default this month), direction (kirim / chiqim), turi, usul, kassir, o'quvchi, sinf, toifa, holat, **chek raqami** search, "faqat birinchi to'lov" (EduSchool `isFirstPayment`). Footer: Σ kirim, Σ chiqim, sof for the whole filter, not the page. Default sort newest first; sortable by date and amount. RBAC `FinanceAction.ViewBillingReports`. | new `SchoolLms.Application/Billing/TransactionJournalQuery.cs`, new `SchoolLms.Server/Controllers/TransactionJournalController.cs` (`GET /api/admin/finance/transactions`), new `pages/admin/finance/TransactionsPage.tsx`, new `api/services/transactions.ts` | no | 12 | 12 | **P0** |
| F9.02 | **Row actions**: payment → *Storno qilish* (new `ReversePaymentModal.tsx`: reason required; show `self_reversal` 403, `already_reversed` 409, `no_open_shift` 409 in Uzbek), *Chek (PDF)*, *Telegramga qayta yuborish*; expense → existing storno. | new `pages/admin/finance/ReversePaymentModal.tsx`, `TransactionsPage.tsx`, `transactions.ts` | no | 0 | 6 | **P0** |
| F9.03 | xlsx export of the filtered journal (numeric cells, totals row). | `TransactionJournalController.cs`, `ExcelExport.cs` (F0.01) | no | 4 | 1 | P1 |
| F9.04 | *Kirim* → opens `/cashier` for the chosen student; *Chiqim* → `ExpenseFormModal`. | `TransactionsPage.tsx` | no | 0 | 1 | P1 |
| F9.05 | "First payment only" filter — the student's earliest non-reversed payment. | `TransactionJournalQuery.cs` | no | 1 | 0 | P2 |

---

### 2.10 Abonement tranzaksiyalari (`FINANCE_ALL.SUBSCRIPTION_TRANSACTIONS`, `/subscription-transactions`)

#### 2.10.1 EduSchool **[bundle]**

- `GET /students/transactions` (every student's subscription ledger); no search, no export.
- Filters `state`, `studentId`, `isDebterSubscriptionCharges` (yes / no), **month** (sets
  `fromPeriodDate` / `toPeriodDate`, defaults to the current month, not clearable).
- Columns `TABLE.STUDENT` (link) · `TABLE.TRANSACTION_TYPE` (`transaction_type.{type}`:
  `studentSubscription`, `studentSubscriptionReturn`, `income`, …) · `TABLE.TO_BE_PAID` · `TABLE.PAID` ·
  `TABLE.AMOUNT` · `TABLE.BEFORE_BALANSE` / `TABLE.AFTER_BALANSE` (only `canSeeStudentBalance`) ·
  `TABLE.STATE` · `TABLE.PERIOD` (`forPeriods.fromDate – toDate`) · `TABLE.DATE`.
- Student-card counterpart (`student_transaction_pagin`): category filter `income · incomeCancelled ·
  studentSubscription · studentSubscriptionReturn · studentSubscriptionCancelled` with a row colour
  each; row action print receipt; export `GET /students/transactions/export?studentId&category`;
  *undo last cancellation* and *delete all subscription transactions* (both §2.0).
- Permission `getSubscriptionTransactions`.

#### 2.10.2 Ours **[ours]**

**No screen and no endpoint.** `InvoiceService.ListAsync(InvoiceQuery{StudentId, CategoryId, FromMonth,
ToMonth, Status, OnlyOverdue, ClassName})` and `InvoiceService.VoidAsync(id, reason)` (refuses an
invoice with allocations) exist with no caller; `InvoiceDto` (payable, paid, remaining, due, status,
overdue) is ready. `POST /api/admin/billing/accrual/run?month` exists with no UI. A student's months
are visible only in `PaymentHistoryModal.tsx` (all categories summed) and the portal `FinanceView.tsx`.

#### 2.10.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F10.01 | **Invoice register** "Hisob-fakturalar": server-paged `GET /api/admin/billing/invoices` over `InvoiceService.ListAsync` (add paging + totals). Filters: oy (from / to, default current month), o'quvchi, sinf, toifa, holat (open · partial · paid · void), faqat muddati o'tgan, faqat qarzdorlar. Columns: O'quvchi (link) · Sinf · Toifa · Oy · Summa · Chegirma · To'lanadi · To'langan · Qoldiq · Muddat (+ "Muddati o'tgan") · Holat · Yaratilgan. Footer Σ for the filter. RBAC `ViewBillingReports`. | new `SchoolLms.Server/Controllers/InvoicesController.cs`, `InvoiceService.cs` (paging overload, additive), `IBillingServices.cs`, new `pages/admin/billing/InvoicesPage.tsx`, new `api/services/invoices.ts` | no | 4 | 10 | **P0** |
| F10.02 | **Void** a wrong charge: row action *Bekor qilish* with required reason → `POST /api/admin/billing/invoices/{id}/void`, `FinanceAction.ManageSubscriptions`; 409 when it still has an effective allocation ("Avval to'lovni storno qiling" with a link to the journal); audit. Three **[defect]**s in `InvoiceService.VoidAsync` (`:430-479`) to fix on the way: (a) it refuses on **any** allocation, including those of a reversed payment, so an invoice that was paid and then stornoed can never be voided — use `FinanceReportQueries.EffectiveAllocations`; (b) it throws `ArgumentException` / `InvalidOperationException`, which `[BillingFault]` does not map (→ 500) — throw `BillingRuleException`; (c) the ledger reversal refuses anyone who authored a line of the batch, and the accrual job posts as the first superadmin (`BillingAccrualService.cs`), so **that director can never void an auto-accrued invoice** — keep the rule, but say so in the modal ("Bu hisob-fakturani boshqa admin bekor qilishi kerak"). | `InvoicesController.cs`, `InvoiceService.cs`, new `pages/admin/billing/VoidInvoiceModal.tsx` | no (reason in audit) | 5 | 3 | **P0** |
| F10.03 | *Oyni hisoblash* — run accrual for a month from the register (endpoint exists; idempotent result `Created / Skipped / Total`). | `InvoicesPage.tsx`, `invoices.ts` | no | 0 | 2 | P1 |
| F10.04 | Store who voided and why on the invoice (today audit only). | `InvoiceService.VoidAsync` | **yes** — `invoices.voided_by`, `voided_at`, `void_reason` | 1 | 1 | P2 |
| F10.05 | xlsx export. | `InvoicesController.cs` + F0.01 | no | 2 | 1 | P2 |

---

### 2.11 Bonus (`FINANCE_ALL.BONUS`, `/bonus`) and 2.12 Jarima (`FINANCE_ALL.PENALTY`, `/penalty`)

#### 2.11.1 EduSchool **[bundle]**

Two near-identical pages.
- List `GET /bonus-pagin` · `GET /penalty-pagin`, **searchable**; add (`createBonus` · `createPenalty`);
  a delete-style icon that **cancels** (`cancelBonus` · `cancelPenalty`, hidden when cancelled) →
  `POST /bonus-cancel {_id}` · `POST /penalty-cancel {_id}`.
- Columns: `general.student` (student or employee, link) · `bonus_penalty.beforeAmount` · `amount` ·
  `afterAmount` · `general.state` (chip active / cancelled) · `comment` · **Jarima only** `general.image`
  (`fileUrl` 50×50) · `keys.createdAt` (DD.MM.YYYY).
- Form (all required unless noted): `transactionTypeId` — `transaction-types?type=vouncher` (Bonus) ·
  `?type=penalty` (Jarima), its `hasImpactOn` picks `studentId` (`/students/pagin?noArchive=true`) **or**
  `employeeId` (`/employees/pagin`); `amount`; `comment` (optional); **Jarima only** `imageUrl`
  (upload) → `POST /bonus` · `POST /penalty`.
- The types come from the Finance-settings type tree, sub-tabs *Bonus* and *Jarima*, with
  `hasImpactOn` ∈ student · employee.

#### 2.11.2 Ours **[ours]**

None. `Teacher.BonusPct` (a percentage on the hourly estimate) is the only bonus; `hr.md` specifies
rule-based penalties and bonuses (`hr_rules`) but **not** manual one-off ones.

#### 2.11.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F11.01 | **Employee bonus / fine register** — two pages "Bonus" and "Jarima" (list, search, add, *Bekor qilish* = append a reversing row with reason). Fields: xodim (`hr_employees`), sabab (F11.02), summa > 0, qaysi oy (year, month), izoh, rasm (Jarima; `POST /api/admin/uploads`). Rows are consumed by the payroll fill for that month (F3.03); reversing a row already inside a **posted** payroll → 409. Columns: Xodim · Sabab · Summa · Oy · Holat (Faol / Bekor qilingan) · Izoh · Rasm · Yozgan · Sana. No before / after balance (§2.0). Depends on HR-01 (`hr_employees`). | new `SchoolLms.Application/Hr/PayrollAdjustmentService.cs`, new controller `/api/admin/hr/adjustments`, new `pages/admin/hr/AdjustmentsPage.tsx` (two routes, one component) | **yes** — `payroll_adjustments` (append-only, REVOKE) | 8 | 8 | P1 |
| F11.02 | Reason catalogue per kind (EduSchool transaction types `vouncher` / `penalty`): name, kind, active, position. | same service, settings tab | **yes** — `adjustment_reasons` | 3 | 4 | P1 |
| F11.03 | **Student** one-off fine (a manual charge, e.g. a lost book) and credit. Fine = a manual invoice (appears in debt, pivot, allocation and receipts with no new arithmetic). Credit → a discount request for the next month (director approval), not a new money path. | `InvoiceService.cs` (`CreateManualAsync`), `InvoicesController.cs`, `InvoicesPage.tsx` | **yes** — `invoices.source`, `note`, `created_by` + unique index change (§3) | 8 | 5 | P2 · Q4 |

Split for the summary: Bonus = F11.01 half + F11.02 half (P1 12 h), Jarima = the other halves (P1 11 h);
F11.03 is shared P2.

---

### 2.13 Abonement tranzaksiyalari (Qarzdorlik oyma-oy) (`FINANCE_ALL.SUBSCRIPTION_TRANSACTIONS_PIVOT`)

#### 2.13.1 EduSchool **[bundle]**

(`existing-module-gaps.md` §3.4.1 recovered the cell and colour rule; the rest of the screen:)
- `GET /students/subscription-transactions-pivot`, **one row per student × subscription**
  (`getUniqueId = studentId_subscriptionId`), **numbered**; the response returns `monthColumns[]`
  (`YYYY-MM`).
- Columns `TABLE.STUDENT` (`fullName (uuid)`, link) · `TABLE.PHONE_NUMBER` · `TABLE.SUBSCRIPTION` · one
  per month (header `monthNames.{m} YYYY`, cell `{amount, paid, toBePaid}`) · `…PIVOT.TOTALS`. No
  sortable column.
- Sticky footer in lock-step: `FOOTER_LABEL`, `FOOTER_TOTAL_DEBT` (red), `FOOTER_TOTAL_PAID` (green).
- Header: switch `LABELS.IS_DEBTOR_SUBSCRIPTION_CHARGES`, *Filter*, **export**
  `GET /students/subscription-transactions-pivot/export` (same params + page/limit), legend + hint.
- Filters: **class** (multi), **group** (multi), **student state** (multi `active · inactive · archive ·
  new · other`), **students** (multi), **period** (`fromPeriodDate` / `toPeriodDate`).
- Permission `getSubscriptionTransactions`.

#### 2.13.2 Ours **[ours]**

`pages/admin/finance/ArrearsPage.tsx` (`GET /api/admin/finance/arrears-pivot`, ≤ 12 months, ≤ 600
students): from / to month, class (single), category (single), name search (client), "Faqat
qarzdorlar", "Arxivdagilar"; sort by class or by debt; KPIs Hisoblangan / To'langan / Qoldiq /
Qarzdor o'quvchilar; sticky student column (name, archive tag, class); month cells show `toBePaid`
coloured paid / partial / unpaid / no invoice with a tooltip of all three numbers; "Jami qoldiq"
column; footer; legend; CSV.

#### 2.13.3 Gaps — all P2

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F13.01 | Multi-select class and student filters. | `FinanceReportQueries.cs` (`ArrearsPivotQuery`), `FinanceReportsController.cs`, `ArrearsPage.tsx` | no | 1 | 2 | P2 |
| F13.02 | *Toifalar bo'yicha ajratish* — one row per student × category (EduSchool: × subscription). | same | no | 3 | 2 | P2 |
| F13.03 | Parent phone column, link to the student card, row numbers. | same | no | 1 | 1 | P2 |
| F13.04 | Server paging instead of the 600-student cap. | same | no | 3 | 2 | P2 |
| F13.05 | xlsx export. | controller + F0.01 | no | 2 | 1 | P2 |
| F13.06 | Group filter. | after `PLAN` §4 step B | no | 1 | 1 | P2 |
| — | Three numbers in every cell | — | — | — | — | our visual language: one number + tooltip (`CLAUDE.md`) |
| — | Student states *new* / *other* | — | — | — | — | Students-module states; ours has active / archived |

---

### 2.14 Reachable catalogues — Finance settings (`/finance-settings`)

#### 2.14.1 EduSchool **[bundle]**

Tab list, each tab only with its permission:

| Tab | Permission | Content |
|---|---|---|
| `TRANSACTION_TYPE` | `transactionTypeGet` | sub-tabs payIn · payOut · Bonus (`vouncher`) · Jarima (`penalty`); form `name` (uz/ru/en), `parentId`, `color`, `type` radio (options follow `hasImpactOn`), `hasImpactOn` `student`·`employee`·`other`; `isDefault` rows locked; `POST\|PUT /transaction-type`, `DELETE transaction-type/{id}` |
| `PAYMENT_METHOD` | `paymentMethodsGet` | `name`, `isActive`, `showOnIncomeExpence`, `showOnTransfer`; defaults undeletable |
| `SUBSCRIPTION` (Abonement) | `subscriptionGet` | `/subscription/pagin` with `connectedStudents` → `/aboniment/:id`; edit · archive · unarchive; form `name`, `price`, `privilagePrice` (setting `onTimePrivilegeEnabled`), `timeRange` (`date`\|`month`), `duration` 1–12 months, `daysInMonth` (lesson days per week 5\|6), `branchIds[]`, `isEducational`; money fields **locked once students are connected** |
| `DISCOUNT` | `getDiscount` (hidden with `onTimePrivilegeEnabled`) | `/discount/pagin` (`N%` or money, `transactionCount` → `/discount/:id`); form `name`, `amount`, `amountType` (`percentage` \| fixed **[inferred]**), `state` (`active`\|`archive`), `description` |
| `CURRENCY` · `COIN_UNIT` · `SYSTEM_SUBSCRIPTION` | … | currency name; gamification; EduSchool SaaS billing |
| `PLANNED_EXPENSE` | `expenseTemplateGet` | §2.6 |
| `TAX` | `editPayroll` | NDFL 12 / INPS 0.1 / ESP 12 % + two flags — `hr.md` §2.5 |
| `DEBTOR_STATUSES` | `_id` (edit `debtorStatusEdit`) | §2.2 |

Discount assignment: students-list row action and student-card menu → `PUT /student {_id, discountId}`
(one catalogue discount per student). With `onTimePrivilegeEnabled` the entry opens **billing
discount** (`billingDiscountManage`): existing month periods (`month year — value`, created by, delete)
and `amountType`, `value`, `periods[]{year, month}` (required) → `POST /finance/billing-discount`,
`DELETE /finance/billing-discount/{id}`.

#### 2.14.2 Ours **[ours]**

`CategoriesPage.tsx` (code, name, active; no delete); `SubscriptionsPage.tsx` (per student: category,
monthly amount prefilled from the class fee, detail, start, end; close); `DiscountsPage.tsx` (per
student request: category or all, percent and/or amount, reason, start, end → director approval
queue). **No billing-settings screen or endpoint**: `billing_settings` (`payment_due_day`,
`overdue_after_day`, `expense_approval_threshold`) is editable only in the database;
`FinanceAction.ManageBillingSettings` is unused.

#### 2.14.3 Gaps

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F14.01 | **Billing settings** "Moliya sozlamalari": to'lov muddati kuni (1–28), muddati o'tgan deb hisoblash kuni (≥ due day, ≤ 28), chiqim tasdiq chegarasi (≥ 0) — `GET` / `PUT /api/admin/billing/settings`, `FinanceAction.ManageBillingSettings` (superadmin only for the threshold — it is a dual-control parameter), audited. | new `BillingSettingsService.cs` or `InvoiceService` addition, `BillingCatalogController.cs`, new `pages/admin/billing/BillingSettingsPage.tsx`, `Dtos/BillingDtos.cs` (`UpdateBillingSettingsRequest` gains the threshold) | no | 3 | 4 | P1 |
| F14.02 | **Discount types** — a named catalogue ("Aka-uka", "Xodim farzandi"): name, percent or fixed amount, active / archive, description, usage count; the request form picks a type (prefills percent / amount, reason stays required); the director still approves every discount. | `DiscountService.cs`, `BillingCatalogController.cs`, `DiscountFormModal.tsx`, new `DiscountTypesPage.tsx` | **yes** — `discount_types`, `discounts.discount_type_id` | 5 | 6 | P2 |
| — | Subscription **plans** with duration, lesson days, branches | — | — | — | — | **declined** — a course-centre model; a school bills per category per month, and the class monthly fee already prefills the amount |
| — | Transaction types, payment methods, currency, system subscription | — | — | — | — | **declined** (§2.0) |
| — | Billing discount by month periods | — | — | — | — | **have** — `discounts.starts_on` / `ends_on` |

---

### 2.15 Cross-cutting

| Id | What is missing | Files | Schema | BE h | FE h | Pri |
|---|---|---|---|---|---|---|
| F0.01 | xlsx writer with **numeric** cells, a bold header and a totals row (today `ExcelExport.cs` writes inline strings only). Additive overload; every finance xlsx gap uses it. | `SchoolLms.Application/Services/ExcelExport.cs` | no | 3 | 0 | P1 |
| F0.02 | **[access]** money reads are open to `staff`: `AdminPermAttribute` passes staff on every GET (`:40-42`), exposing the salary report, salary rates, teacher salary ledgers, student ledgers and balances (`SPEC.md` §4.3: reports are admin and director only). Put `[Authorize(Roles = Roles.FinanceStaff)]` on the money GETs (`FinanceController.salary-report`, `SalaryRatesController` GETs, `TeachersController` salary GETs, `StudentsController.{id}/ledger`); the student list's *Balans* column stays (Students decision). | those controllers, `SchoolLms.Tests/Security/*` | no | 3 | 0 | P1 |
| F0.03 | **[grant]** `deploy/init-roles.sql` §5 does not list `finance_anomaly_flags`, so a re-run after migrations grants full UPDATE / DELETE again and undoes `anomaly_guards.sql` (column-level UPDATE on the resolution columns only). Add it with the same column grant. | `deploy/init-roles.sql` | grant only | 1 | 0 | P1 |
| F0.04 | Receipt PDF ownership: `GET /api/receipts/{id}.pdf` lets a cashier open any receipt; restrict a cashier to receipts of their own shifts. | `ReceiptsController.cs` | no | 1 | 0 | P2 |
| — | Per-user column visibility / order / pin / width (EduSchool `/table-settings/columns/*`) | — | — | — | — | **declined for this pass** — a platform capability, not a Finance screen |

---

## 3. Schema changes, consolidated

One migration owner, one migration per batch (`PLAN-STUDENTS-FINANCE.md` §2 Phase 1). Conventions as
`SchoolLms.Domain/Billing.cs`: `uuid` ids, `numeric(14,2)` money, `date` accounting dates,
`timestamptz` instants (`DateTimeOffset`, never `DateTime`), `text` for `students.id`, `teachers.id`,
`app_users.id`. Every new table needs a `GRANT` block in a new `Migrations/Sql/finance_parity_guards.sql`
(embedded resource) wrapped in the `IF NOT EXISTS app_rw → RAISE NOTICE; RETURN` pattern of
`billing_guards.sql`. `Up()` must contain **no DROP** — read it line by line.

### 3.1 Batch A — Finance P0 / P1 (goes with the Students schema in Phase 1)

| # | Change | For | Grants |
|---|---|---|---|
| A1 | `expenses.cash_shift_id uuid null references cash_shifts(id) on delete restrict`; index `(cash_shift_id)`. No retro check on old rows; the service requires it for new cash expenses. | F1.03 | `expenses` keeps full CRUD (existing) |
| A2 | `create table cash_handovers (id uuid pk default gen_random_uuid(), cash_shift_id uuid not null references cash_shifts(id) on delete restrict, amount numeric(14,2) not null check (amount > 0), destination text not null check (destination in ('bank','safe')), note text, created_by text not null references app_users(id), created_at timestamptz not null default now(), reversal_of uuid null unique references cash_handovers(id), constraint ck_cash_handovers_reversal_not_self check (reversal_of is null or reversal_of <> id))`; index `(cash_shift_id)`. | F1.04 | ⚠ **financial, append-only** — `GRANT SELECT, INSERT`; `REVOKE UPDATE, DELETE, TRUNCATE`; **add `cash_handovers` to `deploy/init-roles.sql` §5** |
| A3 | `create table student_refunds (id uuid pk default gen_random_uuid(), student_id text not null references students(id) on delete restrict, amount numeric(14,2) not null check (amount > 0), method text not null check (method in ('cash','card','transfer','online')), reason text not null check (btrim(reason) <> ''), requested_by text not null references app_users(id), requested_at timestamptz not null default now(), approved_by text null references app_users(id), approved_at timestamptz null, cash_shift_id uuid null references cash_shifts(id), rejected_reason text null, reversal_of uuid null unique references student_refunds(id), constraint ck_student_refunds_approver_differs check (approved_by is null or approved_by <> requested_by), constraint ck_student_refunds_cash_shift check (approved_by is null or method <> 'cash' or cash_shift_id is not null))`; index `(student_id)`. | F1.05 | ⚠ **financial** — `GRANT SELECT, INSERT`, `REVOKE UPDATE, DELETE, TRUNCATE`, then `GRANT UPDATE (approved_by, approved_at, cash_shift_id, rejected_reason)` (the `anomaly_guards.sql` column-grant pattern); a `BEFORE UPDATE` trigger refusing any change once `approved_at` or `rejected_reason` is set; **add to `init-roles.sql` §5 with the column grant — grant review required** |
| A4 | `create table expense_attachments (id uuid pk default gen_random_uuid(), expense_id uuid not null references expenses(id) on delete restrict, file_url text not null, file_name text not null, content_type text not null, size_bytes bigint not null check (size_bytes > 0), uploaded_by text not null references app_users(id), uploaded_at timestamptz not null default now())`; index `(expense_id)`. | F1.08 | evidence of a money record — `GRANT SELECT, INSERT` only (no REVOKE list entry needed beyond this, but do not grant UPDATE / DELETE) |
| A5 | Code only, no DDL: `LedgerRefType` gains `cash_handover` and `refund` (`ledger_entries.ref_type` has no DB check); `CashDayQueries` labels follow. | F1.04, F1.05 | — (touches no grant, but the constants live in `Billing.cs`, owned by the migration agent) |

### 3.2 Batch B — Payroll (with `hr.md` HR-03, when HR starts)

| # | Change | For | Grants |
|---|---|---|---|
| B1 | The 22 tables of `hr.md` §4.2 and `payroll_guards.sql` as specified there. | F3.01 | as `hr.md` §3.2 — ⚠ `payroll_payments` append-only (REVOKE, add to `init-roles.sql` §5) |
| B2 | `create table adjustment_reasons (id uuid pk, kind text not null check (kind in ('bonus','penalty')), name text not null, is_active boolean not null default true, position int not null default 0, unique (kind, name))`. | F11.02 | full CRUD (catalogue, retire not delete) |
| B3 | `create table payroll_adjustments (id uuid pk default gen_random_uuid(), employee_id uuid not null references hr_employees(id), kind text not null check (kind in ('bonus','penalty')), reason_id uuid not null references adjustment_reasons(id), amount numeric(14,2) not null check (amount > 0), period_year smallint not null check (period_year between 2000 and 2100), period_month smallint not null check (period_month between 1 and 12), comment text, image_url text, created_by text not null references app_users(id), created_at timestamptz not null default now(), reversal_of uuid null unique references payroll_adjustments(id), reversal_reason text, constraint ck_payroll_adjustments_reversal check ((reversal_of is null) = (reversal_reason is null)))`; index `(employee_id, period_year, period_month)`. | F11.01 | ⚠ money-bearing, append-only — `GRANT SELECT, INSERT`; `REVOKE UPDATE, DELETE, TRUNCATE`; add to `init-roles.sql` §5 |

### 3.3 P2 — schedule later, not in Batch A

| # | Change | For | Notes |
|---|---|---|---|
| C1 | `billing_settings`: `receipt_header text`, `receipt_footer text`, `receipt_logo_url text`, `receipt_auto_print boolean not null default false` | F1.12 | ordinary |
| C2 | `expenses.period_month date null check (period_month is null or extract(day from period_month) = 1)` | F1.13 | ordinary |
| C3 | `debtor_actions.period_month date null` (same check) | F2.03 | ordinary (no REVOKE, per `parity_wave2_guards.sql`) |
| C4 | `invoices.voided_by text null references app_users(id)`, `voided_at timestamptz null`, `void_reason text null`; check `status <> 'void' or voided_at is not null` added `NOT VALID` for legacy rows | F10.04 | ordinary |
| C5 | `invoices.source text not null default 'accrual' check (source in ('accrual','manual'))`, `invoices.note text null`, `invoices.created_by text null`; **replace** `unique (student_id, category_id, period_month)` with a partial unique index `… where source = 'accrual'` | F11.03 | ⚠ the accrual job's idempotency relies on that unique index — the replacement must be created before the old one is dropped **in the same migration**, and this is the one place a `DROP` (of an index) is expected in `Up()`; test-migration must cover it |
| C6 | `create table discount_types (id uuid pk, name text not null unique, percent numeric(5,2) not null default 0 check (percent between 0 and 100), amount numeric(14,2) not null default 0 check (amount >= 0), description text, is_active boolean not null default true)`; `discounts.discount_type_id uuid null references discount_types(id)` | F14.02 | ordinary |
| C7 | `create table expense_templates (id uuid pk, name text not null, category text not null, amount numeric(14,2) not null check (amount > 0), frequency text not null default 'monthly' check (frequency = 'monthly'), starts_on date not null, ends_on date null check (ends_on is null or ends_on >= starts_on), reminder_day smallint null check (reminder_day between 1 and 28), is_active boolean not null default true, created_by text not null, created_at timestamptz not null default now())` | F6.01 | ordinary |

### 3.4 Tables under `REVOKE` — what this document touches

| Table | Touched? |
|---|---|
| `payments` | **No DDL.** F1.10 adds an advisory lock in code only. |
| `payment_allocations` | **No DDL.** |
| `ledger_entries` | **No DDL.** New `ref_type` values (A5) are code constants; rows are still inserted only by `LedgerService`. |
| New append-only tables | `cash_handovers` (A2), `student_refunds` (A3, with a column grant), `payroll_adjustments` (B3), `payroll_payments` (B1, `hr.md`) — each must be added to `deploy/init-roles.sql` §5 **and** revoked in its own guard SQL, because `ALTER DEFAULT PRIVILEGES` hands every new table full CRUD the moment it is created. |
| `finance_anomaly_flags` | grant fix F0.03 (missing from §5 today). |

---

## 4. Build slices

Phase order follows `PLAN-STUDENTS-FINANCE.md` §2. **The migration agent (M) runs first and alone.**
Slices that need no schema can start in parallel with M.

| Slice | Gaps | Needs schema | Owns (new unless marked *edit*) | Hours |
|---|---|---|---|---|
| **M — Migration Batch A** | A1–A5, F0.03 | — | `SchoolLms.Domain/Billing.cs` (*edit*, additive), new `SchoolLms.Domain/CashDesk.cs` (`CashHandover`, `StudentRefund`, `ExpenseAttachment`), new `SchoolLms.Infrastructure/Data/FinanceParityModel.cs`, `AppDbContext.cs` (*edit*), the migration + `AppDbContextModelSnapshot.cs`, new `Migrations/Sql/finance_parity_guards.sql` + `.csproj` embedded resource (*edit*), `deploy/init-roles.sql` (*edit*) | 8 |
| **S1 — Journal and invoices** | F9.01–F9.05, F10.01–F10.03, F0.01 | no | `TransactionJournalQuery.cs`, `TransactionJournalController.cs`, `InvoicesController.cs`, `InvoiceService.cs` (*edit*, additive paging overload), `IBillingServices.cs` (*edit*), `ExcelExport.cs` (*edit*, additive), `TransactionsPage.tsx`, `ReversePaymentModal.tsx`, `InvoicesPage.tsx`, `VoidInvoiceModal.tsx`, `api/services/transactions.ts`, `api/services/invoices.ts` | 64 |
| **S2 — Expenses and the till** | F1.01–F1.04, F1.08, F1.09, F1.11 (nav via orchestrator) | F1.03, F1.04, F1.08 after M | `ExpenseService.cs`, `ExpensesController.cs`, `CashShiftService.cs`, `CashShiftsController.cs`, `CashHandoverService.cs`, `CashHandoversController.cs`, `ExpenseFormModal.tsx`, `ExpensesPage.tsx`, `ShiftBar.tsx`, `ZReportTab.tsx`, `api/services/expenses.ts`, `api/services/cashHandovers.ts` | 43 |
| **S3 — Refunds, subscription end, payment hardening, billing settings** | F1.05, F1.06, F1.07, F1.10, F14.01 | F1.05 after M | `StudentRefundService.cs`, `StudentRefundsController.cs`, `StudentBalanceQuery.cs`, `SubscriptionService.cs`, `BillingCatalogController.cs`, `PaymentService.cs`, `PaymentsController.cs`, `ReceiptService.cs`, `Dtos/BillingDtos.cs`, `EndSubscriptionModal.tsx`, `RefundsPage.tsx`, `BillingSettingsPage.tsx`, `api/services/billingCatalog.ts`, `api/services/refunds.ts` | 43 |
| **S4 — Statements and dashboards** | F4.01–F4.02, F5.01–F5.03, F7.01, F7.03, F8.01 | no | `FinanceStatementsQueries.cs`, `FinanceDashboardQueries.cs`, `FinanceStatementsController.cs`, `PnlTab.tsx`, `CashFlowTab.tsx`, `FinancialReportsPage.tsx`, `LedgerDetailsModal.tsx`, `CashDayPage.tsx`, `FinancePage.tsx` (*edit*, tab host), `api/services/financeStatements.ts` | 64 |
| **S5 — Debtors** | F2.01, F2.02 | no | `DebtorWorkflowService.cs`, `FinanceReportQueries.cs` (DebtorsAsync only), `FinanceReportsController.cs`, `DebtorsTab.tsx`, `api/services/financeReports.ts`, `SchoolLms.Tests/DebtorWorkflowTests.cs` | 6 |
| **S6 — Salary access** | F3.05, F0.02 | no | `TeachersController.cs`, `SalaryRatesController.cs`, `FinanceController.cs`, `StudentsController.cs` (ledger GET only — **coordinate with the Students slice that owns this file**), `SalaryCalcPage.tsx`, `SchoolLms.Tests/Security/*` | 7 |
| **S7 — Payroll (HR-1) + Bonus / Jarima** | F3.01–F3.04, F11.01–F11.02, Batch B | Batch B (its own migration owner, per `hr.md` HR-01…03) | everything `hr.md` §9 lists + `PayrollAdjustmentService.cs`, `AdjustmentsPage.tsx` | 240 |
| **S8 — P2 backlog** | the rest | C1–C7 | decided after P0 / P1 ship | 193 |

**Dependencies between slices.** S4's drill-downs call S1's journal endpoint — S1 publishes the
`GET /api/admin/finance/transactions` query contract (params and row DTO) on day one so S4 codes against
it. S2 and S3 both change the expected-cash arithmetic: S2 owns `CashShiftService.cs`; S3's refund posts
through a method S2 exposes (`CashShiftService.AddCashOutflowAsync` or equivalent) — agree the signature
before both start, or run S3 after S2 merges. S7 waits on the client's `hr.md` §11 answers.

**Shared files — contended, orchestrator-owned, one pass per wave:**

| File | Who needs it |
|---|---|
| `schoollms.client/src/App.tsx` | S1 (transactions, invoices), S3 (refunds, billing settings), S4 (financial reports), S7, F2.07 |
| `schoollms.client/src/config/navigation.ts` | S1, S3, S4, S7, F1.11 |
| `schoollms.client/src/config/constants.ts` | none unless a new `adminPermissions` key is chosen (not proposed) |
| `SchoolLms.Server/Controllers/FinanceRoleAttribute.cs` | S2 (`HandOverCash`: cashier + admin + superadmin), S3 (`RequestRefund`: admin + superadmin; `ApproveRefund`: superadmin), S7 (`hr.md` §5.4) — add all members in one edit before the slices start |
| `SchoolLms.Application/Services/AuditService.cs` | S1 (`EntityInvoice`), S2 (`EntityCashHandover`, `EntityExpenseAttachment`), S3 (`EntityStudentRefund`, `EntityBillingSettings`), S7 |
| `SchoolLms.Server/Program.cs` | S2, S3, S7 (DI registrations) |
| `SchoolLms.Domain/Billing.cs` | M only (`LedgerRefType`, `Expense.CashShiftId`) |
| `SchoolLms.Application/Billing/Accounts.cs` | S7 only (`hr.md` §5.2: three codes); F5.05 (P2) |
| `SchoolLms.Application/Billing/CashDayQueries.cs` | S4 (F8.01) — S2 must **not** edit it; new ledger ref types render under the existing "Boshqa harakat" label until S4 adds labels |
| `SchoolLms.Application/Billing/FinanceReportQueries.cs` | S5 only; S4 writes new files instead |
| `SchoolLms.Server/Controllers/StudentsController.cs` | S6 and the Students slices — sequence them |

**Suggested waves.** Wave 1 (parallel): M, S1, S4, S5, S6. Wave 2 (after M merges): S2, then S3. Wave 3:
S7 once `hr.md` §11 is answered. S8 on request.

**Definition of done per slice** (`CLAUDE.md` global): `dotnet test` green incl. an RBAC test for every
new endpoint, `npm run build` + lint, `test-migration` for M and Batch B, `qa-review` on every money path.
A reversal, void, refund or handover test must assert the trial balance still sums to zero and — for the
till slices — that `expected_cash` reconciles after a cash expense, a handover and a refund in one shift.

---

## 5. Open questions for the client

Only questions that change scope. Each carries the decision that stands if nobody answers.

**Q1 — How does cash leave the drawer today?**
*"Kassadagi naqd pul qanday chiqadi: bankka topshiriladimi, direktorga beriladimi? Kim va qancha vaqtda
bir?"*
**Decision if silent: build F1.04** with two destinations — bank deposit (moves `cash` → `bank`) and
hand-over to the director's safe (lowers the shift's expected cash, stays school cash). Without it
Kassa kuni's closing cash drifts upward forever.

**Q2 — Do you return money to parents?**
*"O'quvchi ketganda oldindan to'langan pulni ota-onaga qaytarasizmi?"*
**Decision if silent: yes — F1.05, the advance only** (money not yet allocated to a charge), requested
by admin, approved by the director, cash out of an open shift. Money already allocated to a charge
comes back through a storno of the payment (re-taking the part that stays) plus a void of the charge —
not through a refund.

**Q3 — Are expenses paid in cash from the cashier's drawer?**
*"Xarajatlar kassirning javonidagi naqd puldan to'lanadimi yoki direktor alohida pul beradimi?"*
**Decision if silent: yes — F1.03.** A cash expense needs an open shift of whoever records (or
approves) it and lowers that shift's expected cash. If the answer is "no, the director pays from a
separate fund", F1.03 becomes "cash expenses do not need a shift" and the P0 drops to the two form
defects.

**Q4 — Do you fine or credit students?**
*"O'quvchiga jarima (masalan, yo'qotilgan kitob) yoki bonus yozasizmi?"*
**Decision if silent: no student-side bonus / fine (F11.03 stays P2).** Employee bonus and fine are
built with payroll (F11.01).

**Q5 — Payroll formula** — `hr.md` §11 Q1 (how a fixed salary follows worked hours) and Q6 (is tax
charged on the card part only).
**Decision if silent: the `hr.md` §11 recommendations.** Both decide what a teacher is paid, so ask
before S7 starts; if there is still no answer when HR-01…HR-04 (schema and contracts) are done, HR-07
and HR-09 proceed on the recommendations and the formula stays isolated in `PayrollService`.

**Q6 — Do you plan revenue and expenses ahead (P&L 2.0)?**
*"Oylik reja (kutilayotgan tushum, rejalashtirilgan xarajat) va haqiqat taqqoslanishi kerakmi?"*
**Decision if silent: deferred** (`existing-module-gaps.md` §3.6) — revisit after one full year of
actuals; §2.6 stays P2.

**Q7 — Owner withdrawals / dividends in P&L?**
*"Muassis pul oladimi va u P&L da alohida ko'rinishi kerakmi?"*
**Decision if silent: no** — no equity account is added to the closed chart; F5.05 stays P2.
