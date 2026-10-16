# Attendance red list ("Qizil ro'yxat") and parent follow-up

Status: item #5 of `docs/MARKAZ-GAPS.md`, built 2026-10-15.

## Why

The client: *"Ketma-ket va sababsiz dars qoldirayotgan o'quvchilar bo'yicha alohida ogohlantirish tizimi
… tizim o'quvchini avtomatik 'Qizil ro'yxat'ga kiritsin. Sinf rahbari va maktab rahbariyatiga ota-ona
bilan bog'lanish talabi (task) avtomatik ochilsin. Holatlar: Aloqa kerak, Qayta qo'ng'iroq, Hal bo'ldi,
Bog'lanib bo'lmadi."*

The absentees card (Davomat analitikasi → "Dars qoldirayotgan o'quvchilar") counts unexcused days in a
period, has no notion of consecutive days and records nothing about who called the family.

## When a pupil goes on the list

The scan reads the attendance report's own pupil-days (`AttendanceAnalytics.BuildAsync` → `StudentDay`,
one per pupil and date — the rule every attendance screen uses) and classifies each (`RedListCriteria`):

| Day | Counts as |
|---|---|
| absent the whole day, main reason unexcused ("sababsiz" in the name or negative points) | **missed** |
| absent the whole day, excused reason | not counted — ends a run (counted when `count_excused` is on) |
| present or partly present (missed some lessons) | came — ends a run, sets "qaytdi" |
| nobody checked the pupil, no lessons (weekend, holiday) | not a day — neither ends nor extends a run |

A case opens when either rule is met and the pupil has no open case:

| Rule | Default | Meaning |
|---|---|---|
| `consecutive` | 3 | the latest run of missed days is at least this long and ended inside the window |
| `window` | 5 in 14 | at least this many missed days in the last N calendar days (today included) |

Criteria live in one row (`attendance_red_list_settings`, id = 1, seeded with the defaults) and are edited
on the admin page by the `attendanceCases` key.

## Cases

`attendance_cases` — one row per case; **one open case per pupil** (partial unique index
`ux_attendance_cases_open`). Columns: trigger, streak (longest run seen while open, never lowered),
window count, last absent / last present day, the missed dates (≤ 20), status, call-back day, last action
time, closed at / by, notified at.

`attendance_case_actions` — append-only history: status, comment (required), call-back day, actor
(users.id, name, `teacher` | `staff`), soft delete. Every open, action and delete is in `audit_logs`
(`AttendanceCase`, `AttendanceRedListSettings`).

Status flow:

- the scan opens a case as **Aloqa kerak**;
- an action sets the status: **Qayta qo'ng'iroq** (call-back day, tomorrow by default, ≤ 60 days),
  **Bog'lanib bo'lmadi**, **Hal bo'ldi**;
- **Hal bo'ldi** closes the case; a closed case takes no more actions (409);
- the scan never closes a case; it refreshes the numbers of open ones and shows **Qaytdi** when the pupil
  came back after the last missed day;
- after a case is resolved only days after its last missed day count, so the next scan does not reopen
  it — a new run opens a new case;
- deleting a mistaken action puts the case back to the status of the latest remaining one; deleting the
  "Hal bo'ldi" action reopens the case unless the pupil already has a newer open case (409).

**Overdue** (red on the admin page, in the teacher's card and in the digest): status Bog'lanib bo'lmadi;
a call-back day that has passed; or no action for `escalate_after_days` (default 2) calendar days since the
last action or since opening. A call-back agreed for a later day is not overdue.

## Scan and notices

`AttendanceRedListScheduler` (hosted service) wakes every 5 minutes and runs the scan at **13:00** (after
the nurse's roll-call) and **18:00**, school days only (not Sunday, not a holiday); each slot runs once.
The admin page has "Hozir tekshirish". The scan reads 31 days back (more when the window is longer).

Telegram only, staff only — **nothing goes to parents**:

- **new case** → the homeroom teachers of the pupil's class and the teachers of the pupil's track group,
  one message per teacher per scan listing their new cases (switch `notify_teachers`, ships **off**);
- **daily digest** (first scheduled scan of the day) → staff accounts holding the `attendanceCases` key
  with a linked Mini App account: open count and the overdue cases (switch `notify_management`, ships **off**).

Chats are the two links `PenaltyNotifier` uses (bot registration, Mini App account). Texts are neutral
placeholders until the school approves its own (`AttendanceRedListService.TeacherNotice`, `DigestText`).

## API

Admin — `[AdminPerm("attendanceCases")]`: any staff reads (the attendance section's rule), writing needs
`attendanceCases`; admin / superadmin always; teachers 403.

- `GET    /api/admin/attendance/red-list?status=open|aloqa_kerak|qayta_qongiroq|boglanib_bolmadi|hal_boldi&className=&teacherId=&overdue=`
  — counts per tab, rows, filter options; "hal_boldi" = closed in the last 30 days.
- `GET    /api/admin/attendance/red-list/{id}` — case with history.
- `POST   /api/admin/attendance/red-list/{id}/actions` — `{ status, comment, callbackOn }`.
- `DELETE /api/admin/attendance/red-list/actions/{actionId}` — soft delete.
- `GET    /api/admin/attendance/red-list/open` — open case per pupil (links from the report).
- `GET|PUT /api/admin/attendance/red-list/settings` — criteria and switches (PUT audited).
- `POST   /api/admin/attendance/red-list/scan` — scan now.

Teacher — `[Authorize(Roles = "teacher")]`, own pupils only (classes the teacher leads + track groups they
teach; anything else 404):

- `GET  /api/teacher/homeroom/red-list` — open cases with streak, missed dates, parent phones, history.
- `POST /api/teacher/homeroom/red-list/{caseId}/actions` — `{ status, comment, callbackOn }`.

## Screens

- Admin: Davomat → **Qizil ro'yxat** (`/admin/attendance/red-list`, `?case=<id>` opens a case): status
  tabs with counts, class / teacher / overdue filters, drawer with reason, missed days, parent phones,
  history and the action form; criteria card. "Dars qoldirayotgan o'quvchilar" rows link to the pupil's
  open case. Role editor: the page under Davomat — "To'liq" also grants `attendanceCases`.
- Mini App: Bugun → **Sinfim · Qizil ro'yxat** (only when the teacher has open cases) → list → pupil:
  missed days, tap-to-call parent phones, four status buttons, comment, call-back day, history.
- Teacher PWA: Sinf rahbarligi shows a red banner and a line per pupil (read-only; acting is in the Mini App).
