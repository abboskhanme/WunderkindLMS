# HR topshiriqlari (WunderkindHR port) — SPEC

Status: approved by the client 2026-09-27 (approach A — full port into this system).
Source system being retired: `../WunderkindHR` (FastAPI + aiogram + ARQ + React 19),
live at `hr.wunderkindedu.uz` (2.28.59.146, `/opt/wunderkind-hr`). 14 employees,
526 tasks, 12 templates, 17 proofs since 2026-09-02.

**This is a port, not a redesign.** Rules, texts, screens and notification policy follow
the old system 1:1. The old code is the reference for every detail not repeated here:

| Topic | Old file (in `../WunderkindHR/`) |
|---|---|
| Rules (authoritative) | `docs/hr/SPEC.md` (§1–§15), `docs/hr/ASSUMPTIONS.md`, `docs/hr/REJA-2026-09-03-tuzilma.md` |
| Domain rules, state machine | `backend/src/modules/hr/domain/{entities,recurrence,enums,exceptions}.py` |
| Use cases (what each action does) | `backend/src/modules/hr/application/use_cases/*.py`, `application/dto.py` |
| Scope (manager subtree) | `backend/src/modules/hr/infrastructure/read_models.py`, `presentation/api/deps.py` |
| Tables | `backend/src/modules/hr/infrastructure/models.py` + `backend/migrations/versions/*hr*` |
| Panel API | `backend/src/modules/hr/presentation/api/{router,schemas}.py` |
| Employee API (Mini App) | `backend/src/modules/hr/presentation/api/miniapp.py` |
| Scheduler jobs | `backend/src/modules/hr/application/tasks.py`, `use_cases/{generate,send_*,close_*}*.py` |
| Telegram texts, digest | `backend/src/modules/hr/presentation/bot/texts.py`, `infrastructure/digest.py`, `infrastructure/notifier.py` |
| Panel screens | `frontend/src/pages/hr/**`, `frontend/src/entities/hr/{api,lib}.ts` |
| Employee Mini App | `frontend/src/hrapp/**` |

**JSON contract: `schoollms.client/src/types/hrTasks.ts`** — the old `model.ts` +
`hrapp/types.ts` converted to camelCase, plus `userId`, `role`, `hrRole` on
`HrEmployee`. Backend DTOs serialize to exactly these shapes; values (statuses,
sources, kinds, `keep_open`, …) are unchanged.

## 1. Not ported — our system already has it

- Its own login (phone + bot OTP, password, Telegram Login Widget browser token) → our
  login (web) and our Telegram Mini App auth.
- Its own bot machinery (bot profiles, webhook app, bot settings page, `/id`, reply
  keyboard, contact-sharing linking) → **our single bot** (`TelegramService` /
  `TelegramBotService`) and our Telegram linking (link codes, login in the Mini App).
  CLAUDE.md: Telegram is the only channel — one bot, one Mini App.
- `admin_users` / panel roles → our users, roles and permission keys.

## 2. Identity

An HR employee is **always one of our users** (`staff`, `teacher`, `admin`,
`superadmin`, `cashier`) plus one row in `hr_employees` (1:1 on `user_id`).

- Creating an employee in the HR panel: pick an existing user, or give name + phone and
  the system creates a `staff` user (login = phone digits, generated password shown
  once, no permissions) + the HR row. A manager who creates an employee becomes that
  employee's manager (old ASSUMPTIONS 2026-09-09).
- **People are added and managed in Boshqaruv → Xodimlar, not in HR** (client, 2026-09-28).
  The HR panel has no "+ Xodim" and no "Lavozimlar" button; its edit dialog shows name,
  phone and position read-only (it edits managers, HR role, note). The create and position
  endpoints stay for the API.
- **Every LMS employee is an HR employee** (client, 2026-09-28). The HR scheduler's loop
  adds a row for each active teacher (non-archived teacher card with an account, position
  "O'qituvchi") and each staff/cashier/admin user (position = their LMS "Lavozim", else
  "Xodim"); superadmins are skipped. Additive only — nothing is changed or deactivated. New
  rows have no manager and show under "Biriktirilmagan" in the tree until a director places
  them. Every pass also copies the LMS name and "Lavozim" onto each row (teacher →
  "O'qituvchi"; an empty LMS position leaves the HR one alone) and fills a missing phone.
  `hr_employees.phone` is nullable for them (unique when present; Telegram goes by
  `user_id`). Deleting a teacher/staff account removes an HR row with no history (no task, no
  rule aimed at it); with history it is still 409.
- Deactivating an employee (HR) deactivates the HR row and cancels open tasks (old §15.7);
  it does NOT delete or block the user account.
- Telegram: messages go to every `telegram_accounts` row of the employee's user.
  `HrEmployee.telegramUserId` = first linked Telegram id or null. "Invite" = our existing
  one-time link code (`/api/admin/telegram/link-codes` logic), returned with a
  `https://t.me/<bot>?startapp=` / Mini App hint the panel can copy.

## 3. Roles and scope

| Who | HR panel pages | Scope |
|---|---|---|
| `admin`, `superadmin` | yes | whole organisation (`director`) |
| staff with permission **`hrTasks`** and `hr_employees.hr_role = 'director'` | yes | whole organisation |
| staff with permission **`hrTasks`** (`hr_role` `manager`) | yes | self + whole subtree (`hr_employee_managers`, recursive, cycle-safe) |
| any user with an `hr_employees` row | own tasks only (`/api/hr/me/*`, "Mening topshiriqlarim", Mini App) | self |

- Out-of-scope record → **404**, never 403 (old §2 rule).
- Positions registry and HR settings: directors only (old `hr_position:manage`).
- A manager can add employees (they land under the manager) but cannot change
  positions, settings, or anyone's panel access.
