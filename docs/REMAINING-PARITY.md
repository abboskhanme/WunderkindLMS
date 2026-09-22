# What is still not the same as EduSchool — 2026-09-18, night

Written after a full pass over their Moliya screens (read-only, through the browser extension)
and over ours, screen by screen. Everything in this file is **module-sized or needs a client
decision** — the small deltas found on the way were closed the same night and are recorded in
`ASSUMPTIONS.md` and `modules/finance-parity.md`.

Read this as: *"pick the next one"*, not as a backlog that should be built in order.

---

## 1. Closed during the night of 2026-09-18

| Area | What changed |
|---|---|
| Kassa ledger | `Izoh` and `Sabab` columns, date + time on `Sana`, receipt printable for any cash-box row, `pay_in`/`posted` label defect fixed |
| Tranzaksiyalar journal | `Kassa` and `Sabab` columns; the storno reason moved out of the note into its own column |
| Kassa forms | Kirim merged into one form driven by the transaction type; Chiqim gained the type + date; back-dating (with limits) on all four cash actions and on pupil payments |
| Whole app | One date-picker and month-picker style everywhere (61 + 9 fields), table headers never wrap, long cells truncate with the full text on hover, ISO dates rendered as `DD.MM.YYYY` |
| Kassa kuni | Leftover open-shift cards removed (the shift concept was dropped on 2026-09-18) |
| Moliya hisobotlari | *Chegirmalar tahlili* block — total, count, average and the split by fee category |
| Davomat | New screen: one member of staff marks every class, lesson by lesson, three marks (present / absent / excused) |

---

## 1b. Closed on 2026-09-19

| Area | What changed |
|---|---|
| Dars jadvali | Quarter and class selectors dropped — one **week** picker, every class's grid one under another. Same for **O'qituvchi jadvali**: every teacher, one under another |
| Jadval to'ri | The period column now carries the **bell times** (start–end) on all three schedule screens; the grid stops at the last real lesson |
| Jadval yaratish | The side editor became a **cell modal**: click a cell, pick subject and teacher, save, green toast. Same grid as the view screens |
| Moliya eksporti | **.xlsx** for *Pul oqimi* (F7.04) and *Moliya hisobotlari* (F4.05) and the teacher salary report; the leftover CSV buttons next to an Excel one are gone |
| P&L | Opens **by year** (as theirs does) and the *Daromad* / *Xarajat* totals **collapse** |

Still CSV, on purpose: the three P&L 2.0 (beta) tabs and the cashier ledger — their rows are built in the
browser, so a server-side .xlsx would need a second copy of the Uzbek labels. See `ASSUMPTIONS.md`, 2026-09-19.

**Not verified in a browser.** Everything above was built while the Chrome extension was disconnected:
types, lint, the client build and all 1504 backend tests pass, but no screen was opened.

---

## 1c. Closed on 2026-09-21 — Savdo va marketing

