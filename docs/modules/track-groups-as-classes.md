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
