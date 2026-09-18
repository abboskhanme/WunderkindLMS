# Menu parity with EduSchool

The client asked on 2026-09-13 that our sidebar match EduSchool's — *"menular
ketma ketligi ham bir xil bo'lsin, ichidagi funksionallik ham"*. This file is
the target order and the record of every deviation.

**Two orders exist, and the live one wins.** Reading the bundle gives the
*default* menu array that EduSchool ships. The school has reordered its own
sidebar since — the `Berkitish` control and a `sidebarVisibility` setting let
them — so what the client actually sees differs. The table below is the **live**
order, read off the running page on 2026-09-13, because that is the one he is
comparing us against.

## Target order (live)

| # | EduSchool (live) | Ours today | Note |
|---|---|---|---|
| 1 | Dashboard | Bosh sahifa | — |
| 2 | Lidlar | Lidlar | — |
| 3 | Moliya | Moliya | — |
| 4 | Jurnal | Jurnal | — |
| 5 | O'quv bo'limi | O'quv bo'limi | new group; absorbed our separate O'quvchilar, Sinflar, Fanlar, Shartnomalar |
| 6 | Topshiriqlar (staff tasks) | — | `docs/modules/staff-tasks.md` |
| 7 | Dars jadvali | Dars jadvali | — |
| 8 | Chat | Xabarlar | label kept — ours is not only chat |
| 9 | Gamifikatsiya | — | `docs/modules/gamification.md` |
| 10 | Qabul | — | `docs/modules/admission-and-testing.md` |
| 11 | Imtihonlar | — | same file |
| 12 | HR | HR | new group; absorbed O'qituvchilar + davomat + oylik |
| 13 | Analitika | Analitika | new group; absorbed Baholar hisoboti, O'qituvchilar hisoboti, Sinflar reytingi |
| 14 | Boshqaruv | Boshqaruv | — |
| 15 | Xulq-atvor | Xulq-atvor | was "Intizomiy ball" |
| 16 | Blok Test | — | `docs/modules/admission-and-testing.md` |
| 17 | Sozlamalar | Sozlamalar | — |

Hidden in their live sidebar but present in the product: **WareHouse**,
**Keldi-ketdi**, **Administrator**, **Mavsumiy baholash**. WareHouse still has a
spec (`docs/modules/warehouse.md`) because the client asked for parity with the
product, not with one school's current sidebar configuration; Administrator is
out by decision (single branch).

Ours with no EduSchool counterpart, placed where they belong rather than forced
into the sequence: **Davomat** and **Keldi-ketdi** after Xabarlar (both are
attendance), **Ilova** after Analitika.

**Unbuilt modules are absent from the menu, not present and empty.** A menu entry
that leads to a blank page is worse than no entry: it reads as a broken product
rather than an unfinished one. Each row above names the spec that will fill it,
and the position it takes when it lands.

## Labels come from their translation file, not the source

The `title` field in the bundle is a fallback. The rendered UI uses the i18n
key, and the two differ — the source says `Sinf`, `Group`, `Sertifikat`; the
screen says **Sinflar**, **Guruhlar**, **Sertifikatlar**. Ours follow the screen.

## How submenus open

Side flyout, not a downward accordion. Opening a parent shows a panel to its
right with the children split into columns under uppercase group headings —
`O'QUV JARAYONI`, `O'QUVCHILAR`, `HUJJATLAR`. `NavChild.group` carries that.

Below `lg` the panel would run off the screen (256px sidebar on a 375px phone),
so small screens keep the accordion. One open state, two renderings.

## Deviations, and why

**Kept, with no EduSchool counterpart at top level.** `Davomat` (after Jurnal)
and `Ilova` (the mobile-app section: assignments, LMS, canteen, teacher app).
EduSchool scatters these across other modules; folding ours in would move
working screens for no gain. Revisit when the matching modules land — the
canteen in particular belongs under WareHouse once meal costing exists.

**Schedule configuration stayed under Dars jadvali** — Choraklar, Dars vaqtlari,
Bayram kunlari, Davomat sabablari. EduSchool files this kind of thing under
Sozlamalar. Ours is where a user looks for it, and moving it is a UX change
rather than a parity one. Flagged rather than done.

**`Fanlar` moved** from Dars jadvali to O'quv bo'limi, matching EduSchool.

