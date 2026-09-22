# Module spec — Admission, Block Test and Seasonal Assessment

Status: **specification only. No code, no migration, no entity, no page.**
The schema is assembled centrally by the coordinator; this file is an input to that.

Scope covered here (EduSchool equivalents in brackets):

| Area | EduSchool module | EduSchool permission |
|---|---|---|
| Admission pipeline + entrance test | `ADMISSION` → `CANDIDATES`, `TEST_BASE` | `getLeads` |
| Block test | `BLOCK_TEST` → `EXAM`, `EXAM_TYPE`, `RESULT` | `getBlockTest`, `getBlockTestType`, `getBlockTestResult` |
| Seasonal assessment | `EXAMINATIONS` → `MONTHLY_ASSESSMENT`, `SEASIONAL_ASSESSMENT_BY_SUBJECTS` | `getSeasonalMarks` |
| Seasonal assessment report | `ANALITICS_ALL.MONTHLY_ASSESSMENT_REPORTS` | `getTeacherWorkReport` |
| Public entrance test page | route `/qabul-test/:token` | **none — public** |

---

## 0. Evidence and confidence

Everything below marked **[bundle]** was read out of `.eduschool-bundle/all.js`
(the client's own minified frontend). Everything marked **[ours]** was read out of
this repository. Everything marked **[assumption]** is a decision I made because
the evidence does not exist; each one says why.

**What the bundle does contain**

* The complete public exam page — `/qabul-test/:token`, its four API calls,
  its five states, its timer, its device lock, its result view. Offsets
  ~7 736 000–7 772 000 in `all.js`.
* The complete Test bazasi admin screens — list, form, detail, question form,
  Excel import wizard. Offsets ~1 053 000–1 077 000.
* The complete Mavsumiy baholash screens — list, bulk entry form, filters,
  mark-history dialog. Offsets ~2 151 000–2 172 000.
* The complete Mavsumiy baholash hisoboti (report) screen. Offset ~1 808 000.
* The `by-subjects` pivot screen. Offset ~8 105 000.
* The block-test **router** (`data-testid="block-test-page"`, offset 2 522 806)
  which names its four child chunks.
* Every API path constant, every route constant, the whole RBAC registry and
  the whole menu tree.

**What the bundle does NOT contain** — the concatenation captured 64 lazy chunks,
and these four were not among them:

* `index-f-qucIpe.js` — the **Nomzodlar** (candidates) screens.
* `ExamTypePage-*.js`, `ExamPage-*.js`, `ResultListPage-*.js`,
  `ResultEntryPage-*.js` — all four **Blok Test** screens.

Proof: `admission.styles-DDfZNX0S.js` is imported exactly once (by the Test bazasi
chunk) and `exam.types-BmfX02HW.js` zero times; no `admission.candidate.*` or
`blockTest.*` i18n key exists in the file. So for Nomzodlar and Blok Test we have
**the API surface and nothing else**. Their field lists below are marked
**[assumption]** and were derived from the endpoint names, the route names and
the shape of the neighbouring screens we *can* read.

---

## 1. Goal

Give the school one place to run entrance admission end to end — a candidate list
that grows out of the existing lead pipeline, a reusable question bank, an online
entrance test a candidate takes from a link without an account, and the resulting
score feeding the decision to enrol — and one place to record the periodic
(monthly / quarterly / yearly) subject assessment that today has nowhere to live.

Two things must be true when this ships that are not true in EduSchool:
the question bank must not be readable by every staff account, and the correct
answers must not be handed back to the candidate.

---

## 2. The four decisions

These are the questions the coordinator asked. Each is answered once, here, and
the rest of the document follows from the answer.

### 2.1 One question bank, not two

**Evidence [bundle].** EduSchool has exactly one bank. `/admission/question*`
hangs off an admission *test base*; the entire `/block-test/*` surface is

```
/block-test/type            /block-test/type/pagin
/block-test                 /block-test/pagin        /block-test/cancel
/block-test/{id}/details
/block-test/result/pagin    /block-test/result/entry-table
/block-test/result/bulk     /block-test/result/import-file/confirm
/block-test/result/export?blockTestId={id}
```

There is no question, no option, no attempt and no answer anywhere under
`block-test`. The frontend route list confirms it: `exam`, `exam-type`, `result`,
`result/:examId/entry` — an *entry* screen, i.e. a grid somebody types scores
into. **Blok Test in EduSchool is a paper exam whose marks are keyed in or
imported from Excel.** The only online test is the admission one.

**Decision.** We build **one** bank, **one** delivery engine and **one** scoring
engine, with two ways in:

* `delivery = 'online'` — the engine serves questions and grades them. Used by
  admission today.
* `delivery = 'manual'` — no questions; staff type per-subject points into the
  entry grid, or import them. Used by block test today.

Both write the same `exam_participants` score summary and the same
`exam_section_scores` rows, so **Natijalar**, the export and every report are one
implementation.

**Consequence — do not build EduSchool's split.** There is to be no
`block_test_questions` table and no second scoring path. A block test that the
school later wants to run on screen becomes `delivery='online'` on an existing
row; only the student-facing page is missing, and that page is an explicit
non-goal (§9.3).

**The third bank we already have, and are not touching.** `TestQuestion` **[ours,
`SchoolLms.Domain/Entities.cs:852`]** belongs to `Assignment` where
`Format='test'` and `AutoGrade=true` — the homework quiz in the pupil mobile app.
It stays exactly as it is. Merging it into the new bank is a rewrite of a working
module and is forbidden by the project rules. A later, separately-requested task
may add "pull questions from the bank into an assignment"; nothing in this spec
depends on it.

### 2.2 A candidate is a lead, not a new person

**Evidence [bundle].** EduSchool's Qabul module — *both* entries — is gated by the
literal permission `getLeads`, and its candidate has exactly two displayed
fields, `fullName` and `grade` (0–11), which are `Lead.FullName` and
`Lead.TargetGrade`. Their leads carry `studentId` (`/leads/set-class`,
`/leads/archive-student`, `DELETE leads/{id} {archiveStudent:bool}`), so a lead
already becomes a pupil in their model. They nevertheless keep a separate
`/admission/candidate` table — almost certainly because their `leads` row is
heavy (custom fields, timeline, tasks, responsible, funnel).

**Decision.** **No new person table.** A candidate *is* a row in `leads`, in a
later phase of its life. We add two columns to `leads` and one new table that
points at `leads.id`:

```
leads.admission_status  text not null default 'none'
```

> **Changed 2026-09-22 (client decision, already built):** there is **no
> `leads.student_id`**. `POST /api/admin/leads/{id}/enrol` exists (in
> `StudentsController`) and **deletes the lead** on enrolment; only a statistic row
> survives in `lead_conversions` (`converted_at`, `source`, `survey_id`). So
> `enrolled` is never a stored `admission_status` — an enrolled candidate is simply
> no longer a lead. Candidate history after enrolment (which exam, what score) must
> therefore be kept on `exam_participants` with `lead_id` set to NULL on delete, not
> on the lead. Do not re-add `student_id` to `leads`; do not rebuild the enrol endpoint.

`admission_status ∈ { none, invited, testing, tested, accepted, rejected, enrolled }`.

Why a column and not "these stages are the candidate stages": `lead_stages` are
user-editable — the client renames and reorders them from the board — so a
stage-name rule would silently break the day somebody renames a column.

Why not a separate `candidates` table: the same human would then exist in two
tables, and every question ("has Aziza already been tested?", "did the lead
convert?") would have two answers that drift.

**Trade-off, stated plainly.** EduSchool's separate table lets them show a
candidate list with columns that leads do not have. Ours will need those columns
too (test score, invitation state) — they come from a join to
`exam_participants`, not from `leads`. That join is the price of not duplicating
the person, and it is the right price.

### 2.3 The Leads board is frozen — Nomzodlar is a new page

`CLAUDE.md` (repo root, "PROTECTED DESIGN: the Leads board", 2026-09-13) protects
these six files **[ours]**:

```
schoollms.client/src/pages/admin/leads/LeadsPage.tsx
schoollms.client/src/pages/admin/leads/LeadColumn.tsx
schoollms.client/src/pages/admin/leads/LeadCard.tsx
schoollms.client/src/pages/admin/leads/LeadDetailModal.tsx
schoollms.client/src/pages/admin/leads/LeadFormModal.tsx
schoollms.client/src/pages/admin/leads/StageFormModal.tsx
```

**Binding rules for every agent who implements this spec:**

1. **Do not open those six files to change how they look.** No restyling, no
   spacing, no colour, no interaction change, no "let's align it with
   EduSchool", no extracting `LeadCard` into a shared component so Nomzodlar can
   reuse it. Fixing a genuine bug is allowed; changing the appearance is not.
2. **Nomzodlar is a new page at a new route** (`/admin/admission/candidates`), a
   **table**, not a board. It reads leads; it does not render `LeadColumn`.
3. The two new `leads` columns are **additive**. `LeadCard.tsx` renders
   `fullName`, `parentPhone`, `targetGrade` — adding columns to the row does not
   change it. Do not add an admission badge to the card.
4. `LeadsController.cs` **[ours]** is not protected and may be extended
   additively (§6.1). The board's five calls (`GET`, `POST`, `PUT`, `PATCH`,
   `DELETE /api/admin/leads`) and their exact response shapes must not change —
   `schoollms.client/src/api/services/leads.ts` depends on them.

**Observation, for the record.** EduSchool's candidate flow does imply a
different board: their lead card opens a detail pane with a timeline, tasks,
calls, contracts and a class-assignment control, and Qabul is a second, tabular
view of the same pipeline. Our board is deliberately simpler and the client likes
it that way. **Recommendation: keep ours.** Everything the admission flow needs
lives on the new candidate page and the new candidate card; the board keeps doing
the one thing it does well.

### 2.4 Seasonal assessment is new storage; its report is not

**Evidence [bundle].** `seasonal-mark` carries a **0–100 score** (`min:0`,
`max:100`, `step:"0.1"`) plus a free-text comment plus an edit history, keyed by
`(student, class, subject, type, year, month|quarter)` where
`type ∈ {monthly, quarterly, yearly}`.

**Evidence [ours].** Nothing we have holds that.

* `JournalEntry.Grade` is 1–5 per lesson; `JournalEntry.Mastery` is 0–100 but
  per lesson, not per period.
* `QuarterGrade.Grade` is 2–5 per (class, subject, quarter, student).
* `EvaluationGrade.Score` is 1–5 per (student, subject, evaluation type, month) —
  the "Feedback" feature, a different instrument with a different scale.

**Decision.** One new table, `seasonal_marks` (§5.12). It reuses `quarters`
(`QuarterPeriod` **[ours]**) for the quarterly variant and stores nothing that
`journal_entries` already stores.

**And the report is a report.** `ANALITICS_ALL.MONTHLY_ASSESSMENT_REPORTS`
**[bundle]** returns, per teacher: `totalStudents`, `seasonalMarks`,
`uncheckedSeasonalMarks`, and `seasonalMarksPercent = min(seasonalMarks /
totalStudents × 100, 100)`, with a drill-down that filters
`hasSeasonalMark = true|false`. Every number is derivable. **No table.**
Likewise `SEASIONAL_ASSESSMENT_BY_SUBJECTS` is a pivot of `seasonal_marks` —
**no table.** Both are listed as reports in §10.

**Boundary — not in this spec.** `ANALITICS_ALL.SEASONAL_APPROPRIATION` and
`…_BY_SUBJECTS` (`/analitics/students/quarter`) are *o'zlashtirish* — achievement
computed from journal marks, with school settings
`formativeAssessmentPercent` + `summativeAssessmentPercent` (they sum to 100) and
`quarterlyGradeSource` **[bundle]**. Different feature, different agent. If you
are implementing this spec and find yourself computing an average of
`journal_entries.grade`, you have crossed the line.

---

## 3. Screens

Permission keys are ours (§4), not EduSchool's.

### 3.1 Admin panel — Qabul

| # | Screen | Route | Perm | Notes |
|---|---|---|---|---|
| 1 | Nomzodlar | `/admin/admission/candidates` | `admission` | Table over `leads` where `admission_status <> 'none'`. Columns: FISH, sinf (target grade), ota-ona telefoni, holat, imtihon, ball, taklif havolasi holati, amallar. Filters: `admissionStatus`, `grade`, `examId`, `search`. Row actions: kartochka, imtihonga biriktirish, taklifni qayta yuborish, bekor qilish. Bulk action: tanlanganlarni imtihonga biriktirish. |
| 2 | Nomzod kartochkasi | `/admin/admission/candidates/:leadId` | `admission` | Left: lead data (read-only; edit goes to the board). Right: exam assignment, invitation state (`issued / opened / in progress / finished / revoked / expired`), the link with a "Nusxalash" button shown **only immediately after issue** (§7.3), attempt device + IP, the result with per-subject bars and the per-question review. Actions: "Imtihonga biriktirish", "Havolani qayta chiqarish", "Biriktirishni bekor qilish", "Natijani qayta hisoblash", "O'quvchi qilib ro'yxatga olish". |
| 3 | Test bazasi | `/admin/admission/banks` | `admission` **(read gated, §4.3)** | Question-bank list. Columns **[bundle]**: sinf, fan, bankdagi savollar soni, ball (`pointsPerCorrect`), testdagi savollar (`questionsPerTest`), vaqt (`timeLimitMin`), holat. Holat is derived, not stored **[bundle]**: no `questionsPerTest` → `unconfigured` (grey); `questionsCount < questionsPerTest` → `notEnough` (amber); otherwise `ready` (green). |
| 4 | Bank tafsiloti | `/admin/admission/banks/:bankId` | `admission` **(read gated)** | Settings strip (3 numeric fields + Saqlash), warning banner when `notEnough`, question cards with A–F options and the correct one highlighted, search box filtering by question text client-side, "Savol qo'shish", "Excel'dan import". |

### 3.2 Admin panel — Imtihonlar (block test)

| # | Screen | Route | Perm | Notes |
|---|---|---|---|---|
| 5 | — | `/admin/exams` | `exams` | Redirect to `/admin/exams/list` **[bundle: their router redirects `index` → `exam`]**. |
| 6 | Imtihonlar | `/admin/exams/list` | `exams` | Exam list + create/edit drawer. Create defines: nomi, turi (`exam_types`), sana, sinflar, fanlar and each fan's `max_score`. Actions: tahrirlash, bekor qilish (`status='cancelled'`), natija kiritishga o'tish. |
| 7 | Imtihon turi | `/admin/exams/types` | `exams` | Flat CRUD list: nomi, izoh, faolmi. |
| 8 | Natijalar | `/admin/exams/results` | `exams` | Cross-exam result list, paged. Filters: `examId`, `classId`, `subjectId`, `search`. Default filter `status=finished` **[bundle: the menu prefetches `/block-test/pagin?status=completed`]**. Export to `.xlsx`. |
| 9 | Natija kiritish | `/admin/exams/results/:examId/entry` | `exams` | The grid. Rows = participants, columns = the exam's subjects, cells = points (0…`max_score`). Save-all in one request. Excel template download + import. |

### 3.3 Admin panel — Mavsumiy baholash

| # | Screen | Route | Perm | Notes |
|---|---|---|---|---|
| 10 | Mavsumiy baholash | `/admin/seasonal-marks` | `seasonalMarks` | Paged list. Columns **[bundle]**: o'quvchi, sinf, tur, davr, fan, ball, izoh. Ball and izoh are **inline-editable in the cell** (popover with a number input 0–100 / a 3-row textarea, saved on blur or Enter). Row actions: tarix, o'chirish. Filters: tur, yil, oy (when `monthly`), chorak (when `quarterly`), sinf, fan, o'quvchi. Export. |
| 11 | Baholash (kiritish) | `/admin/seasonal-marks/entry` | `seasonalMarks` | Bulk form **[bundle]**: choose tur → davr → sinf → fan, the student list loads with any existing score prefilled, one Saqlash writes them all. |
| 12 | Fanlar kesimida | `/admin/seasonal-marks/by-subjects` | `seasonalMarks` | Pivot: rows = pupils, columns = the selected subjects, cells = score. Requires tur + davr + ≥1 sinf + ≥1 fan before it queries **[bundle: `params` stays `null` until all four are set]**. Export. |
| 13 | Mavsumiy baholash hisoboti | `/admin/seasonal-marks/report` | `seasonalMarks` | Per-teacher coverage. Columns **[bundle]**: FISH, jami o'quvchi, baholangan, baholanmagan, foiz. The first three are links that drill into a filtered pupil list. Export. |

Placement note: EduSchool files this report under Analitika with permission
`getTeacherWorkReport` **[bundle]**; our nearest key is `teacherReports`. I keep
it under `seasonalMarks` and at `/admin/seasonal-marks/report` so the whole
feature is one module owned by one agent and one permission. A read-only link to
it from `/admin/teacher-reports` is a one-line follow-up, not part of this spec.

### 3.4 Teacher panel

| # | Screen | Route | Perm | Notes |
|---|---|---|---|---|
| 14 | Mavsumiy baholash | `/teacher/seasonal-marks` | teacher perm `seasonalMarks` | Screens 10 + 11 restricted to the (class, subject) pairs the teacher actually teaches. **[bundle: their non-moderator route table contains `/monthly-assessment/*` with `role:"getSeasonalMarks"` — teachers do enter these marks.]** |

### 3.5 Public

| # | Screen | Route | Perm | Notes |
|---|---|---|---|---|
| 15 | Qabul testi | `/qabul-test/:token` | **none** | §7. Registered as a **sibling** of `/login`, outside `ProtectedRoute`. |

### 3.6 Navigation

Added to `navByRole.admin` in `schoollms.client/src/config/navigation.ts`
**[ours]**, placed after `Jurnal` and before `Xabarlar`:

```
{ label: 'Qabul',              to: '/admin/admission/candidates', icon: UserRoundCheck, perm: 'admission', children: [
    { label: 'Nomzodlar',      to: '/admin/admission/candidates', end: true },
    { label: 'Test bazasi',    to: '/admin/admission/banks' } ] }

{ label: 'Imtihonlar',         to: '/admin/exams/list',           icon: ClipboardCheck, perm: 'exams', children: [
    { label: 'Imtihonlar',     to: '/admin/exams/list' },
    { label: 'Imtihon turi',   to: '/admin/exams/types' },
    { label: 'Natijalar',      to: '/admin/exams/results' } ] }

{ label: 'Mavsumiy baholash',  to: '/admin/seasonal-marks',       icon: BarChart3,      perm: 'seasonalMarks', children: [
    { label: 'Baholar',        to: '/admin/seasonal-marks', end: true },
    { label: 'Fanlar kesimida',to: '/admin/seasonal-marks/by-subjects' },
    { label: 'Hisobot',        to: '/admin/seasonal-marks/report' } ] }
```

Added to `navByRole.teacher`, after `Feedback`:

```
{ label: 'Mavsumiy baholash', to: '/teacher/seasonal-marks', icon: BarChart3, perm: 'seasonalMarks' }
```

The three icon names are chosen because they are already imported and building
in this project (`UserRoundCheck` in `components/layout/NotificationsBell.tsx`,
`ClipboardCheck` and `BarChart3` in `config/navigation.ts` **[ours]**). Pick a
different `lucide-react` name only after confirming it exists in the installed
version — the icon is decoration, a failed import is a broken build. The
bulk-entry routes
(`/admin/seasonal-marks/entry`, `/admin/exams/results/:examId/entry`,
`/admin/admission/banks/:bankId`, `/admin/admission/candidates/:leadId`) are
reached from their parent screen and are **not** nav entries.

### 3.7 What a permission hides

`RequirePerm` **[ours, `components/auth/RequirePerm.tsx`]** wraps the route, and
`Sidebar` filters `navByRole` by `perm` — so a `staff` account without the key
sees neither the menu entry nor the page. Inside a page, the write controls
follow the same key, because `AdminPermAttribute` will reject the request anyway
and a button that always 403s is worse than no button:

| Hidden without the perm | Screens |
|---|---|
| "Baza qo'shish", edit, delete, the settings Saqlash | 3, 4 |
| "Savol qo'shish", "Excel'dan import", question edit/delete | 4 |
| "Imtihonga biriktirish", "Havolani qayta chiqarish", "Bekor qilish", "Natijani qayta hisoblash" | 1, 2 |
| "O'quvchi qilib ro'yxatga olish" — needs **`admission` and `students`** | 2 |
| "Imtihon qo'shish", edit, publish, cancel | 6, 7 |
| Every editable cell in the entry grid, Saqlash, import | 9 |
| Inline score/comment editing, delete, "Baholash" | 10, 11, 14 |
| "Natijani qayta hisoblash" / attempt reset — needs **`exams` and `admission`** | 2, 8 |

Read-only screens (8, 12, 13) show data and the export button, nothing else.
The public page (15) has no permission concept at all.

---

## 4. Permissions

### 4.1 New admin permission keys

Added to `adminPermissions` in `schoollms.client/src/config/constants.ts`
**[ours, line 75]** and used by `AdminPermAttribute` **[ours]**:

| Key | Uzbek label | Covers |
|---|---|---|
| `admission` | `Qabul` | screens 1–4, `/api/admin/admission/**` |
| `exams` | `Imtihonlar` | screens 5–9, `/api/admin/exams/**` |
| `seasonalMarks` | `Mavsumiy baholash` | screens 10–13, `/api/admin/seasonal-marks/**` |

EduSchool gates the entire admission module — candidate list *and* question bank
with its correct answers — behind `getLeads`, i.e. anyone who may see the sales
pipeline may also read the entrance exam. We do not copy that.

### 4.2 New teacher permission key

`TeacherPermissions.SeasonalMarks = "seasonalMarks"` in
`SchoolLms.Domain/TeacherPermissions.cs` **[ours]**, added to
`TeacherPermissions.All` and to `teacherPermissions` in
`schoollms.client/src/config/constants.ts` **[ours, line 61]**.

Because `All` is the default for a new teacher **[ours,
`TeachersController.cs:60`]**, existing teachers keep whatever list they already
have and will **not** get the new key automatically. The migration must
back-fill: append `"seasonalMarks"` to every existing `teachers.permissions`
array. State this in the migration comment.

### 4.3 `AdminPermAttribute` must learn to gate reads — shared file, sequential

`AdminPermAttribute` **[ours,
`SchoolLms.Server/Controllers/AdminPermAttribute.cs`]** today lets **any** `staff`
account issue **any** `GET` to **any** admin controller; only writes are checked.
The comment says why, and for pupil lists and finance categories that is a
reasonable call.

It is not a reasonable call for `GET /api/admin/admission/banks/{id}/questions`,
whose response contains the correct answer to a live entrance exam.

**Required change (additive, one property):**

```
[AdminPerm("admission", GatedRead = true)]
```

`GatedRead` defaults to `false`, so **no existing controller changes behaviour**.
When `true`, the `GET`/`HEAD`/`OPTIONS` early-return is skipped and the `perm`
claim is required for reads too.

Apply `GatedRead = true` to exactly two controllers:
`AdmissionBanksController` and `AdmissionQuestionsController` (§6.2).
Everything else keeps the existing behaviour.

`AdminPermAttribute.cs` is therefore a **shared, sequential** file (§11).

### 4.4 Endpoint → permission matrix

| Endpoint group | Role gate | Perm | Read gated |
|---|---|---|---|
| `/api/admin/leads/**` (existing + new) | admin, superadmin, staff | `leads` | no (unchanged) |
| `/api/admin/admission/candidates/**` | admin, superadmin, staff | `admission` | no |
| `/api/admin/admission/banks/**` | admin, superadmin, staff | `admission` | **yes** |
| `/api/admin/admission/questions/**` | admin, superadmin, staff | `admission` | **yes** |
| `/api/admin/exams/**` | admin, superadmin, staff | `exams` | no |
| `/api/admin/seasonal-marks/**` | admin, superadmin, staff | `seasonalMarks` | no |
| `/api/teacher/seasonal-marks/**` | teacher | `TeacherPermissions.SeasonalMarks` + teaches that (class, subject) | n/a |
| `/api/public/exam/**` | **anonymous** | none | n/a |

---

## 5. Data model

Conventions taken from the existing code **[ours]**, not from `docs/SPEC.md` §3
(which describes a target schema the running system does not use):

* PK and every FK column: `text`, value `Guid.NewGuid().ToString()`.
* Tables and columns snake_case via `EFCore.NamingConventions`
  (`UseSnakeCaseNamingConvention`, `Program.cs:62`).
* Every `DateTime` maps to `timestamp without time zone`
  (`AppDbContext.OnModelCreating`, the trailing loop). Values come from
  `AppClock.Now` (Asia/Tashkent wall clock). **Do not use `DateTimeOffset`** —
  that is the finance exception and these tables are not finance.
* Dates without a time are `text` in ISO `YYYY-MM-DD`, matching
  `Student.EnrollmentDate`, `QuarterPeriod.StartDate`, `JournalEntry.Date`.
* Constraints, indexes and FKs go in the **EF model**, in a new
  `SchoolLms.Infrastructure/Data/ExamModel.cs` (the pattern set by
  `BillingModel.cs`, `AnomalyModel.cs`, `GuardianModel.cs`), so
  `--autogenerate` will not DROP them. Only GRANTs go in raw SQL (§11).
* Money: none of these tables hold money. No currency, no rounding rule.
* Soft delete: none of these tables soft-delete except where stated
  (`exams.status='cancelled'`, `question_banks.is_archived`).

Twelve new tables, in dependency order.

### 5.1 `question_banks` — EduSchool "Test bazasi"

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `grade` | int | no | | 0–11. `check (grade between 0 and 11)` **[bundle: `class_grade.grade0`…`grade11`]** |
| `subject_id` | text | no | | FK → `subjects.id`, `on delete restrict` |
| `questions_per_test` | int | yes | null | how many are drawn per sitting **[bundle: `settings.questionsPerTest`]** |
| `time_limit_min` | int | yes | null | minutes for this bank's block **[bundle: `settings.timeLimitMin`]** |
| `points_per_correct` | numeric(6,2) | yes | null | **[bundle: `settings.pointsPerCorrect`]** |
| `is_archived` | boolean | no | false | replaces EduSchool's vestigial `state:"draft"` (see below) |
| `created_at` | timestamp | no | `AppClock.Now` | |
| `created_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null` |

* Unique `(grade, subject_id)` where `is_archived = false`. One live bank per
  grade × subject; that is what the EduSchool list shows and how the exam picks a
  bank without asking.
* Checks: `questions_per_test is null or questions_per_test >= 1`;
  `time_limit_min is null or time_limit_min between 1 and 600`;
  `points_per_correct is null or points_per_correct > 0`.
* `questions_count` is **not stored** — it is `count(*)` over `questions`. The
  list endpoint returns it as a computed field. **[bundle exposes it as
  `questionsCount`; storing a counter that can drift from the rows is a bug
  waiting to happen at zero benefit — the table is a few hundred rows.]**
* EduSchool's create payload sends `state:"draft"` and no screen ever changes it
  **[bundle]** — a dead field. We replace it with `is_archived`, which the bank
  list actually needs (an old bank must stop appearing in the exam picker without
  destroying the exams that used it).
* EduSchool's `branchIds[]` **[bundle]** is dropped: we are single-school
  (`Branch` exists but `/admin/boshqaruv/branches` is superadmin-only and nothing
  scopes by it).

### 5.2 `questions`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `bank_id` | text | no | | FK → `question_banks.id`, `on delete cascade` |
| `text` | text | no | | trimmed, non-empty |
| `image_url` | text | yes | null | `/uploads/<guid>.<ext>` from `POST /api/admin/uploads` |
| `order` | int | no | 0 | display order inside the bank |
| `created_at` | timestamp | no | `AppClock.Now` | |

* Index `(bank_id, order)`.
* Check `btrim(text) <> ''`.
* No `is_active`: deleting a question that has been answered is blocked by the
  `exam_answers` FK (`on delete restrict`); the UI turns the delete button into
  "arxivlash" only if the client asks for it later.

### 5.3 `question_options`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `question_id` | text | no | | FK → `questions.id`, `on delete cascade` |
| `text` | text | no | | trimmed, non-empty |
| `is_correct` | boolean | no | false | |
| `order` | int | no | 0 | 0→A, 1→B … 5→F |

* Index `(question_id, order)`.
* **Exactly one correct option per question.** Enforced two ways, because the
  client-side check is not enough:
  * a partial unique index `(question_id) where is_correct` — stops *two*;
  * a server-side check in the save transaction — stops *zero*.
  Postgres cannot express "at least one" declaratively across rows without a
  deferred constraint trigger; the service check is the honest place for it.
* Between **2 and 6** options — **[bundle: `Me=2`, `we=6`, letters `"ABCDEF"`]**.
  Service-enforced.

### 5.4 `exam_types` — EduSchool "Imtihon turi"

| Column | Type | Null | Default |
|---|---|---|---|
| `id` | text | no | |
| `name` | text | no | |
| `description` | text | no | `''` |
| `is_active` | boolean | no | true |
| `created_at` | timestamp | no | `AppClock.Now` |

Unique `(lower(btrim(name)))`. **[assumption — the ExamTypePage chunk is absent;
this is the shape of every other catalogue table we have, e.g. `AssignmentType`,
`EvaluationType`.]**

### 5.5 `exams`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `title` | text | no | | |
| `kind` | text | no | | `admission` \| `block` |
| `delivery` | text | no | | `online` \| `manual` |
| `exam_type_id` | text | yes | null | FK → `exam_types.id`, `on delete set null` |
| `grade` | int | yes | null | admission only; `check (grade is null or grade between 0 and 11)` |
| `exam_date` | text | yes | null | `YYYY-MM-DD`; manual exams |
| `opens_at` | timestamp | yes | null | online only — window start |
| `closes_at` | timestamp | yes | null | online only — window end |
| `time_limit_min` | int | yes | null | online only; total for the sitting |
| `status` | text | no | `draft` | `draft` \| `published` \| `closed` \| `cancelled` |
| `created_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null` |
| `created_at` | timestamp | no | `AppClock.Now` | |

* Index `(kind, status)`, index `(exam_date)`.
* Checks: `kind in ('admission','block')`; `delivery in ('online','manual')`;
  `status in ('draft','published','closed','cancelled')`;
  `delivery <> 'online' or (opens_at is not null and closes_at is not null and time_limit_min is not null)`;
  `opens_at is null or closes_at > opens_at`.
* Status transitions (service-enforced, one direction only):
  `draft → published → closed`, and `draft|published → cancelled`.
  * `publish` requires ≥1 section and, for `online`, every section's bank
    `ready` (§8.1). It **freezes** the sections: after publish, sections cannot
    be added, removed or repointed at another bank.
  * `cancelled` revokes every live invitation and marks every unfinished
    participant `cancelled`. **[bundle: `/block-test/cancel`.]**
  * `closed` is set by the nightly job when `closes_at` passes, or by hand.

### 5.6 `exam_sections` — one row per subject in the sitting

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `exam_id` | text | no | | FK → `exams.id`, `on delete cascade` |
| `subject_id` | text | no | | FK → `subjects.id`, `on delete restrict` |
| `bank_id` | text | yes | null | FK → `question_banks.id`, `on delete restrict`; required when `delivery='online'` |
| `question_count` | int | yes | null | online: how many to draw |
| `points_per_correct` | numeric(6,2) | yes | null | online: copied from the bank **at publish** |
| `max_score` | numeric(6,2) | no | | manual: the ceiling for the entry grid. Online: `question_count × points_per_correct`, written at publish |
| `order` | int | no | 0 | question blocks appear in this order |

* Unique `(exam_id, subject_id)`; index `(exam_id, order)`.
* `points_per_correct` and `time_limit_min` are **copied, not referenced**, at
  publish. Editing a bank afterwards must not silently rescore a finished exam.
  `exams.time_limit_min` is likewise seeded from `sum(bank.time_limit_min)` when
  the sections are chosen, then editable **[bundle: their lobby shows one
  `timeLimitMin` and one `questionCount` for a multi-subject exam, i.e. they sum
  the bases]**.

### 5.7 `exam_participants`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `exam_id` | text | no | | FK → `exams.id`, `on delete cascade` |
| `participant_kind` | text | no | | `lead` \| `student` |
| `lead_id` | text | yes | null | FK → `leads.id`, `on delete cascade` |
| `student_id` | text | yes | null | FK → `students.id`, `on delete cascade` |
| `class_id` | text | yes | null | FK → `classes.id`, `on delete set null`; block tests, snapshot at assignment |
| `status` | text | no | `assigned` | `assigned` \| `in_progress` \| `finished` \| `absent` \| `cancelled` |
| `correct_count` | int | yes | null | online only |
| `question_count` | int | yes | null | online only |
| `total_points` | numeric(8,2) | yes | null | summary |
| `max_points` | numeric(8,2) | yes | null | summary |
| `percent` | numeric(5,2) | yes | null | `round(total_points / nullif(max_points,0) * 100, 2)` |
| `scored_at` | timestamp | yes | null | |
| `scored_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null`; null = graded by the engine |
| `created_at` | timestamp | no | `AppClock.Now` | |

* Unique `(exam_id, lead_id)` where `lead_id is not null`;
  unique `(exam_id, student_id)` where `student_id is not null`.
* Check `(participant_kind = 'lead') = (lead_id is not null)` and
  `(participant_kind = 'student') = (student_id is not null)` — exactly one
  pointer is set. Same shape as
  `ck_student_guardians_*` **[ours, `GuardianModel.cs`]**.
* Index `(exam_id, status)`, index `(lead_id)`, index `(student_id)`.
* Why the score summary lives here and not in a separate `exam_results` table:
  there is exactly one result per participant, always; a 1:1 table would only
  add a join.

### 5.8 `exam_section_scores`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `participant_id` | text | no | | PK part, FK → `exam_participants.id`, `on delete cascade` |
| `section_id` | text | no | | PK part, FK → `exam_sections.id`, `on delete cascade` |
| `correct_count` | int | yes | null | online |
| `question_count` | int | yes | null | online |
| `points` | numeric(8,2) | no | 0 | manual: typed. online: computed |
| `max_points` | numeric(8,2) | no | | copied from `exam_sections.max_score` |
| `updated_at` | timestamp | no | `AppClock.Now` | |

* PK `(participant_id, section_id)`.
* Check `points >= 0 and points <= max_points`.

### 5.9 `exam_invitations` — the public token

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `participant_id` | text | no | | FK → `exam_participants.id`, `on delete cascade` |
| `token_hash` | text | no | | **SHA-256 of the token. The token itself is never stored.** |
| `token_hint` | text | no | | last 6 characters, for support ("…7fQ2xA") |
| `valid_from` | timestamp | no | | defaults to `exams.opens_at` |
| `valid_until` | timestamp | no | | defaults to `exams.closes_at` |
| `issued_at` | timestamp | no | `AppClock.Now` | |
| `issued_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null` |
| `first_opened_at` | timestamp | yes | null | first successful `state` call |
| `revoked_at` | timestamp | yes | null | |
| `revoked_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null` |

* Unique `(token_hash)`.
* **Partial unique `(participant_id) where revoked_at is null`** — at most one
  live link per participant. Re-issuing revokes the old row and inserts a new
  one, so the audit trail of who re-sent what survives.
* Index `(valid_until)` for the expiry sweep.
* Modelled on `TelegramLinkCode` **[ours, `SchoolLms.Domain/TelegramLink.cs`]**,
  which already does hash-only storage, expiry and single use, and whose
  reasoning comment applies unchanged.

### 5.10 `exam_attempts`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `participant_id` | text | no | | FK → `exam_participants.id`, `on delete cascade` |
| `invitation_id` | text | no | | FK → `exam_invitations.id`, `on delete restrict` |
| `started_at` | timestamp | no | `AppClock.Now` | |
| `deadline_at` | timestamp | no | | `started_at + exams.time_limit_min`, capped at `exams.closes_at` |
| `finished_at` | timestamp | yes | null | |
| `finish_reason` | text | yes | null | `manual` \| `timer` \| `admin` **[bundle: `finishReason === "timer"` else manual]** |
| `status` | text | no | `in_progress` | `in_progress` \| `finished` |
| `device_session_hash` | text | no | | SHA-256 of the value put in the device cookie |
| `device_label` | text | no | | e.g. `Chrome · Windows` — shown on the block screen **[bundle: `blockedBody({device})`]** |
| `first_ip` | text | no | | |
| `user_agent` | text | no | | truncated to 400 chars |
| `answered_count` | int | no | 0 | cheap progress + abuse ceiling (§7.6) |
| `abuse_flagged` | boolean | no | false | set when the ceiling is hit; shows a badge on the admin card |

* **Unique `(participant_id)`** — one attempt, ever. The second `start` returns
  the existing attempt if `in_progress`, or the result if `finished`. Retaking
  requires an admin to reset (§8.3), which writes an audit row.
* Index `(status, deadline_at)` for the expiry sweep.

### 5.11 `exam_answers`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `attempt_id` | text | no | | PK part, FK → `exam_attempts.id`, `on delete cascade` |
| `question_id` | text | no | | PK part, FK → `questions.id`, `on delete restrict` |
| `section_id` | text | no | | FK → `exam_sections.id`, `on delete cascade`; denormalised so scoring is one GROUP BY |
| `order` | int | no | | position in this attempt's paper (0-based) |
| `selected_option_id` | text | yes | null | FK → `question_options.id`, `on delete restrict` |
| `answered_at` | timestamp | yes | null | |

* PK `(attempt_id, question_id)`; unique `(attempt_id, order)`.
* **The paper is materialised at `start`.** All rows are inserted with
  `selected_option_id = null` in the same transaction that creates the attempt.
  Consequences that matter: the question set cannot change under the candidate,
  the order is stable across reloads, "12/20 answered" is one count, and grading
  never re-draws.
* `is_correct` is **not** a column — it is `selected_option_id` matched against
  `question_options.is_correct` at grading time. Storing it would put the answer
  key one careless `SELECT *` away from the public endpoint.

### 5.12 `seasonal_marks`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | text | no | | PK |
| `student_id` | text | no | | FK → `students.id`, `on delete cascade` |
| `class_id` | text | no | | FK → `classes.id`, `on delete restrict`; snapshot at entry |
| `subject_id` | text | no | | FK → `subjects.id`, `on delete restrict` |
| `period_kind` | text | no | | `monthly` \| `quarterly` \| `yearly` **[bundle: `yU`]** |
| `year` | int | no | | e.g. 2026 |
| `month` | int | yes | null | 1–12, only when `monthly` |
| `quarter` | int | yes | null | 1–4, only when `quarterly`. **A number, not a FK — see below** |
| `period_key` | text | no | **generated stored** | see below |
| `score` | numeric(5,2) | yes | null | 0–100 **[bundle: `min:0 max:100 step:0.1`]**. null = comment only |
| `comment` | text | yes | null | |
| `created_by_user_id` | text | yes | null | FK → `users.id`, `on delete set null` |
| `created_at` | timestamp | no | `AppClock.Now` | |
| `updated_at` | timestamp | no | `AppClock.Now` | |

```sql
period_key text generated always as (
  case period_kind
    when 'monthly'   then 'M:' || year || '-' || lpad(month::text, 2, '0')
    when 'quarterly' then 'Q:' || year || '-' || quarter
    else                  'Y:' || year
  end
) stored
```

* **Unique `(student_id, subject_id, period_key)`.** A generated key is used
  instead of a multi-column unique index because Postgres treats `NULL`s as
  distinct, so `(student_id, subject_id, period_kind, year, month, quarter)`
  would happily accept two yearly marks for the same pupil and subject.

* **`quarter` is an `int`, and there is deliberately no FK to `quarters`.**
  EduSchool sends a `quarterId` **[bundle]**. We must not. Two reasons, both
  from our own code:
  1. `PUT /api/admin/settings/quarters` **[ours,
     `SettingsController.cs:29`]** does `db.Quarters.RemoveRange(db.Quarters)`
     and re-adds every row **with fresh GUIDs**. Every save of the Choraklar
     screen would orphan every quarterly mark, and with a `restrict` FK it would
     instead throw a 500 and make the screen unusable.
  2. Nothing else in this system references `QuarterPeriod.Id`. `JournalEntry`,
     `QuarterGrade` and `WeekAssignment` all carry `Quarter` as an `int`
     **[ours]**. Following the house style keeps the join with existing grade
     data trivial.
  `(year, quarter)` is exactly as expressive as an id — `quarters` holds only the
  current year's four rows and has no year column of its own — and it survives
  that destructive save.
  *(The delete-and-recreate in `SaveQuarters` is a latent bug worth fixing on its
  own merits, but it is not this module's job and this design does not depend on
  it being fixed.)*
* Index `(class_id, subject_id, period_key)` — the entry screen, the pivot and
  the coverage report all filter on exactly that.
* Index `(period_key)`.
* Checks: `period_kind in ('monthly','quarterly','yearly')`;
  `(period_kind = 'monthly') = (month is not null)`;
  `(period_kind = 'quarterly') = (quarter is not null)`;
  `month is null or month between 1 and 12`;
  `quarter is null or quarter between 1 and 4`;
  `year between 2000 and 2100`;
  `score is null or (score >= 0 and score <= 100)`;
  `comment is null or char_length(btrim(comment)) >= 3`
  **[bundle: the inline editor refuses a 1–2 character comment]**;
  `score is not null or comment is not null` — an empty row is not a mark.
* Rounding: `numeric(5,2)`, half-up, applied server-side before insert.
  `100.00` is the maximum; a client sending `100.4` is clamped to `100`
  **[bundle does the clamp client-side; we do it server-side too]**.
* **Edit history is not a column.** `AuditLog` **[ours,
  `SchoolLms.Domain/Entities.cs:678`]** already stores `Before`/`After` JSON,
  `ActorName`, `Timestamp` and `StudentId`. Write
  `EntityType = "SeasonalMark"`, `EntityId = id`, `StudentId = student_id` and
  the history popup (screen 10) is `GET /api/admin/audit?entityType=SeasonalMark&entityId=…`.
  EduSchool embeds a `markHistory[]` array on the row **[bundle]**; we already
  have the general mechanism and a second one would rot.

### 5.13 Changes to existing tables

`leads` — two nullable/defaulted columns, no data migration beyond the default:

```
admission_status text not null default 'none'
student_id       text null references students(id) on delete set null
```

* Check `admission_status in ('none','invited','testing','tested','accepted','rejected','enrolled')`.
* Index `(admission_status)` — the candidate list's only filter.
* Nothing in `LeadsController` or the six protected board files reads these
  columns. The board keeps working with no change (§2.3).

`school_meta` — one column:

```
admission_show_answers_to_candidate boolean not null default false
```

Gates whether the finished public page shows the per-question review (§7.7).
Default `false` on purpose: shipping with EduSchool's behaviour and turning it
off later means the first cohort has already seen the bank.

`teachers.permissions` — back-fill `"seasonalMarks"` (§4.2). Data change, not
schema change.

**Nothing else changes.** No column is dropped, renamed or retyped anywhere.
If `--autogenerate` emits a `Drop*` for anything outside this list, the
migration is wrong — read it (global rule) and delete the spurious operation.

### 5.14 Referential side effects on existing delete endpoints

These twelve tables are the **first** in this database to declare real foreign
keys onto `subjects` and `classes`. Today those tables have no inbound FK at all
— `JournalEntry.SubjectId` is a bare `text` column. Adding `on delete restrict`
therefore changes the behaviour of endpoints that already exist, and the change
must be handled or it becomes a 500.

| Existing endpoint | New inbound FK | What happens if nothing is done | Required additive fix |
|---|---|---|---|
| `DELETE /api/admin/subjects/{id}` **[ours, `SubjectsController.cs:39`]** — deletes unconditionally today | `question_banks.subject_id`, `exam_sections.subject_id`, `seasonal_marks.subject_id` | `DbUpdateException` → 500 | pre-check the three tables, return **409** with `"Bu fan test bazasi / imtihon / mavsumiy baholashda ishlatilgan — o'chirib bo'lmaydi."` |
| `DELETE /api/admin/classes/{id}` **[ours, `ClassesController.cs:98`]** — already refuses when pupils exist | `exam_participants.class_id` (`set null`), `seasonal_marks.class_id` (`restrict`) | `DbUpdateException` → 500 once a class has seasonal marks | add one more pre-check in the same style as the existing pupil check, return **400** with `"Bu sinfda mavsumiy baholash yozuvlari bor — avval ularni o'chiring."` |
| `DELETE /api/admin/students/{id}` **[ours, `StudentsController.cs:271`]** — already refuses when invoices or payments exist | `exam_participants.student_id`, `seasonal_marks.student_id` — both `cascade` | pupil deleted, their marks and exam rows go with them | **none.** Deleting a pupil is the "created by mistake" path; the normal exit is archiving (`IsArchived`). Cascade is correct here and matches the existing behaviour for journal data. |
| `DELETE /api/admin/leads/{id}` | `exam_participants.lead_id` — `cascade` | participation removed with the lead | **none** (§8.5) — the board's delete must keep working |
| `PUT /api/admin/settings/quarters` **[ours, `SettingsController.cs:29`]** — deletes and recreates all four rows with new GUIDs on every save | **none** | nothing | **none** — `seasonal_marks.quarter` is an `int`, precisely so this screen keeps working (§5.12) |

One consequence for the plan: `SubjectsController.cs` and `ClassesController.cs`
are **touched by this module** even though they belong to other features, and
each edit is a single guard clause — no restructuring. They are listed in §11.
`SettingsController.cs` is **not** touched.

Do not "solve" this by dropping the foreign keys. Referential integrity between
a seasonal mark and its subject is exactly the thing that stops a report from
one day showing a score against a subject that no longer exists.

---

## 6. API contract

Base: `/api`. Auth: `Authorization: Bearer <jwt>` (`api/client.ts` **[ours]**),
except `/api/public/**`.

**Controller attributes — not optional.** `Program.cs` registers no fallback
authorization policy, so an endpoint with no attribute is wide open. Every new
controller carries its gate explicitly, copying `LeadsController` **[ours]**:

```csharp
[ApiController] [Authorize] [AdminPerm("admission")]              // admin/staff
[ApiController] [Authorize] [AdminPerm("admission", GatedRead = true)]
[ApiController] [Authorize(Roles = "teacher")]                    // teacher
[ApiController] [AllowAnonymous] [EnableRateLimiting("exam")]     // public
```

**Timestamp format.** All `DateTime` values serialise as
`yyyy-MM-ddTHH:mm:ss` with **no offset and no `Z`** — Tashkent wall clock, from
`AppClock.Now` (§5). `serverNow` and `deadlineAt` are in that same frame, so the
browser may parse both with `new Date(...)` and subtract them: the local-time
interpretation cancels out. **Never send only `deadlineAt`** — without
`serverNow` from the same response the client would compare a Tashkent wall
clock against a browser clock in some other zone and show the wrong countdown.

**Pagination envelope.** The codebase has no paged endpoint today — it uses
`Take(N)` caps and returns bare arrays. Four of the new lists can reach tens of
thousands of rows (a year of seasonal marks for 600 pupils × 12 subjects × 9
periods ≈ 65 000), so this spec introduces one envelope and every new paged
endpoint uses it, unchanged:

```
request:  ?page=1&limit=50&search=&sortBy=&sortOrder=asc|desc   (limit ≤ 200, default 50)
response: { "items": [ … ], "total": 1234, "page": 1, "limit": 50 }
```

Endpoints that return a bounded set (types, sections, a single exam's grid)
return a bare array, matching the existing style.

**Errors.** `{ "message": "<Uzbek text>" }` with the appropriate status, matching
`LeadsController` / `StudentsController` **[ours]**. Public endpoints add a
machine-readable `code` (§7.5).

### 6.1 Leads — additive only

| Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|
| GET | `/api/admin/leads/candidates` | `page,limit,search,admissionStatus,grade,examId` | paged `CandidateRowDto` | `admission` |
| GET | `/api/admin/leads/{id}/admission` | — | `CandidateCardDto` | `admission` |
| PATCH | `/api/admin/leads/{id}/admission-status` | `{ status }` | 204 | `admission` |
| POST | `/api/admin/leads/{id}/enrol` | `StudentPayload` + `{ className }` | `{ studentId }` | `admission` **and** `students` |

`GET /api/admin/leads`, `POST`, `PUT /{id}`, `PATCH /{id}`, `DELETE /{id}` are
**unchanged in path, body and response shape**. The board depends on them.

> **Built 2026-09-22, with one change from this spec:** the lead is **deleted** on
> enrolment (client decision) and only a statistic row survives in `lead_conversions`
> (time + source). There is no `leads.student_id`. When this module is built,
> `admission_status = 'enrolled'` therefore has no lead row to live on — read it from
> `lead_conversions` / the student instead. See `docs/ASSUMPTIONS.md`, 2026-09-22.

`POST /leads/{id}/enrol` creates the `Student` via the existing
`StudentPayload` **[ours, `Dtos.cs:27`]**, sets `leads.student_id` and
`admission_status='enrolled'`, and writes an audit row. It requires **both**
permissions because it creates a pupil; enrolling is not an admission-clerk
decision alone.

```
CandidateRowDto {
  leadId, fullName, parentPhone, targetGrade,
  admissionStatus,                       // none|invited|…|enrolled
  examId, examTitle,                     // latest participation, null if none
  participantStatus,                     // assigned|in_progress|finished|absent|cancelled
  invitationState,                       // none|issued|opened|revoked|expired
  totalPoints, maxPoints, percent,       // null until finished
  studentId                              // null until enrolled
}
```

### 6.2 Question banks and questions

| Method | Path | Body | Response | Perm |
|---|---|---|---|---|
| GET | `/api/admin/admission/banks` | `page,limit,search,grade,subjectId` | paged `BankRowDto` (incl. `questionsCount`, derived `state`) | `admission` (read gated) |
| POST | `/api/admin/admission/banks` | `{ grade, subjectId, questionsPerTest?, timeLimitMin?, pointsPerCorrect? }` | `BankDto` | `admission` |
| GET | `/api/admin/admission/banks/{id}` | — | `BankDto` | `admission` (read gated) |
| PUT | `/api/admin/admission/banks/{id}` | `{ questionsPerTest, timeLimitMin, pointsPerCorrect }` | `BankDto` | `admission` |
| DELETE | `/api/admin/admission/banks/{id}` | — | 204, or **409** if any `exam_sections` row points at it | `admission` |
| GET | `/api/admin/admission/banks/{id}/questions` | `page,limit,search` | paged `QuestionDto` **with** `options[].isCorrect` | `admission` (read gated) |
| POST | `/api/admin/admission/questions` | `{ bankId, text, imageUrl?, options:[{text,isCorrect}] }` | `QuestionDto` | `admission` |
| PUT | `/api/admin/admission/questions/{id}` | `{ text, imageUrl?, options:[{id?,text,isCorrect}] }` | `QuestionDto` | `admission` |
| DELETE | `/api/admin/admission/questions/{id}` | — | 204, or **409** if the question has been answered, or if its bank feeds a `published` exam (§8.2) | `admission` |
| GET | `/api/admin/admission/questions/import/template` | `bankId` | `savollar_shablon.xlsx` | `admission` |
| POST | `/api/admin/admission/questions/import` | multipart `file`, `bankId`, `dryRun` | `QuestionImportResultDto` | `admission` |

**Import is one endpoint with a dry run, not EduSchool's two-step token.**
EduSchool does `import/preview` → `importToken` → `import/confirm`, with a
`59305` "session expired" error when the token dies **[bundle]**. That needs a
staging store and adds a failure mode. Our existing imports
(`StudentsController`, `JournalController` **[ours]**) are single-step. So:
`dryRun=true` validates and reports, `dryRun=false` validates and writes, and the
browser posts the file twice. No staging table, no token, no expiry error.

```
QuestionImportResultDto {
  fileName, totalRows, validCount, errorCount, imported,
  errors: [ { reason, rows:[int] } ]      // grouped by reason, as EduSchool renders it
}
```

Template columns (sheet `Savollar`), one row per question, in this order:
`Savol matni | A | B | C | D | E | F | To'g'ri javob (A-F) | Rasm havolasi`.
A second sheet `Yo'riqnoma` carries the rules, matching
`StudentsController.ImportHeaders` **[ours]**. File must be `.xlsx`; `.xls` is
rejected with `"Faqat .xlsx (Excel) fayl qabul qilinadi"` (EduSchool accepts
`.xls` **[bundle]**; `ExcelImport` **[ours]** does not, and adding a second
parser for a dead format is not worth it).

`reason` is one of exactly these seven strings — the UI renders each as one chip
with the offending row numbers, so the set must be closed:

| `reason` | Uzbek chip text | Trigger |
|---|---|---|
| `emptyText` | `Savol matni bo'sh` | column A blank after trim |
| `tooFewOptions` | `Variant 2 tadan kam` | fewer than 2 non-blank option cells |
| `tooManyOptions` | `Variant 6 tadan ko'p` | more than 6 (structurally impossible with 6 columns; kept for a hand-edited sheet) |
| `noCorrect` | `To'g'ri javob ko'rsatilmagan` | column H blank |
| `badCorrect` | `To'g'ri javob noto'g'ri` | column H is not A–F, or points at a blank option |
| `duplicateOption` | `Bir xil variant` | two option cells identical after trim |
| `badImageUrl` | `Rasm havolasi noto'g'ri` | column I is non-empty and does not start with `/uploads/` |

A row with any error is skipped whole; valid rows in the same file still import
when `dryRun=false`. Row numbers in `rows[]` are **1-based sheet rows including
the header**, so they match what the user sees in Excel.

### 6.3 Exams

| Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|
| GET | `/api/admin/exams/types` | — | `ExamTypeDto[]` | `exams` |
| POST | `/api/admin/exams/types` | `{ name, description }` | `ExamTypeDto` | `exams` |
| PUT | `/api/admin/exams/types/{id}` | `{ name, description, isActive }` | `ExamTypeDto` | `exams` |
| DELETE | `/api/admin/exams/types/{id}` | — | 204, or 409 if used | `exams` |
| GET | `/api/admin/exams` | `page,limit,search,kind,status,examTypeId,from,to` | paged `ExamRowDto` | `exams` |
| POST | `/api/admin/exams` | `ExamUpsertDto` | `ExamDto` | `exams` |
| GET | `/api/admin/exams/{id}` | — | `ExamDto` incl. `sections[]` and counts | `exams` |
| PUT | `/api/admin/exams/{id}` | `ExamUpsertDto` | `ExamDto`; **409** if `status <> 'draft'` and sections changed | `exams` |
| POST | `/api/admin/exams/{id}/publish` | — | `ExamDto`; 409 with the reason if §8.1 fails | `exams` |
| POST | `/api/admin/exams/{id}/cancel` | `{ reason }` | `ExamDto` | `exams` |
| GET | `/api/admin/exams/{id}/participants` | `page,limit,search,status` | paged `ParticipantRowDto` | `exams` |
| POST | `/api/admin/exams/{id}/participants` | `{ leadIds?: [], studentIds?: [], classIds?: [] }` | `{ added, skipped }` | `exams` (admission exam also needs `admission`) |
| DELETE | `/api/admin/exams/{id}/participants/{pid}` | — | 204; 409 once an attempt exists | `exams` |
| GET | `/api/admin/exams/{id}/entry-table` | — | `EntryTableDto` (bare, capped at 500 rows) | `exams` |
| POST | `/api/admin/exams/{id}/entry-table` | `{ rows:[{participantId, scores:[{sectionId, points}], absent:bool}] }` | `{ saved }` | `exams` |
| GET | `/api/admin/exams/{id}/entry-table/template` | — | `natijalar_shablon.xlsx` | `exams` |
| POST | `/api/admin/exams/{id}/entry-table/import` | multipart `file`, `dryRun` | `ResultImportResultDto` | `exams` |
| GET | `/api/admin/exams/results` | `page,limit,search,examId,classId,subjectId,status` | paged `ResultRowDto` | `exams` |
| GET | `/api/admin/exams/results/export` | same query | `.xlsx` | `exams` |
| GET | `/api/admin/exams/participants/{pid}/review` | — | `AttemptReviewDto` (**with** correct answers) | `exams` or `admission` |
| POST | `/api/admin/exams/participants/{pid}/force-finish` | `{ reason }` | `AttemptReviewDto` — grade now, `finish_reason='admin'` (§8.3) | `exams` |
| POST | `/api/admin/exams/participants/{pid}/reset-attempt` | `{ reason }` | 204 — destroys the attempt (§8.3) | `exams` **and** `admission` |

`POST /exams/{id}/participants` with `classIds` expands to every non-archived
pupil in those classes. It is idempotent: a pupil already on the exam is counted
in `skipped`, not duplicated (the unique indexes in §5.7 make that structural).

The entry table returns a bare array capped at 500 rows because a single sitting
never exceeds one grade's worth of pupils; if the cap is hit the endpoint returns
409 telling the user to split the exam, rather than silently truncating.

```
ExamUpsertDto {
  title, kind,            // admission | block   — immutable after create
  delivery,               // online | manual     — immutable after create
  examTypeId?, grade?, examDate?, opensAt?, closesAt?, timeLimitMin?,
  sections: [ { subjectId, bankId?, questionCount?, maxScore?, order } ]
}

EntryTableDto {
  examId, title, examDate,
  columns: [ { sectionId, subjectId, name, maxScore } ],
  rows: [ {
    participantId, fullName, className,
    status,                                   // assigned|finished|absent|cancelled
    scores: { "<sectionId>": 42.5 | null },
    totalPoints, maxPoints, percent
  } ]
}

AttemptReviewDto {                            // admin only — carries the answer key
  participant: { id, fullName, kind, grade, className },
  exam:        { id, title, kind, examDate },
  attempt:     { startedAt, finishedAt, finishReason, deviceLabel, firstIp,
                 answeredCount, abuseFlagged } | null,   // null for manual exams
  summary:     { correctCount, totalCount, totalPoints, maxPoints, percent },
  perSubject:  [ { subjectId, name, correct, total, points, maxPoints } ],
  questions:   [ { id, subjectId, order, text, imageUrl,
                   options: [ { id, text } ],
                   selectedOptionId, correctOptionId, isCorrect } ] | null
}
```

Export file names, so two agents do not invent two conventions:
`natijalar_<examId-short>_<yyyy-MM-dd>.xlsx`,
`mavsumiy_baholash_<yyyy-MM-dd>.xlsx`,
`mavsumiy_baholash_fanlar_<yyyy-MM-dd>.xlsx`,
`mavsumiy_baholash_hisoboti_<yyyy-MM-dd>.xlsx`,
templates `savollar_shablon.xlsx` and `natijalar_shablon.xlsx`. All built with
`ExcelExport.Build` **[ours]** and returned with
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.

### 6.4 Invitations (admin side of the public token)

| Method | Path | Body | Response | Perm |
|---|---|---|---|---|
| POST | `/api/admin/exams/participants/{pid}/invitation` | `{ validFrom?, validUntil? }` | `{ url, tokenHint, validFrom, validUntil }` — **the only time `url` is ever returned** | `admission` |
| DELETE | `/api/admin/exams/participants/{pid}/invitation` | — | 204 (revoke) | `admission` |
| POST | `/api/admin/exams/{id}/invitations` | `{ validFrom?, validUntil? }` | `{ issued, skipped }` — bulk issue for everyone `assigned` with no live invitation | `admission` |
| POST | `/api/admin/exams/participants/{pid}/unlock-device` | `{ reason }` | 204 — clears the device lock, keeps the answers (§7.4, point 5) | `admission` |

`url` is `{PublicBaseUrl}/qabul-test/{token}`, where `PublicBaseUrl` is the new
configuration key **`Admission:PublicBaseUrl`** (`appsettings.json`, overridable
by `Admission__PublicBaseUrl` in the environment; add it to `.env.example` and
`docker-compose.server.yml`). No trailing slash. If the key is empty the issue
endpoint returns **500** with
`"Admission:PublicBaseUrl sozlanmagan — havola yaratib bo'lmaydi"`.

It must **not** be derived from the request `Host` header. A `Host`-derived link
is a host-header-injection primitive: an attacker who can reach the admin API
sets `Host: evil.tld` and the school then SMSes candidates a link to
`evil.tld/qabul-test/<real token>`, harvesting live tokens.

### 6.5 Public exam endpoints — anonymous

Controller `PublicExamController`, `[ApiController] [AllowAnonymous]
[Route("api/public/exam")] [EnableRateLimiting("exam")]`.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/public/exam/state` | query `token` | `PublicStateDto` |
| POST | `/api/public/exam/start` | `{ token }` | `PublicPaperDto` |
| POST | `/api/public/exam/answer` | `{ token, questionId, optionId }` | `{ deadlineAt, serverNow, answeredCount }` |
| POST | `/api/public/exam/finish` | `{ token }` | `PublicResultDto` |

```
PublicStateDto {
  state,                    // lobby | in_progress | finished | blocked | not_assigned
  serverNow,                // ISO, always present — the client's clock is not trusted
  candidate: { fullName, grade } | null,
  exam:      { title, subjects:[{id,name}], questionCount, timeLimitMin } | null,   // lobby
  paper:     PublicPaperDto | null,                                                  // in_progress
  result:    PublicResultDto | null,                                                 // finished
  deviceLabel: string | null                                                         // blocked
}

PublicPaperDto {
  deadlineAt, serverNow,
  subjects: [ { id, name } ],
  questions: [ {
    id, subjectId, order, text, imageUrl,
    options: [ { id, text } ],        // NO isCorrect. NO correctOptionId.
    selectedOptionId
  } ]                                 // grouped by subject, in section order
}

PublicResultDto {
  correctCount, totalCount, totalPoints, maxPoints, percent,
  finishReason,                       // manual | timer | admin
  finishedAt,
  perSubject: [ { subjectId, name, correct, total, points, maxPoints } ],
  questions: [ … ] | null             // null unless school setting allows it (§7.7)
}
```

### 6.6 Seasonal marks

| Method | Path | Body / query | Response | Perm |
|---|---|---|---|---|
| GET | `/api/admin/seasonal-marks` | `page,limit,search,periodKind,year,month,quarter,classId,subjectId,studentId` | paged `SeasonalMarkRowDto` | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/scope` | `classId?` | `ScopeDto` (bare) | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/students` | `classId,subjectId,periodKind,year,month?,quarter?` | `SeasonalEntryRowDto[]` (bare) | `seasonalMarks` |
| POST | `/api/admin/seasonal-marks/bulk` | `{ classId, subjectId, periodKind, year, month?, quarter?, rows:[{studentId, score, comment}] }` | `{ created, updated, deleted }` | `seasonalMarks` |
| PUT | `/api/admin/seasonal-marks/{id}` | `{ score?, comment? }` | `SeasonalMarkRowDto` | `seasonalMarks` |
| DELETE | `/api/admin/seasonal-marks/{id}` | — | 204 | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/export` | same query as list | `.xlsx` | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/by-subjects` | `page,limit,classIds[],subjectIds[],periodKind,year,month?,quarter?` | paged pivot | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/by-subjects/export` | same | `.xlsx` | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/coverage` | `page,limit,periodKind,year,month?,quarter?,teacherIds[]` | paged `CoverageRowDto` | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/coverage/detail` | `teacherId,periodKind,year,month?,quarter?,hasMark?` | paged pupil rows | `seasonalMarks` |
| GET | `/api/admin/seasonal-marks/coverage/export` | same as coverage | `.xlsx` | `seasonalMarks` |
| GET | `/api/teacher/seasonal-marks/scope` | — | `ScopeDto` — only pairs this teacher teaches | teacher `seasonalMarks` |
| GET | `/api/teacher/seasonal-marks/students` | as above, own pairs only | `SeasonalEntryRowDto[]` | teacher `seasonalMarks` |
| POST | `/api/teacher/seasonal-marks/bulk` | as above, own pairs only | `{ created, updated, deleted }` | teacher `seasonalMarks` |

**`/scope` exists because nothing today answers "which subjects are taught in
this class".** `SubjectsController` **[ours]** returns the global subject list;
`JournalController` **[ours]** always receives `classId` *and* `subjectId` from a
caller that already knows both. The entry screens (11 and 14) have to populate a
subject dropdown from the class, so:

```
ScopeDto {
  classes:  [ { id, name, grade } ],
  pairs:    [ { classId, subjectId, subjectName, teacherId, teacherFullName } ]
}
```

Built from `ScheduleTemplate` → `ScheduleLesson` **[ours]** — the distinct
`(ClassId, SubjectId, TeacherId)` triples of the current academic year, which is
the same join `TeacherActivityReport` **[ours]** already performs. For the
teacher route, filtered to `TeacherId = the caller`. EduSchool solves this with
`/journal/seasonal-mark-subjects?_id=<classId>&isSeasonalMark=true` **[bundle]**
— a per-class flag on the class-subject link that we do not have and are not
adding; every subject a class is taught can receive a seasonal mark.

```
SeasonalMarkRowDto {
  id, student: { id, fullName }, class: { id, name },
  subject: { id, name },
  periodKind, year, month, quarter,          // quarter = 1..4 or null
  periodLabel,               // server-formatted: "Mart 2026" | "2 - chorak 2026" | "2026"
  score, comment, updatedAt, createdByName
}

SeasonalEntryRowDto {        // the bulk-entry grid, one row per pupil
  studentId, fullName, score, comment, markId    // markId null = nothing stored yet
}
```

`periodLabel` is formatted **on the server**, in Uzbek, so the list, the export
and the pivot cannot disagree about how a period is spelt.

Pivot response, because a dynamic-column table needs its columns described:

```
{ "columns": [ { "subjectId", "name" } ],
  "items":   [ { "studentId", "fullName", "className", "scores": { "<subjectId>": 87.5 } } ],
  "total", "page", "limit" }
```

`POST /bulk` semantics, exactly: for each row, upsert on
`(studentId, subjectId, period_key)`. A row whose `score` **and** `comment` are
both empty **deletes** the existing mark (that is how a teacher un-marks). Every
create, update and delete writes one `AuditLog` row inside the same transaction.

`CoverageRowDto { teacherId, fullName, totalStudents, marked, unmarked, percent }`
where `percent = min(marked / nullif(totalStudents,0) * 100, 100)` rounded to a
whole number **[bundle: `Math.min(j/y*100,100).toFixed()`]**. `totalStudents` is
the distinct count of pupils across the (class, subject) pairs the teacher
teaches in that period, taken from the schedule the same way
`TeacherActivityReport` **[ours]** already does it.

---

## 7. The public entrance test — `/qabul-test/:token`

This is the only unauthenticated write in the module and the part we would most
easily get wrong. Everything in §7.1 is observed behaviour; §7.2 onward is our
design, with each deviation from EduSchool named.

### 7.1 What EduSchool actually does [bundle]

Route registration, in their router, as a **sibling** of `/survey/entry` and
outside the tree guarded by `localStorage.token`:

```js
<Route path="/survey/entry"     element={<SurveyPage/>} />
<Route path="/qabul-test/:token" element={<QabulTest/>} />
```

A dedicated axios instance — **not** the authenticated one:

```js
Hb = axios.create({ baseURL: "…/moderator-api/", withCredentials: true,
                    headers: { Organization: "test" } })
```

Four calls, and only four:

```js
GET  /admission/test/state   ?token
POST /admission/test/start   { token }
POST /admission/test/answer  { token, questionId, optionId }
POST /admission/test/finish  { token }
```

Observed facts, each load-bearing:

1. **Five states**: `lobby`, `in_progress`, `finished`, `blocked`,
   `not_assigned`. A token that resolves to nothing makes the query error and the
   page renders `notFound`.
2. **`not_assigned` still returns the candidate's name** — so their token
   identifies the *candidate*, not the sitting, and the exam is attached
   separately (`/admission/candidate/assign`).
3. **Answers are saved one at a time**, on every option click, and the header
   shows `saving` / `saved`. There is no batch submit.
4. **The timer is server-authoritative.** Every response carries the pair
   `{ deadlineAt, serverNow }`; the client computes
   `Date.now() + (deadlineAt − serverNow)` once and counts down against that, and
   every `answer` response refreshes the pair. The client clock is never trusted.
5. **Error `59505` on `answer` = time is up** → the client opens the time-up
   modal and calls `finish` itself. **Error `59504` = session no longer valid** →
   the client drops its paper and refetches `state`.
6. **`blocked` carries a `deviceLabel`** and the axios instance sends cookies →
   the attempt is bound to one browser by a cookie, and a second device is
   refused by name.
7. **A lobby gate**: four rules (`rule1…rule4`) plus a mandatory "I agree"
   checkbox; the Start button is disabled until it is ticked.
8. **Multi-subject papers**: the lobby lists subject chips, one aggregate
   `questionCount` and one aggregate `timeLimitMin`; the paper groups questions
   into consecutive per-subject blocks with their own progress counters.
9. **On finish the candidate is shown the full review**, including
   `correctOptionId` for every question.
10. **Finish is confirmed** by a modal; the timer path finishes without asking.

### 7.2 Token — issue, shape, lifetime

* 32 bytes from `RandomNumberGenerator.GetBytes`, base64url-encoded → 43 URL-safe
  characters. Not a GUID: a GUID is 122 bits with structure and reads like an
  internal id.
* Stored as **SHA-256 hex only** (`token_hash`), plus `token_hint` = the last 6
  characters for support. Lookup is by hash. This mirrors `TelegramLinkCode`
  **[ours]** and its reasoning: a database copy that leaks must not contain live
  tokens.
* Issued explicitly, per participant, by a person with `admission` — never
  automatically on assignment. Issuing returns the URL **once**; it is never
  returned again by any endpoint (§6.4).
* Lifetime = `[valid_from, valid_until]`, defaulting to the exam's
  `opens_at`/`closes_at`. Outside the window the token behaves exactly like an
  unknown token (§7.5).
* **Re-issue revokes.** "Havolani qayta chiqarish" writes `revoked_at` on the old
  row and inserts a new one. The partial unique index (§5.9) makes two live links
  impossible.

**Deviation from EduSchool, deliberate.** Theirs is a candidate permalink (fact
2 above). Ours is per participant, per exam, revocable and time-boxed. A
permalink cannot be withdrawn for one sitting only, and it turns "the parent
forwarded the link" into a permanent hole.

**Cost of hashing, and the answer.** Staff cannot look the link up later. Support
path: `token_hint` identifies which link a parent is holding; if it must be
re-sent, staff click "Qayta chiqarish", which invalidates the old one. That is
the correct trade and it must be spelled out in the UI: *"Havola faqat hozir
ko'rsatiladi. Yo'qotsangiz — qayta chiqaring, eskisi ishlamay qoladi."*

### 7.3 States and transitions

| State | When | HTTP |
|---|---|---|
| `not_assigned` | token valid; exam `draft` or participant `cancelled` | 200 |
| `lobby` | token valid, exam `published`, inside the window, no attempt yet | 200 |
| `in_progress` | attempt exists, `status='in_progress'`, **this** device | 200 |
| `blocked` | attempt exists, `status='in_progress'`, **different** device | 200 on `/state`; **409** on `/answer` and `/finish` |
| `finished` | attempt `status='finished'` | 200 |
| *(none)* | token unknown, revoked, expired, or exam `cancelled` | **404** |

Three transition rules that are easy to get wrong and must not be:

* **`start` is idempotent.** From the same device it returns the same paper.
  From a different device it returns `blocked` (409). It never re-draws a paper
  and never resets the deadline.
* **`finish` is idempotent.** On an attempt that is already `finished` — by the
  candidate, by the timer or by the sweep — it returns **200 with the existing
  `PublicResultDto`**, not 409. This is required by the time-up flow: the server
  grades on the deadline, the client then calls `finish`, and that call must
  succeed. `not_in_progress` (409) is therefore raised by **`/answer` only**.
* **The device check is dropped once the attempt is `finished`.** After that the
  token alone is enough to read the result. Rationale: the result carries no
  answer key (§7.7), the candidate has usually closed the tab by then, and
  refusing to show them their own score because a cookie expired is a support
  call for nothing.

### 7.4 Device binding

1. `start` generates a 32-byte session value, stores `SHA-256(value)` in
   `exam_attempts.device_session_hash`, and sets

   ```
   Set-Cookie: wk_exam_<attemptId>=<value>; HttpOnly; SameSite=Lax;
               Path=/api/public/exam; Max-Age=<seconds until deadline_at>
               [; Secure]      // only when Request.IsHttps
   ```

   **The cookie name carries the attempt id** so that a candidate who sits two
   exams from the same browser does not present exam A's cookie to exam B. The
   server resolves token → participant → attempt first, then reads
   `Request.Cookies["wk_exam_" + attempt.Id]`.

   **`Secure` is conditional on `Request.IsHttps`.** An unconditional `Secure`
   flag is silently dropped by the browser on `http://localhost`, so every
   developer and every e2e run would see `blocked` on the second request and
   nobody would know why. Production is HTTPS behind Caddy, so the flag is set
   there.

2. Every subsequent `state`/`answer`/`finish` re-hashes the cookie and compares
   in constant time. Mismatch or missing → `blocked` (409 for the write
   endpoints) with `deviceLabel` from `exam_attempts.device_label`.
3. `device_label` is a coarse User-Agent parse — `"Chrome · Windows"`,
   `"Safari · iPhone"`, or `"Noma'lum qurilma"`. Coarse on purpose: the string
   is shown to an unauthenticated stranger.
4. `first_ip` and `user_agent` are recorded for the admin card. IP is **not** part
   of the check — mobile networks rotate addresses mid-exam and we would lock out
   honest candidates.
5. **Escape hatch.** A candidate who clears cookies, switches from the SMS
   browser to Chrome, or has a phone die mid-exam is locked out with progress
   intact. `POST /api/admin/exams/participants/{pid}/unlock-device` (perm
   `admission`) clears `device_session_hash`; the next `start`/`state` re-binds
   to whatever device arrives and the answers already saved are untouched. It
   writes an `AuditLog` row with the caller. This is **not** the same as
   `reset-attempt` (§8.3), which destroys the attempt — an agent must implement
   both, and the UI must label them so nobody reaches for the wrong one:
   *"Qurilma qulfini ochish"* versus *"Urinishni bekor qilish (natija o'chadi)"*.

Same-origin, so no CORS change is needed: the SPA and the API are served from one
host by Caddy and `Program.cs` **[ours]** registers no CORS policy. If a build
ever splits them, the cookie needs `SameSite=None` plus an explicit
`AllowCredentials` origin — write that in the controller comment so whoever
splits them finds it.

### 7.5 Error contract — no enumeration

Unknown, revoked, expired and cancelled all return **the same** response:

```
404  { "code": "not_found", "message": "Havola topilmadi yoki muddati o'tgan" }
```

No candidate name, no exam title, no distinction. A different status or message
for "expired" versus "never existed" turns the endpoint into an oracle for
guessing tokens.

Other codes:

| Status | `code` | When |
|---|---|---|
| 409 | `blocked` | another device holds the attempt; body carries `deviceLabel` |
| 409 | `time_up` | deadline passed; the server has already finished and graded the attempt |
| 409 | `not_in_progress` | **`answer` only** — the attempt is finished or does not exist. `finish` is idempotent and returns 200 (§7.3) |
| 409 | `not_published` | exam is `draft` (the `state` call reports `not_assigned` instead) |
| 400 | `bad_question` | `questionId` is not a row of this attempt's paper |
| 400 | `bad_option` | `optionId` does not belong to `questionId` |
| 429 | `too_many_requests` | rate limit |

The client maps `time_up` → time-up modal + refetch, `not_in_progress` → refetch
state, everything else → the not-found card. This is EduSchool's `59505`/`59504`
behaviour with readable codes.

### 7.6 Abuse limits

* New rate-limit policy `"exam"` in `Program.cs` **[ours,
  `AddRateLimiter`, line 201]**, alongside `"login"` (10/min) and `"telegram"`
  (20/min): **120 requests per minute per IP**. Sized for the real load — one
  `answer` per click, a 60-question paper, a family on one connection — while
  still stopping a script.
* Per-attempt ceiling: `exam_attempts.answered_count` is incremented on every
  accepted `answer`. Above `question_count × 10` the endpoint returns **429** and
  sets `exam_attempts.abuse_flagged = true`, which shows a warning badge on the
  admin card. A candidate who genuinely changes their mind ten times per question
  does not exist.
  It **does not terminate the attempt.** Ending someone's entrance exam because
  a flaky connection retried is a worse outcome than the abuse it would prevent;
  the ceiling exists to stop a script, and 429 stops a script perfectly well.
* `answer` is rejected unless `now <= deadline_at + 5s`. The five seconds absorb
  request latency on the last click and nothing more.
* Request body is `{ token, questionId, optionId }` and **nothing else**. No
  `attemptId`, no `candidateId`, no `examId`, no `score`. Every other value is
  derived server-side from the token. An implementation that accepts an id from
  the client here has failed review.

### 7.7 The answer key must not leave the server

* `PublicPaperDto.options` has exactly `{ id, text }`. There is no `isCorrect`,
  no `correctOptionId`, no `order` of the correct answer.
* The projection is written by hand in the service; `Select` onto the DTO, never
  `Include(...).ToList()` and map later. A test asserts that the raw JSON of
  `start` and of `state`-while-`in_progress` contains neither the substring
  `isCorrect` nor `correctOptionId`.
* Grading happens on the server at `finish`, or by the sweep at deadline.
* **Deviation from EduSchool, deliberate.** They return the full per-question
  review with correct answers to the candidate the moment the test ends
  (fact 9). For an entrance exam sat by successive candidates from one bank, that
  is the bank walking out of the door. Ours returns score, per-subject breakdown
  and finish reason. The per-question review is admin-only
  (`GET /api/admin/exams/participants/{pid}/review`).
  A school setting can restore EduSchool's behaviour if the client insists:
  `SchoolMeta.AdmissionShowAnswersToCandidate` (`boolean not null default false`),
  alongside the existing `TurnstileEnabled` / `GpsEnabled` / `CameraEnabled`
  flags **[ours, `Entities.cs:623,643,655`]**, surfaced in
  `/admin/settings/school`. When false, `PublicResultDto.questions` is `null`
  and the server does not even project the questions.
* Question images are `/uploads/<guid>.<ext>`, served as public static files with
  GUID names from `UploadGuard.SafeName` **[ours]** — unguessable, and the
  directory is not listable. Accepted as is; no token-scoped image proxy.

### 7.8 Client-side rules

* The page uses its **own** axios instance with `withCredentials: true` and **no**
  `Authorization` interceptor. It must not import `api` from
  `schoollms.client/src/api/client.ts` **[ours]** — that instance attaches the
  admin bearer token and, on a 401, wipes `localStorage` and redirects the
  browser to `/login`. A candidate hitting that would lose their paper and an
  admin testing on the same machine would be logged out.
* The route sits outside `ProtectedRoute`, next to `/login`, in
  `schoollms.client/src/App.tsx` **[ours]**.
* Timer: compute the offset once from `{deadlineAt, serverNow}`, tick locally,
  re-sync on every `answer` response. On expiry call `finish` exactly once
  (guard with a ref — EduSchool does, and it matters: the interval fires again
  before the request returns).
* Optimistic UI: paint the chosen option immediately, then `answer`. On
  `not_in_progress`, drop the paper and refetch `state`.
* Lobby: four rules + a mandatory checkbox; Start disabled until ticked.
* Finish: confirm modal for the manual path; the timer path does not ask.
* No answer is clearable once chosen (EduSchool has no clear either). Keeping it
  that way removes a state and a request shape.

---

## 8. Business rules

### 8.1 Publishing an online exam

`POST /exams/{id}/publish` fails with 409 and a specific Uzbek message unless:

1. `status = 'draft'`.
2. At least one `exam_sections` row.
3. For each section: `bank_id` is set, and the bank is `ready` —
   `questions_per_test`, `time_limit_min` and `points_per_correct` are all set,
   and `count(questions) >= questions_per_test`
   **[bundle: the `notEnough` / `unconfigured` / `ready` triple]**.
4. Every question in every used bank has 2–6 options and exactly one correct.
5. `opens_at < closes_at`, and `closes_at` is in the future.

Publish then, in one transaction, copies `points_per_correct` into each section,
computes `max_score = question_count × points_per_correct`, sums
`exams.time_limit_min` if it is still null, and sets `status='published'`.
After publish, sections are frozen (§5.5).

### 8.2 Drawing the paper

At `start`, in one transaction, per section in `order`:

* Take `question_count` questions from `bank_id`, **at random without
  replacement** (`Random.Shared.Shuffle` then `Take`). No seed is stored and none
  is needed — the drawn set is persisted as `exam_answers` rows, so the paper is
  fixed from that moment whatever the random source does next.
* Insert them into `exam_answers` with a global `order` running across sections
  (section 0's questions get 0…n-1, section 1's get n…, and so on), so a
  section's questions are consecutive — that is what the subject blocks and the
  per-subject progress counters render **[bundle: their grouping function walks
  the list and starts a new group whenever `subjectId` changes]**.
* Option order within a question is the stored `order`, **not** shuffled. A
  shuffled option order makes an invigilator's paper copy useless for
  cross-checking and buys nothing once the questions themselves are random.
* `deadline_at = min(started_at + exams.time_limit_min, exams.closes_at)`.

**The bank cannot shrink under a live exam.** `DELETE /admission/questions/{id}`
returns **409** if the question's bank is referenced by any `exam_sections` row
whose exam is `published` (§6.2 already refuses a question that has been
answered; this is the wider rule). Without it, a bank edited between publish and
the first sitting could leave a section unable to draw its `question_count`, and
"take whatever is left" would quietly give one candidate a shorter paper than
another. Deleting is allowed again once the exam is `closed` or `cancelled`.

### 8.3 Grading

Online exams only; a `delivery='manual'` exam is scored by §8.4.

* Triggered by `POST /public/exam/finish` (`finish_reason='manual'`), by the
  deadline sweep (`'timer'`), or by
  `POST /api/admin/exams/participants/{pid}/force-finish` — perm `exams`,
  body `{ reason }`, used when a candidate walks out — (`'admin'`).
  All three call **one** service method. In one transaction:
  * per section: `correct = count(answers where selected option is_correct)`,
    `points = correct × points_per_correct`;
  * write `exam_section_scores`;
  * write the summary onto `exam_participants` (`correct_count`,
    `question_count`, `total_points`, `max_points`, `percent`, `scored_at`,
    `scored_by_user_id = null`);
  * `exam_attempts.status='finished'`, `finished_at`, `finish_reason`;
  * `exam_participants.status='finished'`;
  * if `participant_kind='lead'`, set `leads.admission_status='tested'`;
  * one `AuditLog` row, `EntityType="ExamAttempt"`.
* A background sweep (hosted service, every 60 s) finishes any attempt with
  `status='in_progress' and deadline_at < now()` using `finish_reason='timer'`.
  Needed because a candidate who closes the tab never calls `finish`.
  It reuses the same grading service — no second scoring path.
* `reset-attempt` deletes the attempt, its answers and its scores, revokes the
  invitation, returns the participant to `assigned`, and writes an audit row with
  the supplied reason. It requires **both** `exams` and `admission`; wiping a
  score is not a one-key action.

### 8.4 Manual result entry

* `POST /exams/{id}/entry-table` upserts `exam_section_scores` for the submitted
  rows and recomputes the participant summary. `correct_count` and
  `question_count` stay null (there were no questions).
* `absent: true` sets `exam_participants.status='absent'` and clears the scores.
* `points` outside `[0, max_score]` → 400 naming the row and the subject.
* `scored_by_user_id` = the caller.
* Import validates the same way and reports per-row errors before writing.

### 8.5 Candidate lifecycle

```
none → invited → testing → tested → accepted → enrolled
                                  ↘ rejected
```

* `invited` — a participant row was created for an admission exam.
* `testing` — the attempt started.
* `tested` — the attempt was graded.
* `accepted` / `rejected` — a human decision, set from the candidate card. Never
  automatic: the system does not decide admissions on a score.
* `enrolled` — `POST /leads/{id}/enrol` created the pupil.
* Backward moves are allowed only to `rejected` and only by hand.
* **Cancelling never regresses a fact.** Cancelling a participation, or
  cancelling the whole exam, sets `admission_status` back to `'invited'` **only
  if it is currently `'testing'`**. `tested`, `accepted`, `rejected` and
  `enrolled` are left alone: the candidate really was tested, and withdrawing the
  sitting does not unmake that.
* Deleting a lead that has an `exam_participants` row cascades that row away
  (§5.7). That is intentional: `DELETE /api/admin/leads/{id}` already exists and
  is used by the board; making it fail on a foreign key would break a protected
  screen.

### 8.6 Seasonal marks

* Score 0–100, 2 decimals, half-up, server-clamped.
* Comment either null or ≥3 characters after trim
  **[bundle: their inline editor silently refuses 1–2 characters]**.
* A row with neither score nor comment is not stored; submitting one through
  `bulk` deletes any existing mark for that key.
* `class_id` is a snapshot from the pupil's class at entry time; a later class
  change does not rewrite history.
* Teacher scope: `/api/teacher/seasonal-marks/**` accepts only (class, subject)
  pairs the teacher teaches, checked the way `TeacherPortalController.Authorized`
  **[ours]** already checks the journal. An admin has no such restriction.
* `quarterly` requires `quarter ∈ {1,2,3,4}`. The server checks that a
  `QuarterPeriod` row with that number exists **[ours]** — a soft validation, not
  a foreign key (§5.12) — and rejects with
  `"Bunday chorak sozlanmagan"` if not.
* `QuarterPeriod.GradesOpen` is **not** consulted. It gates the 2–5 quarter grade
  in the journal; conflating the two locks would mean closing the journal for
  grading silently also closes seasonal assessment, which nobody asked for.

---

## 9. What we already have

### 9.1 Reuse, do not rebuild

| Need | Existing thing **[ours]** |
|---|---|
| **Reference implementation for the whole module** | `SchoolLms.Server/Controllers/StudentEvaluationController.cs` + `SchoolLms.Domain/Entities.cs` (`EvaluationType`, `EvaluationGrade`) + `schoollms.client/src/pages/admin/students/` — the closest analogue: catalogue + per-pupil scored rows + admin screen + teacher screen. Copy its shape. |
| Hash-only, expiring, single-use token | `SchoolLms.Domain/TelegramLink.cs` (`TelegramLinkCode`), `SchoolLms.Application/Services/TelegramLinkService.cs` |
| Anonymous endpoint with a rate limit | `SchoolLms.Server/Controllers/TelegramAuthController.cs` (`[AllowAnonymous] [EnableRateLimiting("telegram")]`), `SchoolLms.Server/Controllers/GpsIngestController.cs` |
| Rate-limit policy registration | `SchoolLms.Server/Program.cs:201` |
| Lead CRUD + kanban + stages | `SchoolLms.Server/Controllers/LeadsController.cs`, `LeadStagesController.cs`, `schoollms.client/src/api/services/leads.ts`, `stages.ts` |
| Pupil creation from a payload | `SchoolLms.Server/Controllers/StudentsController.cs`, `StudentPayload` (`Dtos.cs:27`) |
| Excel export / import | `SchoolLms.Application/Services/ExcelExport.cs`, `ExcelImport.cs`; template pattern in `StudentsController` and `JournalController` |
| Image upload | `SchoolLms.Server/Controllers/UploadsController.cs`, `SchoolLms.Application/Services/UploadGuard.cs` (20 MB cap, GUID filenames) |
| Edit history | `SchoolLms.Domain/Entities.cs` `AuditLog`, `SchoolLms.Application/Services/AuditService.cs` |
| Quarters | `SchoolLms.Domain/Entities.cs` `QuarterPeriod`, `SchoolLms.Server/Controllers/SettingsController.cs` |
| Teacher → (class, subject) mapping for the coverage report | `SchoolLms.Application/Services/TeacherActivityReport.cs` |
| Teacher-side scope check | `SchoolLms.Server/Controllers/TeacherPortalController.cs` (`Authorized(classId, subjectId)`) |
| EF model file pattern (separate file per module) | `SchoolLms.Infrastructure/Data/BillingModel.cs`, `GuardianModel.cs`, `AnomalyModel.cs` |
| Raw-SQL GRANT pattern | `SchoolLms.Infrastructure/Migrations/Sql/billing_guards.sql`, loaded by `Migrations/MigrationSql.cs` |
| RBAC test style | `SchoolLms.Tests/Security/RbacMatrixTests.cs` |

### 9.2 What is genuinely missing

Everything in §5 except the two `leads` columns. There is no question bank
(outside `Assignment`), no exam, no attempt, no seasonal mark, no public exam
surface, and no paged list envelope.

### 9.3 Explicit non-goals

Listing them so nobody helpfully builds them:

* Migrating `Assignment`/`TestQuestion` onto the new bank (§2.1).
* A pupil-facing online exam page (`delivery='online'` + `participant_kind='student'`).
  The schema allows it; the screen is not in scope.
* Multi-branch scoping (`branchIds`) — we are single-school.
* `o'zlashtirish` analytics (`/analitics/students/quarter`) — §2.4.
* Restyling, refactoring or "unifying" the Leads board (§2.3).
* EduSchool's server-driven column definitions (`/table-settings/get`).

---

## 10. Build plan

Effort is in focused developer-days for one agent, implementation only —
excluding review and the coordinator's schema assembly.

### Phase A — schema (sequential, coordinator)

| Unit | Files | Depends on | Parallel? | Days |
|---|---|---|---|---|
| **A1 Entities** | `SchoolLms.Domain/Exams.cs` (new), `SchoolLms.Domain/SeasonalMarks.cs` (new), `SchoolLms.Domain/Entities.cs` (2 fields on `Lead`), `SchoolLms.Domain/TeacherPermissions.cs` | — | **no** — `Entities.cs` and `TeacherPermissions.cs` are shared | 1.0 |
| **A2 EF model** | `SchoolLms.Infrastructure/Data/ExamModel.cs` (new), one call added to `AppDbContext.OnModelCreating`, 12 `DbSet`s | A1 | **no** — `AppDbContext.cs` is shared | 1.0 |
| **A3 Migration + GRANT** | one migration, `Migrations/Sql/exam_guards.sql` (new), `MigrationSql.cs`, `AppDbContextModelSnapshot.cs` | A2 | **no** — the snapshot is the classic conflict file | 0.5 |

Table creation order inside A3 (FK order):
`question_banks → questions → question_options → exam_types → exams →
exam_sections → exam_participants → exam_section_scores → exam_invitations →
exam_attempts → exam_answers → seasonal_marks`, then the two `leads` columns,
then the `teachers.permissions` back-fill.

`exam_guards.sql` grants `app_rw` full CRUD on all twelve. None of them holds
money, so the append-only rule of `billing_guards.sql` does not apply; the file
exists only because `ALTER DEFAULT PRIVILEGES` is a manual step that gets
forgotten (see the reasoning already written in `billing_guards.sql`).

### Phase B — backend services and controllers

Everything here starts after A3 and the four units are independent of each other.

| Unit | Files | Depends on | Parallel? | Days |
|---|---|---|---|---|
| **B1 Question bank** | `AdmissionBanksController.cs`, `AdmissionQuestionsController.cs`, `Application/Services/QuestionBankService.cs`, `QuestionImportService.cs`, DTOs | A3, **S1** | **yes** | 2.5 |
| **B2 Exams + manual results** | `ExamsController.cs`, `ExamTypesController.cs`, `ExamResultsController.cs`, `Application/Services/ExamService.cs`, `ExamScoringService.cs`, `ResultImportService.cs` | A3 | **yes** | 3.5 |
| **B3 Online delivery + public endpoint** | `PublicExamController.cs`, `ExamAttemptAdminController.cs` (review / force-finish / reset / unlock-device / invitations), `Application/Services/ExamAttemptService.cs`, `ExamInvitationService.cs`, `ExamDeviceGuard.cs`, `ExamExpirySweep.cs` (hosted), `Program.cs` (rate policy + hosted service) | A3, **B2** (shares `ExamScoringService`), **S3** | no — needs B2's scoring service | 4.5 |
| **B4 Seasonal marks + reports** | `SeasonalMarksController.cs`, `TeacherSeasonalMarksController.cs`, `Application/Services/SeasonalMarkService.cs`, `SeasonalCoverageReport.cs`, `SeasonalPivotQuery.cs` | A3 | **yes** | 3.0 |
| **B5 Candidate projection** | `LeadsController.cs` (4 additive endpoints), `Application/Services/CandidateQuery.cs` | A3, B2 | no — needs `exam_participants` populated by B2 | 1.5 |

### Phase C — frontend

| Unit | Files | Depends on | Parallel? | Days |
|---|---|---|---|---|
| **C1 Test bazasi** | `pages/admin/admission/BanksPage.tsx`, `BankDetailPage.tsx`, `QuestionFormModal.tsx`, `QuestionImportModal.tsx`, `api/services/admissionBanks.ts` | B1, **S2**, **S4** | **yes** | 3.0 |
| **C2 Nomzodlar** | `pages/admin/admission/CandidatesPage.tsx`, `CandidateCardPage.tsx`, `api/services/candidates.ts` | B5, S2, S4 | **yes** | 2.5 |
| **C3 Imtihonlar** | `pages/admin/exams/ExamsPage.tsx`, `ExamTypesPage.tsx`, `ResultsPage.tsx`, `ResultEntryPage.tsx`, `ExamFormModal.tsx`, `api/services/exams.ts` | B2, S2, S4 | **yes** | 4.0 |
| **C4 Mavsumiy baholash (admin)** | `pages/admin/seasonal-marks/SeasonalMarksPage.tsx`, `SeasonalEntryPage.tsx`, `BySubjectsPage.tsx`, `CoverageReportPage.tsx`, `api/services/seasonalMarks.ts` | B4, S2, S4 | **yes** | 4.0 |
| **C5 Mavsumiy baholash (teacher)** | `pages/teacher/seasonal-marks/*`, `config/navigation.ts` (teacher list) | B4, C4 (reuses its entry grid), S2 | no | 1.5 |
| **C6 Public exam page** | `pages/public/QabulTestPage.tsx`, `Lobby.tsx`, `Paper.tsx`, `Timer.tsx`, `DeviceLock.tsx`, `ResultView.tsx`, `api/publicExamClient.ts` (own axios instance), `App.tsx` route | B3, **S5** | no — S5 is a shared file | 4.0 |

### Phase D — tests

| Unit | Files | Depends on | Parallel? | Days |
|---|---|---|---|---|
| **D1 Public exam security** | `SchoolLms.Tests/Security/PublicExamTests.cs` | B3 | **yes** | 2.0 |
| **D2 RBAC matrix** | `SchoolLms.Tests/Security/AdmissionRbacTests.cs` | B1, B2, B4 | **yes** | 1.5 |
| **D3 Scoring + import** | `SchoolLms.Tests/Exams/ScoringTests.cs`, `QuestionImportTests.cs`, `SeasonalMarkTests.cs` | B1, B2, B4 | **yes** | 2.0 |
| **D4 Migration** | `SchoolLms.Tests/Exams/ExamMigrationTests.cs` | A3 | **yes** | 0.5 |

**Total ≈ 42 developer-days.** Longest dependency chain:
A1 → A2 → A3 → B2 → B3 → C6 → D1 ≈ 16.5 days. With four agents running the
parallel units, wall-clock is bounded by that chain, not by the total.

### Reports, not storage

Called out separately because the temptation to add a table is real:

| Screen / endpoint | Reads from | New table? |
|---|---|---|
| Mavsumiy baholash hisoboti (13) — `/seasonal-marks/coverage` | `seasonal_marks` + schedule + `students` | **no** |
| Fanlar kesimida (12) — `/seasonal-marks/by-subjects` | `seasonal_marks` pivot | **no** |
| Natijalar (8) — `/exams/results` | `exam_participants` + `exam_section_scores` | **no** |
| Candidate list (1) and card (2) | `leads` ⋈ `exam_participants` ⋈ `exam_invitations` ⋈ `exam_attempts` | **no** |
| Bank `state` badge (3) | derived from `questions_per_test` vs `count(questions)` | **no** |
| Seasonal mark history (10) | `audit_logs` | **no** |
| `questionsCount` on the bank list | `count(*)` | **no** |

---

## 11. Shared files — sequential, never parallel

Every one of these is touched by more than one unit. Assign each to exactly one
agent, in this order, and let the others rebase.

| File | Touched by | Why it conflicts |
|---|---|---|
| `SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | A3 | EF rewrites the whole file; two parallel migrations destroy it |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | A2 | `DbSet` block + one `Apply` call |
| `SchoolLms.Domain/Entities.cs` | A1 | two fields on `Lead`; everyone else adds new files instead |
| `SchoolLms.Domain/TeacherPermissions.cs` | A1 | one constant + `All` |
| **S1** `SchoolLms.Server/Controllers/AdminPermAttribute.cs` | B1 | the `GatedRead` property (§4.3) — must land before B1 and C1 |
| **S2** `schoollms.client/src/config/constants.ts` | C1–C5 | three `adminPermissions` keys + one `teacherPermissions` key |
| **S3** `SchoolLms.Server/Program.cs` | B3 | the `"exam"` rate policy + the sweep hosted service |
| **S4** `schoollms.client/src/config/navigation.ts` | C1–C5 | three new admin sections + one teacher entry |
| **S5** `schoollms.client/src/App.tsx` | C1–C6 | 14 admin/teacher routes **and** the one public route outside `ProtectedRoute` |
| `SchoolLms.Infrastructure/Migrations/MigrationSql.cs` | A3 | one embedded-resource name |
| `SchoolLms.Domain/Entities.cs` (again) | A1 | one flag on `SchoolMeta` (§5.13) — same file as the `Lead` fields, one edit |
| `SchoolLms.Server/Controllers/SubjectsController.cs` | B4 | one guard clause before delete (§5.14) |
| `SchoolLms.Server/Controllers/ClassesController.cs` | B4 | one guard clause before delete (§5.14) |
| `schoollms.client/src/pages/admin/settings/SchoolSettings.tsx` + `SchoolController.cs` / `SettingsController.cs` school DTO | C1 | one checkbox for `AdmissionShowAnswersToCandidate` (§7.7) and the field on `SchoolInfoDto` |

Recommended order: `A1 → A2 → A3 → S1 → S3 → (B1‖B2‖B4) → B3 → B5 → S2 → S4 → S5 → (C1‖C2‖C3‖C4) → C5 → C6 → (D1‖D2‖D3‖D4)`.

**And the six protected Leads files are not on this list because nobody may touch
them (§2.3).**

---

## 12. Definition of Done

Machine-verified, per the global rules.

Schema and migration
- [ ] `dotnet ef database update` applies cleanly on an empty database and on a
      copy of the current one.
- [ ] The migration was read by a human and contains **no** `DropTable`,
      `DropColumn` or `DropIndex` for anything outside this module.
- [ ] `exam_guards.sql` runs, and skips with a NOTICE when `app_rw` is absent
      (the pattern in `billing_guards.sql`).
- [ ] Every existing `teachers.permissions` array contains `"seasonalMarks"`
      after the migration.
- [ ] Inserting two `question_options` with `is_correct = true` for one question
      fails at the database, not in C#.
- [ ] Inserting two `seasonal_marks` for the same `(student, subject, period)` —
      including two `yearly` ones — fails at the database.
- [ ] Two live `exam_invitations` for one participant fail at the database.
- [ ] Two `exam_attempts` for one participant fail at the database.

Public endpoint (the security surface)
- [ ] The JSON returned by `POST /api/public/exam/start` contains neither
      `isCorrect` nor `correctOptionId`. Asserted on the raw string.
- [ ] Same assertion for `GET /state` while `in_progress`.
- [ ] Unknown, revoked, expired and cancelled tokens all return **byte-identical**
      404 bodies.
- [ ] A second browser (no cookie) gets `blocked`, not the paper.
- [ ] Over plain HTTP the `Set-Cookie` header carries **no** `Secure` flag, and
      the second request from the same client is **not** `blocked` — i.e. the
      whole flow passes end-to-end against `http://localhost`.
- [ ] Two attempts by one person on two different exams from the same browser
      do not block each other (per-attempt cookie name).
- [ ] `POST /unlock-device` lets a new device continue **with the answers
      already saved still selected**, and writes an `AuditLog` row.
- [ ] `finish` called twice returns 200 both times with the identical result;
      the second call does not re-grade and does not change `finished_at`.
- [ ] `answer` after `deadline_at + 5s` returns `time_up` and the attempt is
      graded exactly once.
- [ ] Exceeding the per-attempt answer ceiling returns 429 and sets
      `abuse_flagged`; the attempt is still `in_progress`.
- [ ] `answer` with a `questionId` from another attempt returns `bad_question`
      and writes nothing.
- [ ] The 121st request in a minute from one IP returns 429.
- [ ] An abandoned attempt is finished by the sweep with `finish_reason='timer'`.
- [ ] The page never sends an `Authorization` header and never redirects to
      `/login` (asserted in the frontend test, and by grepping the page's imports
      for `api/client`).

RBAC
- [ ] `AdminPermAttribute` without `GatedRead` behaves exactly as before —
      the existing suite is green with no changes.
- [ ] A `staff` account **without** `admission` gets 403 on
      `GET /api/admin/admission/banks/{id}/questions`.
- [ ] A `staff` account **with** `admission` gets 200 on the same call.
- [ ] Every new endpoint appears in `AdmissionRbacTests` with an explicit expected
      status per role — one `[InlineData]` per cell, in the style of
      `RbacMatrixTests`.
- [ ] A teacher cannot write a seasonal mark for a (class, subject) they do not
      teach.
- [ ] `POST /leads/{id}/enrol` is refused to a caller holding only `admission`.

Behaviour
- [ ] Publishing an exam whose bank has fewer questions than `questions_per_test`
      is refused with the `notEnough` message.
- [ ] A published exam's sections cannot be edited.
- [ ] Deleting a question whose bank feeds a `published` exam returns 409.
- [ ] Editing `points_per_correct` on a bank does not change a finished exam's
      score.
- [ ] Cancelling a participation whose `admission_status` is `tested` leaves it
      `tested`; cancelling one that is `testing` returns it to `invited`.
- [ ] Manual entry rejects `points > max_score`.
- [ ] `seasonal-marks/bulk` with an empty score and empty comment deletes the row
      and writes an `AuditLog` `delete`.
- [ ] Coverage percent is capped at 100 when a teacher has more marks than pupils.
- [ ] Deleting a lead with an exam participation succeeds and cascades.

Protected design
- [ ] `git diff --stat` shows **zero** changes under
      `schoollms.client/src/pages/admin/leads/`.
- [ ] `GET/POST/PUT/PATCH/DELETE /api/admin/leads` return exactly the shapes
      `api/services/leads.ts` expects — the board still loads, drags and saves.

Build
- [ ] `dotnet test` green.
- [ ] `npm run build` green.
- [ ] No `console.log`, no `TODO` left in shipped code.

---

## 13. Open questions

Each carries my recommendation. **Silence means the recommendation stands** — an
implementing agent follows it and logs one line in `docs/ASSUMPTIONS.md`.

1. **Should the candidate see the correct answers after finishing?**
   EduSchool does **[bundle]**. It leaks the bank to every subsequent candidate.
   → **Recommendation: no.** Score and per-subject breakdown only. Setting
   `admission.showAnswersToCandidate`, default `false`.

2. **One token per candidate (EduSchool) or one per exam participation (mine)?**
   Theirs is a permalink that cannot be withdrawn for a single sitting.
   → **Recommendation: per participation**, revocable and time-boxed (§7.2).

3. **The link is shown once and cannot be recovered — acceptable?**
   The alternative is storing the token in clear so staff can re-copy it.
   → **Recommendation: hash only.** "Qayta chiqarish" is the recovery path and it
   revokes the old link. Consistent with `TelegramLinkCode` **[ours]**.

4. **Does the school want a block test taken on screen?**
   EduSchool's is paper-and-keyboard-entry only **[bundle]**. Our schema supports
   online; only the pupil-facing page is missing (~4 days).
   → **Recommendation: not now.** Ship manual entry, matching what they use today.

5. **Seasonal mark on 0–100 while the journal is on 1–5 — two scales in one
   product?** EduSchool uses 0–100 **[bundle]** and the client is used to it.
   → **Recommendation: keep 0–100** and never convert between the two. Label the
   column `Ball (0–100)` on every screen so nobody reads 4 as "yaxshi".

6. **Who may enrol a candidate as a pupil?**
   → **Recommendation: `admission` + `students` together.** Creating a pupil
   creates a billing subject; it should not be one clerk's single key.

7. **How many questions may one exam have in total?**
   The paper is materialised at `start` (§5.11) so a 500-question exam is 500
   rows and a very long page.
   → **Recommendation: cap at 200 per sitting**, refuse publish above it with a
   clear message. Raise it when someone actually asks.

8. **Should `admission_status` be visible on the Leads board?**
   It would answer "has this lead been tested?" without leaving the board.
   → **Recommendation: no** — the board is frozen (§2.3), and the candidate list
   answers it. Revisit only if the client asks, and then only with their explicit
   approval of the visual change.

9. **Excel import: accept `.xls` as well as `.xlsx`?**
   EduSchool accepts both **[bundle]**; `ExcelImport` **[ours]** accepts only
   `.xlsx`.
   → **Recommendation: `.xlsx` only**, with the existing Uzbek error message. A
   second parser for a format Excel has not written by default since 2007 is not
   worth its bugs.

10. **The Nomzodlar and Blok Test screens were not in the bundle.** Their field
    lists in §5.4–§5.8 are my design, not observed fact.
    → **Recommendation: build as specified**, and if the client can screen-share
    those two screens for ten minutes before Phase C starts, reconcile then.
    The gap is in column choice and labels, not in the data model — the endpoint
    names constrain the model tightly enough.
