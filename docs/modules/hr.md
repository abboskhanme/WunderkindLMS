# HR module — payroll, timesheet, penalties, staff requests

**Status:** specification, not yet implemented · **Date:** 2026-09-13
**Source of truth for EduSchool behaviour:** `.eduschool-bundle/all.js` (frontend bundle, read-only study).
**Source of truth for our own platform:** `docs/SPEC.md` §3.7 and §4, `SchoolLms.Domain/Billing.cs`,
`SchoolLms.Application/Billing/`, `SchoolLms.Application/Services/SalaryLedger.cs`.

Everything in this file that is stated as fact was read out of the bundle and the line is quoted or
the symbol named. Everything reconstructed is marked **[inferred]**. Everything unknown is in §11
with a recommended decision — if nobody objects, the recommendation stands.

> **Reference implementation.** When writing code for this module, copy the shape of
> `SchoolLms.Application/Billing/ExpenseService.cs` + `SchoolLms.Server/Controllers/ExpensesController.cs`
> + `schoollms.client/src/pages/admin/billing/ExpensesPage.tsx`. That triple is the closest existing
> analogue: money-touching, ledger-posting, two-man-rule, no update/delete verbs, status derived from
> the ledger. Do not invent a different layering.

---

## 1. Scope

Fourteen EduSchool menu entries collapse into one module because they are all the same story:
*how much does the school owe an employee this month, and why*.

| # | EduSchool key | EduSchool title (from the bundle) | EduSchool route | EduSchool permission |
|---|---|---|---|---|
| 1 | `HR_ALL.TIMESHEET` | Tabel | `/hr/timesheet` | `getHrTimesheet` |
| 2 | `HR_ALL.PAYROLL` | Oylik hisobi | `/hr/payroll` | `getPayroll` |
| 3 | `HR_ALL.HOURLY_PAYROLL` | Soatbay oylik | `/hr/hourly-payroll` | `getPayroll` |
| 4 | `HR_ALL.PENALTIES` | Jarima va bonuslar | `/hr/penalties` | `getHrRules` |
| 5 | `HR_ALL.REQUESTS` | Xodim so'rovlari | `/hr/requests` | `getEmployeeRequests` |
| 6 | `FINANCE_ALL.MONTHLY_SALARY` | Ish haqi | `/salary` | `getEmployeeSalaryData` |
| 7 | `FINANCE_ALL.BONUS` | Bonus | `/bonus` | `getBonus` |
| 8 | `FINANCE_ALL.PENALTY` | Jarima | `/penalty` | `getPenalty` |
| 9 | `SETTINGS_ALL.WORK_SCHEDULE` | Ish jadvali | `/work-schedules/all` | *(none — always visible)* |
| 10 | `SETTINGS_ALL.EMPLOYEE_FREE_TIME` | Xodimlar bo'sh vaqti | `/employee-free-time` | `getMeetings` |
| 11 | `SETTINGS_ALL.MISSED_LESSONS` | O'qituvchi dars qoldirish | `/missed-lessons` | `getMissedLessons` |
| 12 | `ANALITICS_ALL.DISMISSAL_REPORT` | Ishdan bo'shatishlar hisoboti | `/employee/dismissal-report` | `getDismissalReport` |
| 13 | *(no menu entry)* | Loans — employee card tab + payroll columns | — | `getEmployeeLoans` |
| 14 | *(settings tab)* | Tax rates (NDFL / INPS / ESP) | `/finance-settings?tab=tax` | `editPayroll` |

The task brief listed the first five with generic titles ("Ish haqi / Soatbay ish haqi / Jarimalar /
Arizalar / Tabel"). The live tenant uses the titles in the table above; we use the live ones.

**Not in scope here** (owned by other module specs): employee CRUD, roles/permissions registry,
turnstile ingestion, teacher evaluation (`SETTINGS_ALL.TEACHER_EVALUATION`), employee tasks.
This module *consumes* employees and turnstile events; it does not own them.

---

## 2. How EduSchool actually computes a salary

This is the part that matters. Read it before you touch the schema.

### 2.1 Two pay kinds, decided per employee

`timesheet.utils` (chunk `timesheet.utils-DT6EZJ8K.js`) contains:

```js
$o = { fixed: "hr.timesheet.doc.category.fixed", hourly: "hr.timesheet.doc.category.hourly" }
Qe = t => {
  const n = t.categories;
  if (Array.isArray(n) && n.length) { /* use explicit categories, filtered to fixed|hourly */ }
  return J(t.lessonPlanCount) > 0 || J(t.singleLessonCost) > 0 ? ["hourly"] : ["fixed"];
}
```

* `fixed` — a monthly salary. Timesheet measures **minutes** (`plannedMinutes` vs `workedMinutes`).
* `hourly` — paid per conducted lesson. Timesheet measures **lessons** (`planCount`, `approvedCount`)
  and the rate is a single per-lesson price, `singleLessonCost`.
* An employee can be **both** (`categories: ["fixed","hourly"]`) — a head-of-department who also
  teaches. The timesheet document has two independent checkboxes, `includeFixed` and `includeHourly`,
  and at least one must be ticked (`hr.timesheet.doc.form.categoriesRequired`).

The employee's money fields live on the **branch employment record**, not on the person:
`branchEmployees[i] = { branchId, roleId, isActive, salary, cardSalary, cashSalary }`.
Validation, from the employee form (`Fr` in the employees chunk):

```js
u = S => {
  if (E("type") === "teacher") return true;             // teachers are not validated
  const A = N(`branchEmployees.${S}`);
  const z = Number(A?.cardSalary) || 0, W = Number(A?.cashSalary) || 0;
  if (!z && !W) return true;
  const U = Number(A?.salary) || 0;
  return z + W <= U || r("employees.salarySplitExceeds");
}
```

So for non-teachers: **`cardSalary + cashSalary ≤ salary`**. `cardSalary` is the officially declared,
taxed part; `cashSalary` is the envelope. Teachers are exempt from the check because their pay is
lesson-driven.

There is also `employees.workScheduleRequiredForSalary` and `employees.workScheduleMissing`, and the
payroll page renders a blocking alert `hr.payroll.scheduleMissing.*` with an export link
`?withoutWorkSchedule=true&state=active`. **An employee with no work schedule cannot be paid.**

### 2.2 What counts as a payable lesson — the one rule that is fully proven

From `timesheet.utils`:

```js
Os = (t, n) => !!t && !!n;                       // ok = both true
Ke = (row, drafts) => {                          // "is this lesson payable?"
  const s = drafts?.[row.lessonId];
  if (typeof s === "boolean")               return s;                  // unsaved UI edit
  if (typeof row.overridePayable === "boolean") return row.overridePayable;  // admin override
  if (typeof row.payable === "boolean")     return row.payable;        // server verdict
  if (typeof row.computedPayable === "boolean") return row.computedPayable;
  return Os(row.hasFaceId, row.isMarked);        // ← THE RULE
};
Ro = row => typeof row.overridePayable === "boolean"
         && row.overridePayable !== (row.computedPayable ?? Os(row.hasFaceId, row.isMarked));
Fo = (row, singleLessonCost, drafts) => {
  if (!Ke(row, drafts)) return 0;
  const a = J(singleLessonCost);
  return a > 0 ? a : J(row.amount);
};
```

**A scheduled lesson is paid if and only if both are true:**

1. `hasFaceId` — the teacher was recorded by the FaceID/turnstile device that day, and
2. `isMarked` — the teacher filled the journal for that lesson.

Neither alone is enough. A **substituted** lesson is a normal row that additionally carries
`substitutedForEmployeeId` / `substitutedForFullName`; it is subject to the same two conditions and,
when payable, is paid to the **substitute** at the substitute's own `singleLessonCost` — the row sits
under the substitute's employee id, and the KPI counts it separately
(`substitutedCount = rows.filter(r => r.approved && r.substitutedForEmployeeId).length`).

An admin with `editHrTimesheet` can flip `payable` per lesson (`PUT /hr/timesheet-document/lesson-payable`,
batch limit **500** rows per save, constant `Qt = 500`). A flipped row is badged
`hr.timesheet.doc.lessonRow.manual`.

**Per-lesson amount = `singleLessonCost`.** Not pro-rated by minutes, not reduced by lateness.
`lateMinutes` is displayed on the row and aggregated into the KPI, but `Fo` never multiplies by it —
lateness only feeds the *penalty rules* (§2.5).

Hourly total for a month:

```
approvedCount   = Σ payable lessons
unpaidCount     = max(0, planCount − approvedCount)
amount          = Σ over payable lessons of singleLessonCost      (== approvedCount × singleLessonCost)
```

Confirmed by `_e` in the hourly-payroll chunk, which reduces the page rows exactly that way.

### 2.3 What counts as a worked hour for a fixed employee

The timesheet day row is
`{ _id, date, dayType, plannedStart, plannedEnd, actualIn, actualOut, workedMinutes, offScheduleMinutes, flag }`.

* `dayType !== "work"` renders the planned column as "off" and greys the row —
  `Dr = (t, s, n) => s === "off" ? alpha(text.disabled, .07) : ...`.
* `plannedStart`/`plannedEnd` come from the employee's **work schedule** (`/work-schedules`), which is
  stored per weekday: `{ dayOfWeek, startTime, endTime, employeeId }`.
* `workedMinutes` comes from turnstile check-in/check-out.
* `offScheduleMinutes` is time inside the building outside the planned window; it is reported in its
  own column (`hr.timesheet.table.offSchedule`) and **not** added to on-time hours:
  `onTime = Σ (flag === "late" ? 0 : workedMinutes)`.

Aggregates per employee: `plannedDays`, `plannedMinutes`, `factDays`, `workedMinutes`,
`offScheduleMinutes`. Completion percentage and its colour band:

```js
Zt = (plan, fact) => plan <= 0 ? null : Math.round(fact / plan * 100);
Bo = p => p === null ? "success" : p <= 80 ? "error" : p < 95 ? "warning" : "success";
```

**The bundle does not reveal how `plannedMinutes` → money for a fixed employee.** The `gross` figure
arrives from the server already computed, and the per-row "formula" strings shown in the accruals
table (`payroll-accrual-formula-body-*`) are server-rendered text. See §11 Q1.

### 2.4 Payroll figures and their relationships

The payroll row, read off the two column sets (`Ql` = accruals tab, `ea` = deductions tab) and the
detail drawer (`ql`):

| Field | Tab | Notes |
|---|---|---|
| `employee { _id, fullName, imageUrl, type }` | both | |
| `calcType` | accruals | default label key `schedule-hours`; only that one value appears in the bundle |
| `cardPaid` | accruals | official part |
| `cashPaid` | accruals | envelope part |
| `taxBase` | accruals | NDFL hint is `hr.payroll.detail.fromTaxBase` → NDFL is charged on this |
| `gross` | accruals | KPI tile `hr.payroll.kpi.gross` |
| `ndfl` | deductions | shown as `−`, header suffixed with `ndflPercent` |
| `inps` | deductions | shown as `−` |
| `esp` | deductions | shown **without** a sign, tooltip `hr.payroll.table.espHint`, plus a standing info alert `hr.payroll.espInfo` |
| `penaltyAmount` | deductions | `−` |
| `bonusAmount` | drawer only | `+` |
| `loanTotal`, `loanThisMonth` | deductions | drawer hint is `hr.payroll.detail.loanNotDeducted` |
| `net` | deductions | bold, the payout |

Two relationships are proven, not inferred:

```js
// KPI "withheld"
c = nl({ ndfl: t.ndfl, inps: t.inps, penalty: t.penalty });   // withheld = ndfl + inps + penalty
// document list "held" column
S((row.totalNdfl ?? 0) + (row.totalInps ?? 0) + (row.totalPenalty ?? 0))
```

**`withheld = ndfl + inps + penalty`. ESP is not withheld** — it is the employer's own cost, which is
why it has an info banner instead of a minus sign, and why the KPI label carries
`hr.payroll.kpi.espSuffix`.

**Loans are shown but not deducted** — `hr.payroll.detail.loanNotDeducted` with the outstanding total
as a parameter. `loanThisMonth` is informational.

The drawer renders the breakdown in this order, which is the intended reading order of the formula:
`cardPaid`, `cashPaid`, `−ndfl`, `−inps`, `−penalty`, `+bonus`, `loan (info)`, `= net`.

**[inferred]** `net = cardPaid + cashPaid − ndfl − inps − penaltyAmount + bonusAmount`, and
`gross = cardPaid + cashPaid`. The bundle never asserts either equation. See §11 Q2.

### 2.5 Tax rates

From the finance settings "tax" tab (`bt`, gated on `editPayroll`):

```js
ue = { payrollNdflPercent: 12, payrollInpsPercent: .1, payrollEspPercent: 12 }
me = [ndfl, inps, esp]                                   // numeric, validated 0 ≤ x ≤ 100
pe = [payrollMergeRows, payrollNoCashIfLowHours]          // booleans
ht = o => { const t = Number(o.replace(",", ".")); return !Number.isFinite(t) || t > 100 || t < 0 ? null : t }
```

* **NDFL 12 %** (personal income tax), **INPS 0.1 %** (pension savings), **ESP 12 %** (employer social tax).
  All three are editable percentages, 0–100, comma accepted as decimal separator.
* `payrollMergeRows` — **[inferred]** merge an employee's several employment/calc-type rows into one
  payroll line instead of listing them separately.
* `payrollNoCashIfLowHours` — **[inferred]** when actual hours fall short of plan, pay only the card
  (official) part and drop the cash part. Name and context support this; nothing proves it. See §11 Q3.

The document-level totals mirror these: `totalGross`, `totalNdfl`, `totalInps`, `totalEsp`,
`totalPenalty`, and per-line `rates { ndflPercent, inpsPercent, espPercent }` are snapshotted onto the
document, so re-reading an old document uses the rates that were in force then, not today's.

### 2.6 Penalty and bonus rules

`/hr/penalties` has two tabs: **rules** (`/hr/rules/pagin`) and **applied** (`/hr/applied-rules/pagin`).

Rule shape, taken verbatim from the submit handler `U`:

```js
{
  kind,                       // "penalty" | "bonus"
  reason,                     // see matrix below
  scope,                      // "department" | "position" | "employee" | "all"
  departmentId | jobTitleId | employeeId,   // exactly the one the scope names
  amountType,                 // "percent" | "fixed"
  percent | fixedAmount,      // exactly one, per amountType
  allowedLateMinutes,         // integer 0..600
  fromYear, fromMonth,        // effective from this month, inclusive
  isActive                    // toggle in the list
}
```

Reason matrix — a reason belongs to exactly one kind:

```js
Tt = { penalty: ["late", "early", "absence"],
       bonus:   ["no-late", "full-hours", "substitute"] }
```

| kind | reason | label key | meaning |
|---|---|---|---|
| penalty | `late` | `hr.penalties.reason.late` | arrived after the planned start |
| penalty | `early` | `hr.penalties.reason.early` | left before the planned end |
| penalty | `absence` | `hr.penalties.reason.absence` | scheduled work day with no attendance |
| bonus | `no-late` | `hr.penalties.reason.noLate` | no late arrivals in the period |
| bonus | `full-hours` | `hr.penalties.reason.fullHours` | worked the full planned hours |
| bonus | `substitute` | `hr.penalties.reason.substitute` | covered someone else's lesson |

Validation (all three produce their own error key):

* `percent`: `Number.isFinite(c) && c > 0 && c <= 100` → `hr.penalties.errors.percentRange`
* `fixedAmount`: `Number.isFinite(c) && c > 0` → `hr.penalties.errors.fixedPositive`
* `allowedLateMinutes`: `Number.isInteger(c) && c >= 0 && c <= 600` → `hr.penalties.errors.allowedLateRange`
* effective month cannot be earlier than `dayjs().subtract(5,"year").startOf("year")`
* scope `all` shows a warning banner `hr.penalties.form.allWarning` before saving

**Rules are additive** — the tab header shows a standing info alert `hr.penalties.additiveInfo`.
Several matching rules all fire; there is no "most specific wins" resolution.

Applied rules (the result of running the rules over a period) are one row per
`(employee, kind, reason, year, month)` with a **`count`** — how many times the rule fired — and an
`amount`. The list is filterable to a day range inside the period (`fromDay` / `toDay`), which means
the underlying records are dated.

`allowedLateMinutes` sits on the rule for **both** kinds. **[inferred]** it is the grace window:
a `late` penalty fires only past it, and a `no-late` bonus survives lateness inside it. See §11 Q4.

The payroll deductions table shows, per deduction line, `kind`, `base`, `rate` (`%`) and `amount` —
so a percent rule is `amount = base × rate%`. **What `base` is (monthly salary? gross? day rate?)
is not in the bundle.** See §11 Q5.

### 2.7 Finance "Bonus" and "Jarima" are a different thing

`FINANCE_ALL.BONUS` / `FINANCE_ALL.PENALTY` are **not** the HR rules. They are manual, one-off
adjustments to an employee's (or a student's) running **balance**:

