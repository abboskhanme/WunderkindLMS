# Gaps in the modules we already ship

**Status:** analysis + specification. No code, no migration, no entity.
**Date:** 2026-09-13 · **Author:** plan-architect

This file covers the five modules where we already ship *something* and EduSchool ships
*more*: **O'quv bo'limi**, **Moliya**, **Analitika**, **Sozlamalar**, **Xulq-atvor**. For each:
what they have, what we have, and the delta — with our file names, not hand-waving.

The four whole products we have never started have their own specs and are **not repeated
here**:

| Product | Spec | Owns |
|---|---|---|
| HR | `docs/modules/hr.md` | payroll, timesheet, **Bonus**, **Jarima**, staff requests, loans, work schedules, missed lessons, dismissal report, tax rates, `departments`, `job_titles`, `hr_*` tables |
| Gamification | `docs/modules/gamification.md` | the whole coin economy, `coin_*` tables, `point_transactions` |
| WareHouse | `docs/modules/warehouse.md` | stock, library, recipe-costed canteen, `wms_*` tables — **and the `rooms` table** (§2.1) |
| Admission / Block Test | `docs/modules/admission-and-testing.md` | admission pipeline, entrance test, block test, seasonal assessment, `exam_*` / `admission_*` / `block_test_*` / `seasonal_marks` |
| Staff task board | `docs/modules/staff-tasks.md` | `staff_task*` tables, the `tasks` permission |

Where a gap here touches one of those, this file **cross-references and stops**. Two specs
describing the same table is how the schema ends up with two versions of it.

---

## 0. Method, and how to read a claim

| Label | Meaning |
|---|---|
| **[bundle]** | Read out of `.eduschool-bundle/all.js`, `edu-menu.json` or `edu-endpoints.txt`. A literal string or path is quoted. The tenant was never written to (`CLAUDE.md` hard rule). |
| **[ours]** | Read out of this repository at the commit this file was written on. |
| **[inferred]** | Not in the bundle; deduced from endpoint names plus a sibling screen that *is* in the bundle. The reasoning is given. |
| **[decision]** | Ours, with the reason. |

**Two constraints that shape every recommendation below.**

1. **Single branch, confirmed** (`docs/ASSUMPTIONS.md`, 2026-09-13, client's words:
   *"bizda bitta filial yetadi, hozircha"*). Nothing here proposes cross-branch aggregation,
   a `branch_id` on a list query, or the `Administrator` module. EduSchool's `Branch` request
   header, `/cashboxes-branch`, `/cashbox/branch-transfer`, `/monitoring/branch-series` and
   `ANALITICS_ALL.BRANCH_STATUS` are all noted as **out of scope for that reason**, not because
   they are hard.
2. **We rebuild functionality, never appearance** (`CLAUDE.md`). Where EduSchool's screen is
   better *arranged*, that is recorded as an observation. It is never a recommendation to make
   our screen look like theirs. The **Lidlar** board is design-frozen and nothing in this file
   touches it.

**Before you propose a table name, grep two places:**

```
deploy/init-roles.sql
SchoolLms.Infrastructure/Migrations/Sql/*.sql
```

`deploy/init-roles.sql:172-179` already REVOKEs `UPDATE, DELETE` from `app_rw` on
`payments`, `payment_allocations`, `ledger_entries`, `access_events` and `point_transactions` —
including tables that **do not exist yet**, so that the script locks them the moment a
migration creates them. A name in that list carries protection. Renaming it, or creating a
similar table under a different name, silently drops the protection and nothing fails.

---

## 1. Three things the brief says we lack that we already have

Correcting these first, because building them again would be pure waste.

### 1.1 `ARCHIVE_STUDENTS` — we have it **[ours]**

| | |
|---|---|
| Data | `SchoolLms.Domain/Entities.cs` → `Student.IsArchived`, `ArchivedAt`, `ArchiveReason`, `ArchivedWithClass` |
| API | `SchoolLms.Server/Controllers/StudentsController.cs`: `GET /api/admin/students/archived` (line 54), `POST /api/admin/students/{id}/archive` with `{reason}` (line 302), `POST /api/admin/students/{id}/restore` with an optional `newPassword` (line 334). `GET /api/admin/students?includeArchived=true`. Every archive writes an `audit_log` row. |
| UI | `schoollms.client/src/pages/admin/students/StudentsPage.tsx` — a **Faol / Arxiv** tab toggle (`type Tab = 'active' \| 'archived'`, line 32) with counts, an "Arxiv sanasi" column, and a reason modal. |
| Client service | `schoollms.client/src/api/services/students.ts` → `getArchivedStudents` (111), `archiveStudent(id, reason)` (121), `restoreStudent(id, newPassword?)` (130) |
| Extra we have that they do not | `ArchivedWithClass` — archiving a class archives its students and unarchiving brings back *only* those, leaving individually-archived students alone. |

**Real delta** (small): EduSchool's reason is a **catalogue** entry, not free text —
`PUT student/archive-with-reason { reasonId, archivedReason }` where the options come from
`GET /reasons?type=student-archive` plus a hard-coded `{_id:"other"}` **[bundle]**. They also
have bulk archive (`/student/archive-many-with-reason`) and a `reason-type` entity shared with
attendance reasons and class-removal reasons.

### 1.2 `STUDENTS_LOCATIONS` — we have it **[ours]**

| | |
|---|---|
| Data | `Student.Latitude`, `Longitude`, `LocationAddress`, `LocationUpdatedAt` |
| API | `SchoolLms.Server/Controllers/LocationsController.cs`, `[AdminPerm("app")]`, `GET /api/admin/locations` |
| Service | `SchoolLms.Application/Services/GpsService.cs` (bus tracking is separate: `GpsController`, `GpsIngestController`) |
| UI | `schoollms.client/src/pages/admin/locations/LocationPage.tsx` — Leaflet map (`react-leaflet`, already a dependency), pins per student, popup with F.I.SH / class / address / updated-at, class filter |
| Nav | `Ilova → Joylashuv` (`/admin/locations`) |

**Real delta:** EduSchool has four *query* shapes we do not — `/locations/bypoint`,
`/locations/byname`, `/locations/bounding-box` **[bundle]** — i.e. "who lives within N metres of
this point", "search by address text", "everyone inside the current map viewport". That is a
route-planning tool (which children could share a bus stop), not a map. Our page renders every
student and filters client-side, which is fine at 500 students and stops being fine at 5 000.

### 1.3 The rest of the brief's O'quv bo'limi list is correct

`ROOMS`, `CERTIFICATES` and `GROUPS` we genuinely do not have. §2.

---

## 2. O'quv bo'limi (`academicDepartment`)

### 2.1 Xonalar / Rooms — **owned by the WareHouse spec**

**[bundle]** `GET rooms/all`, `GET /rooms/pagin`, `POST|PUT /rooms`, `POST rooms/multiple`,
`DELETE rooms/{id}`; `GET building/pagin`. Permissions `getRooms`, `editRooms`, `deleteRooms`,
`getBuilding`, `editBuilding`, `deleteBuilding`.

Model, read from the room form **[bundle]**:

```
Room     { _id, name, buildingId → building{_id,name}, maxStudentsCount:int|null }
Building { _id, name }
rooms/multiple  { count:int (1..50, validated client-side "max 50"), buildingId }  // bulk create
```

Row actions on the Rooms list: edit, **delete**, and **"Xona jihozlari"** →
`/wms/room-asset?roomId=<id>` — the room register is what the warehouse's asset module hangs
off.

**[ours]** We have `SchoolClass.Room` — a nullable free-text string
(`SchoolLms.Domain/Entities.cs:214`). Nothing else. No room entity, no building, no capacity,
no booking.

**Do not specify or create `rooms` here.** `docs/modules/warehouse.md` §3.2 already claims it
as a shared, sequential prerequisite (task `W-26`), with the exact shape from `docs/SPEC.md`
§3.3, seeded from `select distinct room from classes where room is not null and room <> ''`,
and an explicit rule: *"`rooms` is a shared file … must not be created by two modules"*.
`docs/SPEC.md` §3.3 also has it, for the schedule builder. Whichever lands first creates it.

**Delta this file adds on top of the warehouse version** — three things EduSchool has that
neither `SPEC.md` §3.3 nor the warehouse spec covers:

| Addition | EduSchool | Cost | Verdict |
|---|---|---|---|
| `buildings` table + `rooms.building_id` | `building/pagin`, a required select on the room form | ~4 h | **Skip for now.** One building. `SPEC.md` §3.3's free-text `rooms.building` column covers a second one without a migration. Revisit if the client opens a second site. |
| `rooms.max_students_count` | `maxStudentsCount` on the form | 0 h | Already in `SPEC.md` §3.3 as `capacity smallint default 30`. Same field, keep the SPEC name. |
| Bulk create N rooms | `rooms/multiple { count, buildingId }` | ~2 h | **Do it.** Seeding 24 classrooms one modal at a time is the sort of thing that makes a school decide the software is not worth it. |

**Verdict:** not our work. Cross-reference and move on. Add the two lines above to the
warehouse spec's `W-26` when it is built.

### 2.2 Arxiv o'quvchilar — we have it; the delta is the reason catalogue

See §1.1. What is genuinely missing:

**[bundle]** A shared `reasons` catalogue, keyed by type:

```
GET  /reasons?type=<t>        POST|PUT /reason        GET /reason-type
observed types: "student-archive", "student-remove"
```

The same catalogue backs attendance reasons and — with the general setting
`makeClassStudentRemoveReasonRequired` — removing a student from a class **[bundle]**.

**[ours]** We have `AbsenceReason` (`Entities.cs:355`: `Name`, `Short`, `IsLate`, `Points`) and
`DisciplineReason` (`Entities.cs:377`: `Name`, `Points`). Two reason tables already, each
purpose-built. Archive reason is free text.

**[decision] Do not build a generic `reasons` table.** Reason: we already have two typed
reason tables and they carry type-specific columns (`IsLate`, `Short`, `Points`) that a generic
table would have to hold as nullable columns or JSON. A third typed table
(`student_archive_reasons { id, name, is_active, position }`) is 3 h and stays honest.

| Item | Cost | Verdict |
|---|---|---|
| `student_archive_reasons` catalogue + a select (with "Boshqa" → free text) on the archive modal | 3 h backend + 2 h frontend | **Do it.** It is the difference between an archive list you can group and 400 unique sentences. |
| Bulk archive (`/student/archive-many-with-reason`) | 3 h | **Do it.** End-of-year graduation is a bulk operation by nature. |
| Generic `reason_types` | — | **Decline.** |