- `HrEmployee.panelRole` in DTOs: `superadmin`/`admin` from our role, `admin` for
  `hr_role = director`, `manager` for staff with `hrTasks`, else null. Panel access
  itself is granted with our permission key, not from the HR screen.
- Managers are also employees: they get tasks from above and see them in
  "Mening topshiriqlarim".

## 4. Tables (EF migration, `hr_` prefix, public schema)

Port the old tables one-to-one (columns, CHECKs, unique keys, `KEEP_OPEN`, generation
idempotency key `(template_id, employee_id, period_start, due_at)`), renamed with the
`hr_` prefix: `hr_positions`, `hr_employees` (+ `user_id` unique FK users, + `hr_role`),
`hr_employee_managers`, `hr_task_templates` (+ its target/attachment tables if the old
schema has them), `hr_tasks`, `hr_task_proofs`, `hr_task_attachments`,
`hr_task_progress`, `hr_settings` (single row), `hr_job_runs`. Ids are string GUIDs —
**migrated rows keep their old UUID** as id (the import relies on it). Times
`timestamptz`, "today" = `AppClock` (Asia/Tashkent). No native PG enums (varchar +
CHECK), like the old code.

## 5. Rules (all from the old system — do not reinvent)

Lifecycle open → done / returned / cancelled (missed only for legacy `close` rows);
done requires a proof file OR a non-empty comment; KPI (`numeric`) tasks add progress
steps, `current_amount` updated in the same transaction, auto-done at target; reopen
only the same local day and before today's digest ran; overdue stays open with an
"overdue" flag, `is_late` when completed after `due_at`; template generation daily /
weekly / monthly incl. month-days, short-month clamp, no materialising past-due dates,
POSITION targets expand to active employees; inactive employees get nothing new;
template delete → `template_id = null` on tasks; batch create (tasks and templates)
with the old limits (`HR_MAX_BATCH_*`); proof limits 5 files × 10 MB, blocked types
html/svg/js/xml; voice attachments ≤ 300 s.

## 6. API

JSON camelCase exactly as `types/hrTasks.ts`; keyset pages `{items, nextCursor,
hasMore, total?}` like the old API. Every response `Cache-Control: no-store`
(reuse `NoStoreAttribute`).

- **Panel** — `/api/admin/hr/...`, mirror of the old `presentation/api/router.py`
  (positions, employees + tree + managers + invite + deactivate, templates + toggle +
  batch, tasks + batch + get/patch/return/cancel + attachments, proofs, reports
  summary/employees + export, settings, dashboard data). Auth: admin/superadmin or
  staff with `hrTasks`; scope per §3.
- **Employee** — `/api/hr/me/...`, mirror of the old `miniapp.py` (me, tasks by day,
  create self task, complete, reopen, progress, proof upload, attachments, summary,
  "messages" if the old app has them). Auth: our JWT (web and Mini App token), user must
  have an active `hr_employees` row, else 403 `hr_not_employee`.
- Files: our `uploads/` via `UploadGuard`, unguessable GUID names.

## 7. Telegram and scheduler

Notification policy is the old §4 table verbatim: new task from a manager/director and
returned task → the employee **immediately**; morning list (07:30), deadline reminders
(offsets 120/60/30, settings), evening digest (19:00, Mon–Sat) — one message per
recipient: directors get the whole organisation, managers their subtree; empty digest
not sent; split at 4096 chars on employee boundaries. Texts ported verbatim from the
old `texts.py` / `digest.py`, links point to our Mini App. Sent through
`TelegramService` (our bot); nothing is sent when the bot is not configured.

Scheduler = one `BackgroundService`, tick every 5 minutes, `hr_job_runs`
(`job_name`, `run_date` unique) for idempotency — jobs: generate (00:10), morning,
reminders, digest, close_overdue (legacy). Times from `hr_settings`.

## 8. Screens

**Admin** — new menu group under **HR**: "Topshiriqlar" section with
Boshqaruv paneli · Topshiriqlar (+ create, detail drawer, return/cancel) ·
Takroriy topshiriqlar · Xodimlar (list/tree, managers, positions inline) ·
Hisobotlar · Sozlamalar (directors), perm `hrTasks`; plus **"Mening topshiriqlarim"**
for any logged-in user with an HR row. Port the old pages' structure, texts,
filters and behaviour 1:1; visual language = our admin panel.

**Telegram Mini App** (`schoollms.client/src/pages/miniapp/ui-tg`) — port the old
`hrapp` (tasks by day with date strip, task card, complete sheet with comment + files
+ voice note, KPI progress, "+ o'zimga topshiriq", summary):
- staff/admin/superadmin/cashier **with an HR row** → an employee panel (tabs
  "Topshiriqlar", "Natijam") with a "Boshqaruv paneli" button that opens the web panel
  the way `StaffPortal` does today; without an HR row → today's `StaffPortal` behaviour;
- teacher **with an HR row** → an extra "Topshiriqlar" tab in `TeacherPanel`.
Mobile-first, brand colours, rules of the rest of the Mini App (keyboard, safe area).

## 9. Data import (main session, one-time)

Only **school** employees (client, 2026-09-27): Markaz employees (Muzaffar
Abdubannoyev, Fayzullo) and their tasks stay in the archive dump. Bahriddin gets a new
`staff` account. Employees are matched to our users by phone, then name; all rows keep
their old UUIDs; proof files are copied into our `uploads/`; Telegram ids from
`hr.employees.telegram_user_id` are written as `telegram_accounts` rows so employees do
not have to link again (they still press Start once in our bot).

## 10. Cut-over

Backup → deploy → import (dry run, then commit) → verify counts → stop the old HR
stack → tell employees to open our bot → the old server/domain are retired by the
client after they confirm.