```js
row = { student | employee, beforeAmount, amount, afterAmount, state, comment, createdAt }
state ∈ { "active", "cancelled" }
```

Create form: `transactionTypeId` (from `/transaction-types?type=vouncher`, each type carries
`hasImpactOn: "student" | "employee"`), then `studentId` **or** `employeeId` depending on
`hasImpactOn`, `amount`, `comment`. Endpoints `POST /bonus`, `POST /bonus-cancel`, `GET /bonus-pagin`
(and the `penalty` triplet). Permissions `createBonus` / `cancelBonus` / `getBonus`
(and `...Penalty`). A cancelled row is never deleted, only flagged.

The employee balance itself is served by `/employees/balance`.

### 2.8 Legacy "Ish haqi" (`FINANCE_ALL.MONTHLY_SALARY`)

The older, pre-HR payroll. One document per date range, listing per employee:

`fixSalary`, `flexSalary`, `bonus`, `punish`, `beforeAmount` (balance before),
`salary` (paid this run), `afterAmount` (balance after).

Document columns: `fromDate–toDate`, `employeesCount`, `totalSalaryAmount`.
Cancel via `POST /salary/cancel { _id }` or `{ _id, employeeIds[] }` for a subset —
permission `cancelEmployeeSalary`. Excel export headers name the columns in Uzbek
(`fix_salary`, `flex_salary`, `bonus`, `punish`, `current_balanse`, `salary`, `balanse_from_salary`).

This is the generation the HR module replaced. **We do not rebuild it.** Its only useful idea — that
paying an employee moves a *balance* — is preserved in our design as an accrual liability (§5).

### 2.9 Missed lessons (`SETTINGS_ALL.MISSED_LESSONS`)

Manual record of a teacher skipping a lesson. Independent of the timesheet's automatic payability.

```
POST /employee-missed-lesson { employeeId, classId, subjectId, hours, lessonDate }
hours: integer 1..24                       → missedLessons.errors.hoursRange
lessonDate: not in the future (maxDate = today) and must be a day the schedule actually has a lesson on
```

Cascading option source `GET /employee-missed-lesson/lesson-options` returns
`{ employees[], classes[], subjects[], lessonDates[] }`, each narrowed by whatever is already picked;
the form clears any selection the server drops out of the option set.

Server error codes: **60006** = no scheduled lesson on that date
(`missedLessons.errors.noScheduledLesson`), **60001** = duplicate record
(`missedLessons.errors.duplicate`).

Analytics tab (`GET /employee-missed-lesson/analytics`):
`{ totalHours, totalRecords, employeeCount, subjectCount, topEmployee {label,hours,count},
topSubject {label,hours}, byEmployee[], bySubject[], byClass[] }`.

### 2.10 Staff requests (`HR_ALL.REQUESTS`)

```
kinds    = ["missed-day", "schedule", "manual-entry"]
statuses = ["pending", "approved", "rejected"]
row      = { _id, kind, date, employee, detail, asked, requestedIn, requestedOut,
             fileUrl, status, reviewer, reviewedAt, rejectReason }
```

`GET /hr/requests/pagin?page&limit&status&kind&departmentId&employeeId&dateFrom&dateTo`
`PUT /hr/request/approve { _id }`
`PUT /hr/request/reject  { _id, rejectReason }` — reason is mandatory, empty is blocked client-side.

Error **61403** on approve/reject means the period is already closed
(`hr.requests.periodClosedHint`). This is the immutability guard leaking through: once the timesheet
document for that month is posted, a request that would change the month is refused.

### 2.11 Dismissal report (`ANALITICS_ALL.DISMISSAL_REPORT`)

Two levels, both filtered by `fromDate` / `toDate`:

* `GET /analitics/employee/dismissal-report` — one row per dismissal reason: `reasonId`, count, `percent`.
* `GET /analitics/employee/dismissal-report/details?reasonId=…` — the employees behind one reason.
* Both have `/export` siblings.

The data comes from the employee resign flow: `resignReasonId` (a reason of
`type: "employee-resign"`), `resignDate`, `resignComment`.

### 2.12 Work schedule and free time

* **Work schedule** (`/work-schedules`) — a named, reusable weekly pattern with a `status` enum,
  assigned to employees. CRUD plus `POST /work-schedules/create-many` and
  `PUT /work-schedules/update-many` for bulk day rows. Permissions `getWorkSchedules` /
  `editWorkSchedules` / `deleteWorkSchedules`.
* **Employee free time** (`/working-hours`, menu title "Xodimlar bo'sh vaqti", permission
  `getMeetings`) — per employee, one row per weekday: `{ dayOfWeek, startTime, endTime, employeeId }`,
  POST/PUT/DELETE one row at a time. Used for booking meetings with staff, **not** for payroll.
  Do not confuse the two: the payroll plan comes from the work schedule.