**Group vs Sinf.** EduSchool's O'quv bo'limi lists `Sinf` and `Group` as separate
entries: a teaching group independent of the homeroom class. Ours has only
Sinf, because `students.class_name` is a single column. That gap is priced at
184 h in `docs/modules/existing-module-gaps.md` and hinges on one question:
does the school teach groups drawn from several classes at once? Until that is
answered the second entry stays out.

**Cross-branch Administrator is not coming.** Single branch, by decision.

## Inside the menus

Parity is not only order. Each spec in `docs/modules/` carries the screens,
columns, fields and rules for its module, so "ichidagi funksionallik ham" is
tracked there rather than duplicated here.

## O'quv bo'limi — the submenu, item by item (2026-09-17)

The client sent EduSchool's own flyout and asked for the same sequence inside
it, not only at the top level. Ours now reads exactly:

| Group | EduSchool | Ours |
|---|---|---|
| O'QUV JARAYONI | Sinflar · Guruhlar · Fanlar · Xonalar | same |
| O'QUVCHILAR | O'quvchilar · Arxiv o'quvchilar · O'quvchilar manzili · Ota onalar | same |
| HUJJATLAR | Sertifikatlar · Shartnomalar | same |

Three entries are ours alone and sit at the **end of their group**, so the
EduSchool sequence reads unbroken: `O'quvchi holatlari` (their statuses live
in a settings dialog), `Sertifikat turlari` (theirs is under Sozlamalar, ours
cannot be — that route is gated on `settings` while the API is gated on
`students`), and the `BAHOLASH` pair, which they do not have at all.

**Xonalar moved here from Dars jadvali.** It was filed under the timetable's
settings when the register was built; EduSchool keeps it in O'quv bo'limi and
so do we now.

**Arxiv o'quvchilar is a real menu entry, not a tab.** Ours was a tab inside
the pupil list; `/admin/students/arxiv` now opens that list with the archive
tab selected, so the menu matches without duplicating the screen.

## The submenu was complete; the permissions were not (2026-09-17)

The client asked whether every entry in EduSchool's flyout actually exists on
our side — *"bu menular bizni tizimda boshqa joyda bo'lgan bo'lishi ham mumkin,
agar bo'lsa olib kelib shu yerga qo'y, bo'lmasa yarat"*. Checked entry by entry:
all ten are present, each with a route and a real page (Guruhlar 319 lines,
Xonalar 325, Ota-onalar 410, Manzil 224, Shartnomalar 384 — none is a stub).

**What was actually broken was the permission wiring.** The section declared
`perm: 'students'` and its children declared nothing, so:

- a staff member with only `students` saw the whole flyout and hit "ruxsat
  yo'q" on Sinflar, Guruhlar, Fanlar, Xonalar, Manzil, Ota-onalar and
  Shartnomalar — seven of the twelve entries;
- a staff member with only `classes` did not see the section at all, although
  Sinflar and Guruhlar are exactly theirs.

The section's `perm` is gone and every child now carries the key that really
guards its page. `Sidebar` already filters children and hides a group whose
children are all hidden, so the menu stops offering what the user cannot open.
The parent row is a button that only opens the panel, so dropping its `perm`
costs no navigation.

**Four different keys sit inside one section**: `classes` (Sinflar, Guruhlar),
`schedule` (Fanlar, Xonalar), `app` (Manzil, Ota-onalar), `students` (the rest),
`contracts` (Shartnomalar). That is the state of the server, not a menu choice.
`app` is a leftover from the mobile-app era and, now that Manzil and Ota-onalar
live here, belongs on `students` — but moving it means moving
`LocationsController` and `ParentsController` too, or the menu starts lying
again. It travels with X-2.

## Ours-only entries removed from the submenu (2026-09-17)

The client, seeing the full flyout after the rebuild: *"baholash qismi sub
menulari va o'quvchi holatlari menusi kerakmas, eduschoolda bor menular bo'lsa
yetadi."* Removed from `O'quv bo'limi`:

- the whole **BAHOLASH** group — `O'quvchilarga feedback`, `Feedback nomi`;
- **O'quvchi holatlari**.

Both were ours alone: EduSchool has no feedback section at all, and keeps pupil
statuses in a settings dialog rather than a menu entry.

