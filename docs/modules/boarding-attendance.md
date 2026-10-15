# Evening-lesson and dormitory attendance

Client request, 2026-09-23. Some pupils (senior "track" groups, grades 9–11 mixed) stay in the
dormitory and attend organised evening lessons, so attendance is taken three times a day by three
different people: daytime lessons (existing journal), the evening lesson and the dormitory check.

## Decisions (client answers, 2026-09-23)

| Question | Answer |
|---|---|
| Evening lesson | One roll call per day per track group (no subjects, no periods) |
| Grouping | By track group (`study_groups.is_track`); a boarder in no track group is listed under "Guruhsiz · <class>" |
| Who is expected | Only pupils with an **active dormitory subscription** that day (`fee_categories.code = 'dormitory'`, `starts_on <= day <= ends_on`). Evening lessons are attended only by boarders |
| Pupils without it | Shown greyed, cannot be marked, counted nowhere, rejected by the server |
| Statuses | Evening: keldi / kelmadi / sababli. Dorm: joyida / yo'q / ruxsat bilan (stored as present / absent / excused) |
| Parent notice | Telegram on absent, both sessions, once per absence (`notified_at`) |
| Track groups | No new structure: existing study groups get an "Yo'nalish guruhi" flag |

## Model

- `study_groups.is_track boolean not null default false`
- `boarding_attendance(id, date, session['evening'|'dorm'], student_id → students, status['present'|'absent'|'excused'], marked_by, marked_at, notified_at)`, unique `(date, session, student_id)`

The daytime journal is not touched: lesson attendance %, discipline points and teacher pay keep
reading only `journal_entries`.

## API

- `GET  /api/admin/boarding-attendance?date=YYYY-MM-DD&session=evening|dorm` → sections
- `PUT  /api/admin/boarding-attendance` `{ date, session, marks: [{ studentId, status }] }`

Permission per session, **reads included**: `attendanceEvening`, `attendanceDorm` (admin and
superadmin always). Future dates are refused.

## UI

`Davomat → Kechki dars` / `Davomat → Yotoqxona` (`/admin/attendance/boarding?session=…`). The
Davomat menu is now gated per child, so an evening/dorm keeper sees only their own entry.

## Female and male commandants (2026-10-15)

Client: *"Yotoqxona o'quvchilarini davomat qilish uchun o'g'il bolalar va qiz bolalarga ajratish — ayol va erkak
komendant davomat qila olishi uchun."* The dorm day was already grouped girls / boys (2026-10-13); now a commandant
can be limited to one half.

- Boshqaruv → Rollar: when the «Yotoqxona» page is granted, "Yotoqxona: kimni belgilaydi" — Hammasi / Faqat qizlar /
  Faqat o'g'il bolalar. It saves a narrowing flag next to `attendanceDorm`: `dormGirlsOnly` or `dormBoysOnly`
  (`DormGender` in Domain). Both or none = the whole dorm; admin and superadmin always see the whole dorm.
- The flag applies everywhere on `BoardingAttendanceController` for the dorm session: the day list, saving (a pupil of
  the other half → 400 "… sizning bo'limingizda emas"), the roster and its candidates, add / remove (the other half
  → 400 / 404), and the statistics with its Excel.
- Teachers who are commandants get the flag through the same role (`TeacherPermissions.RoleKeys`); the Mini App
  then shows and saves only their half without any change of its own.
- Handing out a role with a flag needs no extra right (`RoleAssignment.Within`, client `withinUserRights`): it only
  narrows. Pupils with no gender on file stay with whole-dorm holders.