### 2.13 Loans

`GET /hr/loans/pagin?employeeId&year&month`, permissions `getEmployeeLoans` / `editEmployeeLoans` /
`deleteEmployeeLoans`, plus a tab on the employee card. No dedicated page in the bundle. Feeds
`loanTotal` / `loanThisMonth` on the payroll row, which are **informational only**.

---

## 3. States and immutability

Payroll is money. `docs/SPEC.md` §4 forbids silent edits of money records, and the same reasoning
applies here: a payroll run that has been paid must not be quietly rewritten.

### 3.1 EduSchool's state machines

**Timesheet document** — states `draft` | `posted`.

```
create (draft)
  → fill      POST /hr/timesheet-document/fill      populate days/lessons from schedule + turnstile
  → edit      PUT  /hr/timesheet-document           header only, draft only
  → correct   day-level edits, each recorded as a correction row
  → post      POST /hr/timesheet-document/post      requires a fill first (hr.timesheet.doc.postNeedsFill)
  → delete    draft only; blocked when a payroll document uses it as a source
              (hr.timesheet.doc.deleteBlocked / deleteBlockedGeneric)
posted → read-only (hr.timesheet.doc.postedNotice, hr.timesheet.doc.lockedNotice)
```

Guards observed: a second document covering the same employees in the same period is refused with
`conflictingEmployeeIds` + `conflictingCount` (`hr.timesheet.doc.overlap.*`); creating a duplicate
document for a period warns with `hr.timesheet.doc.createDuplicate.*`.

**Corrections** are first-class: `GET /hr/timesheet-corrections/pagin`,
`POST /hr/timesheet-correction/revert`, columns `field`, `oldValue`, `newValue`, `reason`,
`correctionState` ∈ {applied, reverted, isRevert}. A correction is never edited — it is reverted by
appending an opposite correction. Same pattern as our `ledger_entries.reversal_of`.

**Payroll document** — states `draft` | `closed` (the `closed` state is *labelled* "posted"):

```js
gl = { draft: "hr.payroll.doc.state.draft", closed: "hr.payroll.doc.state.posted" };
jl = { draft: "warning", closed: "success" };
bl = t => !!t && t.state === "draft";     // "can edit"
Sl = t => !!(t?.filledAt);                // "has been filled"
```

```
create (draft, sources = selected posted timesheet documents)
  → preview  POST /hr/payroll-document/preview   accruals[] + deductions[] + totals + rates, nothing saved
  → fill     POST /hr/payroll-document/fill      materialise payroll_lines, sets filledAt
  → edit     PUT  /hr/payroll-document           draft only
  → post     POST /hr/payroll-document/post      requires fill (hr.payroll.doc.postNeedsFill)
  → delete   draft only
closed → read-only (hr.payroll.doc.alert.postedTitle/postedBody)
```

Source validation refuses bad selections with structured errors:

* **61511** — one or more selected timesheet documents no longer exist → `data.timesheets[]`
* **61512** — the selected timesheets do not cover all the chosen departments →
  `data.departments[]`, `data.timesheets[]`
* **61605** — (period payroll) there is no *posted* timesheet for the month; `data.drafts[]` lists the
  draft documents so the user can go post one

The alternative, period-oriented payroll (`/hr/payroll/preview|generate|pagin|close`) has the same
shape with `state` ∈ {…, `closed`} and a one-way `PUT /hr/payroll/close { _id }`; after closing, both
preview and generate are disabled and a banner `hr.payroll.closedBanner` is shown.

### 3.2 What we will do

We implement **one** payroll model — the document model — because it is the newer of the two, it
carries a number, an author, an explicit source, and a real state. Our state machine:

```
draft ──fill──► draft (filled) ──post──► posted ──reverse──► reversed
  │                                         │
  └──delete (only while draft) ─────────────┘  (posted can never be deleted or edited)
```

| Transition | Who | Preconditions | Side effects |
|---|---|---|---|
| create | `editPayroll` | period not already covered by a posted document | `state='draft'`, `number` assigned |
| fill | `editPayroll` | `state='draft'`; every source timesheet document is `posted` | replaces `payroll_lines`, sets `filled_at` |
| edit header | `editPayroll` | `state='draft'` | — |
| post | `editPayroll`, **and the poster ≠ the creator** | `state='draft'` and `filled_at` not null | `state='posted'`, `posted_at`, `posted_by`; **one balanced ledger batch** (§5.2) |
| reverse | `superadmin`, reason mandatory, reverser ≠ poster | `state='posted'` | `state='reversed'`; `LedgerService.ReverseAsync` mirrors the batch |
| delete | `editPayroll` | `state='draft'` and no payroll payment references it | row removed |

Timesheet document: `draft → posted → reversed`, same rule set, minus the ledger.

**Enforcement is two-layered, exactly like billing:**

1. **Service layer** — `PayrollDocumentService` refuses any mutating call when `state <> 'draft'`.
2. **Database** — `Migrations/Sql/payroll_guards.sql`, following the pattern of
   `Migrations/Sql/anomaly_guards.sql`:
   * `BEFORE UPDATE OR DELETE` trigger on `payroll_documents` and `payroll_lines` that raises
     `check_violation` when `OLD.state <> 'draft'` (for lines: when the parent document is not draft).
     A trigger, not a grant, because draft rows *must* stay editable — a blanket `REVOKE UPDATE`
     would break the fill step.
   * `GRANT SELECT, INSERT ON payroll_payments TO app_rw` and
     `REVOKE UPDATE, DELETE, TRUNCATE ON payroll_payments FROM app_rw` — a salary payment is money
     that left the building; it is append-only like `payments`, and a mistake is corrected by a
     reversal, never an edit.
   * `GRANT SELECT, INSERT, UPDATE, DELETE` on the mutable HR tables
     (`departments`, `job_titles`, `work_schedules`, `work_schedule_days`, `hr_employees`,
     `hr_rules`, `hr_requests`, `hr_missed_lessons`, `hr_settings`, `hr_loans`).
   * `GRANT SELECT, INSERT` + `REVOKE UPDATE, DELETE, TRUNCATE` on `hr_timesheet_corrections` —
     a correction is an audit record; it is reverted by appending, never rewritten.
   * As always, the block is wrapped in `DO $guards$ ... IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE
     rolname='app_rw') THEN RAISE NOTICE ... RETURN; END IF;` so the test harness (which has no
     `app_rw`) still migrates.

3. **Audit** — every post/reverse writes an `audit_log` row inside the same `SaveChanges` as the
   ledger batch. `LedgerService.PostAsync` already does this for us; the document-state change must
   be in the same transaction.

---

## 4. Data model

Conventions: `snake_case`; new HR money tables use real types (`uuid`, `date`, `timestamptz`,
`numeric(14,2)`), matching `SchoolLms.Domain/Billing.cs`. References to Phase-0 entities
(`teachers`, `app_users`, `classes`, `subjects`) are `text` because those PKs are `text` — the FK
column type must match the parent, so the mix is deliberate, exactly as documented in `Billing.cs`.

> **Reminder for whoever writes the migration:** all new EF `DateTime` columns get forced to
> `timestamp without time zone` by `AppDbContext.OnModelCreating`. Use `DateTimeOffset` for anything
> that must survive a timezone question (posting timestamps, payment timestamps) and `DateOnly` for
> accounting dates. Every new table needs a `GRANT` line in `payroll_guards.sql`.

### 4.1 Tables we extend (three columns, total)

| Table | Change | Why |
|---|---|---|
| `expenses` | **none** | `teacher_id` (added by P1-21) already exists and stays. Ad-hoc salary payouts outside a payroll run keep using it. |
| `billing_settings` | **none** | HR gets its own settings row; mixing tax rates into billing settings would confuse the "who may edit money settings" question. |
| `teachers` | **none** | HR-side attributes go on `hr_employees`. `teachers.Category`, `BonusPct`, `SalaryStartDate` stay for the existing `SalaryRatesController`, which we do not touch. |

Plus **two shared-file edits**, both tiny and both sequential (see §6, HR-02):

* `SchoolLms.Application/Billing/Accounts.cs` — three new account codes and their dictionary entries:
  `liability:salary`, `liability:tax`, `expense:payroll_tax`.
* `SchoolLms.Server/Program.cs` — DI registrations.

### 4.2 New tables