**The routes and pages stay.** `/admin/students/baholash`,
`/admin/students/baholash-turlari` and `/admin/students/holatlar` still resolve
and still work; only the menu entries are gone. Nothing else links to
`holatlar` or to `baholash`, so those two are now reachable by URL only
(`baholash-turlari` is still linked from the evaluation page itself). Deleting
the features is a separate decision and was not asked for.

**`Sertifikat turlari` stays**, and is now the only ours-only entry in the
section. EduSchool does have certificate types — under Sozlamalar, not here —
so it is not an invention. It cannot move to our Sozlamalar because that route
is gated on `settings` while the API is gated on `students`.

## Moliya — the submenu, item by item (2026-09-17)

Same exercise as O'quv bo'limi above, for the section the client compares hardest —
*"biznikida zo'r chiqqan"* was about Lidlar's looks, but Moliya is where he checks the
numbers. Source: `docs/modules/finance-parity.md` §2.1–§2.14, a discovery pass finished
the same day (no live EduSchool traffic — file-only, per `CLAUDE.md`'s read-only rule).
Read that file's §0.3–§0.4 first: it corrects an earlier mistake (`fin-map` is *Moliya
analitikasi*, not *Moliya*) and explains how each screen was matched to its router chunk.

### 1 — EduSchool's Moliya flyout (their thirteen screens, `FINANCE_ALL.*`)

| § | EduSchool screen | Route | One line |
|---|---|---|---|
| 2.1 | **Moliya** | `/cash` | cashbox cards, per-cashbox table, income/expense/transfer/exchange drawers, debtor allocation, subscription attach/cancel, receipt print |
| 2.2 | Qarzdorlar bilan ishlash | `/debtors` | debtor list, month filter, status + promised-date workflow |
| 2.3 | Ish haqi | `/salary` | payroll runs: list, per-employee fix/flex/bonus/fine/paid, cancel, lesson detail, xlsx |
| 2.4 | Moliya hisobotlari | `/financial-reports` | KPI in/out/remainder with % change, daily chart, breakdown, discount summary |
| 2.5 | Moliya hisobotlari (P&L) | `/pnl-reports` | year × month matrix, start/end balance, drill-down, dividends |
| 2.6 | P&L 2.0 (beta) | `/pnl-expectation` | plan-vs-fact revenue model, change journal, planned-expense templates |
| 2.7 | Pul oqimi | `/cashflow` | operating/investing/financing cash flow by category, month or year |
| 2.8 | Moliya analitikasi (`fin-map`, beta) | `/financial-analytics` | per-method balances, calendar with day drill-down, journal, 12-month chart, top-5 |
| 2.9 | Tranzaksiyalar | `/transactions` | every cashbox transaction across all cashboxes, filters, export |
| 2.10 | Abonement tranzaksiyalari | `/subscription-transactions` | every subscription charge/return, to-be-paid/paid, period |
| 2.11 | Bonus | `/bonus` | one-off credit to a student or employee, cancel |
| 2.12 | Jarima | `/penalty` | one-off charge with a photo, cancel |
| 2.13 | Qarzdorlik oyma-oy | `/subscription-transactions-pivot` | student × subscription × month pivot |
| 2.14 | Finance settings (reachable catalogues) | `/finance-settings` | transaction types, payment methods, plans, discounts, debtor statuses, tax, planned expenses |

### 2 — Ours, from `Moliya`'s `children` in `navigation.ts`

`schoollms.client/src/config/navigation.ts` lines 76–105, group **AMALIYOT**: Umumiy
(:81) · Kassa (:84) · To'lov toifalari (:88) · Obunalar (:89) · Chegirmalar (:90) ·
Chiqimlar (:91) · Hisob-fakturalar (:92) · Qarzdor holatlari (:93). Group **HISOBOTLAR**:
Moliya hisobotlari (:97) · Tranzaksiyalar (:98) · Kassa kuni (:100) · Pul aylanmasi (:101) ·
Oyma-oy qarzdorlik (:103). Thirteen entries, same two-group shape as the rest of the menu.

Two more screens live in **other** top-level sections and are picked up in row 2.3 below:
HR's "Oylik hisoblash" (`navigation.ts:207`, `/admin/teachers/salary`) and the "O'qituvchilar"
tab inside Moliya → Umumiy itself.

### 3 — Row by row

