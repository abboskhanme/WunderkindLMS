# Track (yo'nalish) groups work like classes — 2026-09-26

Client (Uzbek, 2026-09-26): grades 1–8 study as classes. Grades 9, 10, 11 are still assigned to a
class (9-A …) but study **all** their lessons in **track groups** ("yo'nalish guruhlari": Aniq
fanlar, Filologiya, Ijtimoiy, Iqtisodiyot, Tabiiy fanlar). Creating track groups already works
(`study_groups.is_track`). What is missing: a track group must behave like a class in the
timetable, attendance and journal. Confirmed answers:

1. **All** lessons of grades 9–11 are in track groups. Their home classes (9-A …) keep existing for
   documents, finance, contracts, reports — but in timetable / attendance / journal pickers and
   lists they are replaced by the track groups.
2. **Journal too**: grades and lesson attendance are kept per track group lesson.
3. Timetables are entered through the existing "Dars jadvali yaratish" screen (track groups must be
   selectable there). No Excel upload feature. (The client will send the current track timetables
   as Excel; they will be entered separately — not part of this build.)
4. **A track group has no single subject.** The "one subject per group" rule is only for ordinary
   groups (e.g. English split groups). A track group teaches many subjects; each lesson carries its
   own subject.

## Current state (read before designing)
- `StudyGroup` (SchoolLms.Domain/StudyGroups.cs): `SubjectId` required, `(id, subject_id)` is the
  target of a composite FK from `study_group_members` ("a pupil is in one group per subject").
  `IsTrack` exists; used today only by boarding (evening/dorm) attendance.
- `ScheduleLesson.OwnerKind` = `class` | `group`: group-owned lessons store the group id in
  `class_id`. Journal, lesson attendance, week assignments, schedule utils, analytics etc. already
  understand group owners (`LessonRoster`, `owner.IsGroup`), but everything is behind the cut-over
  switch `school_meta.group_lessons_enabled` (prod: **false**; prod has 5 active groups, all tracks).
- Frontend: `api/services/groupLessons.ts` (switch), schedule pages (`SchedulePage`,
  `ClassScheduleViewPage`, `MasterScheduleGrid`, `TeacherSchedulePage`), `classes/ScheduleBoard.tsx`,
  `LessonSlotModal.tsx`, journal service already pass `ownerKind`.
- Admin daily attendance marking: `DailyAttendanceService` + `pages/admin/attendance/DailyMarkingPage.tsx`
  — class-only today (`e.OwnerKind == LessonOwnerKind.Class`).

## Required behaviour
- **Subject optional for track groups:** schema + service + form. Ordinary groups keep the
  one-subject rule unchanged. A pupil may be in at most one active track group. Keep existing
  data valid (the 5 prod track groups currently carry an arbitrary subject — it may stay or be
  cleared by the migration; do not lose members).
- **Track groups are lesson owners independent of the global switch** — i.e. group-owned lessons of
  a *track* group work even when `group_lessons_enabled = false` (ordinary groups keep obeying the
  switch). Document the rule where `GroupLessonsEnabledAsync` is checked.
- **Ordering / replacement rule used everywhere a class list is shown for timetable, attendance and
  journal:** classes of grades that feed a track group (via `study_group_classes` of an active track
  group) are hidden; the list is: ordinary classes by grade/name (1–8), then active track groups by
  name. Class list page (O'quv bo'limi → Sinflar) keeps all classes and shows the track groups at the
  end as a separate block linking to the group.
- **Screens:** Dars jadvali (view, all classes then track groups), O'qituvchi jadvali (group lessons
  shown with the group name), Dars jadvali yaratish (track groups selectable as owners, conflict
  checks keep working — a teacher / room / pupil clash across a class and a track group is a clash),
  Davomat belgilash (DailyMarkingPage: track groups in the list, marking by the group roster, "Barcha
  darslar" mode too), Kunlik davomat / analytics lists, Jurnal (admin + teacher panel + Mini App
  teacher panel if it lists classes), pupil timetable (portal/Mini App) shows the track lessons.
- Everything role/permission-wise as today (AdminPerm sections unchanged); `ViewAccessSweepTests`
  stays green.

## Verification
- Backend tests for: subject-optional track group create/update; one-track-per-pupil; track lessons
  visible with switch off, ordinary group lessons hidden with switch off; daily attendance list order
  and marking a track lesson; journal on a track lesson; the class-hiding rule; migration up/down.
- Full suite green; client `tsc -b`, eslint on changed files, `vite build` green.

## Gamification and block tests by track — 2026-10-30

Client (Uzbek, 2026-10-30): pupils of grades 9–11 study by track, mixed across grades; points and
monthly block tests must work per track too. Decisions: **one block test per track** (each track has
its own subjects and per-subject points), home classes (9-A …) stay for documents.

- **Ballar nazorati:** each row carries `trackName` (the pupil's active track group); the class filter
  also lists the tracks ("Yo'nalishlar"), the Excel export has a "Yo'nalish" column.
- **Whole-group points:** `POST /api/admin/discipline/points/class` accepts a track group id in
  `classId` — points go to its active, non-archived members. Ordinary groups → 404. Button
  "Yo'nalishga ball qo'yish" on the track rows of O'quv bo'limi → Sinflar. Teachers already reach
  track pupils through their lessons (`TeacherReachesAsync` → group membership).
- **Block test participants:** `POST /api/admin/exams/{id}/participants` takes `groupIds` (track groups
  only, else 404 `group_not_found`); the exam form lists the tracks after the classes. The class
  snapshot stays the pupil's home class.
- **Results:** rows carry `trackName` — the track the pupil was a member of **on the exam date**
  (dated membership), else the current one; `groupId` filter (pupils who ever were in that track);
  Excel column "Yo'nalish". Shared helper: `TrackMembership` (Application/Services).
- No migration. Tests: `TrackGamificationExamTests`.

## DTM-shaped block tests and certificate credit — 2026-10-31

Client: *"yo'nalishlar xuddi DTM imtixonidek test topshirishadi … 1-blok 3,1 / 2-blok 2,1 / 3-4-5 bloklar
1,1 ball … umumiy 189 ball … Sertifikati bor o'quvchilar sertifikat olgan fanidan imtixondan ozod
qilinadi va sertifikat natijasiga teng ball avtomatik qo'shib beriladi."*

- **Section = questions × points per correct answer.** A manual section with `question_count` and
  `points_per_correct` gets their product as its ceiling (30 × 3.1 = 93). Without them — a plain
  ceiling, as before.
- **"DTM shabloni (189 ball)"** in the form fills five blocks: block 1 30 × 3.1, block 2 30 × 2.1 (the
  track's subjects — picked by hand), blocks 3–5 10 × 1.1 (native language, maths, history — matched by
  name). One test per track; only blocks 1–2 differ.
- **Entry:** a DTM cell takes the **number of correct answers**; the server computes the points and
  keeps `exam_section_scores.correct_count`. The Excel import still takes points.
- **Certificate credit:** `certificate_types.exam_credits` — a "level=percent" table (`A+=100; A=93.3`),
  or for a scored type "minimum score=percent" (`7=100; 6.5=90`, the highest threshold reached). A pupil
  holding a certificate in the section's subject that is valid on the exam day (issued ≤ day ≤ expiry)
  gets a locked cell worth `ceiling × percent`; `exam_section_scores.certificate_id` records which
  document. The credit is written when the row is saved (grid or import); a value sent for that cell is
  ignored. Results show such a subject in violet with the certificate name.
- Migration `ExamDtmCertificateCredits`. Tests: `ExamDtmCreditTests`.