```sql
-- ---------- Org structure ----------
create table departments (
  id         uuid primary key default gen_random_uuid(),
  name       text not null unique,
  is_active  boolean not null default true,
  created_at timestamptz not null default now()
);

create table job_titles (
  id            uuid primary key default gen_random_uuid(),
  name          text not null unique,
  department_id uuid references departments(id),
  is_active     boolean not null default true,
  created_at    timestamptz not null default now()
);

-- ---------- Payroll identity ----------
-- One row per person who can be paid. NOT a replacement for `teachers` or `app_users`;
-- it is the HR-side facet, added alongside them (CLAUDE.md: work additively).
-- Reason it exists: EduSchool pays every employee, not only teachers, and every HR screen
-- filters by department / job title. Duplicating those columns onto both `teachers` and
-- `app_users` would fork every payroll query.
create table hr_employees (
  id                 uuid primary key default gen_random_uuid(),
  teacher_id         text unique references teachers(id),
  user_id            text unique references app_users(id),
  full_name          text not null,
  department_id      uuid references departments(id),
  job_title_id       uuid references job_titles(id),
  work_schedule_id   uuid references work_schedules(id),
  pay_kind           text not null default 'fixed'
                     check (pay_kind in ('fixed','hourly','mixed')),
  monthly_salary     numeric(14,2) not null default 0 check (monthly_salary >= 0),
  card_salary        numeric(14,2) not null default 0 check (card_salary  >= 0),
  cash_salary        numeric(14,2) not null default 0 check (cash_salary  >= 0),
  single_lesson_cost numeric(14,2) not null default 0 check (single_lesson_cost >= 0),
  hired_on           date,
  dismissed_on       date,
  dismissal_reason_id uuid references dismissal_reasons(id),
  dismissal_comment  text,
  status             text not null default 'active'
                     check (status in ('active','dismissed','archived')),
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now(),
  -- exactly one identity link
  constraint ck_hr_employees_identity check (num_nonnulls(teacher_id, user_id) = 1),
  -- §2.1: the declared split may not exceed the declared salary
  constraint ck_hr_employees_salary_split
    check (pay_kind = 'hourly' or card_salary + cash_salary <= monthly_salary),
  -- a dismissed employee must say when and why
  constraint ck_hr_employees_dismissal
    check (status <> 'dismissed' or (dismissed_on is not null and dismissal_reason_id is not null))
);
create index hr_employees_department on hr_employees (department_id) where status = 'active';

create table dismissal_reasons (
  id        uuid primary key default gen_random_uuid(),
  name      text not null unique,
  is_active boolean not null default true
);

-- ---------- Work schedule ----------
create table work_schedules (
  id         uuid primary key default gen_random_uuid(),
  name       text not null unique,           -- "5/2 08:30–17:30", "2-smena"
  status     text not null default 'active' check (status in ('active','archived')),
  created_at timestamptz not null default now()
);

create table work_schedule_days (
  schedule_id uuid not null references work_schedules(id) on delete cascade,
  weekday     smallint not null check (weekday between 0 and 6),  -- 0 = Monday, matches ScheduleLesson.Day
  starts_at   time,                          -- null on both = day off
  ends_at     time,
  break_min   smallint not null default 0 check (break_min >= 0),
  primary key (schedule_id, weekday),
  constraint ck_wsd_window check (
    (starts_at is null and ends_at is null) or
    (starts_at is not null and ends_at is not null and ends_at > starts_at))
);

-- Per-employee meeting availability. Deliberately separate from work_schedule_days:
-- EduSchool keeps them apart (`/working-hours` vs `/work-schedules`) and they answer
-- different questions — "when may I book you" vs "when are you paid to be here".
create table hr_employee_free_time (
  id          uuid primary key default gen_random_uuid(),
  employee_id uuid not null references hr_employees(id) on delete cascade,
  weekday     smallint not null check (weekday between 0 and 6),
  starts_at   time not null,
  ends_at     time not null check (ends_at > starts_at),
  unique (employee_id, weekday, starts_at)
);

-- ---------- Timesheet ----------
create table hr_timesheet_documents (
  id             uuid primary key default gen_random_uuid(),
  number         text not null unique,        -- auto, gapless per year
  doc_date       date not null,
  period_year    smallint not null check (period_year between 2000 and 2100),
  period_month   smallint not null check (period_month between 1 and 12),
  period_type    text not null default 'full'
                 check (period_type in ('full','first_half','second_half')),
  include_fixed  boolean not null default true,
  include_hourly boolean not null default true,
  note           text,
  state          text not null default 'draft'
                 check (state in ('draft','posted','reversed')),
  filled_at      timestamptz,
  posted_at      timestamptz,
  posted_by      text references app_users(id),
  created_by     text not null references app_users(id),
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),
  constraint ck_ts_doc_categories check (include_fixed or include_hourly)
);

create table hr_timesheet_document_departments (
  document_id   uuid not null references hr_timesheet_documents(id) on delete cascade,
  department_id uuid not null references departments(id),
  primary key (document_id, department_id)
);

-- Explicit employee narrowing. Empty set = every employee in the chosen departments.
create table hr_timesheet_document_employees (
  document_id uuid not null references hr_timesheet_documents(id) on delete cascade,
  employee_id uuid not null references hr_employees(id),
  primary key (document_id, employee_id)
);

-- One row per employee per calendar day inside the document.
create table hr_timesheet_days (
  id                   uuid primary key default gen_random_uuid(),
  document_id          uuid not null references hr_timesheet_documents(id) on delete cascade,
  employee_id          uuid not null references hr_employees(id),
  on_date              date not null,
  day_type             text not null default 'work'
                       check (day_type in ('work','off','holiday','leave','sick')),
  planned_start        time,
  planned_end          time,
  planned_minutes      integer not null default 0 check (planned_minutes >= 0),
  actual_in            time,
  actual_out           time,
  worked_minutes       integer not null default 0 check (worked_minutes >= 0),
  off_schedule_minutes integer not null default 0 check (off_schedule_minutes >= 0),
  late_minutes         integer not null default 0 check (late_minutes >= 0),
  early_leave_minutes  integer not null default 0 check (early_leave_minutes >= 0),
  flag                 text not null default 'ok'
                       check (flag in ('ok','late','early','absent','off','no_schedule','future')),
  unique (document_id, employee_id, on_date)
);
create index hr_timesheet_days_employee_date on hr_timesheet_days (employee_id, on_date);

-- One row per scheduled lesson inside the document, for hourly employees.
create table hr_timesheet_lessons (
  id                        uuid primary key default gen_random_uuid(),
  document_id               uuid not null references hr_timesheet_documents(id) on delete cascade,
  employee_id               uuid not null references hr_employees(id),
  on_date                   date not null,
  period                    smallint not null check (period between 1 and 10),
  class_id                  text not null references classes(id),
  subject_id                text not null references subjects(id),
  sub_group                 smallint not null default 0 check (sub_group between 0 and 2),
  starts_at                 time,
  ends_at                   time,
  has_face_id               boolean not null default false,
  is_marked                 boolean not null default false,
  late_minutes              integer not null default 0 check (late_minutes >= 0),
  substituted_for_employee_id uuid references hr_employees(id),
  computed_payable          boolean not null,              -- has_face_id AND is_marked  (§2.2)
  override_payable          boolean,                       -- null = no manual override
  payable                   boolean generated always as
                              (coalesce(override_payable, computed_payable)) stored,
  single_lesson_cost        numeric(14,2) not null default 0,   -- snapshot of the rate at fill time
  amount                    numeric(14,2) not null default 0,   -- payable ? single_lesson_cost : 0
  unique (document_id, employee_id, on_date, period, class_id, sub_group)
);
create index hr_timesheet_lessons_employee on hr_timesheet_lessons (employee_id, on_date);

-- Append-only. Every manual change to a filled timesheet lands here.
create table hr_timesheet_corrections (
  id           bigserial primary key,
  document_id  uuid not null references hr_timesheet_documents(id) on delete restrict,
  employee_id  uuid not null references hr_employees(id),
  target_kind  text not null check (target_kind in ('day','lesson')),
  target_id    uuid not null,
  field        text not null,                  -- 'actual_in' | 'payable' | 'day_type' | ...
  old_value    text,
  new_value    text,
  reason       text,
  is_revert    boolean not null default false,
  reverted_by  bigint references hr_timesheet_corrections(id),
  created_by   text not null references app_users(id),
  created_at   timestamptz not null default now()
);

-- ---------- Penalty / bonus rules ----------
create table hr_rules (
  id                   uuid primary key default gen_random_uuid(),
  kind                 text not null check (kind in ('penalty','bonus')),
  reason               text not null check (reason in
                         ('late','early','absence','no-late','full-hours','substitute')),
  scope                text not null check (scope in ('all','department','position','employee')),
  department_id        uuid references departments(id),
  job_title_id         uuid references job_titles(id),
  employee_id          uuid references hr_employees(id),
  amount_type          text not null check (amount_type in ('percent','fixed')),
  percent              numeric(5,2) check (percent > 0 and percent <= 100),
  fixed_amount         numeric(14,2) check (fixed_amount > 0),
  allowed_late_minutes smallint not null default 0
                       check (allowed_late_minutes between 0 and 600),
  from_year            smallint not null,
  from_month           smallint not null check (from_month between 1 and 12),
  is_active            boolean not null default true,
  created_by           text not null references app_users(id),
  created_at           timestamptz not null default now(),
  updated_at           timestamptz not null default now(),
  -- kind and reason must agree (§2.6)
  constraint ck_hr_rules_kind_reason check (
    (kind = 'penalty' and reason in ('late','early','absence')) or
    (kind = 'bonus'   and reason in ('no-late','full-hours','substitute'))),
  -- the scope target must be exactly the one the scope names
  constraint ck_hr_rules_scope check (
    (scope = 'all'        and department_id is null and job_title_id is null and employee_id is null) or
    (scope = 'department' and department_id is not null and job_title_id is null and employee_id is null) or
    (scope = 'position'   and job_title_id  is not null and department_id is null and employee_id is null) or
    (scope = 'employee'   and employee_id   is not null and department_id is null and job_title_id is null)),
  -- exactly one amount
  constraint ck_hr_rules_amount check (
    (amount_type = 'percent' and percent is not null and fixed_amount is null) or
    (amount_type = 'fixed'   and fixed_amount is not null and percent is null))
);

-- The result of running the rules over one period. Rebuilt by `fill`, frozen by `post`.
create table hr_applied_rules (
  id           uuid primary key default gen_random_uuid(),
  document_id  uuid not null references payroll_documents(id) on delete cascade,
  rule_id      uuid not null references hr_rules(id),
  employee_id  uuid not null references hr_employees(id),
  kind         text not null check (kind in ('penalty','bonus')),
  reason       text not null,
  occurrences  integer not null check (occurrences > 0),   -- the "count" column in EduSchool
  base         numeric(14,2) not null default 0,           -- what the percent applied to
  rate         numeric(5,2),                               -- null for fixed rules
  amount       numeric(14,2) not null check (amount >= 0),
  detail       jsonb,                                      -- the dates that triggered it
  unique (document_id, rule_id, employee_id)
);

-- ---------- Payroll ----------
create table payroll_documents (
  id             uuid primary key default gen_random_uuid(),
  number         text not null unique,
  doc_date       date not null,
  period_year    smallint not null,
  period_month   smallint not null check (period_month between 1 and 12),
  name           text,
  note           text,
  currency       text not null default 'UZS' check (currency in ('UZS','USD')),
  merge_rows     boolean not null default false,
  -- rates snapshotted at fill time; a reopened old document must not re-tax at today's rates
  ndfl_percent   numeric(5,2) not null,
  inps_percent   numeric(5,2) not null,
  esp_percent    numeric(5,2) not null,
  total_gross    numeric(14,2) not null default 0,
  total_ndfl     numeric(14,2) not null default 0,
  total_inps     numeric(14,2) not null default 0,
  total_esp      numeric(14,2) not null default 0,
  total_penalty  numeric(14,2) not null default 0,
  total_bonus    numeric(14,2) not null default 0,
  total_net      numeric(14,2) not null default 0,
  state          text not null default 'draft'
                 check (state in ('draft','posted','reversed')),
  filled_at      timestamptz,
  posted_at      timestamptz,
  posted_by      text references app_users(id),
  created_by     text not null references app_users(id),
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),
  constraint ck_payroll_poster_differs check (posted_by is null or posted_by <> created_by)
);

-- Only one posted document may cover a period. Drafts may coexist.
create unique index payroll_documents_posted_period
  on payroll_documents (period_year, period_month)
  where state = 'posted';

create table payroll_document_departments (
  document_id   uuid not null references payroll_documents(id) on delete cascade,
  department_id uuid not null references departments(id),
  primary key (document_id, department_id)
);

create table payroll_document_sources (
  document_id           uuid not null references payroll_documents(id) on delete cascade,
  timesheet_document_id uuid not null references hr_timesheet_documents(id) on delete restrict,
  primary key (document_id, timesheet_document_id)
);

create table payroll_lines (
  id            uuid primary key default gen_random_uuid(),
  document_id   uuid not null references payroll_documents(id) on delete cascade,
  employee_id   uuid not null references hr_employees(id),
  calc_type     text not null check (calc_type in ('fixed-month','schedule-hours','lesson-count','mixed')),
  planned_minutes integer not null default 0,
  worked_minutes  integer not null default 0,
  planned_lessons integer not null default 0,
  paid_lessons    integer not null default 0,
  card_paid     numeric(14,2) not null default 0 check (card_paid >= 0),
  cash_paid     numeric(14,2) not null default 0 check (cash_paid >= 0),
  bonus_amount  numeric(14,2) not null default 0 check (bonus_amount  >= 0),
  penalty_amount numeric(14,2) not null default 0 check (penalty_amount >= 0),
  gross         numeric(14,2) not null check (gross >= 0),
  tax_base      numeric(14,2) not null check (tax_base >= 0),
  ndfl          numeric(14,2) not null default 0 check (ndfl >= 0),
  inps          numeric(14,2) not null default 0 check (inps >= 0),
  esp           numeric(14,2) not null default 0 check (esp  >= 0),   -- employer cost, not withheld
  loan_total    numeric(14,2) not null default 0,                     -- informational
  loan_this_month numeric(14,2) not null default 0,                   -- informational
  net           numeric(14,2) not null check (net >= 0),
  formula       jsonb,                    -- ordered human-readable steps, shown in the UI
  unique (document_id, employee_id)
);

-- Actual money leaving for a payroll line. IMMUTABLE (append-only, §3.2).
create table payroll_payments (
  id            uuid primary key default gen_random_uuid(),
  line_id       uuid not null references payroll_lines(id) on delete restrict,
  employee_id   uuid not null references hr_employees(id),
  amount        numeric(14,2) not null check (amount > 0),
  method        text not null check (method in ('cash','card','transfer','online')),
  -- a cash payout leaves the till, so it must belong to an open shift or the
  -- Z-report's expected_cash will not reconcile (SPEC §4.2)
  cash_shift_id uuid references cash_shifts(id),
  on_date       date not null,
  note          text,
  paid_by       text not null references app_users(id),
  approved_by   text references app_users(id),
  created_at    timestamptz not null default now(),
  reversal_of   uuid references payroll_payments(id),
  constraint ck_payroll_payment_approver_differs
    check (approved_by is null or approved_by <> paid_by),
  constraint ck_payroll_payment_shift
    check (method <> 'cash' or cash_shift_id is not null)
);
create index payroll_payments_line on payroll_payments (line_id);

-- ---------- Requests, missed lessons, loans, settings ----------
create table hr_requests (
  id            uuid primary key default gen_random_uuid(),
  employee_id   uuid not null references hr_employees(id),
  kind          text not null check (kind in ('missed-day','schedule','manual-entry')),
  on_date       date not null,
  detail        text not null,
  requested_in  time,
  requested_out time,
  file_url      text,
  status        text not null default 'pending'
                check (status in ('pending','approved','rejected')),
  reviewed_by   text references app_users(id),
  reviewed_at   timestamptz,
  reject_reason text,
  created_at    timestamptz not null default now(),
  constraint ck_hr_requests_reject check (status <> 'rejected' or nullif(btrim(reject_reason),'') is not null),
  constraint ck_hr_requests_review check (status = 'pending' or (reviewed_by is not null and reviewed_at is not null))
);
create index hr_requests_pending on hr_requests (status, on_date desc);

create table hr_missed_lessons (
  id          uuid primary key default gen_random_uuid(),
  employee_id uuid not null references hr_employees(id),
  class_id    text not null references classes(id),
  subject_id  text not null references subjects(id),
  on_date     date not null,
  hours       smallint not null check (hours between 1 and 24),
  note        text,
  created_by  text not null references app_users(id),
  created_at  timestamptz not null default now(),
  unique (employee_id, class_id, subject_id, on_date)      -- error 60001 = duplicate
);

create table hr_loans (
  id           uuid primary key default gen_random_uuid(),
  employee_id  uuid not null references hr_employees(id),
  principal    numeric(14,2) not null check (principal > 0),
  issued_on    date not null,
  monthly_due  numeric(14,2) not null default 0 check (monthly_due >= 0),
  outstanding  numeric(14,2) not null check (outstanding >= 0),
  status       text not null default 'open' check (status in ('open','closed','written_off')),
  note         text,
  created_by   text not null references app_users(id),
  created_at   timestamptz not null default now()
);

-- Single row, same pattern as BillingSettings.
create table hr_settings (
  id                       uuid primary key,             -- fixed singleton id, seeded
  ndfl_percent             numeric(5,2) not null default 12   check (ndfl_percent between 0 and 100),
  inps_percent             numeric(5,2) not null default 0.1  check (inps_percent between 0 and 100),
  esp_percent              numeric(5,2) not null default 12   check (esp_percent  between 0 and 100),
  merge_rows               boolean not null default false,
  no_cash_if_low_hours     boolean not null default false,
  low_hours_threshold_pct  smallint not null default 100 check (low_hours_threshold_pct between 0 and 100),
  updated_at               timestamptz not null default now(),
  updated_by               text references app_users(id)
);
```