| § | EduSchool | Ours | Verdict |
|---|---|---|---|
| 2.1 | Moliya (cash desk) | **Kassa** (`/cashier`, `CashierPage.tsx`) + **Chiqimlar** (`/admin/billing/expenses`, `ExpensesPage.tsx`) | **present**, split into two entries — see note A |
| 2.2 | Qarzdorlar bilan ishlash | `DebtorsTab.tsx`, a tab of `FinancePage` ("Qarzdorlar", opened from **Umumiy**) | **present as a tab, not a menu entry** — see note B |
| 2.3 | Ish haqi | HR → **Oylik hisoblash** (`/admin/teachers/salary`, `SalaryCalcPage.tsx`) + Moliya → Umumiy → "O'qituvchilar" tab (`FinancePage.tsx:216-303`) | **lives elsewhere** — see note C |
| 2.4 | Moliya hisobotlari | **Moliya hisobotlari** (`/admin/finance/reports`, `FinancialReportsPage.tsx`) | **present** |
| 2.5 | Moliya hisobotlari (P&L) | `PnlTab.tsx`, a tab of `FinancePage` ("Foyda va zarar") | **present as a tab, not a menu entry** — see note B |
| 2.6 | P&L 2.0 (beta) | none | **declined/deferred** — `existing-module-gaps.md` §3.6; `finance-parity.md` §2.6.3 keeps it P2 until the client lifts Q6 |
| 2.7 | Pul oqimi | `CashFlowTab.tsx`, a tab of `FinancePage` ("Pul oqimi") | **present as a tab, not a menu entry** — see note B |
| 2.8 | Moliya analitikasi (`fin-map`) | **Kassa kuni** (`/admin/finance/cash-day`, `CashDayPage.tsx` + `CashDayCalendar.tsx`) + **Pul aylanmasi** (`/admin/finance/money-flow`, `MoneyFlowPage.tsx`) + `CashFlowTab.tsx` (chart) | **present**, split into two entries + a tab — see note A |
| 2.9 | Tranzaksiyalar | **Tranzaksiyalar** (`/admin/finance/transactions`, `TransactionsPage.tsx`) | **present** |
| 2.10 | Abonement tranzaksiyalari | **Hisob-fakturalar** (`/admin/billing/invoices`, `InvoicesPage.tsx`) | **present** — this is `finance-parity.md`'s F10.01 invoice register, our equivalent of their subscription-charge ledger |
| 2.11 | Bonus | none | **missing — pending this wave** (`finance-parity.md` F11.01/F11.02; the bonus/penalty agent building it now needs `hr_employees`, not yet in the repo) |
| 2.12 | Jarima | none | **missing — pending this wave**, same slice as 2.11 |
| 2.13 | Qarzdorlik oyma-oy | **Oyma-oy qarzdorlik** (`/admin/finance/arrears`, `ArrearsPage.tsx`) | **have** (already shipped 2026-09-16 per `finance-parity.md` §1) |
| 2.14 | Finance settings | **To'lov toifalari** / **Obunalar** / **Chegirmalar** (`CategoriesPage.tsx` / `SubscriptionsPage.tsx` / `DiscountsPage.tsx`) + **Qarzdor holatlari** (their `DEBTOR_STATUSES` tab, counted once under 2.2's catalogue) | **partial — pending this wave** for the rest, see note D |

**Counts.** 14 EduSchool screens audited: **6 present** as their own menu entry (2.1, 2.4, 2.8,
2.9, 2.10, 2.13 — two of them, 2.1 and 2.8, split across more than one of our entries) ·
**3 present only as a tab** inside Umumiy, not a menu entry (2.2, 2.5, 2.7) · **1 lives under a
different top-level section** (2.3, HR) · **1 partial**, some of it pending (2.14) ·
**2 missing, pending this wave** (2.11, 2.12) · **1 missing, declined/deferred by an earlier,
already-settled decision** (2.6).

### Note A — one EduSchool screen, more than one of ours, by design

EduSchool's *Moliya* (`/cash`) is one page: cashbox cards on the left, the selected cashbox's
transactions on the right, four drawers (income/expense/transfer/exchange). We do not have
cashboxes (`docs/modules/finance-parity.md` §2.0 — declined, money lives in two accounts, `cash`
and `bank`), so the equivalent work is split across **Kassa** (the till itself) and **Chiqimlar**
(expense recording) — both already linked, both already correctly gated (below). Likewise their
*Moliya analitikasi* (`fin-map`) bundles a calendar, a journal and a monthly chart in one page;
ours splits the same information across **Kassa kuni** (the calendar + day drill-down) and **Pul
aylanmasi** (the monthly sources/uses chart), with the journal itself living at **Tranzaksiyalar**
now that it exists (§2.9). Nothing here is missing — it is the same functionality, organised
around our two-account ledger instead of their named-cashbox model.

