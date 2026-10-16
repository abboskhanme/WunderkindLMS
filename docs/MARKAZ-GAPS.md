# What the school LMS should take from "Markaz CRM"

Source: the comparison "Markaz CRM: Maktab CRM bilan taqqoslash va yetishmayotgan funksiyalar"
(Claude Docs, 2026-10-13). "Maktab CRM" in it is this LMS, seen only through its MCP read tools.
Its section "Markazda bor, Maktab CRMda topilmagan imkoniyatlar" and the module table were checked
against this codebase on 2026-10-13. Most "not found" calls were wrong (the read tools do not show
everything). This file lists only what is really missing or half-done here.

Out of scope from that document: its data-quality table and priority plan (they describe Markaz
CRM's own data), course inflow/outflow (an education-centre metric), and anything SMS or mobile
push (Telegram is the only channel, CLAUDE.md).

Size: S ≈ ½–1 day, M ≈ 2–4 days, L ≈ a week or more.

## Already here — no work

| Capability | Where |
|---|---|
| Receipt to the parent's Telegram on payment | `PaymentsController.SendReceiptInBackground` → `ReceiptService.SendToGuardianAsync` |
| Broadcast report (sent / failed / no Telegram) | `MessagesController` `GET broadcasts/{id}`, `BroadcastDetailModal.tsx` |
| Hiring pipeline, vacancies, 30-day onboarding | `Recruitment.cs`, `RecruitmentPage.tsx`, `PublicVacanciesController` |
| Monthly / midterm tests as exam types, results, import | `Exams.cs` `ExamType`, `exams/*` pages |
| Certificates with IELTS score, CEFR level, expiry filter | `Certificates.cs`, `CertificateService` |
| Receipt number and method on every payment, QR receipt | `Billing.cs` `Payment.ReceiptNo` / `Method`, `PublicReceiptController` |
| Parent suggestions and complaints from the Mini App | `FeedbackSheet.jsx`, `FeedbackWorkflowService`, `FeedbackPage.tsx` |
| Discipline score (100 + plus − minus) on the pupil profile | `StudentProfileBuilder`, `OverviewTab.tsx`, `DisciplineTab.tsx` |
| Substitute teacher assignment with Telegram notice | `LessonSubstitutionService`, `SubstitutionsController` |
| Teacher and pupil clash checks in the timetable | `ScheduleUtilsController` `occupied-slots`, `ScheduleConflicts` |
| Change history for money, classes, status, leads, marks | `AuditService`, `AuditController`, `AuditHistoryModal.tsx` |

## To build

| # | Item | What is missing today | Size | Decision needed |
|---|---|---|---|---|
| 1 | Debt reminder on a schedule — **built** (`debt` rule, `docs/modules/auto-messages.md`) | Only a manual broadcast with `OnlyDebtors`; no job sends it | M | Day of month, minimum debt, message text |
| 2 | "New monthly invoice" message — **built** (`invoice` rule) | `BillingAccrualService` issues invoices silently | S | Send at all? Text |
| 3 | Absence message without a button — **built** (`absence` rule, full day up to a lesson cut-off such as 11:30) | `DailyAbsenceNotifier` runs only when staff press the button; single-lesson absence is never sent | S | Fixed time (e.g. 10:00) or keep manual; lesson-level or full-day only |
| 4 | Rating by month / quarter / year, TOP and BOTTOM lists | `RatingService` uses journal average + attendance only; tests and seasonal marks are not in it | M | Formula weights (journal / tests / attendance) |
| 5 | Attendance "red list" and follow-up queue | **Built 2026-10-15** — `docs/modules/attendance-red-list.md` (3 consecutive / 5 in 14 unexcused days, homeroom teacher in the Mini App, management on Davomat → Qizil ro'yxat). Was: `AbsenteesCard` counts unexcused days, not consecutive ones; no "Bog'lanish kerak" queue | M | — (defaults editable on the page) |
| 6 | Attendance taken late / not taken, per teacher | `MarkedAt` and "Unchecked" lessons exist; no deadline rule, no report, fines are manual | S | Deadline (end of lesson? end of day); fine automatically or only report |
| 7 | "Qabul qildim" button for parents | The bot handles no button presses (`callback_query`) | M | Which messages need acknowledgement |
| 8 | One log of every automatic Telegram message — **partly built**: the rules, every receipt send (automatic and resend) and the nurse's absence button share `auto_message_log` (statuses, statistics, Excel) | Receipts, discipline, pickup and penalty messages leave no record; broadcasts, absence and news each have their own. Still open: discipline, pickup and penalty messages; broadcasts and news keep their own reports | M | — |
| 9 | Birthday greeting — **built** (`birthday` rule, to parents) | Admin bell shows birthdays; nothing is sent | S | To pupil, parent or both; text |
| 10 | Certificate expiry reminder | Filter exists, no message | S | Days before expiry; to whom |
| 11 | Leads: manager, trial lesson, stage history | `Lead` has no manager field, no trial-lesson date; stage moves are not stored | M | — (board design is protected: data and screens only) |
| 12 | Rooms in the timetable | `ScheduleLesson` has no room; no room clash, no free-slot suggestion | L | Are rooms shared between classes at all? |
| 13 | HR tasks as a kanban board | `HrTasksPage.tsx` is a table only | S | — |
| 14 | ~~Audit for journal, attendance and timetable edits; one audit screen~~ **Built 2026-10-15** (`docs/modules/audit-log.md`): Boshqaruv → O'zgarishlar tarixi, timetable / pupil / role / staff edits logged | Journal and attendance cells still not logged (cell-by-cell saves from four paths) | M | — |
| 15 | External receipt / bank number on a payment | Only free-text `Note` | S | — |
| 16 | Fines deducted from salary | `PayrollAdjustments` are recorded but `SalaryLedger` / `TeacherSalaryCalc` never read them (waits for the payroll module, HR-09) | L | Deduct now, or wait for payroll |
| 17 | Book sales and warehouse | Specification only (`docs/modules/warehouse.md`) | L | Is it wanted this year? |

## Suggested order

1. **#6, #3, #2** — small, the data is already there, and they tighten daily discipline.
2. **#1, #5** — money and attendance follow-up; need the client's texts and thresholds.
3. **#4** — rating, once the formula is agreed.
4. **#9, #10, #13, #15** — quick wins in one batch.
5. **#7, #8, #14, #11** — infrastructure (bot buttons, one message log, full audit).
6. **#12, #16, #17** — large; only on a clear request.

Every item that sends a message reaches parents through Telegram only; each needs its text
approved by the client before it is switched on.