### 2.3 Sertifikat / Certificates — genuinely missing

**[bundle]** Two entities and a filter integration.

```
GET|POST|PUT|DELETE  /certificate      ·  GET /certificates          (+ export)
GET|POST|PUT|DELETE  /certificate-type ·  GET /certificate-types
Settings menu:  SIDEBAR.SETTINGS_ALL.CERTIFICATE_TYPES
Route:          /certificates            (page ids: certificates-page, certificate-add,
                                          certificate-type-add, certificate-results)
Permission:     none of its own — the menu entry uses `getStudents`
```

Fields, from the i18n keys **[bundle]**:

```
certificate.student · certificate.type · certificate.type_name · certificate.subject
certificate.teacher · certificate.number · certificate.score · certificate.issued_at
certificate.expires_at · certificate.file · certificate.open_file · certificate.comment
certificate.is_sat_hint · certificate.results_title · certificate.tab_title
```

`certificate.is_sat_hint` is the tell: a certificate type can be flagged as a
**standardised test** (SAT/IELTS-shaped), which is why there is both a `score` and a separate
**"Natijalar"** tab — a score table, not just a document register.

It is wired into the student list as two filters **[bundle]**:
`certificateTypeIds` (multi-select) and `certificateTeacherId` — i.e. *"show me every child who
holds an IELTS certificate"* and *"…issued under this teacher"*.

**[ours]** Nothing. The nearest neighbours are `Contract` / `ContractTemplate`
(`SchoolLms.Server/Controllers/ContractsController.cs`, `ContractService.cs`) for
document-with-a-number-and-a-file, and `EvaluationType` / `EvaluationGrade` for
score-per-student-per-type.

**Proposed model [decision]** — two small tables, nothing clever:

```
certificate_types (
  id uuid pk, name text not null unique, is_scored boolean not null default false,
  is_active boolean not null default true, created_at timestamptz not null default now())

certificates (
  id uuid pk,
  student_id  text not null references students(id) on delete cascade,
  type_id     uuid not null references certificate_types(id) on delete restrict,
  subject_id  text null references subjects(id),
  teacher_id  text null references teachers(id),
  number      text null,
  score       numeric(6,2) null,      -- non-null only when type.is_scored
  issued_on   date not null,
  expires_on  date null,
  file_url    text null,              -- via the existing UploadsController + UploadGuard
  comment     text null,
  created_by  text not null references app_users(id),
  created_at  timestamptz not null default now())

index certificates_student (student_id, issued_on desc)
index certificates_type    (type_id)
check  (score is null) or (score >= 0)
check  expires_on is null or expires_on >= issued_on
```

Screens: a **Sertifikatlar** list under O'quv bo'limi (filters: type, student, teacher, date
range, expiring-soon), a form modal, a **Natijalar** tab (student × scored type, latest score),
and a certificates block on `StudentDetailPage.tsx`. Types are managed in Sozlamalar.

Reuse: `UploadsController` + `UploadGuard` for the file, `ExcelExport.cs` for the export,
`[AdminPerm("students")]` for RBAC (matching EduSchool, which does not give it its own
permission).

| | |
|---|---|
| Cost | ~14 h backend, ~16 h frontend |
| Verdict | **Do it — cheap, and the client asks for it constantly in a school that runs external exams.** Ship the register first; the Natijalar tab is a second, optional pass. |

### 2.4 O'quvchilar manzili — we have it

See §1.2.

| Item | Cost | Verdict |
|---|---|---|
| Server-side bounding-box / by-point / by-name queries | 8 h | **Defer.** Client-side filtering is correct at 500 students. Revisit only if the client asks for bus-route planning, at which point it is a real feature with a real brief. |

### 2.5 `Group` separate from `Sinf` — the expensive one

This is the only structural gap in O'quv bo'limi, and it is worth its own section because the
cost is not in the feature, it is in the schema underneath it.

#### 2.5.1 What EduSchool has **[bundle]**

Read out of the group form and list (bundle offset ≈ 1 418 300):

```
Group {
  _id
  name              : text
  subjectId         : → subject      // options come from `subjects/all?isGroupsSubject=true`
                                     //   — a subject must be FLAGGED as groupable
  grades            : int[]          // which year-groups feed it, e.g. [5,6]
  classIds          : string[]       // which classes feed it
  gender            : 'male'|'female'|null   // optional single-sex group
  supportTeacherIds : string[]
  moderatorIds      : string[]       // staff owners
  groupStudents     : GroupStudent[]
  state             : active | archive
}
GroupStudent { _id, groupId, student{_id, fullName}, class{grade, letter} }
```

```
GET  /groups/pagin        ?grades=[…]&state=archive&search=
GET|POST|PUT  /group      ·  DELETE /group/{id}   (labelled "Arxivlash", not "O'chirish")
GET  /group-student?groupId=…
POST groups/transfer      { groupStudentId, toGroupId }     // move one student between groups
POST class-student/transfer                                  // move between classes
GET  class-student/get    ·  /class-student
GET  /students/classes    ·  /student/total-days-in-classes
GET  /attendances/class/group/v2
```

Row actions on the group list: **duplicate** (`state: {duplicateFromGroupId}`), edit, archive,
and a **"Ko'rish"** button into the group's student roster.

Three facts that matter more than the field list:

1. **`SchoolClass` in EduSchool has `grade` and `letter` as separate columns**
   (`class.grade`, `class.letter`, rendered `${grade}-${letter}`) **[bundle]**.
2. **The journal is driven by groups, not only by classes.** The journal menu entry prefetches
   *both* `/class/pagin?types=["class"]` **and** `/groups/pagin` **[bundle]**. And
   `/class/pagin` takes a `types` filter, which means classes and groups share a listing
   endpoint on their side.
3. **A group spans classes.** `classIds` is an array. A group is "the 12 children from 5-A, 5-B
   and 6-A who take Advanced Maths", scheduled and graded independently of their homeroom.

#### 2.5.2 What we have **[ours]**

```
Student.ClassName : string           // Entities.cs:101 — a NAME, not an id
Student.SubGroup  : int              // Entities.cs:118 — 0 | 1 | 2, nothing else
SchoolClass       { Id, Name, Grade, Language, MonthlyFee, Room, IsArchived, ArchivedAt }
ScheduleLesson.SubGroup : int        // a split lesson targets subgroup 1 or 2
```

`Student.SubGroup` is documented in the entity itself as *"0 = guruhsiz, 1 = birinchi guruh,
2 = ikkinchi guruh … faqat o'quv yili boshida (jurnal yozuvlari hali yo'q paytda)
o'zgartirilishi mumkin"*. The UI enforces exactly three buckets:
`schoollms.client/src/pages/admin/classes/ClassGroupsModal.tsx` — `type Bucket = 0 | 1 | 2`,
locked once the year starts, with a `superadmin` override.

So our model is: **one student, one class, optionally one of two halves of that class.**

#### 2.5.3 What it costs to express a real Group

Not "add a table" — the table is the easy part. Here is everything that assumes
`one student → one class`, found by reading the code:

| Assumption | Where | What breaks |
|---|---|---|
| A student's class is a **string on the student row** | `Student.ClassName`; used in `JournalController`, `AttendanceController`, `ChatMessage.ClassName`, `Broadcast`, `MessagesController` (`GET /messages/chat/{className}`), `ClassAnalyticsController`, `RatingService`, `StudentReportBuilder`, `Contract`, the Mini App | There is no join table to add a second membership to. Every one of these reads `s.ClassName == x`. |
| The class **chat** is keyed by class **name** | `ChatMessage.ClassName`, `MessagesController` | A group has no chat, or needs a second keying scheme |
| A lesson belongs to a **class** and optionally a **subgroup int** | `ScheduleLesson { ClassName?, SubGroup }`, `ScheduleTemplate`, `WeekAssignment`, `ScheduleMath.cs` | A group lesson has no class. `SubGroup` cannot name which group. |
| The journal cell is `(class, subject, date, period, subgroup)` | `JournalEntry`, `JournalService.cs`, `LessonNote` | A group's journal has no class to key on |
| Teacher salary counts **scheduled lessons per teacher** | `TeacherSalaryCalc.cs`, `SalaryLedger.cs`, `SalaryRatesController` | Group lessons must be counted exactly once, not once per feeding class |
| Grade reports aggregate **by class** | `ClassAnalyticsController` (`grades-report/school|class|student`), `Analytics.cs`, `SubjectProgressService.cs` | "Sinf o'zlashtirishi" becomes ambiguous when half the marks came from a cross-class group |
| Attendance percentage is over **class lessons** | `AttendanceController`, `SubjectProgressService.ClassSlotsAsync` | Same |
| Billing is per **student**, class only supplies a default fee | `SchoolClass.MonthlyFee`, `SubscriptionService.cs` | Unaffected — the one part that is already safe |

**Cost, honestly:**

| Slice | Work | Hours |
|---|---|---|
| A | `class_memberships (student_id, class_id, kind: homeroom\|group, from, to)`, backfilled from `Student.ClassName`; `SchoolClass.Letter`; keep `ClassName` as a **generated / maintained mirror** so nothing breaks on day one | 24 |
| B | `study_groups` + `study_group_members` + subject `is_groupable` flag; group CRUD, roster, transfer, duplicate, archive | 30 |
| C | Schedule: `ScheduleLesson.GroupId` alongside `ClassName`; conflict rules must now consider a student who is in a class lesson **and** a group lesson in the same period — this is the genuinely hard part | 40 |
| D | Journal + attendance keyed by `(group_id)` as well as `(class, subgroup)`; the journal grid gains a class/group switcher | 36 |
| E | Reports and salary: teach every aggregate to count a group lesson once and attribute it correctly | 30 |
| F | Migration + backfill + a full regression pass over journal, schedule, reports and salary | 24 |
| | **Total** | **~184 h ≈ 5 developer-weeks** |

And that is with the schedule builder rewrite that `docs/SPEC.md` §6 Phase 2 already plans —
doing this **during** Phase 2 costs roughly half as much as doing it after, because C and D
overlap almost entirely with work that is happening anyway.

> **Warning about the cheap version.** Somebody will propose "just let `SubGroup` be 0–9
> instead of 0–2". That does not work: a subgroup is *inside one class*, and the whole point of
> a group is that it spans classes. It would produce "group 3" in 5-A and "group 3" in 5-B
> which are unrelated, and every report would silently merge them.

#### 2.5.4 Verdict

