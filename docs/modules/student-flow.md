# O'quvchilar oqimi (student flow)

Status: request 7 of the client's 2026-10-15 list, 2026-10-15.

## Why

"O'quvchilar sonining dinamikasi va ketgan o'quvchilar tahlili": for a date range — how many came,
how many became active, how many left, why (per catalog reason), in which class, and who. The pupil
row only says how things are now (archived or not, the last reason; archiving even blanks the
class), restore wipes the reason, and a deleted pupil left no trace — so "how many left in October,
and from which class" could not be answered.

## Data — `student_events`

One row per arrival, departure or return: `student_id` (no FK — a deleted pupil keeps the trace),
name snapshot, `kind` (enrolled | left | returned), `on_date`, class id and name **at that moment**,
leave reason (catalog `reason_id`, FK RESTRICT, + `reason_text`), `source`, who, when.

| Path | Kind | Source | Class | Reason |
|---|---|---|---|---|
| New pupil (form) | enrolled | manual | the form's | — |
| Lead → pupil | enrolled | lead | the chosen one | — |
| Excel import (both paths) | enrolled | import | the row's | — |
| Archive one pupil | left | manual | read **before** archiving blanks it | dialog's catalog + note |
| Archive several | left | bulk | each pupil's own | dialog's catalog + note |
| Archive a class | left | class_archive | the class | dialog's catalog + note (new dialog) |
| Year rollover, last grade | left | graduation | the class | "Maktabni bitirdi" (seeded row), "Bitirdi" |
| Delete (one or several), pupil not archived | left | delete | current | "O'chirildi" |
| Restore one pupil | returned | manual | none (archiving emptied it) | — |
| Restore a class | returned | class_archive | the class | — |

Arrival day = the card's enrolment date (today if empty); editing it on the card moves the
pupil's first arrival. Deleting an archived pupil writes nothing (they already left).

**"Faollashgan" is not stored**: it is the pupil's first payment ever (reversal rows excluded),
the same rule as the dashboard's "Birinchi to'lov qilganlar". Pupils brought back from the archive
are counted separately as "Qaytganlar".

## Backfill (migration `AuditStudentFlowAndShop`, `Migrations/Sql/student_events_backfill.sql`)

Re-run safe (skips an existing pupil + kind + day). All rows `source = backfill`:

1. arrivals — enrolment date, else the first class membership; class from that membership, else now;
2. current departures — every archived pupil on `archived_at`, class from the closed membership
   nearest that day (else the class name kept on the row), reason from the catalog id, "Bitirdi" →
   "Maktabni bitirdi", who from that day's archive audit row;
3. earlier departures — archive audit rows of pupils who still exist (note from the summary);
4. returns — restore audit rows ("O'quvchi arxivdan qaytarildi");
5. deletions — `StudentDelete` audit rows of pupils deleted while active.

Not recoverable: single deletes before 2026-10-15, class restores (no per-pupil row), catalog
reasons of earlier departures (shown as "Boshqa").

## Report

`GET /api/admin/reports/student-flow?from&to[&classId]` (≤ 1100 days) →
- totals: yangi, faollashgan, ketgan, qaytgan, sof (= yangi + qaytgan − ketgan), headcount at the
  start of the first day and the end of the last (today's active pupils walked back through later
  events);
- by reason (count, %; none or a deleted reason → "Boshqa");
- by class (the class at the event; no class → "Sinfsiz");
- four lists: pupil, class, date, reason + note, who / source.

With `classId` the headcounts use only that class's arrivals, departures and returns — moves between
classes are not events, so a class headcount is an estimate.

- `GET …/export` — the same as .xlsx (Umumiy, Sabablar, Sinflar and the four lists).
- `PUT …/events/{id}/reason` `{ reasonId, reasonText }` — a departure's reason set or corrected
  later; while the pupil is still archived and it is their latest departure, the card's archive
  reason follows. Audited (`StudentEvent`).

All three: `[AdminPerm("students", GatedRead = true)]` — reading needs `students` or
`students:view`, changing a reason the full key. A catalog reason used by a departure cannot be
deleted (Sozlamalar → Arxivlash sabablari counts these pupils too); it is retired instead.

## Screens

- O'quv bo'limi → O'QUVCHILAR → "O'quvchilar oqimi" (`/admin/students/oqim`): presets (Bu hafta,
  Bu oy, O'tgan oy, O'quv yili) or a custom range, class filter, seven tiles, reason bars, class
  table, lists in tabs (pencil on a departure edits its reason), Excel. `?from&to` opens a range.
- Sinflar → Arxivlash: a dialog with the catalog reason and a note (was a plain confirm).
- Bosh sahifa → Vidjetlar → Kontingent → "O'quvchilar oqimi" (on by default, needs the pupils'
  section): Yangi / Faollashgan / Ketgan for the page's period, returns and net, top 3 reasons, a
  link to the report with the same dates.
