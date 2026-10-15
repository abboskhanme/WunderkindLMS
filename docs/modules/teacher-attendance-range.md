# Teacher attendance over a date range, tied to the timetable

Status: built 2026-10-15 (client request: *"Ustozlar davomatini ikki davrli qilish — misol uchun
01.09.2026 - 30.09.2026 shaklda va jadvalga bog'lash — davomatda faqat o'sha kuni darsi bor ustozlar
chiqishi"*).

## What

The O'qituvchilar davomati board shows any date range (e.g. 01.09.2026 – 30.09.2026) instead of one
calendar month, and on every day it lists — and lets staff mark — only the teachers who have lessons
that day. The daily turnstile dashboard and the automatic turnstile recompute follow the same rule.

## Why

The board used to list every active teacher on every weekday of the month. A teacher with no lessons
on Wednesday still had an empty Wednesday cell, the header click marked them present, and the
turnstile sync wrote `absent` for them on every past day without an event — Sundays and holidays
included. Those `absent` rows feed KPI and salary deductions.

## Rules

"Has lessons on a day" is decided in one place, `TeacherDaySchedule` (Application/Services):

| Day | Lessons |
|---|---|
| Sunday | none |
| holiday (`holidays`) | none |
| outside every quarter | none (also when no quarters are configured at all) |
| any other day | the lessons of the template **assigned to that week** (`week_assignments` → `TeacherLessons.ForWeekAsync`: class and group owners, group-lessons switch respected) on that weekday |

- Standing substitutions (`lesson_substitutions`, not cancelled) on a lesson day move one lesson to
  the substitute. The absent teacher stays listed that day even if every lesson went away (count 0),
  so they can still be marked absent.
- The week-assigned template is the same source as the substitution screen and the teacher's own
  schedule. Salary keeps reading its weekday "main" template — salary logic is unchanged.
- Each (quarter, week) in a range is resolved once: the query count grows with the weeks in the range,
  not with its days or teachers.

What the rule changes:

- **Board** — rows: teachers with ≥ 1 lesson in the range, plus any teacher with an entry in the range
  (nothing recorded disappears). Archived teachers are never shown, as before.
- **Single cell** (`PUT`) — `present` / `absent` / `late` is refused on a day the teacher has no
  lessons (400 *"Bu kuni ustozning darsi yo'q"*). Clearing is always allowed, so an old mark on such a
  day can be removed.
- **Whole day** (`PUT /day`) — marks / clears only the teachers who teach that day (count ≥ 1; a
  teacher whose every lesson went to a substitute is left alone both ways). Nobody teaching →
  400 *"Bu kuni darsi bor ustoz yo'q"*.
- **Dashboard** — lists the teachers with lessons that day, plus anyone who already has an entry that
  day (flagged *"darsi yo'q"*, not markable there). A live *"Kelmadi"* only for teachers with lessons.
  The number of active teachers without lessons is returned for a one-line note.
- **Turnstile recompute** — never writes `absent` on a day the teacher has no lessons. On such a past
  day it drops an `absent` it wrote itself earlier (`source = turnstile`); manual rows are never
  touched. Events still turn into `present` / `late` on any in-quarter day, as before.

Earlier rules stay: a mark only inside a quarter and not before the teacher's start date.

## API (`[AdminPerm("teachers")]` — staff read freely, write only with `teachers`)

- `GET /api/admin/teacher-attendance?from=yyyy-MM-dd&to=yyyy-MM-dd` — board for the range
  (`from ≤ to`, at most 93 days; otherwise 400 with an Uzbek message). `?month=yyyy-MM` still works
  (that month), no parameters = the current month. Response — the old `teachers`, `entries`,
  `quarters`, plus:
  - `from`, `to`;
  - `days[]`: `date`, `isSunday`, `isHoliday`, `holidayName`, `inQuarter`, `teachers` (how many listed
    teachers have lessons that day) — every calendar day, Sundays included;
  - `lessons[]`: `teacherId`, `date`, `lessons` — one row per teacher per lesson day.
- `PUT /api/admin/teacher-attendance` `{ teacherId, date, status, note }` — one cell (rules above).
- `PUT /api/admin/teacher-attendance/day` `{ date, status }` — the whole day (rules above).
- `GET /api/admin/teacher-attendance/dashboard?date=` — rows gain `hasLessons`, `lessons`; the
  response gains `noLessonTeachers`. `summary.total` = the rows listed.

No migration: nothing new is stored.

## Screen

HR → O'qituvchilar davomati (`/admin/teachers/attendance`), **Davomat jadvali** card:

- two date fields (*Davr boshi — Davr oxiri*, at most 93 days) and presets *Shu oy* (default),
  *O'tgan oy*, *Shu hafta*;
- a column per day except Sundays, grouped under month headings; holidays greyed and not markable;
- a cell where the teacher has no lessons shows a muted "—" (title *"Dars yo'q"* / the holiday);
  an existing mark on such a day is shown faded — clicking it offers to remove it, never to change it;
- a markable cell's title shows the day's lesson count;
- the day header marks only that day's teachers ("Keldi" / clear);
- right-hand totals per teacher: Keldi / Kelmadi / Kechikdi and **Darslar** (lessons in the range).

The daily dashboard below lists only that day's teachers (each with the day's lesson count) and
notes *"Bu kuni N nafar ustozning darsi yo'q."*

Tests: `SchoolLms.Tests/TeacherAttendanceRangeTests.cs`.