**Decline as a standalone feature. Fold slices A and B into Phase 2 (schedule) if — and only
if — the client confirms they actually teach cross-class groups.**

Ask exactly one question: *"Bir nechta sinfdan yig'ilgan guruhga alohida dars o'tasizmi
(masalan, kuchli ingliz tili guruhi 5-A va 5-B dan)? Yoki har dars faqat bitta sinf bilanmi?"*

- If **no** — and for a school with 24 classes running a standard curriculum this is the likely
  answer — the existing 0/1/2 split covers language and PE splits, and 184 hours are saved.
- If **yes**, it is a Phase 2 line item, not a bolt-on, and slice A alone (a real membership
  table with `ClassName` kept as a mirror) is worth doing regardless because it unblocks
  everything else later at no behaviour change today.

### 2.6 Also in their O'quv bo'limi that the brief did not list **[bundle]**

For completeness, since it changes the shape of the module:

- `POST /import/students` + `/import/students/preview` + `/confirm` + `/shablon` — an Excel
  import wizard with a preview step. **[ours]** We have `ExcelImport.cs` and `shablon.xlsx`;
  whether the preview step exists is out of scope here.
- `/student/status/pagin`, permissions `getStudentStatus` / `createStudentStatus` / … — a
  user-defined **student status** catalogue (separate from archived). Used as a students-list
  filter and set inline from the grid.
- `/student/profile`, `/student/total-days-in-classes`, `/student/debt-transactions`,
  `/student/attach-contract-many`, `/student/delete-many`.
- `custom-fields/{STUDENTS,PARENTS,LEADS,EMPLOYEES,AGREEMENT}` — user-defined fields on core
  records. A schema-level capability, not a screen. **Decline** (see §7); it is a platform
  decision that deserves its own conversation, not a side effect of this module.

### 2.7 O'quv bo'limi — verdict

| | |
|---|---|
| **Cheap, high value** | Certificates register (~30 h); archive-reason catalogue + bulk archive (~8 h); bulk room create (~2 h, hand to warehouse `W-26`) |
| **Expensive** | Group-separate-from-Class (~184 h standalone, ~90 h folded into Phase 2) |
| **Decline** | `buildings` table, generic `reason_types`, `custom-fields`, server-side geo queries |

---

## 3. Moliya

**Scope of this section.** Only the parts nobody else owns:

- the **cashbox** layer (`fin-map/*`, `/cashbox*`, `/transaction-types`, `/payment-methods`);
- **subscription transactions** and the **month-by-month arrears pivot**;
- the **debtor workflow** (statuses, actions, payment-date changes).

**Not here:** `FINANCE_ALL.BONUS`, `FINANCE_ALL.PENALTY`, `FINANCE_ALL.MONTHLY_SALARY` and
everything payroll-shaped → `docs/modules/hr.md` §1 rows 6–8 and §2.6–2.8. That spec has
already decided how bonuses and penalties post to the ledger; do not re-decide it here.

### 3.1 What we already have — read this before proposing anything **[ours]**

We are further ahead in finance than anywhere else, and in several places we are **stricter**
than EduSchool. The relevant files:

| Concern | File |
|---|---|
| Double-entry journal, the only writer | `SchoolLms.Application/Billing/LedgerService.cs` |
| Closed chart of accounts | `SchoolLms.Application/Billing/Accounts.cs` — 14 codes: `cash`, `bank`, `receivable`, `revenue:{tuition,bus,dormitory,meals,other}`, `expense:{salary,utilities,supplies,rent,repair,other}` |
| Payments, split allocation, storno | `PaymentService.cs`, `PaymentAllocation`, trigger `check_allocation_total()` in `Migrations/Sql/billing_guards.sql` |
| Cash shift, gapless receipts, Z-report, variance | `CashShiftService.cs`, `CashShift.Variance` (generated column) |
| Invoices and monthly accrual | `InvoiceService.cs`, `BillingAccrualService.cs` |
| Subscriptions | `SubscriptionService.cs`, `StudentSubscription` |
| Discounts with director approval | `DiscountService.cs`, `Discount.ApprovedBy` |
| Expenses with a threshold + second approver | `ExpenseService.cs` |
| **Derived** student balance | `StudentBalanceQuery.cs` — computed from `invoices` − `payment_allocations` **every time** |
| P&L, Cash Flow, Debtors, Collection rate | `FinanceReportQueries.cs` → `ProfitLossAsync`, `CashFlowAsync`, `DebtorsAsync`, `CollectionRateAsync` |
| Money-flow (Sankey) | `MoneyFlowQueries.cs` |
| Fraud detection | `AnomalyScanService.cs`, `FinanceAnomalyFlag`, `FinanceFlagsController` |
| Receipts (PDF) | `ReceiptService.cs`, `ReceiptDocument.cs` |
| Role matrix as data | `SchoolLms.Server/Controllers/FinanceRoleAttribute.cs` |
| DB-level immutability | `deploy/init-roles.sql` §5 + `Migrations/Sql/billing_guards.sql` |
| Client | `pages/admin/finance/{FinancePage,PnlTab,CashFlowTab,DebtorsTab,CollectionRateCard,ZReportTab,VarianceTab,VarianceBanner,MoneyFlowPage}.tsx`, `pages/admin/billing/*` |

### 3.2 `fin-map/*` — the cashbox dashboard

**[bundle]** Nine endpoints, no screen chunk (see §3.5):

```
fin-map/cashboxes            fin-map/calendar             fin-map/daily-cash-balance
fin-map/daily-transaction    fin-map/top-five             fin-map/transaction-type
/fin-map/monthly-last-amount /fin-map/monthly-chart       /fin-map/last-day
```

Route `/cash` (`rb = "/cash"`), menu `FINANCE_ALL.CASH` = "Moliya", permission `cashboxGet`.

**[inferred]** — from the names plus the surrounding cashbox API, this is a **daily cash-desk
dashboard**, not a report:

| Endpoint | Almost certainly |
|---|---|
| `cashboxes` | balance per named cash box, right now |
| `daily-cash-balance` | opening → in → out → closing for a chosen day |
| `daily-transaction` | that day's transaction list |
| `top-five` | the five largest movements of the day |
| `transaction-type` | the day's totals grouped by transaction type |
| `calendar` | a month grid with a per-day net figure, for clicking into a day |
| `monthly-chart` / `monthly-last-amount` / `last-day` | the sparkline and the two headline numbers |

### 3.3 The cashbox layer we do not have **[bundle]**

This is the real gap, and it is bigger than `fin-map`.

```
/cashboxes  ·  /cashbox  ·  /cashbox/responsible  ·  /cashboxes-branch
/cashbox-main  ·  /cashboxes-main
/cashbox/income  ·  /cashbox/expense
/cashbox/transaction/pagin  ·  /cashbox/transaction/total
/cashbox/transaction/export-list  ·  /cashbox/transaction/comment
/cashbox/cancel  ·  /cashbox/branch-transfer
/transaction-types  ·  /payment-methods  ·  /money-type
```

Permissions **[bundle]**: `cashboxGet`, `cashboxEdit`, `cashboxDelete`, `transactionsMakeIncome`,
`transactionsMakeExpense`, `cashboxTransfer`, `cashboxExchange`, `cashboxBranch`,
`cashboxGetWithIntegrationPayment`, `cashboxCancel`, `exportCashboxTransaction`,
`actualDateTransaction` ("transaction with old date"), `canSeeStudentBalance`,
`transactionTypeGet|Edit|Delete`, `paymentMethodsGet|Edit|Delete`, `moneyTypeGet|Set`.

The concept map:

| EduSchool | Ours | Gap |
|---|---|---|
| **Cashbox** — a named, persistent container of money with a responsible employee. Several per school. Money moves between them (`cashboxTransfer`), converts between currencies (`cashboxExchange`), and one is "main". | **`CashShift`** — a *session*, not a container. Opened, counted, closed. | We have no persistent cash location. `Accounts.Cash` is a single ledger account. |
| **Transaction type** — a user-defined tree (`transaction.children`) of income/expense categories, each `type: payIn \| payOut`, some with `hasImpactOn: 'student' \| 'employee'` | **`Accounts`** — a closed list of 14 codes in a C# file, deliberately closed (*"Yangi hisob qo'shish = shu faylga bitta qator. Bu ataylab biroz noqulay"*) | Theirs is editable by the school; ours by a developer. |
| **Payment method** — a CRUD'd catalogue with a `type` | `PaymentMethod` — four constants (`cash`, `card`, `transfer`, `online`) in `SchoolLms.Domain/Billing.cs` | Ours is deliberately fixed: only `cash` counts toward `expected_cash` at shift close (SPEC §8.1 Q13). |
| **Currency** (`/money-type`, `SETTINGS_ALL.CURRENCY`) | none — `numeric(14,2)` so'm | Out of scope. |
| **Cancel a transaction** (`/cashbox/cancel`, `state: 'cancelled'`) | **reversal only** — `payments.reversal_of`, and `app_rw` physically cannot `UPDATE` or `DELETE` a payment | **Ours is stricter and must stay that way.** |
| **Back-dated transaction** (`actualDateTransaction`, `actualDate` field) | not possible | **Ours is stricter.** |