### Note B — three EduSchool screens are tabs on our side, not menu entries

`DebtorsTab.tsx`, `PnlTab.tsx` and `CashFlowTab.tsx` all render live inside `FinancePage.tsx`
(`schoollms.client/src/pages/admin/finance/FinancePage.tsx:44-52`, tabs `debtors` / `pnl` /
`cashflow`, gated by `canSeeReports` at `:72`), reached by opening **Umumiy** and clicking a tab
— never by their own sidebar row. This is the distinction the client asked for explicitly on
2026-09-17 for O'quv bo'limi's Arxiv o'quvchilar, and it applies the same way here: **the
functionality exists and is reachable**, just one click deeper than EduSchool's flyout puts it.
`finance-parity.md` already carries the fix as a named, priced gap for each:

- F2.07 — a standalone route for `DebtorsTab` (P2, 1 h FE)
- no gap id is filed for standalone P&L / Cashflow routes; `finance-parity.md` §2.5 and §2.7 treat
  the tabs as the delivered screen and price only their *content* gaps (year×month matrix,
  drill-down, category breakdown), not a menu promotion

None of the three has a route of its own today (`App.tsx` has no `finance/debtors`,
`finance/pnl` or `finance/cashflow` path), so none is added to the diff below — adding a
sidebar row for a route that 404s is worse than the tab it replaces (the same rule the O'quv
bo'limi section states above: *"a menu entry that leads to a blank page is worse than no
entry"*).

### Note C — Ish haqi was moved on purpose, before this audit

The top-level table (row 12) already records that HR "absorbed O'qituvchilar + davomat +
oylik" — Ish haqi's Moliya-side placement was a deliberate call made when the HR section was
built, not a gap this pass found. `FinancePage.tsx`'s "O'qituvchilar" tab (the only tab open to
a `finance`-permitted `staff` user without the admin/superadmin report gate, `:74-75`) is the
Moliya-side remainder: a read-only salary report. The payroll spine itself (runs, cancel,
lesson detail) is `hr.md` HR-1 / `finance-parity.md` F3.01, not part of this Moliya audit.

### Note D — 2.14, what is actually still missing

Three of EduSchool's finance-settings tabs are already reachable from Moliya: **To'lov
toifalari**, **Obunalar**, **Chegirmalar** (`navigation.ts:88-90`, all wired to
`BillingCatalogController.cs` — class-level `[Authorize(Roles = Roles.FinanceStaff)]` at line 47
— and all present in `App.tsx:165-167`). **Qarzdor holatlari** covers their `DEBTOR_STATUSES`
tab. Declined by earlier, settled decisions and not gaps: `TRANSACTION_TYPE`, `PAYMENT_METHOD`,
`CURRENCY`, `SYSTEM_SUBSCRIPTION` (closed chart of accounts, so'm only — `finance-parity.md`
§2.0). Genuinely missing:

- **Billing settings** ("Moliya sozlamalari" — due day, overdue day, expense-approval
  threshold): F14.01, **pending this wave**. `BillingSettingsDto` / `UpdateBillingSettingsRequest`
  already exist in `SchoolLms.Application/Dtos/BillingDtos.cs:409-417`, and
  `FinanceAction.ManageBillingSettings` already exists in the RBAC matrix
  (`FinanceRoleAttribute.cs:84,154`, `AdminAndDirector`) — but no controller applies it yet
  (confirmed: no `billing-settings` route in any controller) and no page exists. Once it lands it
  needs a Moliya entry; see the diff below for the checked shape to copy.
- **Discount types** (F14.02, a named catalogue behind "Chegirmalar" rather than its own row) —
  P2, not part of the current wave.

### 4 — The diff to apply: none, today

Every EduSchool Moliya screen we have actually built already has a `navigation.ts` entry, and
every one of those entries' `perm`/`roles` was checked against the controller that serves it —
they match:

