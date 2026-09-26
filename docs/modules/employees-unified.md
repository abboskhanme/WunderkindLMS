# Unified employees list + salary for every employee — 2026-09-26

Client (Uzbek, 2026-09-26): teachers live under HR, other staff under Boshqaruv. Put them in ONE
"Xodimlar" list (Boshqaruv → Xodimlar), told apart only by a "Lavozim" column, so bonus, penalty
and salary are always searched in one place. Confirmed choices:

1. **One list, data stays where it is.** `teachers` and `users(role=staff)` remain separate tables
   (schedule, journal, attendance, certificates reference `teachers`). The list merges them.
2. **Every employee gets a salary**, not only teachers. Non-teaching staff: a fixed monthly salary
   from a start date; the salary report and salary payment work for them too.

Director-only approvals, append-only money tables and the ledger rules are unchanged.

---

## Backend contract

### Data (one additive migration `StaffSalary`)
- `users.phone` text not null default `''`
- `users.salary` numeric(14,2) not null default 0 — monthly salary of a **staff** account
- `users.salary_start_date` text not null default `''` — ISO `yyyy-MM-dd`; salary accrues from this
  day (first month prorated by calendar days, same rule teachers use via `TeacherSalaryCalc.StartDateOf`)
- `expenses.employee_user_id` text null + index — salary payment to a **staff** account
  (`users.id`). Exactly one of `teacher_id` / `employee_user_id` is set on a `salary` expense,
  both null for other categories. Keep `expenses` guards (`init-roles.sql`, append-only) intact.

### Staff API (`StaffController`, route `api/admin/staff`, gate unchanged `[AdminPerm("staff")]`)
- `StaffDto` appends: `string Phone = ""`, `decimal Salary = 0`, `string SalaryStartDate = ""`.
- `StaffPayload` appends: `string? Phone = null`, `decimal? Salary = null`, `string? SalaryStartDate = null`
  (null = unchanged on update; on create null = empty/0). Salary < 0 → 400. Date not ISO → 400.

### Staff salary (NEW `StaffSalaryController`, route `api/admin/staff/{id}`, gated exactly like the
teacher salary endpoints in `TeachersController`: `[Authorize(Roles = Roles.FinanceStaff)]` + same
`[FinanceRole(...)]` actions)
- `POST {id}/salary-payments` — same request body and semantics as `POST api/admin/teachers/{id}/salary-payments`
  (cash box / shift, director approval threshold, ledger lines), but the expense gets
  `employee_user_id = id`. 404 if the user is not `role=staff`.
- `GET {id}/salary-history` — same shape as the teacher one.
- `GET {id}/salary-ledger?from&to` — same shape as the teacher one; expected per month = `salary`
  (first month prorated from `salary_start_date`; months before it = 0; no lesson/attendance logic).

### Salary report (`GET api/admin/finance/salary-report`, `/export`)
- `SalaryReportRowDto` appends `string Kind = "teacher"` (`"teacher"` | `"staff"`) and
  `string Position = ""` (teachers: `"O'qituvchi"`; staff: `users.position`).
- Staff rows are added after teachers: `TeacherId` carries the **user id**, `TeacherName` the
  full name, `Salary` = monthly, `Expected` = sum over the period's months (prorated as above),
  `TotalPaid` / `PaymentsCount` from `salary` expenses with `employee_user_id`, journaled and not
  reversed — same rules as `SalaryPaymentQuery` for teachers.
- Export gets a "Lavozim" column; file name `xodimlar-maoshi.xlsx`.

### Tests (mandatory)
- Staff create/update with phone/salary/start date; validation 400s.
- Salary report contains a staff row with correct Expected (prorated first month) and TotalPaid
  after a staff salary payment; teacher rows unchanged.
- RBAC: staff salary endpoints — admin OK; staff with `finance` OK; `finance:view` → GET OK,
  POST 403; staff without finance 403; teacher/cashier 403.
- Migration upgrade + downgrade on a DB with data (test-migration rules).
- `ViewAccessSweepTests` must stay green.

---

## Frontend contract

- **Menu:** HR loses "O'qituvchilar" (`/admin/teachers` → `<Navigate to="/admin/boshqaruv/staff?position=teacher">`).
  Boshqaruv → "Xodimlar" is the unified page.
- **Unified page** (`/admin/boshqaruv/staff`): one table of teachers + staff. Columns: №, F.I.SH
  (avatar), Telefon (`formatPhone`), Lavozim (`O'qituvchi` for teachers, staff position otherwise),
  Rol (staff access role; teachers `—`), Oylik, Oxirgi faollik, Amallar. Filters: search (name or
  phone), Lavozim select (Hammasi / O'qituvchi / each staff position), `?position=teacher` preselects.
  Tabs Faol / Arxiv (archive exists for teachers only). "Yangi xodim" asks the type first:
  O'qituvchi → existing `TeacherFormModal`; Boshqa xodim → staff form (right-side `Drawer`) with
  phone (`PhoneInput`), salary, salary start date, role (superadmin), password. Every existing action
  keeps working: teacher view/edit/archive/restore/delete/credentials, staff edit/delete/credentials.
- **Access catalog** (`lib/access.ts`): the Xodimlar page must also grant the `teachers` section
  (it edits teachers) — encode both `staff` and `teachers` for that page.
- **Salary screens** (Moliya → Ish haqi, FinancePage "O'qituvchilar maoshi"): list staff rows too,
  with a Lavozim column and filter; titles say "Xodimlar maoshi"; the detail/payment modal uses the
  staff endpoints for `kind === 'staff'`.
- **Bonus / Jarima:** the person picker is one combined list (teachers + staff) with the position.
- Design: our own iOS-style look; do not touch the Leads board files.
