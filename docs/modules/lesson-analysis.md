# Dars tahlili (lesson observation & teacher rating) — port SPEC

Status: approved by the client 2026-09-27 ("ok roziman ... juda tezkorlik bilan").
Source system being retired: `../WunderkindEmployeesRating` (FastAPI + React 18),
live at `rating.wunderkindedu.uz` (139.59.157.63). After the data is moved the old
system and its server are shut down (client, 2026-09-27).

**This is a port, not a redesign.** Behaviour, rules, texts and screens follow the old
app one-to-one. The old code is the reference for every detail this document does not
repeat — read it:

| Topic | Old file (in `../WunderkindEmployeesRating/`) |
|---|---|
| Rules & drift notes | `docs/SPEC.md`, `docs/ASSUMPTIONS.md`, `docs/QOLLANMA.md` |
| Scoring / rating / analytics | `backend/app/services/rating.py`, `backend/app/api/routes/dashboard.py` |
| Observation CRUD + permissions | `backend/app/api/routes/evaluations.py`, `backend/app/schemas/evaluation.py` |
| AI summary (prompt verbatim) | `backend/app/services/ai.py` |
| PDF report | `backend/app/services/pdf.py` |
| Excel rating export | `backend/app/services/excel.py`, `backend/app/api/routes/reports.py` |
| Criteria seed (34 rows, 8 sections, colours) | `backend/app/db/criteria_data.py` |
| Response shapes | `frontend/src/types/index.ts` |
| Screens | `frontend/src/pages/*.tsx`, `frontend/src/components/charts.tsx` |

## 1. What is NOT ported (our system already has it — single source of truth)

- Teachers list / CRUD / teacher accounts → our `teachers` table and HR → Xodimlar.
- Panel accounts (ADMIN/MANAGER) → our staff users + a new permission.
- Catalogue (subjects, classes, lesson periods) → our `subjects`, `classes`, `lesson_times`.
- Its own login, password change, profile sheet, theme toggle → ours.

## 2. Roles

| Old role | Here |
|---|---|
| ADMIN | `admin`, `superadmin` |
| MANAGER (observer) | `staff` with permission key **`lessonAnalysis`** (also admin/superadmin) |
| TEACHER | `teacher` role — own COMPLETED observations only, via `/api/teacher/lesson-analysis` |