**Table count: 22 new, 0 altered.** That is a lot; §6 stages them.

### 4.3 Rounding, currency, period boundary — decided here, not later

* **Currency.** `numeric(14,2)`, so'm, two decimals. EduSchool offers `UZS | USD` on the payroll
  document; we store the column for compatibility but the only accepted value is `UZS` until someone
  asks otherwise. Reason: our ledger has no FX machinery and adding one for a school that bills in
  so'm is unpaid complexity.
* **Rounding.** Every intermediate result is `decimal.Round(x, 2, MidpointRounding.ToEven)` — the same
  `MoneyScale = 2` constant that `LedgerService` and `ExpenseService` already use. Rounding happens
  **once per stored column**, never on the way into a sum. Concretely: `ndfl` is rounded, `inps` is
  rounded, then `net = gross − ndfl − inps − penalty + bonus` is computed from the *rounded* parts, so
  the stored numbers always add up. This is the same discipline `LedgerService.PostAsync` enforces
  ("Summani BAZA ANIQLIGIGA keltiramiz va balansni AYNAN shu qiymatlarda tekshiramiz").
* **Period boundary.** A payroll period is a calendar month, `[first day 00:00, last day 23:59:59]`
  in Tashkent wall-clock time. `period_type = 'full'` is the only value we implement in HR-1; the
  half-month values exist in the enum because EduSchool has them and adding an enum value later is a
  migration, adding a column is not.
* **Timezone.** `timestamptz` for anything with a clock (`posted_at`, `created_at`), `date`/`time` for
  accounting and schedules. Do **not** use `DateTime` on the new HR entities — `OnModelCreating`
  rewrites those to `timestamp without time zone` and the posting instant becomes ambiguous.
* **Soft delete.** Status columns, never boolean pairs (SPEC §3). `hr_employees.status`,
  `work_schedules.status`, `is_active` on catalogue tables.

---

## 5. How this meets the money we already have

### 5.1 The mistake to avoid

P1-21 made salary flow through `expenses` with `category = 'salary'` and the new
`expenses.teacher_id` column, and `ExpenseService.PostAsync` posts
`debit expense:salary / credit cash|bank`. `SalaryPaymentQuery` reads that back, and only counts rows
that reached the ledger and were not reversed.

If a payroll run *also* posted `debit expense:salary`, every salary would be counted twice in the P&L.
That is the whole integration risk in one sentence.

### 5.2 The design

Accrual and settlement are two different events and get two different ledger batches.

