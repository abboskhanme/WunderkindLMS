# O'zgarishlar tarixi (audit log)

Status: request 6 of the client's 2026-10-15 list; `docs/MARKAZ-GAPS.md` #14 (one audit screen),
2026-10-15. Journal and attendance cells are not logged yet (see the end).

## Why

"Kim, qachon, qaysi bo'limda, qaysi yozuvni qanday o'zgartirdi" — for money, debts, salaries and
pupils. `audit_logs` existed, but history was only shown per screen, the per-record endpoint had no
paging and no default limit (Moliya → "Tarix" loaded the whole table), any staff member could read
salary and payment before/after, some events were filed under the wrong type, and pupil, role,
timetable and staff edits were not logged at all.

## Sections and kinds

`AuditCatalog` (Application) maps every entity type to one section and every action to one kind.
The list, its filters, the Excel file and the money rule all read it.

| Section | Entity types |
|---|---|
| O'quvchilar | Student, StudentClass, SchoolClass, StudentStatus, StudentImport, StudentDelete, Guardian, StudentEvent, Certificate, StudyGroup, Lead, StudentLocation |
| Moliya | Payment, LedgerEntry, Invoice, Expense, CashBox(+Transaction), CashShift, CashHandover, StudentRefund, StudentSubscription, StudentWithdrawal, StudentDiscount, ClassFee, SubscriptionPlan, FeeCategory, DiscountType, TransactionType, ExpenseTemplate, BillingSettings, DebtorAction, DebtorStatus, AnomalyFlag, ExpenseAttachment, FinanceTransaction |
| Maosh | TeacherSalary, StaffSalary, PayrollAdjustment, AdjustmentReason |
| Jadval | ScheduleTemplate, WeekAssignment, Substitution, Holiday |
| Shartnoma | StudentContract, Contract |
| Baholash | SeasonalMark, Exam, ExamType, ExamParticipant, ExamResult, QuestionBank, Question, DisciplinePoint |
| Xodimlar | Teacher, Staff, AccessRole, RoleAssignment |
| Sozlamalar | AcademicYear, TelegramBot, AutoMessageRule, CertificateType, LeadStage, Survey |
| Boshqa | anything else (News, …) |

Kinds: **Yaratdi** (create, enrol, import, request, add), **O'chirdi** (delete, remove),
**Tahrirladi** (everything else). Actions beyond the three show a sub-label: Storno, Bekor qildi,
Tasdiqladi, Rad etdi, Arxivladi, Arxivdan qaytardi, Parol yangilandi, Yubordi, …

**Old misfiled rows are not rewritten.** Before 2026-10-15 pupil archive/restore was written as
`StudentDiscount`, teacher archive/restore as `TeacherSalary`, class archive/restore as `ClassFee`.
They are recognised by summary prefix ("O'quvchi arxiv…", "O'quvchi ommaviy arxiv…",
"O'qituvchi arxiv…", "Sinf arxiv…") and filed under O'quvchilar / Xodimlar — so non-finance staff
now see them. New code writes `Student` / `Teacher` / `SchoolClass` with actions `archive` /
`restore`; class rename and homeroom changes are `SchoolClass` too.

## Money rule

Finance and Maosh rows go only to users with the money right — admin, director, `finance` and
`finance:view` (`AuditAccess.CanSeeMoney`); for everyone else they are not returned, and money
fields (`amount`, `salary`, `monthlyFee`, …) are stripped from the before/after of the remaining
rows. One rule for the audit screen, the per-record endpoint and the pupil card's activity tab.

## What is logged (new, one row per save, changed fields only)

- **Pupil**: create (the card's fields), field changes (F.I.SH, tug'ilgan sana, manzil, jinsi,
  ota-ona, telefonlar, qabul sanasi, til, rasm, hujjat, mo'ljal sinf, "Parol: yangilandi" — never
  the password), archive / restore under the pupil, password reset, single delete (`StudentDelete`,
  like delete-many), old Excel import path. The class change keeps its own `StudentClass` row.
- **Guardians** (card and Ota-onalar screen): add, edit, primary, detach, account / password.
- **Roles**: role create / change (keys added and removed) / delete; role given to or taken from
  a staff member or teacher; hand-picked staff permissions.
- **Timetable**: template create / rename / delete; cells — one row per save listing the changed
  cells ("Dushanba, 2-dars": "Matematika — Aliyev" → …), live edits name the old version kept;
  week assignments; substitutions (create / cancel); holidays.
- **Discipline points**: add (single and whole class), delete.
- **Teacher / staff cards**: create, field changes, delete, password reset, archive / restore
  (salary rows stay as they were). Contract templates and sendings. Student status catalog.

Keys of the new rows are written in Uzbek, so every screen shows them as they are.

## API

- `GET /api/admin/audit` — per-record history (unchanged shape: an array). New filters `actorId`,
  `section` (comma list), `kind`, `q`; **default 100 rows, max 500**; money rule. Gate unchanged:
  admin / superadmin / any staff (the screens that show history in place need it).
- `[AdminPerm("audit", GatedRead = true)]` — the screen:
  - `GET /api/admin/audit/log?from&to&actorId&section&kind&action&studentId&teacherId&q&cursor&limit`
    → `{ items, total, nextCursor }`, newest first, keyset pages of 50 (≤ 200). `to` as a bare day
    includes the whole day. `actorId=system` — rows with no person.
  - `GET /api/admin/audit/actors` — people in the log (current name, row count), most active first.
  - `GET /api/admin/audit/sections` — the section list.
  - `GET /api/admin/audit/export?…` — the filtered view as .xlsx, ≤ 10 000 rows.

Indexes: `(actor_id, timestamp)`, `(entity_type, timestamp)` (migration `AuditStudentFlowAndShop`).

## Screen

Boshqaruv → "O'zgarishlar tarixi" (`/admin/audit`, key `audit` — in the role editor under
Boshqaruv). Filters: date range (last 30 days by default), xodim, amal, bo'lim, o'quvchi (archived
too), search. Columns Vaqt · Xodim · Bo'lim · Yozuv (with a link to the pupil or teacher) · Amal;
a row opens its before → after. "Yana yuklash" pages on; Excel downloads the filtered view.

Also fixed: the per-record lists (payment history, Moliya → Tarix, salary detail, pupil activity,
mark history) no longer crash on actions other than create/update/delete
(`components/audit/auditActions.ts`); Moliya → "Tarix" asks for the finance and salary sections,
200 rows.

## Not done

Journal and attendance cells are not logged: they are saved cell by cell from four places (admin
journal, teacher portal, Mini App, nurse's roll-call), so "one row per lesson save" needs a lesson
level save first — left for a separate task.