| Ours | `navigation.ts` gate | Controller gate |
|---|---|---|
| Tranzaksiyalar | `roles: ['admin','superadmin']` (:98) | `TransactionJournalController.cs:49-50` — `[Authorize(Roles = Roles.FinanceStaff)]` + `[FinanceRole(FinanceAction.ViewBillingReports)]` |
| Hisob-fakturalar | `roles: ['admin','superadmin']` (:92) | `InvoicesController.cs:40-41,61` — same pair |
| Qarzdor holatlari | `roles: ['admin','superadmin']` (:93) | `DebtorWorkflowController.cs:49-50` — same pair |
| Moliya hisobotlari | `roles: ['admin','superadmin']` (:97) | `FinanceReportsController.cs:41-42` — class-level `[Authorize]` + `[FinanceRole(ViewBillingReports)]` |
| Kassa kuni | `roles: ['admin','superadmin']` (:100) | `CashDayController.cs:47-48` — same pair |
| Pul aylanmasi | `roles: ['admin','superadmin']` (:101) | `MoneyFlowController.cs:37-38` — same pair |
| Oyma-oy qarzdorlik | `roles: ['admin','superadmin']` (:103) | `FinanceReportsController.cs:41-42,177` (`arrears-pivot`) — same pair |
| To'lov toifalari / Obunalar / Chegirmalar / Chiqimlar | `roles: ['admin','superadmin']` (:88-91) | `BillingCatalogController.cs:47` / `ExpensesController.cs:52` — `[Authorize(Roles = Roles.FinanceStaff)]` |
| Kassa | `roles: ['admin','superadmin']` (:84) | `App.tsx:190` — `<ProtectedRoute roles={['cashier','admin','superadmin']} />` on `/cashier` |

No entry shows the O'quv bo'limi bug (a row the menu offers but the server refuses): every child
that needs `admin`/`superadmin` says so in `navigation.ts`, matching `Roles.FinanceStaff` exactly,
so a `finance`-permitted `staff` user never sees a link that then 403s. Nothing to fix, nothing to
add — the Tranzaksiyalar/Hisob-fakturalar rows and Kassa's F1.11 note in `navigation.ts:82-84`
show this wiring was already done as its own pass before this audit started.

**For whoever lands the pending screens** — the checked shape to copy (do not paste these into
`navigation.ts` until the route on the right exists; a link to a route that 404s is worse than no
link, per the rule above):

```
// Once F11.01/F11.02 ship a route (proposed /admin/hr/adjustments?kind=bonus|penalty
// or similar — confirm against whatever the bonus/penalty agent actually registers):
{ label: 'Bonus', to: '/admin/hr/bonus', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
{ label: 'Jarima', to: '/admin/hr/penalty', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },

// Once F14.01 ships (proposed /admin/billing/settings — confirm against the billing-settings
// agent's actual route):
{ label: 'Moliya sozlamalari', to: '/admin/billing/settings', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
```

All three follow the pattern every existing Moliya child already uses:
`roles: ['admin', 'superadmin']`, matching `Roles.FinanceStaff` — not `perm: 'finance'` alone,
which a `staff` user can hold without being admin/superadmin and would again show a row the
server refuses.

### Declined, not missing

EduSchool's income drawer in *Moliya* (`/cash`) includes a bulk action that loads
`SmsTemplates` to message the students behind the selected rows (`finance-parity.md` §2.1.1,
"a bulk button... loads SmsTemplates"). **Not ours, and not coming**: `CLAUDE.md`'s Telegram-only
rule — *"bizda mobile app bo'lmaydi faqat telegram mini app bo'ladi xolos va shunga mos ravishda
sms ham... bo'lmaydi"* — declines SMS outright, and a debtor broadcast already exists over
Telegram instead (`POST /api/admin/messages/broadcast`, `OnlyDebtors`, cited in
`finance-parity.md` §2.0). Cashboxes, transfers between them, currency exchange, an editable
transaction-type tree, and "cancel a transaction" as a destructive PUT are declined for the
reasons `finance-parity.md` §2.0 and §3.4 already give (storno instead of cancel — `SPEC.md` §4)
and are not re-litigated here.

## Moliya — the submenu rebuilt to EduSchool's three groups (2026-09-18)