The last two rows of `existing-module-gaps.md` §7.1 (#8 and #14). Spec: `modules/sales-marketing.md`.

| Area | What changed |
|---|---|
| Ommaviy ariza formasi | Public `/ariza/:slug` page — works on the **apex** domain too (SPA fallback). A submission becomes a lead on the board (`source = survey`); the board's look is untouched. Rate limit 5 / 10 min, honeypot, a 2-second time trap, no third-party captcha |
| Arizalar / Topshirilgan arizalar | Survey register + editor with live preview; submissions register with filters, detail drawer and **.xlsx** export |
| Yangiliklar | Composer with live preview; publishing also sends one Telegram message per recipient (de-duplicated) and records the counts; feed in the Mini App (parent *Bosh*, teacher *Bugun*) and in the parent/student portal |
| Voronka | *Manba kesimi* — manual vs each survey — and a survey filter |
| Admin bell | "Yangi ariza" and "Yangilik e'lon qilindi" |
| Menu | *Sozlamalar → Savdo va marketing*, the fourth row; new `marketing` permission key |

Verified by machine: 1786 backend tests (282 new, incl. RBAC for every endpoint × 8 roles and the migration's
`Down()`), client type-check clean, and an end-to-end API run on the local stack (create → public submit → lead
→ export → funnel → bell; news publish → feed; `/ariza/` served on the apex host). **Not opened in a browser** —
the Chrome extension was disconnected again.

---

## 2. Whole modules EduSchool has and we do not

Each one is a module, not a screen. They are listed with the doc that already prices them.

| # | EduSchool module | Screens | Ours | Where it is priced |
|---|---|---|---|---|
| 1 | **Qabul** (admission) | Nomzodlar · Test bazasi · public test link `/qabul-test/:token` | none | `modules/admission-and-testing.md` |
| 2 | **Blok Test** | Imtihonlar · Imtihon turi · Natijalar | none | `modules/admission-and-testing.md` |
| 3 | **Gamifikatsiya** | 13 screens | none | `modules/gamification.md` |
| 4 | **WareHouse** | 16 screens | none | `modules/warehouse.md` |
| 5 | **Topshiriqlar** (staff task board) | kanban · calendar · timeline · comments · reports | ours is the *student* assignment screen, a different thing | `modules/staff-tasks.md` |

**None of these should start without the client naming which one earns its keep first.** Gamification
and WareHouse in particular are large and independent of daily school operation.

---

## 3. Partial modules — the named gaps

### 3.1 HR
EduSchool: *Ish haqi · Soatbay ish haqi · Jarimalar · Arizalar · Tabel*.
We have salary reporting, an hourly salary calculator, bonus and penalty. Missing:

- **Arizalar** — staff requests (leave, advance, certificate) with an approval chain.
- **Tabel** — the monthly timesheet grid that payroll should read from.

Both are in `modules/hr.md` as HR-1; the payroll rebuild there is ~206 h and is the reason
`finance-parity.md` §2.3 stays *partial*.

### 3.2 Sozlamalar
Theirs has eleven entries; ours keeps the equivalents in other menus (staff and permissions under
*Boshqaruv*, feedback under *Boshqaruv*). Genuinely missing:

- **O'qituvchi baholash** (lesson observation) — ~62 h, worth it only if the school actually does
  observations (`existing-module-gaps.md` §5.3).
- **Xodimlar bo'sh vaqti**, **Ish jadvali**, **O'qituvchi dars qoldirish** — three small staff-time
  screens that only make sense together with HR *Tabel*.
- ~~**Savdo va marketing** — News and Surveys (Story declined).~~ **Built 2026-09-21** — see §1c.

### 3.3 Analitika
Ours covers their analytics list with screens that live under the menu they belong to (attendance
analytics under *Davomat*, turnstile under *Keldi-ketdi*, debtors under *Moliya*). Two of theirs
have no equivalent anywhere:

- **Mavsumiy baholash hisoboti** — depends on teacher evaluation (§3.2).
- **Ishdan bo'shatishlar hisoboti** — dismissals report; depends on HR.

**Filial holati** is declined: this is a single school, the multi-branch control plane was removed
on purpose.

### 3.4 Moliya — one half-gap left
- **Chegirmalar tahlili** on *Moliya hisobotlari* — **built the same night**: total discount, how
  many invoices carried one, the average, and the split. One difference from theirs, on purpose:
  the split is **by fee category**, not by discount rule. `invoices` records the discount *amount*
  but not which rule produced it, and with two overlapping discounts on one student the split back
  to rules would be a guess — money reports do not guess. Splitting by rule needs a new
  `invoices.discount_id` column filled at accrual time; from that day forward the numbers would be
  exact, and older months would stay category-only. **Client decision.**

---

## 4. Deliberate differences — not gaps

| Theirs | Ours | Why |
|---|---|---|
| SMS provider, templates, auto-SMS, message log | none | Client, 2026-09-16: everything goes through Telegram |
| Mobile app + push (FCM) | Telegram Mini App + web | same decision |
| Editable payment methods / currencies / branch views | fixed list, one school | `finance-parity.md` §1 |
| Their visual language | ours (iOS/Apple-flavoured) | CLAUDE.md — we rebuild the **functionality**, never the appearance |
| Abonement transactions carry a running per-student balance | we show class, discount, remaining and due date instead | no running balance in our model, and the columns we show answer the same question |

---

## 5. How this list was produced

EduSchool was opened **read-only** (`wunderkind.eduschool.uz`): screens listed, tables and filters
read, no form submitted, nothing saved, no settings touched — the standing rule in `CLAUDE.md`.
Where a screen could not be reached without a write (for example opening a create-drawer that
saves on open), it was left alone and the doc relies on `EDUSCHOOL-INVENTORY.md`, which was
compiled earlier from the same source.
