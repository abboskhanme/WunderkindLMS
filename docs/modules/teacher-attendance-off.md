# Teachers no longer mark pupils' attendance

Status: built 2026-10-15. Client: "O'qituvchi profilidan o'quvchilarni davomat qilish funksiyasi
o'chirilsin (yoki yashirilsin). Sabab: davomat hozirda to'liq hamshira tomonidan olinmoqda; o'qituvchilar
ham davomat qilganda konflikt."

## Why

The nurse and the teachers write the same field — `journal_entries.reason_id` — the nurse through
"Davomat belgilash" (`DailyAttendanceService`), the teacher through `PUT /api/teacher/journal`, which
overwrites grade, reason, homework, behaviour and mastery at once. The Mini App re-sent the reason it
had loaded on every save, so "Hammasi keldi" + "Saqlash" erased the nurse's absences, and a teacher's
"clear" deleted the whole row, her reason included.

## The switch

`school_meta.teachers_mark_attendance` — **default false** (the migration adds it `NOT NULL DEFAULT
false`, so the school switches over on deploy). Sozlamalar → Umumiy: "O'qituvchilar o'quvchilar
davomatini oladi". `GET/PUT /api/admin/settings/general` carry `teachersMarkAttendance`; a PUT without
it leaves the flag unchanged (an older admin client cannot flip it).

## The rule (server — the real guarantee)

While the flag is off, on the **teacher** path only (`TeacherPortalController`):

| Request | Result |
|---|---|
| `PUT /api/teacher/journal` with any `reasonId` (another one, `null`, `""`) | the cell keeps its stored reason (none for a new cell); grade, homework, behaviour and mastery are written; never a 400 — the reason-required gate is skipped |
| the same, carrying nothing but a reason | nothing is stored (no empty row); an emptied row without a reason is removed |
| `DELETE /api/teacher/journal` | a row with a reason stays, with grade / homework / behaviour / mastery emptied; a row without one is deleted as before |

Flag on — exactly the old behaviour. The admin journal (`/api/admin/journal`), the nurse's screen and
the homework sheet are not affected. `GET /api/teacher/me` and `GET /api/tg/me` return
`canMarkAttendance`.

## Screens

- **Mini App lesson screen** (flag off): no "Hammasi keldi", the row is not a toggle, no reason chips, no
  "no reasons configured" gate; the nurse's mark is a small read-only "Yo'q" / "Kech" badge (reason in
  the tooltip); the count line reads "N yo'q · M kech (hamshira)"; grades and homework only for pupils
  in class (no mark or a lateness mark); "Saqlash" sends the stored reason back unchanged. Labels:
  "Davomat va baho" → "Baho va vazifa", "Davomat olingan" → "Dars o'tildi" (lesson list, lesson header,
  Bugun tab button and stat card).
- **Teacher PWA** (`/teacher/*`): the cell sheet hides the DAVOMAT block and shows "DAVOMAT (HAMSHIRA):
  <reason>" when the cell has one; the stored reason is sent back.