The client put the two flyouts side by side: theirs reads **AMALLAR · ISH HAQI ·
HISOBOTLAR**, ours read AMALIYOT · HISOBOTLAR in a different order. Ours now
follows theirs, with every entry of ours that they do not have placed at the
**end of its group**, so their sequence reads unbroken.

| Group | EduSchool | Ours |
|---|---|---|
| AMALLAR | Kassa · Qarzdorlar bilan ishlash · Tranzaksiyalar · Abonement tranzaksiyalari · Abonement tranzaksiyalari (Qarzdorlik oyma oy) | same order; `Abonement tranzaksiyalari` is our **Hisob-fakturalar**, then ours-only: Umumiy · To'lov toifalari · Obunalar · Chegirmalar · Chiqimlar · Qarzdor holatlari · Qaytarimlar · Moliya sozlamalari |
| ISH HAQI | Ish haqi · Bonus · Jarima | same |
| HISOBOTLAR | Moliya hisobotlari · (P&L) · (P&L) 2.0 · Pul oqimi · Moliya analitikasi | Moliya hisobotlari · (P&L) · Pul oqimi · Pul aylanmasi |

**Three of their menu entries were tabs on our side.** `Qarzdorlar bilan ishlash`,
`Moliya hisobotlari (P&L)` and `Pul oqimi` live as tabs inside `FinancePage`.
Rather than split the screen, `FinancePage` now takes an `initialTab` and three
routes open it on the right tab — the same thing `StudentsPage` does for
`Arxiv o'quvchilar`. One screen, three ways in.

**`Ish haqi` moved from HR to Moliya**, where EduSchool keeps it. It is not
listed twice; the HR entry is gone.

**Shift reports left the menu.** `Kassa kuni`, and the Z-hisobot and
Nomuvofiqlik tabs, have no EduSchool counterpart, and the client said the shift
does not belong in this section. **The shift mechanism itself stays** — SPEC §4
requires an open shift before a payment is accepted and before a storno is
approved, so removing it would stop the till working. The cashier opens and
closes a shift on the Kassa screen, and the two reports remain reachable as tabs
inside Umumiy.

**`Moliya hisobotlari (P&L) 2.0` is not coming** — declined in
`existing-module-gaps.md` §3.6, before this menu work.

## The top-level sidebar, reordered to EduSchool's sequence (2026-09-18)

The client sent their sidebar and asked for the same order at the top level too.
Theirs reads: Dashboard · Lidlar · Moliya · Jurnal · O'quv bo'limi ·
Topshiriqlar · Dars jadvali · Chat · Gamifikatsiya · Qabul · Imtihonlar · HR ·
Analitika · Boshqaruv · Xulq-atvor · Blok Test · Sozlamalar.

Every entry we share was already in their relative order; what broke the
sequence was our own three — Davomat, Keldi-ketdi and Ilova — sitting in the
middle of it. They now come after Xulq-atvor and before Sozlamalar, so their
run reads unbroken from the top to Xulq-atvor, and Sozlamalar stays last as it
is for them.

**Their five unbuilt modules stay out of the menu**: Topshiriqlar,
Gamifikatsiya, Qabul, Imtihonlar, Blok Test. The rule from the first pass holds
— an entry that opens a blank page reads as a broken product rather than an
unfinished one. Each has its spec in `docs/modules/` and its position here for
when it lands.

## "Future" — where our own sections wait (2026-09-18)

The client's suggestion, once the top-level order matched: keep the main sidebar
exactly EduSchool's, and collect everything of ours that they do not have into
one section at the bottom, to be restored or dropped later.

`Future` is that section, last in the sidebar, holding three groups:

| Group | Entries |
|---|---|
| DAVOMAT | Kunlik davomat · Davomat analitikasi |
| KELDI-KETDI | Jonli turniket · Turniket analitikasi · Kirib-chiqish statistikasi · Kunlik davomat hisoboti |
| ILOVA | Topshiriqlar · Topshiriqlar bali · Ta'lim (LMS) · Oshxona · O'qituvchilar |

**Nothing moved but the menu.** Every route, page and permission is untouched;
each entry carries the same `perm` its section used to carry at the top level
(`attendance`, `students`, `app`), so who can see what has not changed.

**Undoing it is small**: delete the `Future` block and restore the three
sections — git holds them in the commit before this one.

The sidebar above it now reads EduSchool's sequence from Bosh sahifa to
Xulq-atvor, then Sozlamalar, then Future.