**Three new account codes** in `Accounts.cs` (the file's own comment says adding one is "one line, and
that is deliberately slightly inconvenient" — three lines here, with a reason each):

| Code | Kind | Why |
|---|---|---|
| `liability:salary` | liability | net owed to employees between accrual and payout. Without it "how much do we owe staff right now" has no answer. |
| `liability:tax` | liability | NDFL + INPS withheld from employees, plus employer ESP — money we hold that belongs to the state. |
| `expense:payroll_tax` | expense | employer ESP as a cost. It is not part of `expense:salary` because it never reaches an employee, and lumping them makes "average cost per teacher" wrong. |

**Batch 1 — posting a payroll document** (`ref_type = 'salary'`, `ref_id = payroll_documents.id`,
one call to `LedgerService.PostAsync`, entry date = last day of the period):

```
debit  expense:salary        Σ gross
credit liability:salary      Σ net
credit liability:tax         Σ (ndfl + inps)

debit  expense:payroll_tax   Σ esp
credit liability:tax         Σ esp
```

Balanced because `gross = net + ndfl + inps` (bonuses and penalties are already inside `gross` — see
§11 Q2 for the exact placement, which does not change the balance either way as long as `gross` is
defined as "everything charged to `expense:salary`").

**Batch 2 — paying an employee** (`ref_type = 'salary'`, `ref_id = payroll_payments.id`):

```
debit  liability:salary   amount
credit cash | bank        amount     -- Accounts.SettlementFor(method), unchanged
```

**Batch 3 — remitting withheld tax** — out of scope for HR-1; recorded as an ordinary expense with a
new category later. Until then `liability:tax` grows and that is correct: we do owe it.

**Reversal** — `LedgerService.ReverseAsync(entryId, reason, approverId)` already finds the whole batch
by `(ref_type, ref_id)`, mirrors every line, refuses a double reversal and refuses a self-reversal.
We reuse it verbatim for both batches. Nothing new is needed.

### 5.3 What happens to `expenses.category = 'salary'`

It **stays and keeps working**. It is now the path for salary money that is *not* attached to a payroll
line: advances, one-off payouts to someone not yet on payroll, corrections of historic data. Nothing
in `ExpenseService`, `SalaryPaymentQuery` or `ExpensesController` changes.

But there is a real consequence, and it is a task, not a footnote:

> **HR-16 — `SalaryLedger` must learn about payroll payments.**
> `SchoolLms.Application/Services/SalaryLedger.cs` computes "paid" purely from
> `new SalaryPaymentQuery(db).ForTeacherAsync(...)`, i.e. from `expenses`. Once payroll payments exist,
> a teacher paid through payroll would show as unpaid in the teacher portal and in
> `SalaryRatesController.Detail`. The fix is additive: a new `PayrollPaymentQuery` with the same
> `SalaryPaymentRow` shape, and `SalaryLedger` unions the two sources. Do not rewrite
> `SalaryPaymentQuery`.

Similarly, `SalaryRatesController` and `TeacherSalaryCalc` (schedule × category hourly rate × 4 weeks,
minus absent days, plus `BonusPct`) keep working untouched. They are the *old* teacher-only estimate.
The new payroll is the authoritative accrual. Both may run side by side for one term; after that the
client decides which screen survives. Deleting either one now would break a shipped teacher portal.

### 5.4 Who is allowed to do what

Extend `FinanceAction` in `SchoolLms.Server/Controllers/FinanceRoleAttribute.cs` — the file's own rule
is that the permission matrix lives there and nowhere else.

| Action | Cashier | Staff | Admin | Superadmin |
|---|---|---|---|---|
| View timesheet / payroll | ⛔ | ⛔ | ✅ | ✅ |
| Create / fill / edit a timesheet document | ⛔ | ⛔ | ✅ | ✅ |
| Post a timesheet document | ⛔ | ⛔ | ✅ | ✅ |
| Create / fill / edit a payroll document | ⛔ | ⛔ | ✅ | ✅ |
| **Post a payroll document** | ⛔ | ⛔ | ✅ *(poster ≠ creator)* | ✅ *(poster ≠ creator)* |
| **Reverse a posted payroll** | ⛔ | ⛔ | ⛔ | ✅ *(reason required, reverser ≠ poster)* |
| **Edit or delete a posted payroll** | ⛔ | ⛔ | ⛔ | ⛔ **impossible for anyone** |
| Pay a payroll line (cash) | ✅ *(from an open shift)* | ⛔ | ✅ | ✅ |
| Manage penalty/bonus rules | ⛔ | ⛔ | ✅ | ✅ |
| Approve / reject a staff request | ⛔ | ⛔ | ✅ | ✅ |
| Edit tax rates | ⛔ | ⛔ | ⛔ | ✅ |
| Record a missed lesson | ⛔ | ✅ *(with `teachers` perm)* | ✅ | ✅ |
| View the dismissal report | ⛔ | ⛔ | ✅ | ✅ |

`created_by`, `posted_by`, `paid_by`, `approved_by` are **always** taken from JWT claims and
**rejected if present in the request body** — SPEC §4.4, and `ExpensesController` already shows the
exact implementation.

Cash payouts must come out of an **open cash shift**, otherwise `expected_cash` at shift close will
not reconcile. `payroll_payments.cash_shift_id` and `ck_payroll_payment_shift` (§4.2) enforce it in the
database; the service additionally refuses a shift whose `status <> 'open'` with error 61704.

---

## 6. API surface

All admin routes are under `/api/admin/hr/...`, `[Authorize(Roles = Roles.FinanceStaff)]` at the class
level plus `[FinanceRole(...)]` per method, `[BillingFault]` for the
`{ "code": ..., "message": ... }` error envelope — copy `ExpensesController`.

Pagination and filtering follow the existing admin convention: `?page=1&limit=20` plus named filters;
the response is `{ total, data[] }`.

### 6.1 Catalogue

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/departments` | `page,limit,search` | |
| POST/PUT/DELETE | `/departments[/{id}]` | `{ name, isActive }` | delete refused when referenced |
| GET | `/job-titles` | `page,limit,departmentId` | |
| POST/PUT/DELETE | `/job-titles[/{id}]` | `{ name, departmentId, isActive }` | |
| GET | `/dismissal-reasons` | | |
| POST/PUT/DELETE | `/dismissal-reasons[/{id}]` | `{ name, isActive }` | |

### 6.2 Employees and schedules

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/employees` | `page,limit,search,departmentId,jobTitleId,status,withoutWorkSchedule` | `withoutWorkSchedule=true` powers the payroll blocking alert |
| GET | `/employees/{id}` | | includes current-month timesheet summary |
| PUT | `/employees/{id}/pay` | `{ payKind, monthlySalary, cardSalary, cashSalary, singleLessonCost, workScheduleId }` | 400 when `cardSalary + cashSalary > monthlySalary` and `payKind <> 'hourly'` |
| PUT | `/employees/{id}/dismiss` | `{ dismissedOn, dismissalReasonId, comment }` | sets `status='dismissed'` |
| GET/POST/PUT/DELETE | `/work-schedules[/{id}]` | `{ name, status, days[] }` | `days[] = { weekday, startsAt, endsAt, breakMin }`, replaces the whole set |
| GET/POST/PUT/DELETE | `/employees/{id}/free-time[/{rowId}]` | `{ weekday, startsAt, endsAt }` | |

### 6.3 Timesheet

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/timesheet/documents` | `page,limit,year,month,departmentId,state` | |
| POST | `/timesheet/documents` | `{ docDate, year, month, periodType, departmentIds[], employeeIds[], includeFixed, includeHourly, note }` | 409 + `data.conflictingEmployeeIds[]` on overlap |
| PUT | `/timesheet/documents/{id}` | same shape, header only | draft only |
| POST | `/timesheet/documents/{id}/fill` | | rebuilds days + lessons from schedule, turnstile and journal |
| POST | `/timesheet/documents/{id}/post` | | 409 when `filled_at is null` |
| POST | `/timesheet/documents/{id}/reverse` | `{ reason }` | superadmin |
| DELETE | `/timesheet/documents/{id}` | | draft only, 409 when a payroll document sources it |
| GET | `/timesheet/documents/{id}/summary` | | `{ employeeCount, plannedMinutes, workedMinutes }` + rows |
| GET | `/timesheet/documents/{id}/days` | `employeeId` | |
| PUT | `/timesheet/documents/{id}/days/{dayId}` | `{ actualIn, actualOut, dayType, reason }` | draft only; writes a correction row |
| GET | `/timesheet/documents/{id}/lessons` | `employeeId` | `{ total, data[], employeeId, singleLessonCost, summary }` |
| PUT | `/timesheet/documents/{id}/lessons/payable` | `{ items: [{ lessonId, employeeId, payable }] }` | **max 200 items per call** (EduSchool allows 500; we cap lower because our rows carry more columns). Returns `{ changed }`. Writes one correction row per item. |
| GET | `/timesheet/corrections` | `page,limit,documentId,employeeId,year,month` | |
| POST | `/timesheet/corrections/{id}/revert` | | appends the opposite correction |
| GET | `/timesheet/export` | `year,month,departmentId` | xlsx |

### 6.4 Hourly payroll (read-only view over the timesheet)

| Method | Path | Query | Notes |
|---|---|---|---|
| GET | `/hourly-payroll` | `year,month,employeeId,page,limit` | rows `{ employeeId, employee, planCount, faceIdCount, markedCount, approvedCount, lateMinutes, singleLessonCost, amount, rows[] }` |
| GET | `/hourly-payroll/export` | `year,month,employeeId` | xlsx |

### 6.5 Rules

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/rules` | `page,limit,kind,scope,isActive` | |
| POST/PUT | `/rules[/{id}]` | the §2.6 shape | all §2.6 validations, 400 with the matching error key |
| PATCH | `/rules/{id}/active` | `{ isActive }` | list toggle |
| DELETE | `/rules/{id}` | | soft: `is_active=false` when already used by a posted document, hard otherwise |
| GET | `/applied-rules` | `page,limit,year,month,fromDay,toDay,employeeId,kind` | |

### 6.6 Payroll

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/payroll/documents` | `page,limit,year,month,departmentId,state` | |
| POST | `/payroll/documents` | `{ docDate, year, month, name, note, currency, mergeRows, departmentIds[], sourceTimesheetDocumentIds[] }` | 409/`61511` unknown source, 409/`61512` departments not covered |
| POST | `/payroll/documents/preview` | same body | computes and returns, saves nothing |
| PUT | `/payroll/documents/{id}` | header fields | draft only |
| POST | `/payroll/documents/{id}/fill` | | materialises `payroll_lines` + `hr_applied_rules`, snapshots rates |
| POST | `/payroll/documents/{id}/post` | | poster ≠ creator; posts ledger batch 1 |
| POST | `/payroll/documents/{id}/reverse` | `{ reason }` | superadmin, reverser ≠ poster |
| DELETE | `/payroll/documents/{id}` | | draft only |
| GET | `/payroll/documents/{id}` | | header + totals + rates |
| GET | `/payroll/documents/{id}/lines` | `page,limit,departmentId` | |
| GET | `/payroll/documents/{id}/lines/{lineId}` | | the drawer payload, incl. `formula[]` |
| GET | `/payroll/lines` | `page,limit,year,month,employeeId` | cross-document line list |
| POST | `/payroll/lines/{lineId}/pay` | `{ amount, method, onDate, note }` | document must be `posted`; cash requires an open shift; posts ledger batch 2 |
| POST | `/payroll/payments/{id}/reverse` | `{ reason }` | superadmin; appends a mirror row + reverses the batch |
| GET | `/payroll/documents/{id}/export` | | xlsx |

### 6.7 Requests, missed lessons, loans, settings, reports

| Method | Path | Body / query | Notes |
|---|---|---|---|
| GET | `/requests` | `page,limit,status,kind,departmentId,employeeId,dateFrom,dateTo` | |
| PUT | `/requests/{id}/approve` | | 409/`61403` when the period is posted |
| PUT | `/requests/{id}/reject` | `{ rejectReason }` | reason mandatory, 400 when blank |
| GET | `/missed-lessons` | `page,limit,employeeId,classId,subjectId,dateFrom,dateTo` | |
| GET | `/missed-lessons/options` | `employeeId,classId,subjectId,lessonDate` | cascading, returns `{ employees, classes, subjects, lessonDates }` |
| GET | `/missed-lessons/analytics` | `dateFrom,dateTo,...` | §2.9 shape |
| POST/DELETE | `/missed-lessons[/{id}]` | `{ employeeId, classId, subjectId, hours, lessonDate, note }` | 409/`60001` duplicate, 409/`60006` no scheduled lesson |
| GET/POST/PUT/DELETE | `/loans[/{id}]` | | HR-3 |
| GET | `/settings` | | tax rates + flags |
| PUT | `/settings` | `{ ndflPercent, inpsPercent, espPercent, mergeRows, noCashIfLowHours, lowHoursThresholdPct }` | superadmin only; audited |
| GET | `/reports/dismissals` | `fromDate,toDate` | grouped by reason with `percent` |
| GET | `/reports/dismissals/{reasonId}` | `fromDate,toDate` | employees behind one reason |
| GET | `/reports/dismissals/export` | | xlsx |

**Error code registry for this module** (reuse EduSchool's numbers so the two systems' logs are
comparable, and because they are already free in ours):

| Code | Meaning |
|---|---|
| 60001 | missed lesson already recorded for that employee/class/subject/date |
| 60006 | no scheduled lesson on that date |
| 61403 | period is closed — the request cannot be reviewed |
| 61511 | one or more source timesheet documents no longer exist |
| 61512 | the selected timesheets do not cover every chosen department |
| 61605 | no posted timesheet document for the period |
| 61701 | *(new)* payroll document is not in `draft` |
| 61702 | *(new)* poster equals creator |
| 61703 | *(new)* employee has no work schedule |
| 61704 | *(new)* cash payout attempted with no open cash shift |

---

## 7. UI

Routes live under `/admin/hr/*`. Nav group "HR" with a `Users` icon, gated on a new
`adminPermissions` key `hr`. Uzbek labels, per CLAUDE.md.

> Our visual language stays ours. We rebuild EduSchool's *behaviour*, not its MUI look.
> Reuse the existing Tailwind primitives from `schoollms.client/src/pages/admin/billing/BillingUi.tsx`.

| Route | Title (uz) | Contents | Hidden when |
|---|---|---|---|
| `/admin/hr/tabel` | Tabel | Document list + a month view toggle. Month view: KPI strip (xodimlar / reja soat / fakt soat), grid with department, lavozim, ish jadvali, reja kun/soat, fakt kun/soat, jadval tashqarisi. Row click → employee day drawer. | no `hr` perm |
| `/admin/hr/tabel/:id` | Tabel №… | Header (number, period, state chip, actions), tabs: **Umumiy · Xodimlar · Darslar · Tuzatishlar**. Lessons tab has the payable checkbox column and the batch save. | — |
| `/admin/hr/oylik` | Oylik hisobi | Payroll document list + "Qatorlar" tab. Create button. | no `hr` perm |
| `/admin/hr/oylik/:id` | Oylik hisobi №… | KPI (hisoblangan / ushlab qolingan / NDFL / INPS / ESP), tabs **Hisoblangan · Ushlanmalar · Jarima-bonus**, per-line drawer with the formula. Post / Reverse buttons. | Post hidden without `editPayroll`; Reverse hidden for non-superadmin |
| `/admin/hr/soatbay` | Soatbay oylik | Month + teacher filter, KPI (tasdiqlangan soat / to'lanmagan soat / hisoblangan summa / almashtirilgan), table (reja, FaceID, jurnal, tasdiqlangan, kechikish, bir dars narxi, summa), row drawer with every lesson. | no `hr` perm |
| `/admin/hr/jarima-bonus` | Jarima va bonuslar | Tabs **Qoidalar · Qo'llanganlar**. Rule wizard: 1 Sabab · 2 Qamrov · 3 Summa · 4 Qaysi oydan. Standing info: qoidalar qo'shiladi. | Add/edit hidden without `editPayroll` |
| `/admin/hr/arizalar` | Xodim so'rovlari | Card grid, tabs **Kutilmoqda · Tasdiqlangan · Rad etilgan**, pending badge count. Approve/Reject modals. | Buttons hidden without `editPayroll` |
| `/admin/hr/xodimlar` | Xodimlar (HR) | Employee list with department, job title, pay kind, salary split, work schedule. Pay editor. Dismiss action. | — |
| `/admin/hr/ish-jadvali` | Ish jadvali | Schedule list + weekly editor (7 rows: weekday, start, end, break). | — |
| `/admin/hr/bosh-vaqt` | Xodimlar bo'sh vaqti | Per employee, 7 rows of availability windows. | — |
| `/admin/hr/dars-qoldirish` | O'qituvchi dars qoldirish | Tabs **Yozuvlar · Tahlil**. Cascading add form. | Add hidden without `teachers` perm |
| `/admin/hr/sozlamalar` | Soliq stavkalari | NDFL / INPS / ESP inputs + two switches. | non-superadmin sees it read-only |
| `/admin/analytics/ishdan-boshatish` | Ishdan bo'shatishlar hisoboti | Reason table with percent, drill-down, date range, export. | no `hr` perm |

**Permission-driven hiding, restated as a rule:** the nav entry and the route guard use the same key
as the endpoint. When a button is hidden, the endpoint must still return 403 — the UI is a
convenience, never the control. This is the mistake `navigation.ts` already documents for the billing
catalog; do not repeat it in reverse.

Teacher-facing: `schoollms.client/src/pages/teacher/salary/SalaryPage.tsx` gains a second card,
"Tabel", showing the current month's planned/worked hours and the payable-lesson list, read-only.
Guarded by the existing `TeacherPermissions.Salary` key.

---

## 8. What we already have

| Need | Already in the repo | Gap |
|---|---|---|
| Double-entry ledger, single write path | `SchoolLms.Application/Billing/LedgerService.cs`, `ILedgerService` | 3 account codes |
| Reversal with two-man rule | `LedgerService.ReverseAsync` | none — reuse verbatim |
| Chart of accounts, closed list | `SchoolLms.Application/Billing/Accounts.cs` | 3 codes + 1 dictionary entry each |
| Expense with approval threshold + derived status | `SchoolLms.Application/Billing/ExpenseService.cs` | none — this is the reference implementation |
| Salary paid-to-date per teacher | `SchoolLms.Application/Billing/SalaryPaymentQuery.cs` | must be unioned with payroll payments (HR-16) |
| Expected salary per teacher per month | `SchoolLms.Application/Services/SalaryLedger.cs`, `TeacherSalaryCalc.cs` | superseded by payroll, kept running |
| Hourly rate by teacher category | `SchoolLms.Domain/Entities.cs` `SchoolMeta.SalaryRate{Oliy,1,2,Mutaxasis}`, `SalaryRatesController` | becomes the default for `hr_employees.single_lesson_cost` |
| Teacher bonus percentage | `Teacher.BonusPct`, `PUT /api/admin/salary-rates/{id}/bonus` | overlaps `hr_rules` bonuses — see §11 Q7 |
| Scheduled lessons (plan) | `ScheduleTemplate` / `ScheduleLesson` (Day 0–5, Period 1–10, TeacherId, SubGroup), `WeekAssignment` | the plan source for the timesheet |
| Lesson actually conducted / journal marked | `LessonNote.Conducted`, `JournalEntry` | this is `isMarked` |
| Plan-vs-fact machinery, per teacher/class/subject | `SchoolLms.Application/Services/TeacherActivityReport.cs` | reuse the aggregation, do not rewrite it |
| Turnstile / FaceID events | `TurnstileEvent { TeacherId, EventAt, Direction }`, `TurnstileService`, `TurnstileLiveService` | this is `hasFaceId` |
| Daily staff presence | `TeacherAttendance { Date, Status present\|absent\|late, CheckIn, CheckOut, Source }` | seeds `hr_timesheet_days` |
| Work start time + late grace | `SchoolMeta.WorkStartTime` (`"08:30"`), `SchoolMeta.LateGraceMinutes` (10) | superseded per-employee by `work_schedules`; keep as the fallback |
| Quarters and holidays | `QuarterPeriod`, `Holiday` | `day_type = 'holiday'` |
| Audit with before/after jsonb | `SchoolLms.Application/Services/AuditService.cs`, `audit_log` | add `EntityPayrollDocument` constants |
| DB guard pattern | `Migrations/Sql/billing_guards.sql`, `anomaly_guards.sql` | copy for `payroll_guards.sql` |
| RBAC attribute + action matrix | `AdminPermAttribute.cs`, `FinanceRoleAttribute.cs`, `Roles.cs` | new `FinanceAction` members |
| Frontend money UI kit | `pages/admin/billing/BillingUi.tsx`, `ExpensesPage.tsx`, `ExpenseFormModal.tsx` | copy |
| Frontend salary screens | `pages/admin/schedule/SalaryCalcPage.tsx`, `pages/admin/finance/TeacherSalaryDetailModal.tsx`, `pages/teacher/salary/SalaryPage.tsx` | keep; new HR pages sit alongside |
| API client convention | `src/api/services/expenses.ts`, `salaryRates.ts` | new `hr.ts`, `payroll.ts`, `timesheet.ts` |

**Genuinely missing:** departments, job titles, non-teacher payroll identity, work schedules,
timesheet documents, payroll documents and lines, penalty/bonus rules, staff requests, missed-lesson
records, loans, HR settings, dismissal reasons — i.e. everything in §4.2.

---

## 9. Build plan

Estimates are for one backend developer or one frontend developer, in hours, and assume the reference
implementation is copied rather than re-derived.

Legend: `[P]` parallelizable · `[S]` sequential, one owner · `★` critical path.

### Phase HR-1 — the payroll spine (must ship together, ~118 h)

| Id | Task | Files | Depends on | Par? | Est |
|---|---|---|---|---|---|
| HR-01 ★ | **Entities + DbContext.** `SchoolLms.Domain/Hr.cs` (all 22 entities, one new file — `Entities.cs` and `Billing.cs` are not touched, same rule as P1-04), `AppDbContext` DbSets, `HrModel.cs` fluent config. | new + `AppDbContext.cs` | — | `[S]` | 12 |
| HR-02 ★ | **Shared-file edits, one owner, one commit.** `Accounts.cs` +3 codes; `FinanceRoleAttribute.cs` +6 `FinanceAction` members and matrix rows; `Roles.cs` untouched. | shared | HR-01 | `[S]` | 3 |
| HR-03 ★ | **Migration `HrCore` + `payroll_guards.sql` + seed.** One migration, no `DROP` in `Up()`. Seed: `hr_settings` singleton (12 / 0.1 / 12), a default `work_schedules` row, `dismissal_reasons` starter list, and `hr_employees` back-filled one row per non-archived `teachers` row and per `app_users` with role `staff`/`admin`. | `SchoolLms.Infrastructure/Migrations/**` | HR-01, HR-02 | `[S]` | 10 |
| HR-04 ★ | **Frozen contracts.** DTOs in `SchoolLms.Application/Dtos/HrDtos.cs`, service interfaces, TS types in `schoollms.client/src/types/hr.ts`, API client stubs. Everything downstream compiles against these. | new | HR-03 | `[S]` | 6 |
| HR-05 | Catalogue services + controllers: departments, job titles, dismissal reasons, work schedules, free time, HR settings. | new | HR-04 | `[P]` | 10 |
| HR-06 | `HrEmployeeService` — list, pay editor with the split rule, dismiss, `withoutWorkSchedule` filter, back-fill reconciliation with `teachers`. | new | HR-04 | `[P]` | 10 |
| HR-07 ★ | `TimesheetService` — create / fill / post / reverse / delete, overlap detection, day + lesson generation from `ScheduleLesson` + `WeekAssignment` + `TurnstileEvent` + `LessonNote`, the §2.2 payability rule, corrections. | new | HR-04 | `[P]` | 24 |
| HR-08 | `HrRuleService` — CRUD + validation + the rule engine that produces `hr_applied_rules` for a period. | new | HR-04 | `[P]` | 12 |
| HR-09 ★ | `PayrollService` — preview / create / fill / post / reverse, rate snapshot, the arithmetic of §2.4, **the two ledger batches of §5.2**, `PayrollPaymentService`. | new | HR-04, HR-07, HR-08 | `[S]` | 20 |
| HR-10 | `HrRequestService` — list, approve, reject, the 61403 period guard. | new | HR-04 | `[P]` | 6 |
| HR-11 ★ | `Program.cs` DI wiring. | shared | HR-05…HR-10 | `[S]` | 2 |
| HR-12 | Frontend: Tabel (list, month view, document detail with four tabs, payable batch save). | new page tree | HR-04 | `[P]` | 22 |
| HR-13 | Frontend: Oylik hisobi (list, detail, KPI, drawer, post/reverse) + Soatbay oylik. | new page tree | HR-04 | `[P]` | 20 |
| HR-14 | Frontend: Jarima va bonuslar (rule wizard + applied) + Xodim so'rovlari. | new page tree | HR-04 | `[P]` | 14 |
| HR-15 ★ | Frontend wiring: `App.tsx` routes, `navigation.ts` HR group, `constants.ts` `hr` permission key. | shared | HR-12…HR-14 | `[S]` | 3 |
| HR-16 ★ | **`SalaryLedger` union.** New `PayrollPaymentQuery`, `SalaryLedger` reads both sources. Additive; `SalaryPaymentQuery` untouched. | `Services/SalaryLedger.cs` + new | HR-09 | `[S]` | 4 |
| HR-17 | Tests: payroll arithmetic (rounding, net identity, rate snapshot), payability truth table, rule engine. | `SchoolLms.Tests/**` | HR-09 | `[P]` | 12 |
| HR-18 ★ | Tests: ledger integration (batch balances, no double-count against `expenses`, reversal), immutability (`app_rw` gets 42501 / trigger fires on a posted document), RBAC matrix, server-derived identity. | `SchoolLms.Tests/Security/**` | HR-09, HR-11 | `[P]` | 12 |
| HR-19 ★ | Migration test: `upgrade head` on a clean DB, then on a DB with existing billing data; guards re-applied after the migration. | `SchoolLms.Tests/**` | HR-03 | `[P]` | 4 |

Ordering constraint that is easy to get wrong: **HR-01 → HR-02 → HR-03 → HR-04 is strictly
sequential and owned by one agent.** Those four touch `AppDbContext`, `Accounts.cs`,
`FinanceRoleAttribute.cs`, `Program.cs` and the EF model snapshot. Two agents in there at once
produce a `AppDbContextModelSnapshot.cs` conflict and a lost afternoon.

Shared files — never parallel, listed once so nobody has to hunt:

```
SchoolLms.Infrastructure/Data/AppDbContext.cs
SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
SchoolLms.Application/Billing/Accounts.cs
SchoolLms.Server/Controllers/FinanceRoleAttribute.cs
SchoolLms.Server/Program.cs
schoollms.client/src/App.tsx
schoollms.client/src/config/navigation.ts
schoollms.client/src/config/constants.ts
```

### Phase HR-2 — the rest of the menu (~46 h)

| Id | Task | Depends on | Par? | Est |
|---|---|---|---|---|
| HR-20 | Missed lessons: service, cascading options, analytics, page with two tabs. | HR-04 | `[P]` | 14 |
| HR-21 | Dismissal report: query, drill-down, xlsx export, page. | HR-06 | `[P]` | 8 |
| HR-22 | Xodimlar bo'sh vaqti page + `hr_employee_free_time` CRUD. | HR-05 | `[P]` | 6 |
| HR-23 | xlsx exports for timesheet, payroll and hourly payroll (reuse `Services/ExcelExport.cs`). | HR-09 | `[P]` | 8 |
| HR-24 | Teacher portal: "Tabel" card on `SalaryPage.tsx`. | HR-07 | `[P]` | 6 |
| HR-25 | Payslip PDF per line + Telegram delivery (reuse `ReceiptDocument.cs` / `ReceiptService.cs`). | HR-09 | `[P]` | 4 |

### Phase HR-3 — deferred (~24 h, only on request)

| Id | Task | Est |
|---|---|---|
| HR-26 | Loans: CRUD, employee card tab, payroll columns. Informational only — no deduction. | 10 |
| HR-27 | Tax remittance: expense category `payroll_tax`, `liability:tax` drawdown, monthly reminder. | 8 |
| HR-28 | Half-month periods (`period_type` first/second half). | 6 |

**Total: HR-1 118 h · HR-2 46 h · HR-3 24 h.** With two backend and two frontend agents the HR-1
critical path (HR-01→02→03→04→07→09→11→16→18) is roughly **91 h ≈ 12 working days**.

---

## 10. Definition of done (HR-1)

- [ ] `pytest`-equivalent: `dotnet test` green, including HR-17, HR-18, HR-19.
- [ ] `npm run build` green.
- [ ] `dotnet ef database update` from an empty database and from a database that already has the
      billing schema; `payroll_guards.sql` re-applied afterwards.
- [ ] Every new endpoint carries `[Authorize]` + `[FinanceRole(...)]`, and HR-18 proves the matrix.
- [ ] A posted payroll document cannot be edited or deleted by anyone — proven by a test that gets
      SQLSTATE `check_violation` from the trigger, not just a 409 from the service.
- [ ] `app_rw` gets SQLSTATE 42501 on `UPDATE payroll_payments`; `schoollms_owner` succeeds.
- [ ] Posting a payroll document leaves the trial balance balanced
      (`LedgerService.TrialBalanceAsync` sums to zero), and paying every line leaves
      `liability:salary` at exactly zero.
- [ ] A month with both a payroll payment and an ad-hoc `expenses.category='salary'` row shows each
      amount **once** in `expense:salary` — the double-count regression test.
- [ ] `SalaryLedger` shows a teacher paid through payroll as paid.
- [ ] Existing functionality still works: `SalaryRatesController`, `ExpensesController`,
      the teacher portal salary page, the money-flow view.
- [ ] `docs/ASSUMPTIONS.md` has one line per §11 item that shipped on the recommendation.

---

## 11. Open questions

Each carries the decision that stands if nobody objects.

**Q1 — How does a fixed-salary employee's `gross` follow from worked hours?**
The bundle never shows it; the server computes it and ships pre-rendered formula strings.
*Recommendation:* `gross = monthly_salary × (worked_minutes ÷ planned_minutes)`, capped at
`monthly_salary`, where `planned_minutes` counts only work days in the period that are not holidays.
Days covered by an approved `missed-day` request count as worked. Rationale: it is the only reading
that makes `plannedMinutes` / `workedMinutes` the headline timesheet numbers, and it degrades
correctly (full attendance → full salary). **Ask the client — this is the single number that decides
what a teacher is paid.**

**Q2 — Is `bonus` inside `gross`, and is `penalty` a reduction of `gross` or of `net`?**
Proven: `withheld = ndfl + inps + penalty`, so penalty is grouped with the withholdings, not with the
accrual. Not proven: whether `gross = cardPaid + cashPaid` excludes the bonus.
*Recommendation:* `gross = cardPaid + cashPaid + bonus_amount − penalty_amount`;
`tax_base = card_paid` (§Q6); `ndfl = round(tax_base × ndfl%)`;
`inps = round(tax_base × inps%)`; `esp = round(tax_base × esp%)`, employer-side;
`net = gross − ndfl − inps`. This keeps the ledger identity `gross = net + ndfl + inps` exact, which
batch 1 of §5.2 depends on, and keeps bonuses and penalties in the accrual where an auditor expects
them. Note this differs from EduSchool's drawer ordering, which shows penalty as a deduction from
net — arithmetically the same total, different presentation. If the client wants EduSchool's
presentation, only the drawer changes, not the ledger.

**Q3 — What exactly does `payrollNoCashIfLowHours` do?**
*Recommendation:* when `worked_minutes < planned_minutes × low_hours_threshold_pct / 100`, set
`cash_paid = 0` and pay only `card_paid` (pro-rated per Q1). Default the flag **off** and the
threshold to 100 %. Off by default because silently zeroing part of somebody's salary is the kind of
rule that must be switched on deliberately, by a named person, on a dated setting.

**Q4 — Is `allowed_late_minutes` a grace window?**
*Recommendation:* yes. A `late` penalty fires once per day on which
`late_minutes > allowed_late_minutes`. A `no-late` bonus fires once per period if no day in the period
exceeded it. `early` mirrors `late` using `early_leave_minutes`. `absence` fires once per scheduled
work day with `worked_minutes = 0` and no approved request. `full-hours` fires once per period when
`worked_minutes >= planned_minutes`. `substitute` fires once per payable lesson with a non-null
`substituted_for_employee_id`.

**Q5 — What is the `base` of a percent rule?**
*Recommendation:* the employee's `monthly_salary` for period-scoped reasons (`no-late`,
`full-hours`), and the **day rate** — `monthly_salary ÷ planned work days in the period` — for
day-scoped reasons (`late`, `early`, `absence`), multiplied by `occurrences`. For `substitute`, the
base is `single_lesson_cost`. Store `base` and `rate` on every `hr_applied_rules` row so the number
can always be re-derived.

**Q6 — Is `tax_base` the card part only?**
*Recommendation:* yes — `tax_base = card_paid`. That is the only reading that explains why the design
separates card from cash at all. This is also the only item in this spec with a legal dimension:
**flag it to the client explicitly** and let them tell us whether they want the cash concept at all.
If they do not, drop `cash_salary` / `cash_paid`, set `tax_base = gross`, and the module gets simpler.

**Q7 — `Teacher.BonusPct` versus `hr_rules` bonuses.**
Two bonus mechanisms would drift.
*Recommendation:* keep `BonusPct` for the legacy `SalaryRatesController` estimate only; the payroll
document ignores it and uses `hr_rules`. Add a one-line note to the "Oylik hisoblash" page saying so.
Revisit when the client picks which screen survives.

**Q8 — Does the school actually need departments and job titles?**
One school, ~50 staff.
*Recommendation:* build them — every HR screen in EduSchool filters and scopes by them, and a
penalty rule with `scope = 'department'` is meaningless without them. Seed one department
("Umumiy") and let the client split it later. Cost is ~2 h; retrofitting a scope column onto a live
rules table is not.

**Q9 — Half-month payroll periods.**
EduSchool has `period_type` but every observed default is `full`.
*Recommendation:* keep the enum value, implement `full` only (HR-28 if asked).

**Q10 — Multi-branch.**
EduSchool's employee money lives on `branchEmployees[]`. SPEC §1 says single school.
*Recommendation:* flatten. `hr_employees` holds one salary set. If branches return, add
`branch_id` to `hr_employees` and make the unique keys composite — a smaller change than modelling
a many-to-many now for a school that has one branch.

**Q11 — Where do payroll payments come from, the till or the bank?**
*Recommendation:* both, via the existing `method` enum, and `Accounts.SettlementFor(method)` decides
the credit account exactly as it does for payments and expenses. Cash payouts require an open cash
shift so the Z-report reconciles; card/transfer do not.

**Q12 — Timesheet day `flag` values.**
The bundle proves `off` and `late` and shows a colour map, but the full enum is not visible. The
turnstile daily report uses `present | on_time | worked | absent | late | rest | rest_with_marks |
no_schedule | future`.
*Recommendation:* use the nine values already in §4.2 (`ok, late, early, absent, off, no_schedule,
future` plus `holiday`/`leave`/`sick` on `day_type`). Adding a value later is a check-constraint
migration; getting it wrong now is a data migration.
