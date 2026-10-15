# Homework completion ("Uy vazifasi bajarilishi")

Client request, 2026-10-15: *"Uyga vazifalar bajarilish darajasini aniqlash va jadval holatida qilish
(+ / - / *) holatda — bajardi / chala / umuman yo'q / kech qoldi / kelmadi."*
Record how well each pupil did their homework, and show it as a pupil × lesson table.

## What

- Five statuses per pupil per lesson instead of the old two ("Qildi" / "Qilmadi").
- A table screen per class/group + subject + date range, with per-pupil and per-lesson totals and a
  completion rate, Excel export, and one-tap / one-key marking.
- The same five statuses in the journal cell, the journal grids, the Mini App lesson screen and the
  admin student profile.

## Why no new table

The mark already existed: `journal_entries.homework` (int, no CHECK constraint) — the same journal cell
as the grade, the attendance reason, behaviour and mastery. Production rows carry 1 and 2. The feature
widens the value range and adds a second way to read it; no migration.

## Statuses

| Value | Symbol | Word | Note |
|---|---|---|---|
| 0 | | — | not marked |
| 1 | `+` | Bajardi | the old "qildi" — meaning unchanged |
| 2 | `*` | Umuman yo'q | the old "qilmadi" — meaning unchanged |
| 3 | `−` | Chala | partly done |
| 4 | `K` | Kech qoldi | handed in late |
| 5 | `Y` | Kelmadi | pupil absent |

Symbols follow the client's literal order "+ / - / *" = bajardi / chala / umuman yo'q. One definition
per side: `SchoolLms.Domain/HomeworkStatus.cs`, `schoollms.client/src/lib/homeworkStatus.ts`, and a
JS copy for the Mini App (`ui-tg/src/lib/homework.js`). Every screen shows the legend with words.

## Rules

- **Validation**: every write path accepts 0..5 only; anything else → 400 with
  `HomeworkStatus.InvalidMessage` (journal cell `PUT /api/admin|teacher/journal` and the bulk endpoints).
- **Derived Kelmadi**: a cell with no homework status whose attendance reason is NOT a lateness reason
  (`AbsenceReason.IsLate = false`, or a reason id no longer in the catalogue) is shown as `Y` with
  `derived = true` (muted). It is never written. A lateness reason means the pupil was there.
- **Completion rate** = (bajardi + ½·chala + ½·kech qoldi) ÷ marked × 100, one decimal, where marked =
  bajardi + chala + umuman yo'q + kech qoldi. Kelmadi (stored or derived) is outside the rate: absence
  is an attendance matter. No marked cell → no rate (`null`, shown as "—").
- **Columns** of the sheet: the journal's own timetable columns (`JournalService.ComputeColumnsAsync`,
  holidays and the group cut-over switch included) from `from` to min(`to`, today), plus any lesson in
  that range that is off the timetable but happened (conducted note or attendance mark — `HeldLessons`),
  set homework text, or already has a homework status. Future lessons are not shown.
  A lesson's dot/tooltip is the homework text **set in that lesson** (its lesson note).
- **Rows**: the journal roster (`LessonRoster`): active pupils of the class / active group members.
  Former pupils are not listed. In a split lesson (1/2-guruh) a pupil of the other half has no cell (`—`).
- **Bulk write** changes only `homework` of each cell; grade, reason, behaviour and mastery stay. A missing
  cell is created (quarter from the date), a cell left completely empty is removed (the journal's
  "Tozalash"). All-or-nothing: one bad mark rejects the request. Duplicate cell rows all get the status.
- **Journal guards** apply to the bulk write: archived pupil (400, same message as the journal), pupil
  not on the roster (400), group cut-over switch off (400), a date outside every quarter (400), and the
  automatic "dars o'tildi" of a filled cell (`JournalService.AutoConductAsync`, which waits for grades
  when `is_student_grade_required` is on). Teachers: own class + subject (or a covered lesson on that
  date) and no future dates, exactly like `PUT /api/teacher/journal`.
- Range: at most 120 days; default the last 14 days up to today.
- Parents and pupils do not see the statuses (not requested). Nothing is sent to anyone.

## API

| Method | Path | Gate |
|---|---|---|
| GET | `/api/admin/journal/homework?ownerId=&subjectId=&from=&to=&ownerKind=` | `journal` (AdminPerm: staff read open, write needs the key) |
| GET | `/api/admin/journal/homework/export?…` | same — .xlsx of the shown table |
| PUT | `/api/admin/journal/homework` | `journal` |
| GET | `/api/teacher/journal/homework?…` | teacher `journal` permission + teaches that owner + subject (or substitute) |
| GET | `/api/teacher/journal/homework/export?…` | same |
| PUT | `/api/teacher/journal/homework` | same, per date; future dates → 400 |

`ownerKind` is optional; when given it must match the owner (`class` / `group`), else 404.

Sheet: `{ ownerId, ownerKind, ownerName, isTrack, subjectId, subjectName, from, to,
lessons: [{ date, period, subGroup, held, topic, homework, hasHomework, summary }],
rows: [{ studentId, fullName, subGroup, cells: [{ status, derived, inLesson }], summary }], total }`
— `summary`/`total` = `{ done, partial, notDone, late, absent, marked, percent }`.

PUT body: `{ ownerId, subjectId, ownerKind?, marks: [{ studentId, date, period, subGroup, status }] }`
(≤ 2000 marks; `status` 0 clears).

Student profile (`GET /api/admin/students/{id}/profile`): `homeworkDone` (1) and `homeworkMissed` (2) keep
their meaning; `homeworkPartial`, `homeworkLate`, `homeworkAbsent` were added, also per quarter in `marksTrend`.

## Screens

- **Admin**: `Jurnal → Uy vazifasi bajarilishi` (`/admin/journal-homework`; the Jurnal menu became a group
  with "Jurnal" and this page). Pickers: class/group, subject, half (only for split lessons), date range with
  "Shu hafta / 2 hafta / Shu oy". Grid: sticky pupil column, one column per lesson (date, weekday, period,
  G1/G2, homework-text marker opening the topic and homework, "Hammasi bajardi" filling the empty cells of
  that lesson with `+` after a confirm), right-hand counts per status and the rate, per-lesson rate in the
  footer, legend. A cell opens a five-option picker with "Tozalash"; on a focused cell `+ − * K Y` set,
  `0`/Backspace clears, arrows move. Excel button = server-built .xlsx of the same table. The journal page
  links here ("Uy vazifasi jadvali") with its class and subject.
- **Teacher web**: `/teacher/uy-vazifasi` — the same component, own classes/subjects only. A SPA page like
  "Dars tahlilim" (server fallback exception in `Program.cs`); the PWA's Profil screen links to it.
- **Journal cell** (`JournalCellModal`, admin and teacher web): five chips with symbol and word.
- **Journal grids** (admin, teacher web): the small homework mark shows the status symbol in its colour.
- **Mini App** (teacher lesson screen, "Davomat va baho"): under each pupil who is in class (present or late)
  a one-tap `+ − * K Y` row, "Hammasi bajardi" for the unmarked ones, a legend line; saved by the screen's
  "Saqlash" in the same request as attendance and grade.
- **Admin student profile**: counts per status with the rate, and a stacked per-quarter chart.

The old teacher PWA's journal sheet keeps its two chips ("Qildi" / "Qilmadi"); a stored 3–5 is kept when
that sheet saves (it sends the value it loaded).
