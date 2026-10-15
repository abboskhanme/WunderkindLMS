# Teacher certificates, allowances, bonuses and fines in the salary

Status: built 2026-10-15 (client's "CRM o'zgarish (Maktab)" list, items 4 and 8).

## Why

- Item 4: *"Jarima va o'rindosh ustozning oylik summasi biryola moliyaga bog'langan bo'lishi — avtomatik oylikdagi
  hisob-kitob."* Moliya → Bonus / Jarima were recorded and sent to the teacher on Telegram, but no salary screen
  read them (`MARKAZ-GAPS.md` #16). Substitute pay was already automatic (the substitute gets +1 lesson at their own
  rate, the absent teacher −1 — `LessonSubstitutionService.MonthDeltaAsync`); it now shows next to the rest.
- Item 8: *"Xodimlar bo'limidagi o'qituvchilar ma'lumotlarini ichiga sertifikat va ustama qismlarini qo'shish va
  buni to'g'ridan to'g'ri moliyaga bog'lash (oylik hisob-kitobi)."* There was no place for a teacher's own
  certificate, and the only "ustama" was one school-wide percent per teacher edited in bulk.

## One calculation

`TeacherPayContext` (`SchoolLms.Application/Services/TeacherPay.cs`) loads everything once and `For(teacher, month)`
returns a `TeacherPayBreakdown`. Every teacher salary number goes through it: Moliya → Ish haqi table
(`GET /api/admin/salary-rates`), the salary detail (`GET /api/admin/salary-rates/{id}`), the salary ledger
(`SalaryLedger.BuildAsync`, Moliya → Xodimlar maoshi) and the salary report (`GET /api/admin/finance/salary-report`).
The Ish haqi page's live recompute uses the same formula (`teacherMonthPay` in `salaryRates.ts`).

```
lesson pay  = max(0, planned − missed + substitution delta) × category rate      (unchanged)
ustama      = lesson pay × Teacher.BonusPct / 100                                (unchanged)
allowances  = lesson pay × Σ percent allowances / 100 + Σ fixed allowances       (new; fixed only if planned > 0)
total       = max(0, lesson pay + ustama + allowances + bonus − fine)            (new)
```

`lesson pay + ustama` is exactly the old `TeacherSalaryCalc.MonthlyForMonth`, which stays as it was.

Non-teacher staff: `max(0, fixed salary for the month + bonus − fine)` (`PayrollAdjustmentTotals.Apply`), in the
staff ledger and the salary report.

## Bonuses and fines

`PayrollAdjustmentTotals` sums `payroll_adjustments` per person and `PeriodYear/PeriodMonth`. A cancellation is a
mirror row pointing at the original; both are left out (the KPI's reading). The register still refuses past months
(no back-dating), so a fine lands in the current or a future month's salary.

## Certificates and allowances — `teacher_allowances`

| Column | |
|---|---|
| `source` | `certificate` (the teacher's own: IELTS, milliy sertifikat …) or `ustama` (sinf rahbarligi, to'garak …) |
| `title`, `level`, `number`, `issued_on`, `expires_on`, `file_url` | certificate details; the file is a `/uploads/` URL |
| `amount_kind`, `amount` | `percent` of the lesson pay (0–1000) or `fixed` so'm per month |
| `from_month`, `to_month` | paid from / until ("yyyy-MM"; `to_month` null = open) |

Paid in month M when `from_month ≤ M ≤ to_month` and M is not after the month of `expires_on`. A fixed sum is paid
only in a month with planned lessons (no pay in the summer, like the lessons). The old school-wide percent
(`Teacher.BonusPct`) stays; the "Ustama" tab edits it too.

## API

`api/admin/teachers/{teacherId}/allowances` — `[Authorize(FinanceStaff)]` + `AdminPerm("teachers")`:

- `GET` (finance reports level: admin, director, `finance`, `finance:view`) — items, the ustama percent and the current
  month's breakdown.
- `POST`, `PUT {id}`, `DELETE {id}` (`ManagePayrollAdjustments`: admin, director, `finance`) — validated, audited
  (`AuditService.EntityTeacherSalary`). Delete is for a mistaken line; an allowance that ends gets a `to_month`, an
  expired certificate stops by its date — both keep past months as they were.

`TeacherSalaryDetailDto` gained `AllowanceAmount`, `Allowances` (line, setting, value this month), `AdjustmentBonus`,
`Penalty`, `Adjustments`; `TeacherPayrollRowDto` gained `AllowancePct`, `AllowanceFixed`, `AdjustmentBonus`, `Penalty`;
`MonthSalaryDto` gained `LessonPay`, `Ustama`, `Allowances`, `Bonus`, `Penalty`, `SubstitutionLessons`. All additive.

## Screens

- Boshqaruv → Xodimlar → teacher card: tabs **Sertifikatlari** and **Ustama** (only with finance access; editing for
  admin / director / `finance`), each with the current month's pay broken down. The old "Sertifikatlar" tab (documents
  the teacher issued to pupils) is now **Bergan sertifikatlar**.
- Moliya → Ish haqi → teacher detail: a line per certificate / allowance, bonus and fine, and the list of the month's
  bonuses and fines.
- Moliya → Xodimlar maoshi → ledger: under each month's amount, what it is made of.