ADMIN-only in the old app stays admin/superadmin-only here: delete an observation.
Create/patch criteria: admin/superadmin, **or** staff whose role also carries the separate
flag **`lessonAnalysisCriteria`** (client, 2026-09-29: "the academic director has full access
but cannot add criteria — make it assignable by role"). It is a role-editor flag like
`aiAccess`, not a menu page; plain `lessonAnalysis` does NOT open criteria (existing observers
must not silently gain the right to change the school-wide checklist), and the flag alone does
not pass the class gate — full `lessonAnalysis` is still required to write. Editing a COMPLETED observation: its observer or admin/superadmin
(anyone else with `lessonAnalysis` → 403). Drafts: anyone with `lessonAnalysis`.
A teacher never sees drafts (404 even by id), never sees other teachers, never sees a
ranking of colleagues.

## 3. Data model (new tables, EF migration)

String GUID ids like the rest of the schema. `legacy_id int null` on sections, criteria
and observations — the one-time import keys on it (idempotent re-runs).

- `lesson_observation_sections` — id, title, order_index, color, legacy_id
- `lesson_observation_criteria` — id, section_id FK (restrict), text, order_index, is_active, legacy_id
- `lesson_observations` — id, teacher_id FK teachers (restrict), observer_user_id FK users
  (restrict), lesson_date (date), lesson_time text null, class_name text null,
  subject text null, status `draft|completed`, strengths, growth_areas,
  next_lesson_advice, ai_summary (text, the JSON the model returned), ai_error,
  score_done int, score_total int, score_percent numeric(5,2), created_at, updated_at,
  completed_at null, legacy_id
- `lesson_observation_details` — id, observation_id FK cascade, criterion_id FK (restrict),
  is_done bool null (null = unmarked, not scored), comment; unique (observation_id, criterion_id)

Subject / class / lesson time stay **text** (old rule: renaming a class must never
rewrite history). No DELETE for criteria — deactivate (`is_active=false`).

Seeding: the 34 criteria in 8 sections from `criteria_data.py`, inserted at start-up
**only when the sections table is empty** (old rule). The import maps old criteria to
these by (section order, criterion text).

## 4. Scoring (must match the old app exactly — acceptance test)

- `score_total` = details with `is_done` not null; `score_done` = those true;
  `score_percent` = round(done/total*100, 2), 0 when total = 0. Recomputed on every save.
- Rating of a teacher = average `score_percent` over their COMPLETED observations
  (date filter on `lesson_date`), rounded to 2. Only answered details count anywhere.
- PATCH sends every shown criterion; rows with no mark and no comment are dropped;
  answers to criteria deactivated since are kept (old ASSUMPTIONS 2026-09-16).
- `POST complete` on a completed observation is a no-op (no second AI call).

## 5. API

All responses: `Cache-Control: no-store` (old app had a real cross-account leak without
it). Shapes = old `frontend/src/types/index.ts`, camelCase, ids as strings; teacher
fields come from our `teachers` (fullName, subjects joined with ", " as `subject`).

Staff (`[AdminPerm("lessonAnalysis")]`, admin/superadmin pass) — prefix `/api/admin/lesson-analysis`:

```
GET    /criteria?includeInactive=           sections with nested criteria
POST   /criteria                            admin, or staff + lessonAnalysisCriteria
PATCH  /criteria/{id}                       same (text, orderIndex, isActive)
GET    /teachers?q=                         active teachers + observationsCount, rating, lastDate
GET    /teachers/random                     random active teacher
GET    /teachers/{id}                       one teacher + summary numbers
GET    /teachers/{id}/lessons?date=         that teacher's timetable lessons on the date
                                            (period, startTime, endTime, className, subjectName)
                                            — pre-fills the form; new, not in the old app
GET    /observations?teacherId=&status=&dateFrom=&dateTo=&limit=&offset=
POST   /observations                        create draft (+details)
GET    /observations/{id}
PATCH  /observations/{id}
POST   /observations/{id}/complete          finalise + AI (best-effort)
POST   /observations/{id}/ai-summary        regenerate
DELETE /observations/{id}                   admin only
GET    /observations/{id}/report.pdf
GET    /rating?dateFrom=&dateTo=            leaderboard + teachers without observations
GET    /rating.xlsx?dateFrom=&dateTo=
GET    /dashboard                           headline counters
GET    /analytics?teacherId=&dateFrom=&dateTo=
```

Teacher (`[Authorize(Roles = "teacher")]`) — prefix `/api/teacher/lesson-analysis`:

```
GET /observations            own COMPLETED only
GET /observations/{id}       own COMPLETED, else 404
GET /observations/{id}/report.pdf
GET /analytics               scoped to self
```

## 6. AI (Gemini)

Port `ai.py` 1:1: same system prompt (verbatim), same observation text, same JSON
output keys (`umumiy_xulosa`, `kuchli_tomonlar`, `rivojlantirish`, `tavsiyalar`), REST
`generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` with
`HttpClient`. Config `Gemini:ApiKey` (env `Gemini__ApiKey`, never in the repo),
`Gemini:Model` default `gemini-2.5-flash`. Best-effort: any failure → `ai_error`, the
observation still completes; missing key → a clear `ai_error`, no call. Tests never hit
the network.

## 7. Screens

Admin: **Boshqaruv → "Dars tahlili"** (one menu item, group `TA'LIM SIFATI`, perm
`lessonAnalysis`), a page with tabs **Umumiy · Tahlillar · Reyting · Mezonlar**
(Mezonlar editable by admin, or staff with the `lessonAnalysisCriteria` role flag —
`canManageLaCriteria` in `components/lesson-analysis/format.ts`). Routes under `/admin/boshqaruv/dars-tahlili`:
`''` dashboard, `tahlillar`, `tahlillar/yangi?teacherId=`, `tahlillar/:id`,
`tahlillar/:id/tahrir`, `reyting`, `oqituvchi/:teacherId`, `mezonlar`.
Port the old screens' structure, texts and behaviour 1:1 (form tablet-first,
Bajarildi/Bajarilmadi/Izoh per criterion, draft/complete, random teacher, AI summary
block with retry, PDF button, rating board with date range + Excel, dashboard charts
from `charts.tsx`). Visual language = OUR admin panel (tokens/components), not the old
HR theme. Teacher field on the form is our teacher picker; picking a teacher + date
offers that day's lessons from `/teachers/{id}/lessons` to fill class/subject/time
(manual entry stays possible).

Teacher portal: `/teacher/dars-tahlili` (+ `/:id`) — old "Ko'rsatkichim" + own
observations list/detail + PDF. Menu item "Dars tahlilim".

## 8. Data import (one-time, owner: main session)

From the old DB dump: 37 teachers, 2 admins, 6 completed observations, 203 details,
34 criteria (2026-09-27). Teachers matched to ours by phone digits, then by name;
unmatched are reported for a manual decision. Observers (the 2 admins) → our admin
users. Acceptance: every migrated teacher's rating equals the old app's rating-board.

## 9. Cut-over

Backup ours → deploy → import → verify ratings → redirect `rating.wunderkindedu.uz`
→ keep the old dump in two places → shut the old server down only after the client
confirms (irreversible).