> **The single most important observation in this file.** EduSchool's student transaction rows
> carry `beforeAmount` and `afterAmount` **[bundle]** — a running balance stored on every row.
> That is precisely the pattern `docs/SPEC.md` §4 and
> `SchoolLms.Application/Billing/MoneyFlowQueries.cs` were written to eliminate (*"eski tizimda
> qarz o'quvchi qatoridagi saqlangan ustundan o'qilardi va u OLTI joyda qo'lda
> o'zgartirilardi"*). Combined with `POST /student/subscription/delete-all` — an endpoint whose
> job is to **delete every subscription transaction for a student**, gated only by the
> permission `deleteAllStudentSubscriptionTransactions` **[bundle]** — this is a money model we
> must not copy. Take their *screens*. Never their *ledger*.

**[decision] What we build from the cashbox layer, and what we refuse:**

| Item | Decision | Reason |
|---|---|---|
| A `fin-map`-style **Kassa kuni** dashboard | **Build.** ~20 h backend (all reads over `ledger_entries` + `payments` + `cash_shifts`), ~24 h frontend | Every number it needs already exists. It is the one screen the cashier and the director open daily, and we currently make them read a P&L to answer "how much is in the drawer right now". |
| Multiple named cashboxes | **Decline for now.** | One school, one desk. `CashShift` + `Accounts.Cash` already answers "how much cash exists". A cashbox table would mean a `cashbox_id` on `payments`, which is an append-only table — a migration we should only do once, when there is a second desk. |
| Editable transaction-type tree | **Decline.** | `Accounts.cs` is closed **on purpose**, and the reason is written in the file: an editable free-text account string means one typo (`revenue:tution`) silently loses money from a report. What the school actually wants — "which expense category" — is already `expenses.category`, and adding a category there is one line in `Accounts.ExpenseByCategory`. Offer a settings screen that *lists* the categories read-only. |
| Editable payment methods | **Decline.** | SPEC §8.1 Q13 fixed the four labels and tied `cash` to shift arithmetic. A fifth user-defined method would have undefined behaviour at shift close. |
| Transaction cancel, back-dating, currencies | **Refuse.** | Each one breaks SPEC §4.1. This is not a cost question. |

**The `fin-map` dashboard, specified** — so it is buildable without guessing:

```
GET /api/admin/finance/cash-day?on=YYYY-MM-DD
    // RBAC: a new `FinanceAction.ViewCashDay` member in
    // SchoolLms.Server/Controllers/FinanceRoleAttribute.cs, added to `FinanceMatrix.Rules`
    // as data — never an `if` chain in the controller. Per SPEC §4.3: admin + superadmin
    // see every cashier; a cashier sees only their own shifts (`shifts[]` filtered server-side).
{
  "onDate": "2026-09-13",
  "opening":   { "cash": 1250000 },
  "in":        { "cash": 4800000, "card": 1200000, "transfer": 0, "online": 350000 },
  "out":       { "expense": 900000 },
  "closing":   { "cash": 5150000 },
  "shifts":    [{ "id","cashierName","openedAt","closedAt","expected","counted","variance" }],
  "topFive":   [{ "kind":"payment|expense","at","amount","label","who" }],
  "byCategory":[{ "account":"revenue:tuition","amount":3900000 }],
  "receiptCount": 41
}

GET /api/admin/finance/cash-calendar?month=YYYY-MM
[{ "date":"2026-09-01","in":…, "out":…, "net":…, "hasVariance":false }]

GET /api/admin/finance/cash-trend?months=12
[{ "month":"2026-09","in":…, "out":…, "closing":… }]
```

Rules: every figure derives from `ledger_entries` (`entry_date`, `account`) and `payments`
(`received_at`, `method`), never from a stored balance. `opening` for day *D* is the closing of
*D−1*, computed, not stored. Only `method = 'cash'` moves the `cash` line — the other three
settle to `bank`, exactly as `PaymentMethod.CountsAsCash` already says. Timezone
Asia/Tashkent via `AppClock`. All reads: `AsNoTracking`, no new tables, no migration.

### 3.4 Abonement tranzaksiyalari + the month-by-month arrears pivot

#### 3.4.1 The pivot — fully recovered **[bundle]**

This screen **is** in the bundle (offset ≈ 2 212 900), so we know exactly what it is.

`GET /students/subscription-transactions-pivot` → a **student × month matrix**. Each cell:

```js
{ amount, paid, toBePaid }
```

Colour rule, verbatim:

```js
cellColor = (c) => !c || c.amount === 0 ? GREY
                 : (c.amount < 0 || c.toBePaid > 0) ? RED : GREEN
// GREEN #16a34a "to'langan" · RED #dc2626 "to'lanmagan" · GREY #9ca3af "ma'lumot yo'q"
```

Each cell renders three stacked numbers — `amount` (coloured by the rule), `paid` (dark if
> 0, else grey), `toBePaid` (red if > 0, else grey). Columns are `month_<n>` plus a `totals`
column; there is a sticky footer with per-month totals that scrolls horizontally in lock-step
with the grid (`FOOTER_TOTAL_PAID`, `FOOTER_TOTAL_DEBT`), a legend, and a
**debtors-only switch** (`subscription-transactions-pivot-debtor-switch`).

This is the best-designed screen in their finance module. One grid answers "who owes what, in
which month, and since when" — the question a school director actually asks.

*(Observation only, per `CLAUDE.md`: their layout is well arranged. We build the same
**information** in our own visual language — our table components, our palette, our spacing.
Not a MUI DataGrid.)*

#### 3.4.2 The transaction list **[bundle]**

`GET /students/transactions?studentId=…` (+ `/students/transactions/export`). Row:

```
{ type, state('cancelled'|…), amount, beforeAmount, afterAmount,
  cashier{firstName,lastName}, comment, forPeriods{fromDate,toDate},
  transactionType{name}, paymentMethod{name,type}, cashbox{name},
  discount{amount}, actualDate, createdAt, branch{name}, number, student{…} }
```

Observed `type` values include `studentSubscriptionReturn` and `transfer`. Row action: print a
receipt. Two destructive buttons: **"Undo the last subscription cancellation"**
(`POST /student/subscription/cancel-return {studentId}`) and **"Delete all subscription
transactions"** (`POST /student/subscription/delete-all {studentId}`).

Related: `/student/unpaid-subscription-transactions`, `/student/debt-transactions`,
`/student/additional-subscriptions/{id}`, `/subscription/pagin`, `updateAbonimentAmount`.

#### 3.4.3 What we have, and what the gap actually is **[ours]**

We already hold every number the pivot needs:

```
invoices (student_id, category_id, period_month, amount, discount, due_on, status)
payment_allocations (invoice_id, amount)
payments (reversal_of)          -- and FinanceReportQueries.EffectiveAllocations already
                                --   excludes both sides of a storno
```

Three existing pieces of code already compute pieces of this, and the pivot must **reuse** them
rather than re-derive the arithmetic — a second definition of "debt" is exactly the failure
`FinanceReportQueries.cs` opens by warning about:

| Existing | What it already gives us |
|---|---|
| `FinanceReportQueries.DebtorsAsync` | `debt = Σ(amount − discount) − Σ(effective allocations)`, per student |
| `FinanceReportQueries.CollectionRateAsync` | the same, **per month**, school-wide |
| `SchoolLms.Application/Services/StudentLedger.cs` | the same, **per month for one student**, including the `paid / partial / unpaid` classification the pivot's colour rule needs |

**So the arrears pivot is `StudentLedger` run for every student and transposed.** The only new
thing is the shape and the cell — there is no new arithmetic to get wrong.

```
GET /api/admin/finance/arrears-pivot?fromMonth=2026-09&toMonth=2027-05
    &className=&categoryId=&debtorsOnly=true
{
  "months": ["2026-09", … ],
  "rows": [ { "studentId","fullName","className",
              "cells": { "2026-09": { "amount":900000,"paid":900000,"toBePaid":0 }, … },
              "total": { "amount":…, "paid":…, "toBePaid":… } } ],
  "footer": { "2026-09": { "amount":…,"paid":…,"toBePaid":… }, "total": {…} }
}
```

Definitions, stated so four people compute the same number:

- `amount` = `Σ (invoices.amount − invoices.discount)` for that student, that month, over the
  selected categories.
- `paid` = `Σ payment_allocations.amount` for those invoices, **excluding** allocations of a
  reversed payment and of the reversal itself — reuse `FinanceReportQueries.EffectiveAllocations`
  verbatim, do not re-derive it.
- `toBePaid` = `max(0, amount − paid)`.
- A month with no invoice → the cell is absent (grey), **not** a zero. Grey and zero mean
  different things: "not enrolled" vs "enrolled and free".
- Rounding: none. `numeric(14,2)` throughout, `decimal` in C#, no floats — the rule already
  stated at the top of `MoneyFlowQueries.cs`.
- Cap: 600 students × 12 months. Beyond that, require a class filter.

Performance: two queries, no N+1 — one grouped over `invoices`, one grouped over
`payment_allocations`, joined in memory. Indexes exist already
(`invoices (student_id, category_id, period_month)`, `payment_allocations (invoice_id)`).

#### 3.4.4 Verdict for §3.4

| Item | Cost | Verdict |
|---|---|---|
| Arrears pivot (`/admin/finance/arrears`) | 10 h backend + 16 h frontend | ✅ **Shipped 2026-09-16.** `GET /api/admin/finance/arrears-pivot` → `FinanceReportQueries.ArrearsPivotAsync` → `pages/admin/finance/ArrearsPage.tsx`. Was: No migration, no new table, one query pair over data we already have, and it answers the director's daily question. |
| Per-student month history | — | **We already have it.** `SchoolLms.Application/Services/StudentLedger.cs` returns per-month accrued / discount / paid / remaining plus the payment list, reading `payment_allocations` (not a FIFO guess — see the P1-21 note in that file); surfaced by `GET /api/admin/students/{id}/ledger` → `getStudentLedger` → `schoollms.client/src/pages/admin/students/PaymentHistoryModal.tsx`, with `paid/partial/unpaid` month chips and an `AuditHistoryList`. **Remaining delta:** it aggregates all fee categories into one month row (deliberately — see the file's own comment); a per-category breakdown already exists separately at `GET /api/student/billing` (`InvoiceService.ForStudentAsync`). Nothing to build. ~4 h if we want the reversal rows shown explicitly. |
| `beforeAmount` / `afterAmount` running balance | — | **Refuse.** SPEC §4. The balance is derived by `StudentBalanceQuery.cs` and stays derived. |
| "Delete all subscription transactions" | — | **Refuse.** `app_rw` cannot delete from `payments` or `payment_allocations`, by design, and that is the point. |
| Back-dated / cancellable transactions | — | **Refuse.** SPEC §4.1. |

### 3.5 Debtor workflow — the gap nobody has named yet

**[bundle]** They do not just *list* debtors, they *work* them:

```
/debtor/students (+/export)   /debtor/analytics (+/export)
/debtor/action  ·  /debtor/actions
/debtor-action-statuses  ·  /debtor-action-status
permissions: debtors · debtorsExport · debtorStatusEdit
```

i18n keys **[bundle]**: `debtors.add_action`, `debtors.add_status`, `debtors.status`,
`debtors.status_name`, `debtors.status_color`, `debtors.status_hint`, `debtors.comment`,
`debtors.change_payment_date`, `debtors.current_payment_date`, `debtors.payment_date_hint`,
`debtors.history_tab_title`, `debtors.month`.

So: a debtor row carries a **status** from a user-defined coloured catalogue, an **action log**
with comments, and the ability to **agree a new payment date** with the parent — with history.
A collections CRM, essentially.

**[ours]** `FinanceReportQueries.DebtorsAsync` + `pages/admin/finance/DebtorsTab.tsx`. A list
and a number. No status, no action log, no promised date.

**[decision] Build a thin version.** Two tables, no workflow engine:

```
debtor_statuses (id uuid, name text unique, color text, hint text, position int, is_active bool)
debtor_actions  (id uuid, student_id text, status_id uuid null, comment text not null,
                 promised_on date null, created_by text, created_at timestamptz)
```

The debtors list gains: current status (= the latest action's status), last action date,
promised date, and a "Amal qo'shish" modal.

`debtor_actions` is **append-only in behaviour but not in grants**: it is not a financial table
(no amounts, no ledger posting), so do **not** add it to the `REVOKE` list in
`deploy/init-roles.sql` §5. Editing a comment is a normal `UPDATE`. What must not happen is
deleting the history of what was promised, so `deleted_at` rather than a row delete.

A `promised_on` in the past with the debt still open is a **broken promise**. Surface it as a
fifth `AnomalyKind` alongside the existing four (`ShiftVariance`, `FastReversal`,
`OffHoursPayment`, `PaidWithoutAllocation` — `SchoolLms.Application/Billing/Anomaly.cs:187-190`)
so it lands on the director's dashboard through machinery that already exists, and — if the
client enables it — as a `staff_task` (`docs/modules/staff-tasks.md` §5.7, rule
`invoice_overdue`).

Cost: 14 h backend + 14 h frontend. **High value:** it is the difference between knowing who
owes money and knowing what was done about it.

### 3.6 P&L 2.0 (`PNL_EXPECTATION`) — noted, deferred

**[bundle]** `/reports/pnl`, `/reports/pnl/expectation` and five children
(`/changes`, `/students`, `/daily`, `/yearly`, `/planned-expense`), plus
`/reports/balance-sheet`, `/finance/expense/template` (planned expenses) and
`/finance/expense/reminder/unseen-count`.

"P&L 2.0" is a **forecast**: expected revenue from active subscriptions, versus planned
expenses from templates, versus actual — with a change log.

**[ours]** `ProfitLossAsync` is actual-only.

**Verdict: defer.** The inputs exist (`student_subscriptions`, `invoices`, `expenses`), but a
forecast needs planned-expense templates and a recurring-expense model that we do not have, and
a school that has just started using a real ledger should get one full year of actuals before
anyone forecasts from them. Revisit at the start of the next academic year. (~60 h.)

### 3.7 Moliya — verdict

| | |
|---|---|
| **Cheap, high value** | Arrears pivot (~26 h); Kassa kuni dashboard (~44 h); debtor workflow (~28 h); student transaction history tab (~18 h) |
| **Expensive** | P&L 2.0 forecast + planned expenses + balance sheet (~60 h) — defer one year |
| **Decline for a single school** | multiple cashboxes, cashbox transfers, currency exchange, editable transaction-type tree, editable payment methods |
| **Refuse on principle** | stored running balance, transaction cancel, transaction delete, back-dated transactions. Each breaks `docs/SPEC.md` §4, and the DB grants in `deploy/init-roles.sql` would have to be weakened to allow them. |

---

## 4. Analitika — all fifteen, priced

**[bundle]** `edu-menu.json` lists exactly fifteen entries under `ANALITICS_ALL`. Mapped to
their endpoints and to what we hold.

Legend: **Cheap** = a report over data already in our database, no new table.
**Expensive** = needs data we do not collect, or a new subsystem.
**Owned** = another module's spec already covers it.

| # | Their report | Endpoint **[bundle]** | Permission | Data we hold **[ours]** | Class |
|---|---|---|---|---|---|
| 1 | Analitika (o'zlashtirish o'rtachasi) | `/analitics/students`, `/analitics/classes` | `getMarksAvarageAnalitics` | `JournalEntry`, `QuarterGrade`, `Analytics.cs`, `ClassAnalyticsController.grades-report/*` | **Have** (≈) |
| 2 | O'zlashtirish ko'rsatkichlari | `/analitics/students/quarter` | `getQuarterMarkAnalitics` | `QuarterGrade`, `SubjectProgressService.cs` | **Have** (≈) |
| 3 | O'zlashtirish (fanlar bo'yicha) | `/analitics/students/quarter/by-subjects`, `by-subjects/seasonal-appropriation` | `getQuarterMarkAnalitics` | same + `Subject` | **Cheap** — a pivot of #2. ~10 h |
| 4 | Sinflar reytingi | `/analitics/classes` (+`/export`) | `getClassSeasonalMarkAnalitics` | `RatingService.SchoolAsync`, `pages/admin/classes/ClassRatingPage.tsx` | **Have** |
| 5 | Davomat analitikasi | `/analitics/attendance`, `attendance-analytics/info`, `attendance-analytics/total-info` | `getAttendance` | `JournalEntry` (attendance marks), `AbsenceReason`, `AttendanceController` | **Cheap** — we have the marks, not the roll-up screen. ~14 h |
| 6 | Davomat intizomi bo'yicha hisobot | `/attendance-discipline-report` | `getTurnstileAnalytics` | `AbsenceReason.Points` + `DisciplinePoint` + attendance | **Cheap** — ~12 h. Cross-links §6. |
| 7 | O'qituvchi ish hisoboti | `/analitics/journal-activity` | `getJournalActivity` | `TeacherActivityReport.cs` (`BuildOverviewAsync`, `BuildDetailAsync`), `TeacherReportsController`, `pages/admin/teacher-reports/` | **Have** |
| 8 | Mavsumiy baholash hisoboti | `/analitics/seasonal-mark-activity` | `getTeacherWorkReport` | — | **Owned** → `docs/modules/admission-and-testing.md` |
| 9 | Buyurtmalar voronkasi (leads funnel) | `/leads-funnel`, `/leads/dashboard-counts` | *(none)* | `Lead`, `LeadStage`, `LeadsController` | **Cheap** — ~12 h. **New page only**; the leads *board* is frozen (`CLAUDE.md`) and must not be touched. |
| 10 | Ishdan bo'shatishlar hisoboti | `/analitics/employee/dismissal-report` (+`/details`, +`/export`) | `getDismissalReport` | — | **Owned** → `docs/modules/hr.md` §2.11 |
| 11 | Turniket analitikasi | `/turnstile/attendances`, `/turnstile/violations/pagin`, `/turnstile/late-early/summary`, `/turnstile/today-late-count` | `getTurnstileAnalytics` | `TurnstileEvent`, `TurnstileService.cs`, `TurnstileLiveService.cs`, `StudentTurnstileController`, `TeacherAttendanceController` | **Cheap** — the events are already ingested. ~16 h |
| 12 | Turniket kirib-chiqish statistikasi | `/turnstile-entrance-exit-analytics` | `getTurnstileAnalytics` | same | **Cheap** — ~10 h |
| 13 | Kunlik davomat hisoboti | `/turnstile/daily-report` | `getTurnstileAnalytics` | same + `JournalEntry` | **Cheap** — ~12 h |
| 14 | Filial holati | `/monitoring`, `/monitoring/export`, `/monitoring/branch-series` | `getMonitoring` | — | **Decline** — cross-branch. Single branch, confirmed. |
| 15 | Qarzdorlar bilan ishlash | `/debtor/analytics` (+`/export`) | `debtors` | `FinanceReportQueries.DebtorsAsync`, `DebtorsTab.tsx` | **Partly have**; the workflow is §3.5 |

### 4.1 Reading the table

- **Four are already ours** (#1, #2, #4, #7) — sometimes under a different name. Do not rebuild
  them; add the missing cut or filter to the existing page.
- **Two belong to other specs** (#8, #10).
- **One is declined** (#14, cross-branch).
- **Eight are cheap** — reports over data already in the database: #3, #5, #6, #9, #11, #12,
  #13, and the analytics half of #15. Total ≈ **86 h backend + ≈ 70 h frontend**.
- **Nothing here is expensive**, because the expensive part — collecting the data — is done.
  Journal marks, attendance, quarter grades, turnstile events, discipline points, leads and
  invoices are all already in our database, and in several cases (turnstile, journal) we
  collect *more* than EduSchool's reports display.

That is the headline finding for Analitika: **we are not behind on data, only on screens.**

### 4.2 One structural recommendation **[decision]**

Do **not** create an `/admin/analytics` mega-section with fifteen sub-pages. EduSchool has one
because their menu is fifteen items deep and their sidebar is a flyout tree. Ours is a flat
sidebar and the reports belong next to the thing they measure:

| Report | Lives under |
|---|---|
| #3, #5, #6 | `Baholar hisoboti` and `Davomat` (existing entries) |
| #11, #12, #13 | `O'quvchilar → Turniket` and `O'qituvchilar → Davomat` (existing entries) |
| #9 | `Lidlar → Voronka` (a **new sibling page**, not a change to the board) |
| #15 | `Moliya → Qarzdorlar` (existing) |

Reason: a report you have to go and look for in a separate section is a report nobody looks at.
This also avoids touching `navigation.ts` fifteen times — a shared file (§8).

### 4.3 Analitika — verdict

| | |
|---|---|
| **Cheap, high value** | Attendance analytics (#5) and the daily attendance report (#13) — these two are what a head teacher opens every morning. Then turnstile analytics (#11, #12), the by-subject pivot (#3), the discipline report (#6), the leads funnel (#9). |
| **Expensive** | None. |
| **Decline** | Filial holati (#14) — cross-branch, and we are single-branch by decision. |
| **Do not rebuild** | #1, #2, #4, #7 — we have them. |

---

## 5. Sozlamalar (`getSettings`)

### 5.1 Their settings section is far bigger than our inventory recorded

`docs/EDUSCHOOL-INVENTORY.md` lists 11 entries. The bundle's i18n key set has **36**
**[bundle]**:

```
ACADEMIC_YEAR · AUTO_SMS · BUILDING · CERTIFICATE_TYPES · COIN_UNIT · CURRENCY
DEBTOR_STATUSES · DEPARTMENT · DISCOUNT · EVALUATION_INDICATORS · EVALUTION_SYSTEM
GENERAL_SETTINGS · GROUPS · HOLIDAY · INTEGRATIONS · LESSON_TIME · MEAL_MENU
MEAL_MENU_SCHEDULE · MEAL_MENU_TYPE · PAYMENT_METHOD · PLANNED_EXPENSE · POSITION
QUARTER · REASONS · RECEIPT · SALES_AND_MARKETING · SMS_LIST · SMS_TEMPLATES
STUDENT_STATUS · SUBSCRIPTION · SYSTEM_SUBSCRIPTION · TAX · TRANSACTION_TYPE
TURNSTILE · TURNSTILES · WORK_SCHEDULE
```

Most are catalogue screens for other modules and are owned elsewhere (`TAX`, `POSITION`,
`DEPARTMENT`, `WORK_SCHEDULE` → HR; `COIN_UNIT` → gamification; `MEAL_MENU*` → warehouse;
`CERTIFICATE_TYPES` → §2.3; `DEBTOR_STATUSES` → §3.5; `TRANSACTION_TYPE`, `PAYMENT_METHOD`,
`CURRENCY`, `PLANNED_EXPENSE` → §3.3/§3.6 and declined; `STUDENT_STATUS`, `REASONS`,
`BUILDING`, `GROUPS` → §2). `SYSTEM_SUBSCRIPTION` is EduSchool billing *us*, not the school.

**[ours]** `schoollms.client/src/pages/admin/settings/`: `SchoolSettings.tsx`,
`TelegramSettings.tsx`, `FirebaseSettings.tsx`, `TurnstileSettings.tsx`, `GpsSettings.tsx`,
`CameraSettings.tsx`, plus quarters, lesson times, absence reasons and academic year reached
through the Dars jadvali menu. Backed by `SchoolMeta` (`Entities.cs:575`) and
`SettingsController`.

What follows are the three the brief names, plus the general-settings flags.

### 5.2 `SALES_AND_MARKETING` — News, Story, Surveys

**[bundle]** Permission `salesMarketing`, route `/story`, three children:
`SALES_AND_MARKETING_ALL.{NEWS,STORY,SURVEYS}`.

**Surveys** — public lead-capture forms:

```
GET /surveys/pagin · GET /survey/details · GET /leads/with-survey
survey  { name, subTitle, image, language, webUrl, offerUrl,
          showStudentFirstNameInput, showStudentLastNameInput,
          showStudentGenderInput, showStudentGradeInput, showStudentPhoneNumberInput }
```

A public page whose submission becomes a **lead**, with per-field toggles and a link to an
offer document. This is the school's online enrolment form.

**News** — `news.audience` ∈ `for_employee | for_parent | for_student`, `news.content`, with a
live preview.

**Story** — Instagram-style stories for the parent app. Route `/story`.

**[ours]**

| Their thing | Ours |
|---|---|
| Surveys → leads | `LeadsController` (`POST /api/admin/leads`) exists; there is **no public form**, no `surveys` table, no `/survey/:token` route |
| News | `Broadcast` (`Entities.cs:723`) + `MessagesController.broadcast` + `messageTemplates.ts` — Telegram/push broadcast, not a stored news feed with an audience |
| Story | nothing |

**[decision]**

| Item | Cost | Verdict |
|---|---|---|
| **Surveys** — `surveys` + `survey_submissions`, a public `/ariza/:slug` page, submission → `Lead` with `source = survey`, spam protection (rate limit + honeypot; no third-party captcha, nothing leaves the machine) | 16 h + 20 h | **Do it.** It is the only item in Sozlamalar that brings *money in*: a parent fills a form at midnight and a lead exists in the morning. It also feeds #9 in §4. |
| **News** — `news { id, title, body, audience[], published_at, author }` + a feed in the Mini App and the parent portal | 10 h + 14 h | **Do it, second.** Cheap, and it replaces the "broadcast a Telegram message and hope" pattern with something that has a history. Reuse `NotificationsController`'s derived feed. |
| **Story** | — | **Decline.** Ephemeral image carousels are a consumer-app affordance. A 500-pupil school does not have someone to feed them, and an empty story ring looks worse than no story ring. |

### 5.3 `TEACHER_EVALUATION` — richer than the brief suggests

Unlike most of EduSchool's screens, **this one is fully in the bundle** (offset ≈ 2 213 000+),
so the model is not guesswork.

**[bundle]**

```
/evaluation-set  ·  /evaluation-set/pagin
/evaluation-indicator  ·  /evaluation-indicator/pagin
/evaluation-grade
/lesson-evaluation  ·  /lesson-evaluation/pagin
/lesson-evaluation/lesson-options  ·  /lesson-evaluation/active-set
/lesson-evaluation/analytics  ·  /lesson-evaluation/rating
/lesson-evaluation/teacher-of-day  ·  /lesson-evaluation/export
/evaluation-assignment/teacher-progress
permissions: getTeacherEvaluation · editTeacherEvaluation · deleteTeacherEvaluation
menu: SETTINGS_ALL.TEACHER_EVALUATION ("Tahlil"), route /teacher-evaluation
also:  SETTINGS_ALL.EVALUATION_INDICATORS, SETTINGS_ALL.EVALUTION_SYSTEM
```

The model, from the 73 i18n keys:

- An **evaluation set** is the active questionnaire; exactly one is active at a time
  (`active-set`, and the form errors with `noActiveSet` when there is none).
- A set holds **indicators** (criteria), each with options and a ball value
  (`detail.criteria`, `detail.answer`, `detail.ballShort`, `detail.outOf`).
- One **lesson evaluation** = an observer watching one teacher's lesson:
  `{ teacherId, subjectId, classIds[], periodId, lessonDate, evaluatorName, answers{}, comment }`
  → `{ totalScore, maxScore, percent, gradeColor }`.
- The form shows a **running score** while you fill it in, refuses to submit with
  `missingAnswers`, and **locks after submission** (`form.editLocked`).
- The detail view keeps a **snapshot** of the questionnaire as it was
  (`detail.snapshotNote`) — so changing the set later does not rewrite history. That is the
  right call and we should copy it.
- Four tabs: **Home** (KPI: today / weekly / monthly / total / evaluated teachers / avg percent
  / failing; a daily series chart; a monthly plan with `daysLeft`; today's "heroes"),
  **List**, **Rating** (by teacher, filterable by department and grade, exportable to PDF),
  **Teacher of the day** (a threshold-based daily winner).

**[ours]** Nothing for *teachers*. We have the student-facing analogue:
`EvaluationType` + `EvaluationGrade` (`Entities.cs:409/423`) — "Feedback nomi" and
"O'quvchilarga feedback", one 1–5 score per (student, subject, type, month, week) —
`StudentEvaluationController.cs`, `pages/admin/students/StudentEvaluationPage.tsx`,
`EvaluationTypesPage.tsx`, and `pages/teacher/evaluation`. Also
`TeacherActivityReport.cs` (plan vs fact: lessons scheduled vs journal marked), which is a
*different* measure — activity, not quality.

**[decision] Build a trimmed version.** Cost ~28 h backend + ~34 h frontend for sets +
indicators + the evaluation form + list + rating. **Skip** "Teacher of the day", the monthly
plan widget and the PDF rating export in v1.

**Verdict: do it, but after Analitika.** Reason: it is the one thing in this file that measures
*teaching quality*, which is the school's actual product — but it needs the head teacher to
commit to walking into lessons with a tablet, and that is a process change, not a software
change. Ask the client whether they already do lesson observations on paper before building it.
If they do, this is high value. If they do not, it will be an empty screen.

Two things to copy verbatim when it is built: the **questionnaire snapshot** on each evaluation,
and the **lock after submit**.

### 5.4 `INTEGRATIONS`

**[bundle]** An app-store-shaped catalogue:

```
GET  /integrations?type=moderator&service=sms&page=&limit=
POST /integration/install/{id}
integration { _id, name, logo, images[], isInstalled, description, <config form> }
permission: integrations (label INTEGRATIONS_SETTINGS)
route: /integrations-settings
```

Services seen on the page itself: **SMS providers**, **MCP (AI)**, **Atmos**, **Bito Pay**.

Around it:

```
/sms-templates/pagin   createSmsTemplate · updateSmsTemplate · deleteSmsTemplate
/autosms               permission `autosms`
/sms/pagin  ·  /sms/export     permissions `sendSms`, `exportSms`
filters on the SMS log: integrationId, state, studentId, fromDate, toDate, search
```

**Auto-SMS**, from its 27 i18n keys **[bundle]** — a rules engine, not a checkbox:

| Trigger | Configuration |
|---|---|
| `auto_sms.apsent` (absence) | `attendance_state` ∈ `all \| reasonable \| unreasonable`; `to_parent` / `to_child`; `send_once`; `max` per period; `every` N `day_hour` |
| `auto_sms.turnstile_attendance` | `check_in_template`, `check_out_template`, `check_out_scheduled_time`, and `turnstile_sms_recipient` ∈ `neutral \| father \| mother` |
| `auto_sms.debter` | `debter_min_balance` threshold |
| `auto_sms.payment` | on payment received |
| `auto_sms.behavior` | on a behaviour incident (see §6) |
| `auto_sms.birthday` | on a birthday |
| `auto_sms.otp` | login codes |

**[ours]**

| Their thing | Ours |
|---|---|
| Integration catalogue | Six hand-written settings pages (`TelegramSettings`, `FirebaseSettings`, `TurnstileSettings`, `GpsSettings`, `CameraSettings`, `SchoolSettings`) backed by columns on `SchoolMeta`. Not a catalogue — and for six integrations that is the right shape. |
| SMS provider | **none.** `schoollms.client/src/pages/admin/students/SmsModal.tsx` is a stub: `// TODO: API — SMS yuborish` followed by an `alert()`. |
| Message templates | `schoollms.client/src/config/messageTemplates.ts` — four templates + six tokens (`{fish}`, `{sinf}`, `{qarzdorlik}`, `{balans}`, `{ota-ona}`, `{telefon}`), a **frontend constant**, not a table |
| Message log with delivery state | none — `Broadcast` and `PushMessage` record the send, not the per-recipient outcome |
| Auto-anything | Telegram + FCM push only, triggered manually or by the notification feed |

**[decision]**

| Item | Verdict | Reason |
|---|---|---|
| **Atmos, Bito Pay, Payme, Click, Uzum** | **Out of scope — permanently, by client decision.** | `docs/SPEC.md` §8.1 Q13: *"a plain dropdown on the payment form — **no payment-provider integration, in this or any later phase**"*. `payments.method` is a label. The cashier records what the payer used; the system never talks to a provider. This is settled; do not re-open it in a later spec. |
| **MCP (AI)** integration slot | **Decline for now.** | No stated use case. When one appears it will be a specific feature ("draft the parent message"), not a generic slot. |
| **Integration catalogue UI** | **Decline.** | We have six integrations and six pages. A catalogue with install/uninstall is infrastructure for a marketplace we do not have. |
| **SMS provider** (one, e.g. Eskiz/Play Mobile) + `sms_templates` table + `sms_log` with delivery state | **Do it — this is the real gap.** ~20 h backend + ~14 h frontend | Telegram reaches the parents who installed Telegram and linked their account. SMS reaches the rest. Today `SmsModal.tsx` lies to the user with an `alert()` — that is a bug, not a missing feature. **Money leaves the machine** when an SMS is sent, so this needs the client's provider account and an explicit go-ahead before wiring (global rules: stop and ask when something leaves the machine or money is spent). |
| **Auto-SMS rules** | **Do the two that pay for themselves**: absence (to parent, once per day, max N per month) and debt reminder (threshold + due date). ~16 h. **Defer** turnstile check-in/out SMS — at 10 periods a day it is a bill, not a feature. **Skip** birthday and OTP. |
| **SMS templates as a table** with the existing six tokens | **Do it** as part of the SMS work. ~4 h. Move `messageTemplates.ts` into the database and let the school edit it. |

### 5.5 General settings — flags worth stealing **[bundle]**

Their `/settings` object carries ~40 boolean flags. These are the ones that describe behaviour
we would otherwise have to guess at, and each is one column + one switch:

| Flag | What it controls | Ours |
|---|---|---|
| `isSuspendedForDebt`, `limitDebtorAccess`, `debtSuspensionThreshold` | cut a debtor's app access above a threshold | none |
| `archiveOnlyNonDebterStudents` | refuse to archive a student who owes money | none — and this is a **good** rule: archiving a debtor is how a debt disappears |
| `makeAttendanceReasonRequired`, `makeClassStudentRemoveReasonRequired`, `blockAttendanceBeforeJoinedAt` | data-quality gates | none |
| `isStudentGradeRequired` | a lesson cannot be closed without grades | none |
| `busyTeacher`, `busyRoom` | schedule conflict enforcement | ours is in code, not configurable |
| `enableBehaviorSystem`, `gamification`, `warehouse`, `receptionAttendanceEnabled` | **module kill switches** | none |
| `teacherParentChatEnabled` | turn the teacher↔parent chat off | none |
| `parentScreenshot` | block screenshots in the parent app | none |
| `showLearningProgressInParentDashboard` | hide grades from parents | none |
| `calendarAttendanceDataSource` ∈ `journal \| turnstile` | which source drives the attendance calendar | ours is journal-only |
| `quarterlyGradeSource` | how the quarter grade is computed | ours is fixed |
| `autoCreateStudentContractNumber`, `autoContractNumberFormat`, `structuredContractNumberEnabled`, `copyStudentContractNumberToNewAcademicYear` | contract numbering | ours is manual |
| `defaultStudentLanguage`, `maxMark` | defaults | `maxMark` is fixed at 5 |
| `createChildPickUpTask`, `absenceTaskEnabled/Threshold/Recipients` | automatic task creation | → `docs/modules/staff-tasks.md` §2.14, §5.7 |

**[decision]** Add **four** of them, and only four, to `SchoolMeta` + `SchoolSettings.tsx`
(~6 h total):

1. `archive_only_non_debtor_students` — a money-integrity rule, and cheap.
2. `make_attendance_reason_required` — the single biggest lever on attendance data quality.
3. `is_student_grade_required` — same, for grades.
4. `show_learning_progress_in_parent_dashboard` — parents *will* ask for this to be off during
   a bad term, and hard-coding it means a deploy.

**Decline the module kill switches.** We ship what the client bought; a disabled module should
not be in the build.

### 5.6 Sozlamalar — verdict

| | |
|---|---|
| **Cheap, high value** | ~~SMS provider + templates + log~~ and ~~the two auto-SMS rules~~ — **cancelled 2026-09-16, Telegram only**; surveys (~36 h); the four general-settings flags (~6 h); news (~24 h, delivered through Telegram) |
| **Expensive** | Teacher evaluation (~62 h) — worth it **only** if the school already does lesson observations |
| **Decline** | Integration catalogue, MCP slot, Story, module kill switches, editable payment methods / transaction types / currencies |
| **Out of scope by client decision** | Atmos, Bito Pay and every other payment provider — `docs/SPEC.md` §8.1 Q13. A dropdown label is the whole feature. |

---

## 6. Xulq-atvor

### 6.1 What they have **[bundle]**

Menu `BEHAVIOR` (permission `getBehaviors`), gated by the general-settings flag
`enableBehaviorSystem`, three children:

| Screen | Route | Permission |
|---|---|---|
| Sabablar | `/behavior-reasons` | `getBehaviorReasons` |
| Harakatlar | `/behavior-incidents` | `getBehaviorIncidents` |
| Xulq-atvor reytingi | `/behavior-rating` | `getBehaviorIncidents` |

```
/behaviors/pagin           /behavior-incidents/pagin
/behavior-rating/pagin     /behavior-rating/stats     /behavior-rating/export
permissions: getBehaviors · editBehaviors · deleteBehaviors
             getBehaviorReasons
             getBehaviorIncidents · editBehaviorIncidents · deleteBehaviorIncidents
             exportBehavior · getNotAssignedClasses
```

Model, from the 44 i18n keys **[bundle]**:

```
Behavior (Sabab)  { _id, name, description, points:int, type: positive|negative,
                    isActive:bool, reasonType, smsTemplate }
BehaviorIncident  { _id, studentId, behaviorId, points, comment, createdBy, createdAt }
Rating            per student: total points; filters class / pointsMin / pointsMax;
                  stats { totalStudents, avgPoints, minPoints, maxPoints }; export
```

Two details that matter:

- `behaviorSmsTemplate` **[bundle]** — a reason can carry its own SMS template, so recording an
  incident notifies the parent automatically. This is also one of the `auto_sms` triggers
  (§5.4). That is the loop that makes the feature work: the point is not the number, it is that
  the parent hears about it the same hour.
- `getNotAssignedClasses` — a coverage check: which classes have had **no** behaviour entries.
  Without it, a behaviour system quietly becomes "the two teachers who bother".

### 6.2 What we have **[ours]**

| | |
|---|---|
| Entity | `DisciplineReason` (`Entities.cs:377`) — `{ Id, Name, Points }`. Positive or negative by sign. |
| Entity | `DisciplinePoint` (`Entities.cs:389`) — `{ Id, StudentId, ReasonId, ReasonName, Points, Note, CreatedAt, CreatedBy }`. **Snapshots `ReasonName` and `Points`** so history survives the reason being edited or deleted — the same instinct as EduSchool's questionnaire snapshot, and correct. |
| Rule | Every student starts at **100**; current score = `100 + Σ points`. Documented on the entity. |
| Second source | `AbsenceReason.Points` (`Entities.cs:355`) — marking an attendance reason in the journal moves the discipline score. **EduSchool has no equivalent**; theirs is manual only. |
| API | `SchoolLms.Server/Controllers/DisciplineController.cs` — reasons CRUD, `POST /points`, `GET /points?studentId=`, `DELETE /points/{id}`, `GET /scores` (the rating), `POST /reasons/{id}/attendance-points` |
| UI | `pages/admin/discipline/BallarNazoratiPage.tsx` (Ballar nazorati = their Reyting) and `BallSabablarPage.tsx` (Ball sabablar = their Sabablar). Nav group `Intizomiy ball`, `perm: 'discipline'`. |

### 6.3 The delta, precisely

| EduSchool | Us | Gap |
|---|---|---|
| Sabablar | `DisciplineReason` | **Have.** Missing: `description`, `is_active`, an explicit `type` (we use the sign of `points`, which is equivalent), and `sms_template`. |
| Harakatlar (incident log with comment and author) | `DisciplinePoint` with `Note`, `CreatedBy`, `CreatedAt` | **Have.** Missing: a **school-wide incident feed** — our `GET /points` requires a `studentId`, so there is no "what happened today" view, and no filter by reason, class, date range or author. |
| Xulq-atvor reytingi | `GET /discipline/scores` + `BallarNazoratiPage.tsx` | **Have.** Missing: `pointsMin`/`pointsMax` filters, the stats header (`totalStudents`, `avg`, `min`, `max`) and **export**. |
| Points arrive from attendance | `AbsenceReason.Points` | **We are ahead.** |
| Baseline of 100 | ours | They start from 0. Ours is friendlier to parents; keep it. |
| SMS to the parent on an incident | none | **Not a gap any more** — no SMS in this product (2026-09-16). The parent loop goes through Telegram. |
| Coverage report (`getNotAssignedClasses`) | none | Small, and it is what keeps the system honest. |
| Feature flag | none | Not wanted. |

So: **the data model is done; the missing part is the school-wide view and the parent loop.**

**[decision] The work, in order:**

1. **School-wide incident feed** — `GET /api/admin/discipline/points` without a required
   `studentId`, with filters (date range, class, reason, author, positive/negative) and
   pagination, plus a page under `Intizomiy ball → Harakatlar`. *8 h + 10 h.*
2. **Rating page upgrades** — min/max point filters, the four stat cards, `ExcelExport`.
   *4 h + 6 h.*
3. **Coverage** — `GET /api/admin/discipline/coverage` → per class: incident count this month,
   last entry date, and which classes have zero. *4 h + 4 h.*
4. **Parent notification** — `DisciplineReason.NotifyParent: bool` + an optional message
   template; the notification goes out through the **existing** channel
   (`TelegramService` / `NotificationsController`). **Telegram only** — SMS was cancelled on
   2026-09-16 and §5.4 will not be built. *6 h + 3 h.*
5. **`DisciplineReason.Description` and `IsActive`** — two columns and two inputs. *2 h + 2 h.*

Total ≈ **24 h backend + 25 h frontend**.

One caution on step 4: a discipline point that texts the parent is a different social object
from one that does not. Default `NotifyParent = false` on every existing reason, and make the
admin turn it on per reason, deliberately.

### 6.4 Xulq-atvor — verdict

| | |
|---|---|
| **Cheap, high value** | The school-wide incident feed (~18 h) and the parent notification (~9 h). Together they turn a number nobody looks at into a working feedback loop. |
| **Cheap, lower value** | Rating filters + stats + export (~10 h); reason description/active (~4 h); coverage report (~8 h) |
| **Expensive** | Nothing. |
| **Decline** | The `enableBehaviorSystem` kill switch; renaming our module to match theirs. `Intizomiy ball` is a clearer name than `Xulq-atvor` and it is already in front of users. |

---

## 7. Consolidated priority

One table, ordered by value per hour. Hours are backend + frontend for one developer each.

### 7.1 Cheap and high value — do these first (**329 h** ≈ 4 weeks for one backend + one frontend developer working in parallel)

**Progress: #1 shipped on 2026-09-16 (26 h of the 329). 303 h left in this list.**

"Cheap" here means: **no new subsystem, and in most cases no new table** — a screen over data
the database already holds. Not one of these fourteen items requires a decision from the
client before it can start, and not one of them touches a financial write path.

| # | Item | § | Hours | Why first |
|---|---|---|---|---|
| 1 | ✅ **Month-by-month arrears pivot** — **shipped 2026-09-16** | 3.4 | 26 | Zero new tables. One query pair over `invoices` + `payment_allocations`. Answers the director's daily question in one screen. |
| 2 | **Attendance analytics + daily attendance report** | 4 (#5, #13) | 26 | The head teacher's morning screen. Data already collected. |
| 3 | **School-wide discipline incident feed** | 6.3 | 18 | Makes an existing, unused feature usable. |
| 4 | **Debtor workflow** (statuses, actions, promised date) | 3.5 | 28 | Turns a debtor list into collections. Two small tables. |
| 5 | **Certificates register** | 2.3 | 30 | Frequently asked for; two small tables; no interaction with anything fragile. |
| 6 | **Kassa kuni dashboard** (`fin-map` equivalent) | 3.2 | 44 | Reads only. The cashier and director open it every day. |
| 7 | **Turnstile analytics + entrance/exit stats** | 4 (#11, #12) | 26 | Events already ingested; we currently show a live feed and nothing historical. |
| 8 | **Surveys → leads** (public form) | 5.2 | 36 | The only item that brings money in. Feeds the leads funnel. |
| 9 | **Show reversals explicitly in the student month history** | 3.4 | 4 | The history itself already exists (`StudentLedger.cs` + `PaymentHistoryModal.tsx`); only storno rows are invisible. |
| 10 | **Discipline: parent notification + rating filters/stats/export** | 6.3 | 19 | Completes #3. |
| 11 | **By-subject attainment pivot** + **discipline attendance report** + **leads funnel** | 4 (#3, #6, #9) | 34 | Three small reports over existing data. Leads funnel is a **new page**; the board stays frozen. |
| 12 | **Archive-reason catalogue + bulk archive** | 2.2 | 8 | Small, and it makes the archive list analysable. |
| 13 | **Four general-settings flags** | 5.5 | 6 | One column and one switch each. `archive_only_non_debtor_students` is a money-integrity rule. |
| 14 | **News feed** | 5.2 | 24 | Replaces "broadcast and hope" with something that has a history. |

### 7.2 Expensive — schedule deliberately, do not drift into

| Item | § | Hours | Condition |
|---|---|---|---|
| ~~SMS provider + templates + log + 2 auto-rules~~ | 5.4 | ~~58~~ **0** | ❌ **CANCELLED 2026-09-16 by the client** — Telegram is the only channel (`CLAUDE.md`, "Telegram is the only channel"). No provider, no templates, no log, no auto-SMS rules, no `sms_template` column anywhere. `SmsModal.tsx` stops being a stub waiting for SMS and becomes a Telegram message dialog. Everything below in this table is unaffected. |
| **Teacher evaluation** | 5.3 | 62 | Only if the school already does lesson observations. Ask first. |
| **Group separate from Sinf** | 2.5 | 184 standalone / ≈90 folded into Phase 2 | Only if the client confirms they teach cross-class groups. One question decides it. |
| **P&L 2.0 forecast + planned expenses + balance sheet** | 3.6 | 60 | Revisit after one full year of actuals in the ledger. |

### 7.3 Decline for a single school

| Item | § | Why |
|---|---|---|
| Filial holati / cross-branch monitoring / `Administrator` module | 4 (#14) | Single branch, confirmed 2026-09-13. Re-adding branch scoping touches every list query, every report and the permission model. |
| Multiple cashboxes, cashbox transfers, currency exchange | 3.3 | One cash desk. `CashShift` + `Accounts.Cash` already answers the question. A `cashbox_id` on `payments` is a migration on an append-only table — do it once, when there is a second desk. |
| Editable transaction-type tree, editable payment methods, currencies | 3.3 | `Accounts.cs` is closed on purpose; SPEC §8.1 Q13 fixed the four payment labels and tied `cash` to shift arithmetic. |
| Integration catalogue with install/uninstall, MCP slot | 5.4 | Six integrations, six pages. A marketplace needs a market. |
| Story (Instagram-style) | 5.2 | Nobody at the school will feed it. |
| Module kill switches (`enableBehaviorSystem`, `gamification`, `warehouse`) | 5.5 | We ship what the client bought. |
| `buildings` table | 2.1 | One building. `rooms.building` as free text covers a second. |
| Generic `reasons` / `reason_types` | 2.2 | We already have two typed reason tables with type-specific columns. |
| `custom-fields/*` (user-defined fields on students, parents, leads, employees, contracts) | 2.6 | A platform capability, not a feature. It deserves its own conversation, not a side effect of a gap-closing pass. |
| Server-side geo queries (bounding box / by point / by name) | 2.4 | Client-side filtering is correct at 500 students. |

### 7.4 Refuse on principle — not a cost question

These break `docs/SPEC.md` §4 and would require weakening the grants in
`deploy/init-roles.sql`. If a later spec proposes one of them, this is the objection.

| Item | § | What it breaks |
|---|---|---|
| Stored running balance (`beforeAmount` / `afterAmount`) on transactions | 3.3 | §4 — the balance is derived by `StudentBalanceQuery.cs`. A stored balance has to be maintained in every write path, and the moment one is missed the number is silently wrong. |
| "Delete all subscription transactions" | 3.4 | §4.1 — `app_rw` cannot `DELETE` from `payments` or `payment_allocations`, and that is enforced by PostgreSQL, not by an `if`. |
| Cancel a transaction (`state = 'cancelled'`) instead of reversing it | 3.3 | §4.1 — a mistake is corrected by inserting a reversal; the original stays visible. |
| Back-dated transactions (`actualDate`, `actualDateTransaction`) | 3.3 | §4.2, §4.6 — a payment dated into a closed shift makes `expected_cash` and every Z-report retroactively wrong. |

---

## 8. Shared files — sequential, never parallel

Any unit of work above that touches one of these must queue behind the others. One agent, one
pass, at the end of each batch.

| File | Who touches it | Rule |
|---|---|---|
| `SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | every new table | **One migration per batch, one owner.** Certificates, debtor workflow, surveys, news and the discipline columns are five migrations if written separately and one conflict if written in parallel (SMS is gone — cancelled 2026-09-16). |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | every new table | one `XModel.Apply(b)` line each; follow `GuardianModel.cs` |
| `SchoolLms.Infrastructure/Migrations/Sql/*.sql` + the `csproj` `EmbeddedResource` list | every new table | new tables need a `GRANT … TO app_rw` following `billing_guards.sql`. **None of the tables proposed in this file is financial**; do not copy the append-only `REVOKE` pattern onto them. |
| `deploy/init-roles.sql` | nothing proposed here | Its §5 list is authoritative for append-only tables. Grep it before naming a table; a name already in it carries protection that a rename would silently drop. |
| `SchoolLms.Application/Billing/Accounts.cs` | nothing proposed here | The list is closed on purpose. HR adds three codes (`docs/modules/hr.md` §8) — that is the only pending change. |
| `SchoolLms.Server/Program.cs` | every new service | DI registrations |
| `schoollms.client/src/config/navigation.ts` | certificates, arrears, cash-day, incidents, surveys, news, reports | **Batch every nav change into one edit.** §4.2 exists partly to keep this small. |
| `schoollms.client/src/config/constants.ts` | any new permission key | the admin permission list |
| `schoollms.client/src/App.tsx` | every new page | routes |
| `SchoolLms.Application/Services/AuditService.cs` | certificates, debtor actions, discipline | one `EntityX` constant each |
| `SchoolLms.Domain/Entities.cs` | discipline columns, `SchoolMeta` flags | **Additive only.** New modules get their own file (`Billing.cs`, `Guardians.cs` set the precedent). |
| `docs/SPEC.md` §5 module inventory | each shipped item | one row |
| **`schoollms.client/src/pages/admin/leads/*` (6 files)** | **nobody** | Design-frozen by `CLAUDE.md`. The leads funnel (§4, #9) is a **new sibling page**. Before opening any PR that could have touched them, run `git diff --stat` on those six paths; the expected output is empty. |

---

## 9. Open questions

Each carries the decision that stands if the client says nothing.

**Q1 — Do you teach cross-class groups?**
*"Bir nechta sinfdan yig'ilgan guruhga alohida dars o'tasizmi (masalan, kuchli ingliz tili
guruhi 5-A va 5-B dan)? Yoki har dars faqat bitta sinf bilanmi?"*
**Decision if silent: no.** The existing 0/1/2 subgroup split covers language and PE splits;
184 hours are saved. This is the single highest-leverage question in this file — it is the
difference between a five-week schema change and none.

**Q2 — Do you already do lesson observations on paper?**
**Decision if silent: do not build teacher evaluation.** An observation system with no observer
is an empty screen that makes the product look unfinished.

**Q3 — Which SMS provider, and whose account?** — **ANSWERED 2026-09-16: none.** The client ruled out SMS, the mobile app and mobile push in one sentence; every message goes through Telegram. The rest of this entry is kept for the record and no longer describes the plan.
**Decision if silent: build nothing, and replace `SmsModal.tsx`'s `alert()` with an honest
"SMS hali ulanmagan" state.** Sending an SMS spends the client's money and leaves the machine
— both are stop-and-ask conditions under the global rules. A stub that says "yuborildi" when
nothing was sent is a bug we are currently shipping.

**Q4 — Should archiving a student who owes money be blocked?**
**Decision if silent: yes, blocked, with a `superadmin` override.** It is EduSchool's
`archiveOnlyNonDebterStudents` and it closes the simplest way for a debt to disappear.

**Q5 — Should a discipline point notify the parent?**
**Decision if silent: build the capability, default it off on every reason.** The school turns
it on per reason, deliberately. A system that texts a parent every time a child is late will be
switched off within a week and will take the rest of the module with it.

**Q6 — Public enrolment form: which fields, and where does it live?**
**Decision if silent: F.I.SH, parent phone, target grade, a note** — the fields `Lead` already
has (`SchoolLms.Domain/Entities.cs:222`) — at `/ariza`, rate-limited, no captcha, no third-party
script. Adding a field later is one column; removing a leaked one is not.

**Q7 — Certificates: register only, or scores too?**
**Decision if silent: register first, scores second.** `certificate_types.is_scored` is in the
schema from day one (§2.3), so the Natijalar tab is a screen, not a migration.
