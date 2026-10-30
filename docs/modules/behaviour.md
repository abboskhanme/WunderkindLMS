# Xulq-atvor — behaviour incidents, 1:1 with EduSchool

Status: built 2026-10-15 (client request: "Xulq-atvor moduli … maktab CRM tizimiga 1:1 ko'chirilsin.
Joylashuvi: O'quvchi profili / Kundalik baholash bo'limi"). Gap analysis: `existing-module-gaps.md` §6,
`students-parity.md` §2.2.1 (whole-class incident) and §2.3 (profile tab).

## Why

In EduSchool the teachers record most incidents — 15 of the 16 homeroom teachers did last year. Ours
had the data model, the rating and a read-only feed, but only the office could write, nothing could be
edited, and the profile tab and the parent app showed nothing useful. "Kundalik baholash" is not a
screen name we have: it is read as the teacher's daily lesson screen (Mini App "Jurnal" tab) and the
pupil profile.

## Model (unchanged where it already matched)

| EduSchool | Ours |
|---|---|
| Sabab `{ name, description, points, type: positive \| negative, isActive }` | `discipline_reasons` — the **type is the sign of `points`** (no column, `DisciplineType`); screens ask for "Ijobiy / Salbiy" and a positive number |
| Harakat `{ studentId, behaviorId, points, comment, createdBy, createdAt }` | `discipline_points` — reason name and points are a snapshot; new: `created_by_user_id`, `updated_at`, `updated_by` |
| Reyting (0-based) | **100 + Σ** — manual points plus attendance-reason points once per pupil, day and reason (`AttendanceDisciplineDays`); kept |
| SMS template per reason | Telegram only: `notify_parent` per reason (`DisciplineParentNotifier`), off by default |

Score = 100 + Σ everywhere: Ballar nazorati, the profile overview, the profile sidebar (it used to show
the raw Σ — fixed), the teacher screen and the parent card.

**Start day** (client, 2026-10-30: "yangi o'quv yili uchun hisob yangidan boshlanadi"):
`school_meta.discipline_score_since` ("yyyy-MM-dd", '' = whole history). Only manual points with
`created_at >=` it and attendance days with `date >=` it count — in every score above, the attendance
report's "Qoldi" and the MCP tool. Nothing is deleted: the Harakatlar feed, the admin history and the
profile list keep every entry; the parent card and the teacher sheet list only the counted ones so the
number adds up. Set on Ballar nazorati → "Hisob: … O'zgartirish" (`GET/PUT /api/admin/discipline/score-since`,
full `discipline` key, not in the future, audited). Set on prod to 2026-10-30 — everyone at 100.

## Office (`[AdminPerm("discipline")]`, staff read, full key writes)

- `POST/PUT /api/admin/discipline/reasons` — optional `type` (`positive|negative`): stored points are
  `|points|` with the type's sign, 0 refused. Without `type` (older client) points are stored as sent.
- `PUT /api/admin/discipline/points/{id}` `{ reasonId, note }` — **points follow the reason**: a new
  reason re-snapshots name and points and must be active; the same reason keeps the stored snapshot (a
  re-priced reason does not rewrite history). Archived pupil → 409. Sets `updated_at/by`. Audited
  (`DisciplinePoint`, `update`, before/after) — visible in the pupil's "Harakat tarixi". No parent
  message on an edit.
- `DELETE /api/admin/discipline/points/{id}` — now audited (`delete`, with the snapshot).
- `GET /api/admin/discipline/feed/export` — the Harakatlar feed as .xlsx with the screen's filters, no
  paging: Sana, F.I.SH., Sinf, Turi, Sabab, Ball, Izoh, Kim qo'ydi, Manba.
- `GET /api/admin/discipline/coverage?from=&to=` — EduSchool `getNotAssignedClasses`: every class with
  active pupils and its count of manual incidents in the period (default: 1st of this month → today);
  uncovered classes first. Attendance points do not count (that is the nurse's roll-call).
- New entries (single and whole-class) carry `created_by_user_id`.

## Teachers (`/api/teacher/discipline`, role `teacher` + the `journal` key)

| Endpoint | What |
|---|---|
| `GET reasons` | active behaviour reasons with `type`, points, description, `notifyParent` |
| `GET scores?ownerId=` | every pupil of the lesson roster: `remaining` (100-based) and `today` (manual points today, all authors) — one call for the badges |
| `GET points?studentId=` | score, today's points, the last 20 entries (manual and attendance), `canDelete` on the teacher's own entries made today |
| `POST points` `{ studentId, reasonId, note }` | records at once; author name and user id from the token; active behaviour reason only (attendance reasons → 400); archived pupil → 409; Telegram to the parents when the reason has `notifyParent`, as for the office |
| `DELETE points/{id}` | own entry, same day only — otherwise 403; audited |

**Reach** (`DisciplineIncidents.TeacherReachesAsync`): the teacher reaches the pupil's class (homeroom or
any lesson there) or one of the pupil's active groups (`TeacherOwnerAccess.ReachesAsync`), or covers a
lesson of that class or group **today** (substitution). Anyone else → 403. The roster endpoint uses the
journal's rule for the owner.

## Screens

- **Ball sabablar** — "Ijobiy / Salbiy" selector, points typed as a positive number, a Turi column and a
  Barchasi / Ijobiy / Salbiy filter. Attendance reasons keep their signed number (0 = no effect).
- **Harakatlar** — "Harakat qo'shish" (pupil search, type, reasons of that type, note), edit and delete on
  manual rows, "(tahrirlangan)" marker, Ijobiy/Salbiy filter, Excel of the filtered feed, and the
  "Belgilanmagan sinflar: N / M" card for the chosen period with the list of those classes.
- **Sinflar → sinfga ball** — type first, then the active reasons of that type.
- **O'quvchi profili → Xulq-atvor** — score, type, points, reason, note, author, time; add / edit /
  delete for the full `discipline` key (read-only otherwise). Sidebar "Odob (intizom)" = the same score.
- **Mini App, teacher lesson screen** — each pupil row has a compact "Xulq" chip with the current score
  and a badge of today's points; it opens a bottom sheet: Ijobiy / Salbiy tabs, reason chips with
  points, optional note, "Qo'yish" saves at once (independent of the lesson's "Saqlash"), the last
  entries with delete on the teacher's own entries of today.
- **Mini App, parent home** — "Xulq-atvor" card: the score (100-based, +/−) and the latest entries
  (type, reason, points, date) from `/api/student/discipline`; never hidden.
- The old teacher PWA got nothing (optional, not trivial).

## Not done here

- ~~Importing EduSchool incidents~~ — **done 2026-10-17**, see "EduSchool import" below.
- The per-lesson `journal_entries.behavior` flag (0/1/2) stays as it is; it does not move the score.

## EduSchool import (2026-10-17)

Client: "xulq atvorni o'ziniyam sabablariniyam eduschooldan ko'chirish kerak". Read-only GETs through the
Chrome extension (`moderator-api/behaviors/pagin`, `behavior-incidents/pagin`, `behavior-rating/pagin`, both
academic years); nothing was written to EduSchool.

- EduSchool's 2026–2027 year has **no** incidents; 2025–2026 has 11 254 (06.03.2026 – 07.06.2026), all
  entered by teachers.
- **Reasons:** 73 = the 44 current ones (`is_active = true`) + 29 deleted ones still referenced by old incidents
  (`is_active = false`). Type → sign of `points`; `notify_parent = false` everywhere.
- **Incidents:** 11 221 copied to `discipline_points` for 525 pupils — `points` is EduSchool's `pointsAwarded`
  with the reason's sign, `note` its `notes`, `created_at` the Tashkent wall time, `created_by` the teacher's
  name, `created_by_user_id` the LMS user when the name matched (40 of 51 authors). Pupils matched by full
  name (524 exact + one EduSchool typo by hand); 33 incidents of 3 pupils not in LMS (one a test record) skipped.
- **Score:** EduSchool keeps a pupil's points into the new year, and its 2025–2026 `lifePoints` = 100 + Σ
  incidents for 734 of 738 pupils — our formula. After the import our score equals EduSchool's year-end
  score for 511 of 515 matched pupils (the 4 others disagree inside EduSchool too).
- Ids are uuid5 of the EduSchool ids (namespace `5d1f3c8a-7b2e-4f60-9a4d-e2c81b7f0a35`; generator in the git-ignored `docs/import/behaviour_2026_10_17/`), every
  insert `ON CONFLICT DO NOTHING` — a re-run adds nothing. Undo = delete those ids. Backup before the run:
  `/backups/pre-behaviour-import_20261017-091709.dump` in the prod DB container.
