# O'quv bo'limi (Students) — screen-level parity with EduSchool

**Status:** analysis + specification. No code, no migration, no entity.
**Date:** 2026-09-17 · **Author:** discovery analyst (Students module)
**Supersedes, for this module only:** the O'quv bo'limi parts of `docs/modules/existing-module-gaps.md`
§1–§2 where this file says so explicitly. Everything else in that file still stands.

The client calls Students and Finance *"havodek muhim"* and wants **full feature parity with
EduSchool, including every inner part of every screen**, so that the school can switch over.
Menu-level parity exists (`docs/MENU-PARITY.md`). This file is the screen-level pass that
`docs/EDUSCHOOL-INVENTORY.md` ends by asking for: columns, filters, form fields, validation,
row and bulk actions, import/export, detail tabs, endpoints and permissions — for the ten
screens under `BARN_ALL`, the student profile and every modal reachable from the student list.
Then the same screens on our side, the gaps, the schema, and a build order.

**Two client answers received on 2026-09-17 shape this file:**

1. **Cross-class teaching groups exist** (e.g. a strong English group drawn from 5-A and 5-B).
   That settles `existing-module-gaps.md` §2.5.4 and §9 Q1: the "decision if silent: no" is
   void. **`Group` is P0** and gets the fullest treatment below (§2.1).
2. **Feature parity, not a sync.** Our system does everything EduSchool does, then the school
   switches. There is no integration track with EduSchool.

---

## 0. Method and how to read a claim

| Label | Meaning |
|---|---|
| **[bundle]** | Read out of `.eduschool-bundle/all.js` (the concatenated front-end), `edu-menu.json` or `edu-roles.json`. `@N` is a character offset into `all.js`. The live tenant was **never contacted** (`CLAUDE.md` hard rule). |
| **[inferred]** | Not literally in the bundle; deduced from a request body, a query parameter or a sibling screen. The reasoning is given. |
| **[unrecoverable]** | The information is not in the bundle. Recorded and moved on. |
| **[ours]** | Read out of this repository at commit `7bc7737`. |
| **[decision]** | Ours, with the reason. |

**Four facts about the bundle that apply to every screen below.**

1. **Most tables are server-driven, so most column lists are [unrecoverable].** Lists call
   `POST /table-settings/get {pageName}` and the server returns the columns, default sort,
   order, visibility, pins and widths; each user can hide, reorder, resize and pin columns and
   every change is saved back (`/table-settings/columns/set-{sort,pin,available,width,order}`)
   **[bundle @7036225]**. Column ids look like `column_<field>_<type>`. We see a column only when
   the page overrides how it renders. Sorting and paging are server-side
   (`sortBy`, `sortOrder=1|-1`, `page`, `limit`, `search` debounced 500 ms) **[bundle @7060701]**.
2. **Translations are not in the bundle.** Every label below is an i18n key, not the Uzbek text
   **[unrecoverable]**.
3. **Shared form inputs are required by default** **[bundle @3190700, @7283354, @7345235,
   @7346431]**: text = required + min length 3; password = min length 6; number = required
   unless `required:false`; select, async select and date = required; phone = required +
   pattern `^\+\d{1,4}\d{6,14}$`, default `+998`. A field below with no rule stated inherits
   these.
4. **Several lazy chunks are missing from the capture** and their screens are
   **[unrecoverable]** beyond what other chunks reveal: the Subjects page
   (`index-D-mE4Iin.js`), the contract template register (`index-D2ss0-HR.js`),
   `CertificateForm-BC6Ef4tk.js`, `detail.const-BNqIjZA7.js` (parent types, payment types).

**Product rules that decide how a gap is classified** (`CLAUDE.md`, `ASSUMPTIONS.md`):

- **Telegram is the only channel.** Every EduSchool "send SMS" becomes a Telegram message to
  parents or is declined. SMS providers, templates, logs and call telephony are declined.
- **Single branch.** Branch selectors, cross-branch lists and `Administrator` are declined.
- **Functionality, never appearance.** Nothing here restyles our screens; the Lidlar board is
  frozen and nothing in this file touches `pages/admin/leads/*`.
- **Additive work.** New endpoints and pages go alongside existing ones.

**Settled decisions this file cites and does not re-open:** `existing-module-gaps.md` §2.1
(rooms owned by the warehouse shape, `buildings` declined), §2.2 (typed reason tables, no
generic `reasons`), §2.4 (no server-side geo queries at 500 pupils), §2.6 (`custom-fields`
declined), and `docs/modules/warehouse.md` §3.2 (`rooms` exactly as `SPEC.md` §3.3, no
`classes.home_room_id`, no scheduler wiring in that migration).

---

## 1. Summary

Hours are backend + frontend for one developer each, from the gap tables in §2.
P0 = the school cannot operate daily without it; P1 = used weekly; P2 = nice to have.

| # | Screen | EduSchool (one line) | Ours today | Verdict | Hours to close |
|---|---|---|---|---|---|
| 1 | **Group** | Cross-class groups: subject, feeding classes, teachers, roster picker, transfer, duplicate, archive; group lessons in schedule, journal, attendance, reports | Nothing. `Student.SubGroup` 0/1/2 inside one class | **missing** | **282 P0** + 22 P2 |
| 2 | Sinf | Class list + form (grade, letter, head teachers, language, capacity); **class roster page** with add/remove-with-reason/transfer ("stay in groups"), roster export | List + form + subgroup modal + archive + performance page; class change only via the student form | partial | 20 P0 + 14 P2 |
| 3 | Fanlar | Page chunk missing; subjects carry `color`, `isGroupsSubject`, `isActive` | Name only; delete has no guard | partial | 7 P2 (+ groupable flag and delete guard, priced inside Group G-9) |
| 4 | Xonalar | Rooms register (building, capacity), bulk create, room assets link | Free-text `SchoolClass.Room` | missing | 14 P1 |
| 5 | O'quvchilar (list, form, profile) | 18 filters, server export, 2-step import, status tags, bulk archive/delete/contract/SMS, 2 parents per pupil, tabbed profile with ~22 tabs | Client-side list with 6 filters, 1-step import, CSV of selected rows, one parent, long analytics page | partial | 123 P1 + 15 P2 |
| 6 | Arxiv o'quvchilar | Own page: 7 filters, unarchive with reason, export | Tab inside the student list, catalogue, bulk archive, restore, debtor guard | have | 8 P2 |
| 7 | Sertifikat | Register, types, export, profile tab, teacher tab | Register, types, Natijalar, 2 list filters (shipped 2026-09-16) | have | 4 P1 + 14 P2 |
| 8 | O'quvchilar manzili | Map + up to 3 typed locations per pupil, entered by staff | Map of coordinates that **nothing writes any more** | partial | 9 P1 + 9 P2 |
| 9 | Ota-onalar | Parent list with relation, children, filters, export, message | Read-only list grouped by the pupil's parent phone | partial | 24 P1 + 6 P2 |
| 10 | Shartnomalar | Template register + per-pupil generation + signed-contract register + profile tab | Word templates sent through Telegram; no per-pupil record | partial | 37 P1 + 10 P2 |
| 11 | Cross-cutting | Per-user table settings, fine-grained permissions, teacher group screen | — | partial | 36 P2 |
| | **Total** | | | | **302 P0 · 211 P1 · 141 P2 = 654 h** |

**The P0 work is Group plus the class roster it depends on (302 h).** It is larger than
`existing-module-gaps.md` §2.5.3's 184 h because that estimate left out turnstile expected
times, the student/parent portals and Mini App, the two teacher access checks, the pupil
conflict check, the class roster screen, the rollover, and a regression suite that must exist
before anything is cut over (§2.1.5 lists the corrections).

---

## 2. Screen by screen

### 2.1 Group — P0

#### 2.1.1 EduSchool capture **[bundle]**

**Menu and routes** **[bundle @7185311, @8118172, @7200244]**

| | |
|---|---|
| Menu | `BARN_ALL.GROUPS`, group `STUDY_PROCESS`, role `getClasses` (admin sidebar) / `getStudentGroups` (teacher sidebar). "+" link → `/group/create` |
| Routes | `/group` list · `/group/create` (create or duplicate) · `/group/:id` edit · `/group/:id/students` roster |
| Prefetch | `GET /groups/pagin?search=&page=1&limit=20` |
| Permissions | `getClasses` (view), `editClasses` (create — command palette "create group"), `getStudentGroups` / `updatedStudentGroup` (teacher view / teacher roster edit) **[bundle @2275300]** |

**List — `/group`** **[bundle chunk @1405500–1424365, component `Ia`]**

| Element | Detail |
|---|---|
| Data | `GET /groups/pagin` with `search`, `page`, `limit`, `grades` (JSON array), `state=archive` |
| Columns | Server-driven `group_pagin` **[unrecoverable]**. The actions column is replaced by a **"Ko'rish"** button (header `TABLE.STUDENTS`) → `/group/:id/students`. The journal's local group list shows subject, classes (`grade letter`, joined), group name, teachers **[bundle @959300]** — **[inferred]** the same fields |
| Filters | `grades` multi-select with checkboxes (0–11) · **Arxiv** switch (`state=archive`) · search |
| Row click | → `/group/:id` (edit), only if the user may edit |
| Row actions | **Duplicate** (not for teachers) → `/group/create` with `state.duplicateFromGroupId` · **Edit** (admin, or teacher with `updatedStudentGroup`) · **Archive** (not for teachers) → `DELETE /groups/{id}` with a "really archive" confirm |
| Add button | "create group" (hidden for teachers) |

**Form — create / edit / duplicate** **[bundle `ya` @~1408000, `Ca` @~1416000]**

| Field | Type | Rules | Source / behaviour |
|---|---|---|---|
| `subjectId` | async select | required (default) | `subjects/all?isGroupsSubject=true` — **only subjects flagged groupable** |
| `grades` | multi-select, checkboxes | required | 0–11; drives the class options; locked in duplicate mode |
| `classIds` | multi-select, checkboxes | required | `class/pagin?grades=[…]`, label `grade-letter`; disabled until grades chosen; locked in duplicate mode |
| `name` | text | required, min 3 | label `group.name` |
| `supportTeacherIds` | multi-select | required | `employees/pagin?type=teacher` |
| `moderatorIds` | multi-select | optional | `employees/pagin?type=moderator` |
| `gender` | select | optional | `male` / `female`; filters the picker; locked in duplicate mode |

Teachers see the edit form with every field disabled **except the roster**.

**Roster picker (two panes, same page)**

- **Left pane** — candidates: `GET class-student/get?classIds=[…]&gender=&subjectId=`; enabled only
  once classes **and** subject are set. Rows are *class-student* records
  `{_id, student{_id, fullName}, class{grade, letter}, groupStudent}`. Columns: checkbox, class,
  full name; search box; select-all. **Rows whose `groupStudent` is set are shown greyed and
  cannot be selected** — i.e. a pupil already in a group **for this subject** cannot be added to
  a second one **[inferred: one active group per subject per pupil]**.
- **Right pane** — selected pupils: class, full name, remove; on edit also **transfer**.
- **Transfer** (edit only): dialog with a group select `GET /groups/pagin?subjectId=<same subject>`
  → `POST groups/transfer {groupStudentId, toGroupId}`. A pupil moves only between groups of the
  **same subject**.
- **Submit:** `POST|PUT /groups {name, subjectId, grades:int[], classIds[], gender?, studentIds[],
  supportTeacherIds[], moderatorIds?, _id?}`. Duplicate posts a new group with the copied roster.

**Roster page — `/group/:id/students`** **[bundle `Sa`]**

`GET /group-student?groupId=` (all rows). Columns: pupil (link to `/students/info/:id`), class.
Row selection → **send SMS** to the selected pupils (declined → Telegram, §2.11).

**How a pupil relates to class, group, subject, teacher and schedule** **[bundle]**

| Relation | Evidence |
|---|---|
| Pupil ↔ class: **one active class-student record** **[inferred]**, with history and a leave reason | `class-student` add only offers `students/pagin?withNoClass=true` **[@~965000]**; `/student/total-days-in-classes/{id}` returns `{classGrade, classLetter, joinedAt, leftAt, isActive, days, reason}` **[@~868000]**; remove = `PUT /class-student/{id} {reasonId?, reason?}` |
| Pupil ↔ group: **group-student record**, pointing at the group and remembering the pupil's class | `GroupStudent {_id, groupId, student, class{grade, letter}}`; `groups/transfer {groupStudentId, toGroupId}` |
| Class transfer keeps or drops groups | `POST class-student/transfer {classStudentId, toClassId, shouldStayInGroups}` — checkbox **default true** **[@~973700]** |
| Group ↔ subject: exactly one, and the subject must be groupable | form + `subjects/all?isGroupsSubject=true` |
| Group ↔ classes: many feeding classes (and grades) | `classIds[]`, `grades[]` |
| Group ↔ teacher: many teachers, optional staff moderators | `supportTeacherIds[]`, `moderatorIds[]` |
| Group ↔ schedule: a lesson targets a class **or** a group through the same field | Lesson form: `{isGroupLesson, classId (= group id when isGroupLesson), subjectId, details:[{teacherId, roomId}] (up to 4 pairs), dayOfWeek, periodId, date}`; the "for group" toggle appears only when the subject is groupable; teacher options are the group's `supportTeachers` **[@448899–452400]** |
| Group ↔ journal | Journal page toggles **class / group** lists; group journal at `/journal/{groupId}/attendance/{subjectId}`; a class's subject list flags rows with `class.type==="group"` and opens `/journal/{classId}/attendance/group/{rowId}` **[@957173, @960096–962900]** |
| Group ↔ attendance | `GET /attendances/class/group/v2` **[bundle constant]** |
| Group in reports | report filters call `class/pagin?types=["class","group"]` **[@2163534]** — classes and groups are listed side by side |
| Group elsewhere | students list filter `groupId`; parents filter `groupId`; reception attendance filter `groupId` **[@597691]**; export param `groupId`; student profile tab **"active groups"** lists class and group memberships together, remove needs `deleteClassStudent`, reason required only for a class and only if setting `makeClassStudentRemoveReasonRequired` **[@800770]** |

**[inferred]** EduSchool stores classes and groups as two kinds of one "class" resource
(`class.type`, `types:["class","group"]`, `classId` holding a group id) with separate
membership records. We do not copy that storage; §2.1.4 gives ours.

#### 2.1.2 What we have **[ours]**

`Student.ClassName` (a class **name**, `Entities.cs:101`) and `Student.SubGroup` 0/1/2 (`:118`).
The schedule chain is `WeekAssignment.ClassId → ScheduleTemplate.ClassId → ScheduleLesson
(Day, Period, SubjectId, TeacherId, SubGroup)` (`Entities.cs:324-359`); journal, notes and
quarter grades are keyed by **class id** (`:270, :297, :309`). **No lesson is linked to its
pupils**: every roster is recomputed as `Classes.Id → Name → Students.ClassName == Name`, then
`SubGroup == 0 || Student.SubGroup == lesson.SubGroup`, copied inline into about fourteen server
queries and two client-side filters. There is no teacher↔class↔subject table; access checks,
salary and chat derive it from templates. `Subject` is `{Id, Name}` (`:206`).

#### 2.1.3 Blast radius on our side **[ours]**

"MUST CHANGE" = lesson/roster/access logic that has to see group lessons. "MIRROR-SAFE" =
means *the pupil's homeroom* and keeps working if `students.class_name` stays the homeroom.
The full list is the basis for slices C1–C3 in §4.

| Subsystem | MUST CHANGE (file:line) | MIRROR-SAFE (unchanged) |
|---|---|---|
| **Schema** | `ScheduleTemplate.ClassId` `Entities.cs:327`, `WeekAssignment.ClassId` `:355`, `JournalEntry/QuarterGrade/LessonNote.ClassId` `:270, :297, :309` — must say whose lesson it is; `AppDbContext.cs:117-130` indexes | `Student.ClassName` `:101`, `Student.SubGroup` `:118`, `Teacher.HomeroomClass` `:168`, chat/broadcast/pickup `ClassName` `:801, :814, :852` |
| **Schedule** | `ScheduleTemplatesController.cs:13-139`; `WeekAssignmentsController.cs:13-40`; `ScheduleUtilsController.cs:27-64` (occupied-slots skips non-class templates at `:46`, and **no pupil-conflict check exists at all**); `PortalSchedule.cs:33-42, 64-94`; `ClassesController.cs:98-125` (delete); `AcademicYearController.cs:76-127, 241-244, 294-349, 377-383` (archive export, rollover); FE `ScheduleBoard.tsx:22-62`, `LessonEditorPanel.tsx:45-125`, `ClassSchedulePage.tsx`, `TemplateEditorPage.tsx`, `WeekScheduleModal.tsx`, `TeacherSchedulePage.tsx:64-128`, `TodaySchedule.tsx:83-123`, `ClassScheduleViewPage.tsx:39-92, 286-300`, `api/services/scheduleTemplates.ts`, `weekAssignments.ts` | subgroup filter `PortalSchedule.cs:49-50` |
| **Journal & teacher access** | `JournalService.cs:15-151, 188-300, 318-400` (small: works by owner id; subgroup copied from the pupil at `:84-85` must be 0 for group lessons); `JournalSettingsGuard.cs:90-128` (roster `:98-102`); `JournalController.cs:17-129`; **both access checks** `TeacherPortalController.cs:34-53` and `TelegramTeacherController.cs:300-308` (a group-only teacher gets 403); teacher class list `TeacherPortalController.cs:289-321`; roster `:352-373`; writes `:392-480`; Telegram roster `TelegramTeacherController.cs:111-170`, recent `:172-230`; FE admin `JournalPage.tsx:70-190` (**browser roster filter at `:176`**), teacher `teacher/journal/JournalPage.tsx:59-133`, ui-web `JournalPickerScreen.jsx:57-104`, `JournalGridScreen.jsx:23-77`, Mini App `AttendanceTab.jsx:55-60, 135-156` (**browser roster filter at `:152`**), `TodayTab.jsx:50-76, 160-227` | — |
| **Attendance** | `AttendanceController.cs:24-80, 103-133`; `AttendanceAnalytics.cs:86-160`; `AttendanceDisciplineReport.cs:73-100, 140-160`; `DashboardController.cs:27-62, 114-145`; `StudentEvaluationController.cs:110-195`; FE `AttendancePage.tsx:39-95` | — |
| **Turnstile** | expected arrival/leaving per class `TurnstileAnalyticsQueries.cs:554-640` → must become **per pupil** (earliest/latest of homeroom and group lessons); teacher first lesson `TurnstileService.cs:277-293` | class grouping in reports `TurnstileAnalyticsQueries.cs:124, 174-189, 224, 316, 391, 493-495`; label `TurnstileService.cs:76-102` |
| **Grades & reports** | `Analytics.cs:23-119`; `RatingService.cs:14-33`; `ClassAnalyticsController.cs:18-204`; `SubjectAttainmentReport.cs:70-115`; `StudentReportBuilder.cs:17-25` (**`:23` keeps only entries of the current class** — group entries and pre-transfer history are dropped today); `StudentProfileBuilder.cs:19-45, 144-148`; `SubjectProgressService.cs:43-230`; `TeacherActivityReport.cs:110-215` (the journal stores no teacher id, so group rows cannot be attributed without the group's teachers) | report pages follow the server |
| **Salary** | `TeacherSalaryCalc.cs:23-41` — keeps only templates of existing non-archived **classes** and only the biggest template per class: **group lessons would silently drop out of pay** | callers `SalaryLedger.cs:34-70`, `SalaryRatesController.cs:28, 72`, `FinanceController.cs:84`, `ContractsController.cs:111`, `TeachersController.cs:349` follow the one source |
| **Chat** | teacher channel list `ChatService.cs:36-80` (a group-only teacher gets no channel) | `MessagesController.cs:39-350`, `PickupService.cs:78-97`, `NotificationsController.cs:116-144` |
| **Portals & Mini App** | `StudentPortalController.cs:373-400, 458-620, 625-700, 1067-1098`; `TelegramParentController.cs:104-218, 333-392`; teacher evaluation board `TeacherPortalController.cs:499-560`; FE `TeacherDashboard.tsx:54-60`, `EvaluationPage.tsx:25-112`, teacher `SchedulePage.tsx:213`, ui-web Home/Schedule screens | `StudentPortalController.cs:423-455, 958-983`; `TelegramParentController.cs:280-298, 395-404`; parent Mini App `ScheduleTab`, `HomeTab`, `ChildSwitcher.jsx:61` (a group badge is optional) |
| **Billing & contracts** | none, **unless groups are a paid extra** (Q3) | `SubscriptionService.cs:184, 370`, `InvoiceService.cs:327, 374, 423`, `FinanceReportQueries.cs`, `DebtorWorkflowService.cs:374-419`, `StudentLedger.cs:176` — `Contract` has no class field |
| **Admin pages** | student form class select `StudentFormModal.tsx:36, 108-122, 276` and `StudentsController.cs:194, 258, 278-290` (a class change must decide group memberships); `StudentDetailPage.tsx` (show groups) | `ClassGroupsModal.tsx` + `ClassesController.cs:190-270` (0/1/2 splits stay); discipline, certificates, locations, feedback, guardians, archive (labels and filters) |
| **Import / export** | year archive export (above) | student import/export (groups column optional, G-19) |
| **Assignments / LMS** | optional (G-20): `AssignmentService.cs`, `LmsController.cs:28-53, 229-232` | — |
| **Tests** | **nothing tests** `JournalService`, templates, week assignments, occupied-slots, `TeacherSalaryCalc`, `PortalSchedule`, `SubjectProgressService`, `TeacherActivityReport`, `RatingService`, `StudentReportBuilder`, `StudentProfileBuilder` or teacher journal access | seeds to extend: `AttendanceAnalyticsTests`, `TurnstileAnalyticsTests`, `DisciplineFeedTests`, `GeneralSettingsFlagsTests`, `AnalyticsReportsTests`, `TelegramMiniAppTests`; `FinanceReportsTests.cs:937-938` inserts `class_name`/`sub_group` by raw SQL — **keep both columns** |

**Two live bugs found while mapping this, both P0 on their own:**

- **Renaming a class orphans its pupils.** `ClassesController.Update` (`ClassesController.cs:71-96`)
  sets `cls.Name` and does not touch `Student.ClassName`, `Teacher.HomeroomClass`, chat or
  broadcasts; every pupil silently leaves the class. Only the year rollover renames correctly
  (`AcademicYearController.cs:348-349`). Any plan that keeps `class_name` as a mirror depends on
  fixing this first.
- **Split lessons crash the pupil and parent dashboards.** `StudentPortalController.cs:679` and
  `TelegramParentController.cs:373-374` build `notes.ToDictionary(n => (n.Date, n.Period,
  n.SubjectId))`. When subgroup 1 and 2 both have the same subject in the same period (the
  normal English split) that throws → HTTP 500 on the pupil dashboard and the parent Mini App
  overview. `TelegramTeacherController.cs:150` also picks a lesson note without a `SubGroup`
  filter, so a teacher may see the other half's topic.

#### 2.1.4 Our model **[decision]**

**Principle.** A group is its own entity, never a copy of a lesson inside each feeding class
(copies double-count salary, show the lesson N times in the teacher's week and journal, and pass
straight through the salary filter as if they were class lessons). Group-owned lessons reuse the
existing template/week/journal machinery by storing the **group id in the existing `class_id`
column plus an explicit `owner_kind`**, so `JournalService`, the template and week controllers
barely change and readers cannot mistake one for the other.

Rejected alternatives:

- *"Let `SubGroup` go to 0–9."* A subgroup lives inside one class; "group 3" of 5-A and "group 3"
  of 5-B would be unrelated rows merged by every report (`existing-module-gaps.md` §2.5.3 warning).
- *A separate nullable `group_id` next to a nullable `class_id`* on five tables. Every
  `string ClassId` read in ~60 places would have to handle null; the owner-kind column keeps
  those reads valid and makes the kind explicit.
- *`SPEC.md` §3.3 `class_groups (class_id not null …)`.* Ties a group to one class and cannot
  express a group fed by several classes. **`SPEC.md` §3.3–3.5 must be amended** to point at
  `study_groups` before the Phase 2 schedule rebuild (a doc change inside slice M, §4).

Tables are specified in §3.1. The rules they carry:

| Rule | Where it is enforced |
|---|---|
| A group has exactly one subject, and the subject is groupable | FK + service check (`subjects.is_groupable`) |
| A pupil has **at most one active homeroom class** | partial unique index on `class_memberships (student_id) where left_on is null` |
| A pupil is in **at most one active group per subject** | partial unique `study_group_members (student_id, subject_id) where left_on is null`, with a composite FK `(group_id, subject_id) → study_groups (id, subject_id) on update cascade`, so editing a group's subject cannot silently break the rule |
| A pupil added to a group comes from one of its feeding classes, and matches its gender if set | service check (EduSchool enforces it only in the picker) |
| Membership has dates, not deletes | `joined_on`, `left_on`, `leave_reason`; history drives the profile "active period" |
| `students.class_name` stays and is **maintained** by the membership service | one writer; G-1 makes rename cascade |
| Group lessons do not exist until the school switches them on | `school_meta.group_lessons_enabled` (default false): the week-assignment endpoint refuses a group template while it is off (§4 cut-over) |
| A pupil cannot be in a class lesson and a group lesson in the same (day, period) | checked when a group or class template cell is saved against templates assigned to weeks of the current quarter; **409 with the list of conflicting pupils** (Q2) |
| Year rollover | archives every active group and closes its memberships on the rollover date; the school duplicates groups for the new year (Q8) |
| Class transfer | "Guruhlarda qolsin" checkbox, default **on** (EduSchool default); off closes all active group memberships whose group is no longer fed by the new class |
| Archive a pupil | closes class and group memberships on the archive date; restore reopens the class membership only |

#### 2.1.5 Corrections to `existing-module-gaps.md` §2.5 **[ours]**

| §2.5 says | Correction |
|---|---|
| `ScheduleLesson { ClassName?, SubGroup }` | `ScheduleLesson` has **no** class field; the class is `ScheduleTemplate.ClassId` (an **id**), activated by `WeekAssignment.ClassId`. Slice C's "`ScheduleLesson.GroupId` alongside `ClassName`" should be a group-owned template/week assignment instead |
| `JournalController` reads `ClassName` | It does not. The roster filters are `JournalSettingsGuard.cs:98-102`, `JournalPage.tsx:176` and `AttendanceTab.jsx:152`. `journal_entries`/`lesson_notes` also have **no unique constraint** on a journal cell |
| `Contract` reads `ClassName` | `Contract` has no class field; `ContractsController.cs:216` uses it as a label |
| Salary counts scheduled lessons | It counts only the biggest template per existing non-archived class, and skips any template whose owner is not a class — the silent pay trap above |
| `SchoolClass.Room` at `Entities.cs:214` (also in `warehouse.md` §3.2) | It is at `:221` |
| Chat route `GET /messages/chat/{className}` | `GET /api/admin/messages/chat/{className}` |
| Omitted | turnstile expected times; both teacher access checks and the teacher class list; the chat channel list; `TeacherActivityReport`; `SubjectAttainmentReport`, `AttendanceAnalytics`, `AttendanceDisciplineReport`, `StudentEvaluationController`, `DashboardController`, `RatingService`, `StudentProfileBuilder`; the whole pupil portal and parent Mini App lesson paths; the missing pupil-conflict check; rollover and class delete; the browser-built schedule pages; assignments/LMS; the rename bug; the class roster screen; a regression suite |
| 184 h standalone | **282 h** for Group + **20 h** for the class roster it needs (§2.1.6, §2.2). Folding into the Phase 2 schedule rebuild still saves most of G-11/G-12, as §2.5.3 said |

#### 2.1.6 Gap list — Group

| # | Gap | Files on our side | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| G-1 | **Bug:** renaming a class orphans pupils; cascade the rename to `students.class_name`, `teachers.homeroom_class`, `chat_messages`/`broadcasts`/`pickup_requests.class_name` in one transaction, audited | `SchoolLms.Server/Controllers/ClassesController.cs:71-96` | no | 4 | 0 | P0 |
| G-2 | **Bug:** duplicate-key 500 on split lessons; subgroup filter on the Telegram teacher note | `StudentPortalController.cs:679`, `TelegramParentController.cs:373-374`, `TelegramTeacherController.cs:150` | no | 3 | 0 | P0 |
| G-3 | Regression suite **before** any refactor: journal read/write, templates, week assignments, occupied-slots, salary lesson count, portal schedule, subject progress, teacher access, student report | `SchoolLms.Tests/` new: `JournalServiceTests.cs`, `ScheduleTemplatesTests.cs`, `TeacherSalaryCalcTests.cs`, `PortalScheduleTests.cs`, `SubjectProgressTests.cs`, `TeacherAccessTests.cs`, `StudentReportTests.cs` | no | 20 | 0 | P0 |
| G-4 | Schema batch A (§3.1) + backfill `class_memberships` from `students.class_name`/`enrollment_date` + grants | `SchoolLms.Domain/StudyGroups.cs` (new), `SchoolLms.Domain/Entities.cs` (additive: `Subject.IsGroupable`, `OwnerKind` on 5 entities, `SchoolMeta.GroupLessonsEnabled`), `SchoolLms.Infrastructure/Data/StudyGroupModel.cs` (new), `AppDbContext.cs`, new migration + snapshot, `Migrations/Sql/study_groups_guards.sql` + `.csproj` `EmbeddedResource` | **yes** §3.1 | 12 | 0 | P0 |
| G-5 | One resolver each for **lesson roster**, **pupil timetable** (homeroom ∪ groups for a week) and **teacher lessons** (class ∪ group templates); replace the ~14 inline `ClassName == Name` roster queries; output byte-identical while no group exists | `SchoolLms.Application/Services/LessonRoster.cs`, `PupilTimetable.cs`, `TeacherLessons.cs` (new); call sites in `JournalSettingsGuard.cs`, `TeacherPortalController.cs`, `TelegramTeacherController.cs`, `AttendanceController.cs`, `AttendanceAnalytics.cs`, `AttendanceDisciplineReport.cs`, `Analytics.cs`, `ClassAnalyticsController.cs`, `StudentProfileBuilder.cs`, `StudentEvaluationController.cs` | no | 18 | 0 | P0 |
| G-6 | Group API: list (search, grades, subject, teacher, archived), get, create, update, archive/unarchive, duplicate, roster candidates (`classIds`, `gender`, `subjectId` → pupils + their current group for that subject), add/remove member with date and reason, transfer member (same subject) | `SchoolLms.Server/Controllers/StudyGroupsController.cs`, `SchoolLms.Application/Services/StudyGroupService.cs`, `SchoolLms.Application/Dtos/StudyGroupDtos.cs` (new); perm `classes` | uses G-4 | 20 | 0 | P0 |
| G-7 | Behaviour + RBAC tests for G-6 and C-1 (one-group-per-subject, feeding-class rule, transfer, archive closes memberships, staff without `classes` gets 403 on writes) | `SchoolLms.Tests/StudyGroupTests.cs`, `ClassMembershipTests.cs` (new) | no | 8 | 0 | P0 |
| G-8 | Screens: **Guruhlar** list (grade filter, archive switch, search, Ko'rish/duplicate/edit/archive), group form with the two-pane picker (greyed pupils already grouped for the subject, gender filter), group roster page (Telegram message to parents of selected), transfer dialog | `schoollms.client/src/pages/admin/groups/GroupsPage.tsx`, `GroupFormPage.tsx`, `GroupRosterPage.tsx`, `GroupTransferModal.tsx`, `api/services/groups.ts` (new) | no | 0 | 22 | P0 |
| G-9 | Subject "guruhlarga bo'linadi" flag on the subject form; subject delete guard (also F-1) | `SubjectsController.cs`, `pages/admin/subjects/SubjectFormModal.tsx`, `SubjectsPage.tsx` | `subjects.is_groupable` | 3 | 2 | P0 |
| G-10 | Student profile tab **"Sinf va guruhlar"**: current class and groups, membership history with days and leave reason, remove from a group | `pages/admin/students/profile/MembershipsTab.tsx` (new, mounted by S-10); reads G-6/C-1 APIs | no | 0 | 6 | P0 |
| G-11 | **Cut-over — schedule:** group-owned templates and week assignments (refused while the flag is off), lesson editor for a group owner (teachers limited to the group's teachers), pupil-conflict check, occupied-slots includes group templates, class board shows its pupils' group lessons read-only, rollover archives groups, class delete refuses while a group is fed by the class, archive export names group rows | files in §2.1.3 "Schedule" | `owner_kind` | 16 | 20 | P0 |
| G-12 | **Cut-over — journal & teacher access:** pickers list groups; rosters from G-5; `JournalSettingsGuard` roster; both access checks; teacher class list with `kind`; Telegram teacher roster/recent; web and Mini App teacher journal | files in §2.1.3 "Journal & teacher access" | `owner_kind` | 14 | 16 | P0 |
| G-13 | **Cut-over — attendance:** daily attendance, analytics, discipline report, dashboard, evaluation board count group lessons in a pupil's opportunities | files in §2.1.3 "Attendance" | no | 16 | 4 | P0 |
| G-14 | **Cut-over — turnstile:** expected arrival/leaving per pupil and per teacher from G-5 | `TurnstileAnalyticsQueries.cs:554-640`, `TurnstileService.cs:277-293` | no | 8 | 0 | P0 |
| G-15 | **Cut-over — grades & reports:** group grades count under the pupil's homeroom **[decision]** (EduSchool lists classes and groups side by side in the same reports); `StudentReportBuilder` stops dropping non-current-class entries; teacher activity attributes group rows via the group's teachers | files in §2.1.3 "Grades & reports" | no | 26 | 6 | P0 |
| G-16 | **Cut-over — salary:** count each group lesson exactly once through `TeacherLessons` | `TeacherSalaryCalc.cs:23-41` | no | 6 | 0 | P0 |
| G-17 | **Cut-over — chat:** a group gets a teacher channel keyed `grp:<id>` so a group-only teacher can reach its parents | `ChatService.cs:36-80`, `ChatHub.cs:23-24`, `components/…/ChatPanel.tsx` | no | 4 | 2 | P0 |
| G-18 | **Cut-over — portals & Mini App:** pupil/parent schedule, homework, journal, attendance, dashboard, subject progress through `PupilTimetable`; teacher evaluation board roster | files in §2.1.3 "Portals & Mini App" | no | 18 | 8 | P0 |
| G-19 | Groups column in student import/export | `StudentsController.cs:475-611` (or S-2/S-3 controllers) | no | 6 | 2 | P2 |
| G-20 | Assignments / LMS aimed at a group | `AssignmentService.cs`, `LmsController.cs`, `AssignmentWizard.tsx`, `Lms*Page.tsx` | `owner_kind` on `assignments` | 8 | 6 | P2 |
| | **P0 subtotal** | | | **196** | **86** | **282** |

---

### 2.2 Sinf (classes)

#### 2.2.1 EduSchool capture **[bundle chunk @964244–979483]**

**List — `/class`** (component `ls`)

| Element | Detail |
|---|---|
| Data | `GET /class/pagin` (+ `headTeachersIds` JSON), columns server-driven `class_pagin` **[unrecoverable]**; actions column replaced by **"Ko'rish"** → `/class/:id` |
| Filters | `moderatorId` select (`employees/pagin?type=moderator`) · search |
| Export | `GET /class/export` (button shown with `getClasses`) |
| Row actions | **Add behaviour incident to the whole class** (`editBehaviorIncidents`): `type` (required), `behaviorId` (required, `behaviors/pagin?type=`, shows points + description), `notes` (optional) → `POST /behavior-incidents/class {classId, behaviorId, notes}` · **Edit** (`editClasses`) · **Delete** (`deleteClasses`) → `DELETE class/{id}` |
| Add | `editClasses` |

**Form** (`ns`) → `POST|PUT class`

| Field | Type | Rules |
|---|---|---|
| `grade` | numeric text | required; digits only; ≥ 0 |
| `letter` | text | required, min length 1 |
| `headTeachersIds` | multi async select `employees/pagin` | required (default) |
| `language` | select uz / ru / en / kaa | required (default) |
| `buildingId` | async select `building/pagin` | required (default) |
| `moderatorIds` | multi select `employees/pagin?type=moderator` | optional |
| `maxStudentsCount` | number (label `group.student_count`) | optional |

**Class roster — `/class/:id`** (component `as`)

| Element | Detail |
|---|---|
| Data | `GET /class-student/get?classId=` (all rows, searchable). Title `grade-letter` |
| Columns (local) | Pupil (link to profile) · Balance (only with `canSeeStudentBalance`, red when negative) |
| Export | `GET /class-student/export?classId=` |
| Add pupil | form `studentId` async select `students/pagin?withNoClass=true&noArchive=true` (**only pupils without a class**) → `POST /class-student {studentId, classId}` |
| Remove from class | delete column → dialog "really remove from class": reason select from `GET /reasons?type=student-remove` + "other" → free text; required only when setting `makeClassStudentRemoveReasonRequired` → `PUT /class-student/{id} {reasonId \| reason}` |
| Transfer | per-row icon → dialog: target class from `/class/pagin?grades=[same grade]` **excluding this class**; checkbox **`shouldStayInGroups` default true** → `POST class-student/transfer {classStudentId, toClassId, shouldStayInGroups}` |
| Bulk (selection when `setStudentSubscription`) | assign subscription; assign **additional** subscription `POST /student/additional-subscription {studentIds, subscriptionId, existStudentSubsciptionEdit:"onlyFreeStudents", activatesAt, endsAt}`; send SMS to selected |
| Permissions | `editClassStudent`, `deleteClassStudent` **[bundle @2275300]** |

#### 2.2.2 Ours **[ours]**

`ClassesPage.tsx`: active table (#, name, language, room, average grade, attendance, monthly fee,
actions) and archived table; row actions Guruhlar (0/1/2) → `ClassGroupsModal.tsx`, edit,
archive, delete; row click → `ClassDetailPage.tsx` (performance by subject, no roster editing).
`ClassFormModal.tsx`: name (required), grade, room (free text), language uz/ru, monthly fee.
`ClassesController.cs` (perm `classes`): CRUD, archive/unarchive (archives pupils with
`ArchivedWithClass`), groups get/put/auto-split. **No transfer endpoint**: a class change goes
through the student edit form (audited as `EntityStudentClass`). Year promotion is
`AcademicYearController.cs:219` rollover.

#### 2.2.3 Gap list — Sinf

| # | Gap | Files on our side | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| C-1 | Class roster API: list with balance, add a pupil without an active class, remove with a required free-text reason (§2.2 keeps typed reason tables; a catalogue can follow if asked), transfer to another class of the **same grade** with "stay in groups", roster xlsx export; every change writes `class_memberships` and maintains `students.class_name`; `StudentsController.Update` class change goes through the same service | `SchoolLms.Server/Controllers/ClassMembershipsController.cs`, `SchoolLms.Application/Services/ClassMembershipService.cs` (new); `StudentsController.cs:218-290` | uses `class_memberships` | 10 | 0 | P0 |
| C-2 | Class roster page: search, add, remove with reason, transfer dialog, export, Telegram message to parents of selected; entry from a row action and from the class detail page | `pages/admin/classes/ClassRosterPage.tsx`, `ClassTransferModal.tsx` (new); `ClassesPage.tsx`, `ClassDetailPage.tsx` (link only); `api/services/classMemberships.ts` | no | 2 | 8 | P0 |
| C-3 | Search and xlsx export of the class list | `ClassesController.cs`, `ClassesPage.tsx` | no | 2 | 2 | P2 |
| C-4 | Capacity on the class; warn when an add or transfer exceeds it | `ClassFormModal.tsx`, `ClassesController.cs` | `classes.capacity` | 1 | 1 | P2 |
| C-5 | Pick homeroom teacher(s) on the class form (today only from the teacher side) | `ClassFormModal.tsx`, `ClassesController.cs` (writes `Teacher.HomeroomClass`) | no | 2 | 2 | P2 |
| C-6 | One discipline incident for every pupil of a class | `DisciplineController.cs` (new `POST points/class`), `ClassesPage.tsx` | no | 2 | 2 | P2 |

**Declined / elsewhere.** `buildingId` (§2.1 of gaps, one building). Separate `letter` column
(our `name` "5-A" plus `grade` carries it). Class **moderators** — responsible staff is folded
into Q9. Bulk subscription assignment from the roster → Moliya parity. Class `home_room_id` →
schedule module (`warehouse.md` §3.2). Rename bug → G-1.

---

### 2.3 O'quvchilar — list, form, bulk actions, import/export, profile

#### 2.3.1 EduSchool capture **[bundle chunk @668002–904899]**

**List — `/students`** (component `lu` @742932)

| Element | Detail |
|---|---|
| Data | `GET /students/pagin` with `noArchive=true`, search, paging, sort, filters. Columns server-driven `student_pagin` **[unrecoverable]**; overridden ones reveal: `imageUrl` (avatar, initials fallback), `balance` (currency), `paymentDay`, `coins`, `phoneNumber` (+ `otherPhoneNumbers` under it), `subscription.name`, **`status` as an inline coloured select** (change → `PUT student {_id, statusId}`, not sortable). `balance`/`paymentDay` hidden without `canSeeStudentBalance`; `coins` hidden when gamification is off |
| Footer | with `canSeeStudentBalance`: total debt (red) and total credit (green) |
| Add | `editStudents` → `/students/details` |

**Filters** (collapsible panel, open state remembered)

| # | Filter | Type | Param |
|---|---|---|---|
| 1 | Learning language | select uz/ru/eng/kaa | `language` |
| 2 | Date range | range picker | `fromDate`/`toDate` (**[inferred]** created/enrolment date) |
| 3 | Grade | multi-select, checkboxes, 0–11 | `grade` (JSON ids) |
| 4 | Balance state | select paid-up / debtor | `showDebitors` |
| 5 | Minimum debt | numeric text, spaced thousands, debounced | `minDebtAmount` |
| 6 | Status tag | async select `/student/status/pagin` | `statusId` |
| 7 | Has contract | yes / no | `hasContract` |
| 8 | Has subscription | yes / no | `hasAboniment` |
| 9 | Subscription | async select `/subscription/pagin` | `subscriptionId` |
| 10 | Group | async select `/groups/pagin` | `groupId` |
| 11 | Discount | async select `/discount/pagin` (hidden when `onTimePrivilegeEnabled`) | `discountId` |
| 12 | Gender | boy / girl | `gender` |
| 13 | Certificate type | multi-select | `certificateTypeIds` |
| 14 | Certificate teacher | async select teachers | `certificateTeacherId` |
| 15 | Turnstile not synced | multi-select devices | `unsyncedTurnstileDeviceIds` |
| 16 | Payment day range | day-of-month picker | `fromPaymentDay`/`toPaymentDay` |
| 17 | Age range | two numeric inputs, debounced | `fromAge`/`toAge` |
| 18 | Balance range | two signed numeric inputs, debounced | `fromBalance`/`toBalance` |

**Row actions:** edit (`editStudents`) · delete (`deleteStudents`) → `DELETE student/{id}` ·
give coins (`assignCoinToStudent`, gamification on) · discount (billing discount when
`onTimePrivilegeEnabled` + `billingDiscountManage`, else `PUT student {_id, discountId}`) ·
**archive** → reason from `/reasons?type=student-archive` + "other" (free text required) →
`PUT student/archive-with-reason {_id, archivedReasonId, archivedReason?}`.

**Header / bulk actions** (show when rows are selected)

| Action | Gate | Request | Result |
|---|---|---|---|
| Bulk **archive** | `deleteStudents` | `PUT /student/archive-many-with-reason {studentIds, archivedReasonId \| archivedReason}` | `{total, succeeded[], skipped[], failed[]}` with codes `NOT_FOUND 20600`, `CLASS_EXISTS 20602`, `DEBTED 20603`, `PAID 20604` |
| Bulk **attach contract** | `editStudents` | `PUT /student/attach-contract-many {studentIds, contractNumber (required), contractDate, paymentType, paymentDay}`; submit enabled only when all four set | skipped reason `AllreadyHasContract` |
| Bulk **delete** | `deleteStudents` | `POST /student/delete-many {studentIds}` | same result shape |
| Bulk **cancel subscription** | `cancelStudentSubscription` | preview `POST /student/subscription/cancel/bulk/preview {studentIds, activatesAt (required), comment}` → counts + returning price per pupil → confirm `POST …/cancel/bulk` | Moliya-owned |
| **Send SMS** | none | "to whom": current page (N) or all pages (total); `POST /sms {content, integrationId, studentIds \| [], toStudent:true, type:"student"}` | declined → Telegram |
| **Turnstile sync** | setting `calendarAttendanceDataSource === "turnstile"` | `POST turnstile-sync/branch/bulk-sync {userTypes:["student"], userIds?}` | turnstile-owned |
| **Import** | none | §2.3.1 import | |
| Filter toggle, export | export built from current filters | `GET /student/export?grade&fromDate&toDate&fromPaymentDay&toPaymentDay&showDebitors&state&hasContract&search&hasAboniment&abonimentId&statusId&language&groupId&classId&gender&minDebtAmount&discountId&sortBy&sortOrder…` → `{data:fileUrl}` (**[inferred]** xlsx); columns **[unrecoverable]** | |

**Import** (component `ru` @734239)

1. Template: `GET /import/students/shablon`, saved as `students.csv`; columns **[unrecoverable]**
   (server-side).
2. Upload: `POST /import/students` (multipart `file`) → `{uploadedFilePath}` **or** validation
   errors `message.errors[{row, error[{property, constraints}]}]`, shown per row with the field
   name translated from `keys.<property>`.
3. Confirm: `POST /import/students/confirm {uploadedFilePath}` → `{created, updated, failedCount,
   failedRows[{lastName, firstName, middleName, contractNumber, phoneNumber, error}],
   failedFileUrl}`. **The import upserts** (`created` + `updated`) and offers the failed rows as a
   downloadable file.

**Form — `/students/details[/:id]`** (component `va` @712241, sections @701097–712241). A left
rail scroll-spies four sections: *about pupil*, *login & password* (edit only), *parents*,
*contract*. Submit → `POST|PUT /student`.

| Section | Field | Type | Rules |
|---|---|---|---|
| Pupil | `studentProfile` | image upload | optional |
| | `lastName`, `firstName`, `middleName` | text | required, min 3 (default) |
| | `gender` | select boy/girl | required (default) |
| | `birthday` | date ≤ today | optional |
| | `grade` | select 0–11 | required only when setting `isStudentGradeRequired` |
| | `phoneNumber` | phone | required + pattern (default) |
| | `language` | select uz/ru/en/kaa/kz/tj | optional |
| | `assignedModeratorId` | async select moderators | optional; shown when setting `assignedModeratorEnabled`, with a reassignment note |
| | `otherPhoneNumbers[]` | repeatable phone | optional |
| | `address` | text | required, min 3 (default) |
| | `birthCertificate` | file .png .jpg .pdf (label "passport") | optional |
| | student custom fields | — | declined (§2.6) |
| Parents | `parents[]` | **1–2 entries** (add hidden at 2, delete shown when > 1) | |
| | `type` | select (**[inferred]** father / mother / other) | required |
| | `otherParent` | text, when type = other | required |
| | `fullName` | text | required, min 3 |
| | `phoneNumber` | phone | required + pattern |
| | `email` | email | optional |
| | `birthday` | date ≤ today | optional |
| | `password` | password, 10-char random on create, regenerate button | optional |
| | `parentPassport` | file | optional |
| | removing a saved parent | `POST /student/remove-parent {studentId, parentId}` | |
| Contract | `contractNumber` | text | required, min 3 — **disabled** when setting `autoCreateStudentContractNumber` or `structuredContractNumberEnabled` |
| | `contractDate` | date ≤ today | required (default) |
| | `contractEndDate` | date ≥ contractDate | optional |
| | `paymentDay` | select (constants) | optional |
| | agreement custom fields | — | declined (§2.6) |
| Login (edit) | `login` (read-only), `password` | text / password | optional |

**Class is not on the form.** A pupil is placed through the class roster (§2.2.1).

**Profile — `/students/info/:id`** (component `jm` @879766)

*Left panel* (`gm`): photo with a menu (discount / billing discount, turnstile photo sync,
Google Sheets push); name; **UUID** with copy; **balance** (click → debt by month
`GET /student/debt-transactions/{id}`); coins; life points (behaviour system); phone with call
(`POST /make_call`) and copy; **class** `grade-letter`; birthday; **active period** (click →
`GET /student/total-days-in-classes/{id}` timeline of classes with joined/left dates, days and
leave reason); archive date; address; buttons **Contract** (`studentContract`) and **Edit**
(`editStudents`).

*Tabs* (`bm` @862548) — four primary, the rest under "More":

| Tab | Gate | Shows / does |
|---|---|---|
| Schedule | — | week grid from `GET /students/lessons?_id&fromDate&toDate`: teacher, room, subject, **group name when `class.type === "group"`**, time |
| Transaction history | `canSeeStudentBalance` | `/students/transactions/export`-backed list with category filter, receipt print — Moliya |
| Subscription | — | assign / re-subscribe / cancel with preview — Moliya |
| Comments | `getStudentComments` | `student_comment_pagin`: type positive/negative (chip), comment, image, file download, employee (name, phone, type); filter by type; add/edit (`editStudentComments`), delete (`deleteStudentComments`); `/student/profile` endpoints |
| Chat | `getChats` | chat with the parent |
| SMS messages | — | `student_sms_pagin`: recipient, sendTo, content, sender |
| Contracts | any user | `student_contract_pagin` — §2.10 |
| Active groups | — | class + group memberships, remove (`deleteClassStudent`) — §2.1.1 |
| Attendance | — | calendar view and grouped view |
| Attendance range report | — | `GET /analytics/attendance/range?studentId&fromDate&toDate` → planned / attended / excused / unexcused, and the same per subject |
| Password | — | `POST /student/password {studentId, password}` |
| Activity history | — | `GET /audits?entityId=` infinite scroll: employee, device user agent, changed at, field-level changes (balance hidden without `canSeeStudentBalance`) |
| Behaviour incidents | `editBehaviorIncidents` | `student_incident_pagin`: behaviour type, points; add/edit/delete |
| Locations | — | §2.8 |
| Turnstile access | — | per device: user type normal/blacklist switch, unblock-till date → `PUT /student/access-status`, `PUT /student/turnstile/access` |
| Call history | — | direction, time, operator, duration, recording |
| Seasonal appropriation | — | seasonal marks — admission & testing spec |
| Coin history | `getCoinsHistory`, gamification on | Gamification spec |
| Borrowed books | `readBooksView` | WareHouse spec |
| Certificates | edit: `editStudents` | §2.7 |
| Billing discount | `onTimePrivilegeEnabled` + `billingDiscountManage` | Moliya |
| Debt history | `debtors` | `debt_history_pagin`: month, balance at action, employee, status, comment, expected payment date (with the changed date struck through) — Moliya |
| Turnstile sync history | turnstile source | device, sync status, has face, person id, synced at |

**Student status catalogue** (Settings → `STUDENT_STATUS`) **[bundle @2043547]**:
`GET /student/status/pagin` (`student_status_pagin`, colour swatch column); form `name`
(required), `color` (colour input, optional) → `POST|PUT /student/status`; delete
`DELETE student/status/{id}`; **default statuses (`isDefault`) cannot be edited or deleted**;
perms `getStudentStatus`, `createStudentStatus`, `updateStudentStatus`, `deleteStudentStatus`.
The status object also carries `state`.

**Permissions** **[bundle @2275300]**: `getStudents`, `editStudents`, `deleteStudents`
(= make archive), `inactivateStudents` (= unarchive), `cancelStudentSubscription`,
`deleteAllStudentSubscriptionTransactions` (refused on principle, gaps §7.4), `exportStudent`,
`getParents`, `getStudentComments`, `editStudentComments`, `deleteStudentComments`,
`studentCustomFieldsCreate/Delete` (declined), `canSeeStudentBalance`, `studentContract`.

#### 2.3.2 Ours **[ours]**

`StudentsPage.tsx` loads **every** pupil and filters in the browser: search (name, parent name),
class, gender, balance state, certificate type and certificate teacher (the last two
server-side). Columns: checkbox, #, F.I.SH, class, gender, birthday, parent, parent phone,
balance; archive tab adds archive date and reason. Row actions: payment history, edit, archive
(active) / restore, hard delete (archive). Bulk: Telegram message ("SMS yuborish" label,
`SmsModal.tsx` → `POST /api/admin/messages/broadcast`, perm `messages`), CSV of selected rows,
archive. Import: `GET /import-template` + `POST /import` (xlsx, one step, partial, no preview,
no upsert, no duplicate detection). Export: superadmin credentials xlsx only.

`StudentFormModal.tsx`: surname, name, patronymic, birthday, gender, photo (stored in
`BirthCertificateUrl`), one parent (surname, name, patronymic, phone — no format check,
photo in `ParentPassportUrl`), address, **class select**, enrolment date; edit adds login and new
password. `Guardian`/`StudentGuardian` (`Guardians.cs`) exist and are derived from
`Student.ParentPhone` by `GuardianSync`; relation values are `parent|grandparent|trustee`
(`GuardianModel.cs:87-89`).

`StudentDetailPage.tsx`: one long analytics page (header card, personal data, finance view,
stat cards, attendance and homework charts, grade matrix, absence reasons, monthly feedback,
assignment scores, discipline history). **No tabs, no actions, no certificates, contracts,
guardians list, class history, comments or audit.**

#### 2.3.3 Gap list — O'quvchilar

| # | Gap | Files on our side | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| S-1 | **Server-side list**: paging, sort, search and the filter set — grade (multi), class, **group**, gender, debtor, minimum debt, balance range, age range, **status**, **has contract**, has subscription, fee category, discount, language, certificate type/teacher (have), enrolment date range. Additive endpoint; the old `GET` stays until the page switches | `SchoolLms.Server/Controllers/StudentSearchController.cs`, `SchoolLms.Application/Services/StudentListQuery.cs` (new; uses `StudentBalanceQuery`); `pages/admin/students/StudentsPage.tsx:354-469`, `api/services/students.ts` | reads S-5, S-8, K-1, `study_group_members` | 12 | 10 | P1 |
| S-2 | **Xlsx export of the filtered list** (not only selected rows, not only credentials), same filters as S-1 | `StudentSearchController.cs` (`GET export`), `ExcelExport.cs`; `StudentsPage.tsx` | no | 4 | 1 | P1 |
| S-3 | **Import in two steps**: validate → per-row, per-field errors → confirm; **upsert** (match on surname + name + birthday + class, reported as `updated`); failed rows as a downloadable xlsx; template gains pupil phone, second guardian, contract number | `StudentImportController.cs` (new), `ExcelImport.cs`; import modal in `StudentsPage.tsx` | no | 10 | 8 | P1 |
| S-4 | Debt / credit totals under the list | `StudentsPage.tsx` (S-1 response) | no | 1 | 1 | P2 |
| S-5 | **Status tags**: catalogue (name, colour, position, active; seeded defaults cannot be deleted), inline change from the list, filter, settings page | `StudentStatusesController.cs` (new), `settings/StudentStatusesSettings.tsx` (new), `SettingsPage.tsx`, `StudentsPage.tsx` | `student_statuses`, `students.status_id` | 6 | 8 | P1 |
| S-6 | Telegram message to **every pupil matching the filter**, not only selected rows (EduSchool "all pages") | `MessagesController.cs:113` (scope `filter`), `SmsModal.tsx` | no | 2 | 2 | P2 |
| S-7 | Bulk hard delete on the archive tab with a per-pupil result (blocked where invoices/payments exist) | `StudentsController.cs:309` logic reused in `StudentSearchController.cs`, `StudentsPage.tsx` | no | 3 | 2 | P2 |
| S-8 | **Form fields**: pupil's own phone; learning language; a document scan (birth certificate/passport) **separate from the photo** — `BirthCertificateUrl` keeps meaning "photo" (documented) and a new `document_url` holds the scan, so no data is moved; **second guardian** with relation (father/mother/grandparent/trustee/other + note); Uzbek phone validation on guardians. The payload is agreed up front in §4.2 because slice 1 owns `StudentsController.cs` | `StudentFormModal.tsx`, `StudentsController.cs` (payload, owned by slice 1), `GuardianSync`, `AdminGuardiansController.cs` | `students.phone`, `students.language`, `students.document_url`; `student_guardians.relation` values + `relation_note` | 8 | 10 | P1 |
| S-9 | Pupil without a class yet (target grade only), placed later from the class roster | `StudentFormModal.tsx`, `StudentsController.cs` | `students.target_grade` | 2 | 2 | P2 |
| S-10 | **Profile as tabs**, header actions (edit, archive, contract, reset password): *Umumiy* (today's analytics, unchanged), *Jadval* (week from `PupilTimetable`), *To'lovlar* (existing ledger view embedded), *Sinf va guruhlar* (G-10), *Davomat* (month calendar + date-range report per subject), *Sertifikatlar* (Z-1), *Shartnomalar* (K-1), *Izohlar* (S-11), *Faoliyat tarixi* (S-12), *Intizom* (existing points history). Left panel adds active period and archive date | `StudentDetailPage.tsx` split into `pages/admin/students/profile/*` (new); `StudentsController.cs` (`/profile` unchanged), `AttendanceController.cs` (range report) | no | 6 | 20 | P1 |
| S-11 | **Comments on a pupil**: positive/negative, text, optional image and file, author and time; edit/delete by author or admin | `StudentCommentsController.cs` (new), `profile/CommentsTab.tsx` (new) | `student_comments` | 6 | 8 | P1 |
| S-12 | **Activity history**: `audit_logs` for this pupil with field-level before/after, money fields hidden without the finance right | `AuditController.cs` (filter by `studentId`, index exists `AppDbContext.cs:133`), `profile/ActivityTab.tsx` (new) | no | 2 | 4 | P1 |

**Declined / elsewhere on this screen.** Give coins, coin history → Gamification spec.
Discount / billing discount / subscription / bulk cancel subscription / transaction and debt
history tabs → Moliya parity (our `Moliya → Chegirmalar`, `Obunalar`, `DebtorsTab` already
exist; embedding is not priced here). Turnstile face sync and access blacklist → Keldi-ketdi
parity. Borrowed books → WareHouse spec. Seasonal appropriation → admission & testing spec.
**Declined outright:** SMS send/log (Telegram only), call and call history (telephony — money
and a third party), Google Sheets push, custom fields (§2.6), pupil login UUID copy (our ids are
internal), `assignedModeratorId` (Q9), parent email and password (parents sign in through
Telegram).

---

### 2.4 Arxiv o'quvchilar

#### 2.4.1 EduSchool **[bundle @7881947–7886900]**

Own page `/archive-students`: `GET /students/pagin?state=archive`, columns `student_archive_pagin`
(balance hidden without `canSeeStudentBalance`, rest **[unrecoverable]**). Filters: language,
date range (**[inferred]** archive date), grade (multi), balance state, has contract, **archive
reason** (`/reasons?type=student-archive`), payment day range. Row action **unarchive**
(`inactivateStudents`): optional reason from `/reasons?type=student-inactive` →
`PUT student/inactivate/{id} {reasonId?}`. Header: send SMS, filter, import (same component),
export (`exportStudent`) `GET /student/export?…&state=archive`.

#### 2.4.2 Ours **[ours]**

The *Arxiv* tab of `StudentsPage.tsx` (same filters as active, plus archive date and reason
columns), restore (`POST /{id}/restore`, login stays blocked), hard delete, bulk archive through
`ArchiveStudentsModal.tsx` (`POST /archive-many`, catalogue `student_archive_reasons`, free text
required, debtor guard with superadmin force), settings page `ArchiveReasonsSettings.tsx`. A
separate menu entry is **not** needed: the tab is our design and gaps §1.1 counted it as *have*.

#### 2.4.3 Gap list — Arxiv

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| A-1 | Archive tab filters: archive date range, archive reason, language, grade, debtor, has contract (served by S-1) | `StudentListQuery.cs`, `StudentsPage.tsx` | no | 1 | 3 | P2 |
| A-2 | Free-text reason on restore, recorded in the audit row | `StudentsController.cs:392`, `StudentsPage.tsx` | no | 1 | 1 | P2 |
| A-3 | Xlsx export of the archive list (served by S-2) | `StudentSearchController.cs`, `StudentsPage.tsx` | no | 1 | 1 | P2 |

---

### 2.5 Fanlar (subjects)

#### 2.5.1 EduSchool

The page chunk is **not in the capture** → table, filters and form are **[unrecoverable]**.
Recovered from consumers **[bundle]**: menu role `getSubjects`, prefetch
`subjects/pagin?search&page&limit`; permissions `getSubjects`, `editSubjects`, `deleteSubjects`.
A subject carries `name`, **`color`** (used to tint schedule cells, @458381), **`isGroupsSubject`**
(@448899, @~1409000) and **`isActive`** (`subjects/all?isActive=true`); `/subjects/teacher`
returns a teacher's subjects.

#### 2.5.2 Ours **[ours]**

`SubjectsPage.tsx` card grid (name, edit, delete, count), `SubjectFormModal.tsx` (name,
required). `SubjectsController.cs` guarded by perm **`schedule`** (`:12`) while the menu item sits
under O'quv bo'limi (`students`); **delete has no reference guard** (`:39`).

#### 2.5.3 Gap list — Fanlar

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| F-1 | **Bug:** refuse deleting a subject used by schedule, journal, quarter grades or certificates (suggest deactivation) — **priced and built inside G-9** | `SubjectsController.cs:39` | no | — | — | P0 (G-9) |
| F-2 | Groupable flag | → G-9 | `subjects.is_groupable` | — | — | P0 |
| F-3 | Colour (tints schedule cells) and active/inactive instead of delete | `SubjectsController.cs`, `SubjectFormModal.tsx`, `SubjectsPage.tsx`, `ScheduleBoard.tsx` | `subjects.color`, `subjects.is_active` | 2 | 4 | P2 |
| F-4 | Route guard `schedule` vs menu `students` mismatch | `App.tsx:117`, `navigation.ts:108`, `SubjectsController.cs:12` | no | 0 | 1 | P2 |

---

### 2.6 Xonalar (rooms)

#### 2.6.1 EduSchool **[bundle @663900–667000, buildings @2006800]**

`/rooms`: `GET /rooms/pagin` (`room_pagin`), filter `buildingId`. Row actions: edit
(`editRooms`), **room assets** → `/wms/room-asset?roomId=` (`wmsRoomAssetView`), delete
(`deleteRooms`) → `DELETE rooms/{id}`. **Add room** (`editRooms`): `name` (required, min 3),
`buildingId` (required), `maxStudentsCount` (optional) → `POST|PUT rooms`. **Add rooms** (no
gate): `count` (required, max 50), `buildingId` → `POST rooms/multiple`. Buildings live in
Settings (`getBuilding`/`editBuilding`/`deleteBuilding`): `name` → `POST|PUT building`.

#### 2.6.2 Ours **[ours]**

`SchoolClass.Room` free text (`Entities.cs:221`), shown as "Xona" in the class tables. No entity,
no CRUD. `warehouse.md` §3.2 specifies `rooms` exactly as `SPEC.md` §3.3 and says whichever
module lands first creates it; **this module lands first**.

#### 2.6.3 Gap list — Xonalar

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| R-1 | Rooms register: list, create, edit, delete, **bulk create N rooms** (max 50, gaps §2.1 "do it"), capacity and kind; seeded from `select distinct room from classes` | `SchoolLms.Domain/Rooms.cs`, `SchoolLms.Server/Controllers/RoomsController.cs`, `pages/admin/rooms/RoomsPage.tsx`, `api/services/rooms.ts` (new) | `rooms` (exact `SPEC.md` §3.3 shape) | 6 | 8 | P1 |

**Declined / elsewhere.** Buildings (gaps §2.1). Room assets → WareHouse. Room on a lesson,
room conflicts, `classes.home_room_id` → schedule module (`warehouse.md` §3.2).

---

### 2.7 Sertifikat

#### 2.7.1 EduSchool **[bundle @2268900–2271750, @2046700–2048300, @858293, @1146800]**

List `/certificates` (`getStudents`): `GET /certificates?search&page&limit`, **local** columns:
student, **subjects (many)**, type, number, score, issued at, expires at, teacher, file link,
comment. Writes gated by `editStudents`: add, edit, delete (`DELETE /certificate {_id}`),
**export** `GET /certificate/export`. Form chunk missing → fields only: `studentId`,
`subjectIds[]`, `certificateTypeId`, `teacherId`, `number`, `score`, `issuedAt`, `expiresAt`,
`fileUrl`, `comment`. **Student profile tab** (`student_certificate_pagin`, add/edit/delete).
**Teacher profile tab** "certificate results" (`GET /certificates?teacherId=`, export).
Types in Settings (`generalSettings`): `name` (required), `isSat` → `POST|PUT /certificate-type`.

#### 2.7.2 Ours **[ours]**

Shipped 2026-09-16: `CertificatesPage.tsx` (Ro'yxat/Natijalar tabs, server filters search,
type, teacher, class, issued range, expiring in 60 days), `CertificateFormModal.tsx` (single
subject, validated server-side), `CertificateResultsTab.tsx` (score table),
`CertificateTypesPage.tsx` (`isScored`, `isActive`), `CertificatesController.cs` and
`CertificateTypesController.cs` (perm `students`), two filters on the student list. **No profile
tab, no export.**

#### 2.7.3 Gap list — Sertifikat

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| Z-1 | Certificates tab on the student profile (promised by gaps §2.3, not built); API already filters by `studentId` | `profile/CertificatesTab.tsx` (new), `CertificateFormModal.tsx` (preset student) | no | 0 | 4 | P1 |
| Z-2 | Xlsx export with the current filters | `CertificatesController.cs`, `CertificatesPage.tsx` | no | 3 | 1 | P2 |
| Z-3 | Several subjects per certificate | `CertificateService.cs`, `CertificateFormModal.tsx`, `CertificatesPage.tsx` | `certificate_subjects` | 4 | 3 | P2 |
| Z-4 | Teacher profile: certificates issued under this teacher | teacher detail page under `pages/admin/teachers/` | no | 0 | 3 | P2 |

---

### 2.8 O'quvchilar manzili

#### 2.8.1 EduSchool **[bundle @2260300–2268900, @826300–833000]**

Map page (menu role `_id` = any logged-in user): branch selector (declined), OSM tiles, clustered
markers, viewport fetch `POST /locations/bounding-box` (declined, gaps §2.4). Marker popup:
location name, pupil link, pupil phone, type, pickup time, coordinates, "view on map".
**Locations are entered per pupil on the profile tab**: at most **3**, one per type
`homeLocation` / `schoolLocation` / `pickupLocation`; form `name` (required, address
autocomplete `POST /locations/byname` ≥ 2 chars), `type` (required), `pickupTime` (HH:mm step 5,
required only for pickup, stored as a 5-minute window), `lat`/`lng` by map click with reverse
geocoding `POST /locations/bypoint`, `url` auto (Yandex link) → `POST|PUT /location`,
`DELETE /location/{id}`.

#### 2.8.2 Ours **[ours]**

`LocationPage.tsx`: Leaflet map of `Student.Latitude/Longitude`, class filter, class share chips,
table. `GET /api/admin/locations` (perm `app`). **Nothing in the current product writes these
coordinates**: the only writer is `PUT /api/student/location`
(`StudentPortalController.cs:795`) for the retired mobile app, and no Mini App code calls it.
The screen shows only legacy data.

#### 2.8.3 Gap list — Manzil

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| L-1 | Staff set a pupil's home location: map click + typed address, from the profile and from the map page | `LocationsController.cs` (new `PUT {studentId}`), `profile/LocationTab.tsx` (new), `LocationPage.tsx` | no (existing columns) | 3 | 6 | P1 |
| L-2 | Up to three typed locations (home / school / pickup + pickup time); popup shows type and time | `LocationsController.cs`, `LocationPage.tsx`, `profile/LocationTab.tsx` | `student_locations` | 4 | 5 | P2 |

**Declined.** Address search and reverse geocoding (external provider, Q6), bounding-box and
clustering (gaps §2.4), branch selector (single branch).

---

### 2.9 Ota-onalar

#### 2.9.1 EduSchool **[bundle @8097182–8101300]**

`GET /parents` (`parent_pagin`), row selection, **no add/edit here** (parents are edited inside
the student form). Overridden columns: `type` father/mother/other (+ `otherParent` text),
**children** as links "`lastName firstName - grade letter`", children's phone numbers.
Filters: class, group, pupil, relation (father/mother), **app installed** yes/no, pupil state
(active/inactive/archive), date range. Export (`exportParent`):
`GET /parents-export?fromDate&toDate&studentId&classId&groupId&type&isAppInstalled`
(defaults to the current week when no dates). Bulk **send SMS** to the children's parents (no
gate) — declined → Telegram.

#### 2.9.2 Ours **[ours]**

`ParentsPage.tsx`: read-only, rows built by grouping active pupils by the digits of
`Student.ParentPhone` (`ParentsController.cs:59`); stat cards (total, activated, not activated);
search; activation filter; columns name, phone, children count, activated, device, first login,
last seen; expandable children. Device data comes from the legacy push tokens.
`AdminGuardiansController.cs` (CRUD, link child, create account) **has no screen**.

#### 2.9.3 Gap list — Ota-onalar

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| P-1 | List built from **`guardians` + `student_guardians`** (not phone grouping): relation, children with class links, phone, **Telegram connected** (replaces "app installed"), filters class / group / relation / connected / pupil state, search; the old route keeps its shape until the page switches | `ParentsController.cs` (new action), `ParentsPage.tsx` | no | 6 | 8 | P1 |
| P-2 | Xlsx export of the filtered list | `ParentsController.cs`, `ParentsPage.tsx` | no | 3 | 1 | P1 |
| P-3 | Telegram message to selected parents | `MessagesController.cs` (scope `guardians`), `ParentsPage.tsx`, `SmsModal.tsx` (reuse) | no | 3 | 3 | P1 |
| P-4 | Edit a guardian and link more children from this screen (API exists) | `AdminGuardiansController.cs`, `ParentsPage.tsx` | no | 0 | 6 | P2 |

---

### 2.10 Shartnomalar

#### 2.10.1 EduSchool **[bundle @880800–904900, @812500–815900, @725020]**

Three different things share the word "contract":

1. **Template register** `/contracts`, `/contracts/details` (`getContract`, `createContract`,
   `updateContract`, `deleteContract`). **The page chunk is missing → [unrecoverable].**
   **[inferred]** from its readers: `{_id, name, text: HTML, type: student|employee,
   watermarkImage, watermarkOpacity (0.1), watermarkPosition: center|header}`; list
   `/contract/pagin?type=`.
2. **Per-pupil generation** `/students/contract/:id` (`studentContract`): pick a template;
   editable data form pre-filled from `GET student-contracts/{studentId}` — pupil (names, phone,
   gender, address, birthday, class grade/letter), parents (father/mother/other name, phone,
   email), contract (date ≤ today, number, payment type, balance if allowed, last payment,
   payment day), subscription (state, price, duration), additional subscription (name, yearly,
   monthly, duration), totals (read-only), agreement custom fields. The template text is filled
   by `{{key}}` substitution (custom field ids, dotted paths, formatted dates, translated payment
   type; unknown keys stay literal). Actions: **attach** `PUT /student-contract {contractId,
   studentId, text}`, print (A4, Times New Roman, watermark), PDF export (client-side).
3. **Signed-contract register** per pupil — profile tab `GET /student-contract?studentId=`
   (`student_contract_pagin`): download the uploaded file, or PDF/Word of the generated text
   (DOCX built client-side); add/edit (`contractId` optional, `contractUrl` file optional) →
   `POST|PUT /student-contract`; delete.

Plus **contract scalars on the pupil**: `contractNumber`, `contractDate`, `contractEndDate`,
`paymentType`, `paymentDay`, set in the form (§2.3.1) or by **bulk attach**
(`PUT /student/attach-contract-many`); number auto-generated when
`autoCreateStudentContractNumber` / `structuredContractNumberEnabled`; list filter
`hasContract`; import failed rows carry `contractNumber`.

#### 2.10.2 Ours **[ours]**

`ContractsPage.tsx` (tabs Xodimlar / Ota-onalar): upload a **.docx** template, list/select/delete,
token hints (`@ota_ona @telefon @farzandlar @sana @raqam`; staff tokens), recipients table
(Telegram-registered only selectable), **send** → each recipient gets a filled .docx in Telegram
with the next global number (`ContractsController.cs`, perm `contracts`; filling in
`ContractService.cs`). `Contract` rows are written but **never listed**; there is **no pupil
link**, no dates, no status, no upload of a signed copy, no per-pupil generation.

#### 2.10.3 Gap list — Shartnomalar

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| K-1 | **Pupil contract register**: record per pupil (template, number, signed date, end date, file, source generated/uploaded, comment); list page (search, class, has file, date range) and profile tab | `SchoolLms.Domain/StudentContracts.cs`, `StudentContractsController.cs` (new); `pages/admin/contracts/StudentContractsTab.tsx`, `profile/ContractsTab.tsx` (new); `ContractsPage.tsx` (new tab) | `student_contracts` | 8 | 10 | P1 |
| K-2 | **Generate for one pupil** from a Word template: token set extended with pupil, guardians (both), class, subscription and totals; review-and-edit form before generating; saves the .docx into K-1 | `ContractService.cs` (tokens), `StudentContractsController.cs`, `profile/GenerateContractModal.tsx` (new) | no | 8 | 6 | P1 |
| K-3 | Upload a signed scan into the register | `StudentContractsController.cs` (reuses `UploadsController` + `UploadGuard`) | no | 1 | 2 | P1 |
| K-4 | "Has contract" filter and contract number column on the student list (served by S-1) | `StudentListQuery.cs`, `StudentsPage.tsx` | no | 1 | 1 | P1 |
| K-5 | Bulk attach the same number/date to selected pupils, per-pupil result | `StudentContractsController.cs`, `StudentsPage.tsx` | no | 3 | 3 | P2 |
| K-6 | Numbering rule setting: automatic sequence vs manual | `ContractService.cs`, `SettingsController.cs`, `SchoolSettings.tsx` | `school_meta.contract_number_mode` | 2 | 2 | P2 |

**Declined / elsewhere.** HTML rich-text template editor with watermark — our templates are
edited in Word and filled by `ContractService`, which is the same function without a second
editor to maintain. Agreement custom fields (§2.6). PDF output (Q4). Per-pupil payment day that
moves the invoice due date (Q5, Moliya). Employee contracts → `docs/modules/hr.md`.

---

### 2.11 Cross-cutting

| # | Gap | Files | Schema | BE h | FE h | P |
|---|---|---|---|---|---|---|
| X-1 | Per-user table settings for the big lists (hide / reorder / pin columns, remembered server-side) — EduSchool does this on every list | shared table component under `schoollms.client/src/components/`, `UserTableSettingsController.cs` (new) | `user_table_settings` | 4 | 10 | P2 |
| X-2 | Finer permission keys for the module: view vs edit vs archive vs export (EduSchool: `getStudents`, `editStudents`, `deleteStudents`, `inactivateStudents`, `exportStudent`, comment rights). `AdminPermAttribute` is class-level only today | `AdminPermAttribute.cs` (method-level, `AllowMultiple`), `config/constants.ts`, `StudentsController.cs`, `ClassesController.cs`, `StudyGroupsController.cs` | no | 8 | 4 | P2 |
| X-3 | Teacher-facing groups: a teacher sees own groups and may edit a roster when allowed (EduSchool `getStudentGroups` / `updatedStudentGroup`) | `TeacherPortalController.cs`, `pages/teacher/` (new groups page) | no | 4 | 6 | P2 |

**SMS-shaped actions, mapped.** Students list "send SMS" → our Telegram broadcast (have; S-6
adds "all matching"). Class roster, group roster, parents list → Telegram message (C-2, G-8,
P-3). Archive list SMS, profile SMS log, SMS templates, SMS integrations, auto-SMS, call and call
history → **declined** (`CLAUDE.md`).

---

## 3. Schema changes, consolidated

One migration owner writes each batch in one migration, reads `Up()` line by line, adds a
`Migrations/Sql/*.sql` grant file (`GRANT … TO app_rw`, following
`parity_wave2_guards.sql`) and registers it as `EmbeddedResource`. **None of these tables is
financial: no `REVOKE`.** None of the names below appears in `deploy/init-roles.sql` §5.
Conventions follow `certificates` (uuid PK, `text` FKs to `students`/`classes`/`subjects`/
`teachers`/`users`, `created_by`/`created_at`, `btrim(name) <> ''` checks).

### 3.1 Batch A — Groups and class memberships (P0) — migration `StudyGroupsAndMemberships`

```sql
-- 1. Groups
create table study_groups (
  id           uuid primary key default gen_random_uuid(),
  name         text not null check (btrim(name) <> ''),
  subject_id   text not null references subjects(id) on delete restrict,
  gender       text null check (gender in ('male','female')),
  is_archived  boolean not null default false,
  archived_at  timestamptz null,
  created_by   text not null references users(id) on delete restrict,
  created_at   timestamptz not null default now(),
  unique (id, subject_id)                                   -- target of the composite FK below
);
create unique index ux_study_groups_name_active
  on study_groups (subject_id, lower(name)) where not is_archived;

-- 2. Feeding classes (grades are derived from the classes)
create table study_group_classes (
  group_id  uuid not null references study_groups(id) on delete cascade,
  class_id  text not null references classes(id) on delete restrict,
  primary key (group_id, class_id)
);
create index ix_study_group_classes_class on study_group_classes (class_id);

-- 3. Teachers
create table study_group_teachers (
  group_id    uuid not null references study_groups(id) on delete cascade,
  teacher_id  text not null references teachers(id) on delete restrict,
  primary key (group_id, teacher_id)
);
create index ix_study_group_teachers_teacher on study_group_teachers (teacher_id);

-- 4. Members (dated; history is kept)
create table study_group_members (
  id            uuid primary key default gen_random_uuid(),
  group_id      uuid not null,
  subject_id    text not null,
  student_id    text not null references students(id) on delete cascade,
  joined_on     date not null,
  left_on       date null,
  leave_reason  text null,
  created_by    text not null references users(id) on delete restrict,
  created_at    timestamptz not null default now(),
  foreign key (group_id, subject_id) references study_groups (id, subject_id)
    on update cascade on delete cascade,
  check (left_on is null or left_on >= joined_on)
);
create unique index ux_group_members_one_active
  on study_group_members (group_id, student_id) where left_on is null;
create unique index ux_group_members_one_group_per_subject
  on study_group_members (student_id, subject_id) where left_on is null;   -- Q1
create index ix_group_members_student on study_group_members (student_id);

-- 5. Homeroom class memberships (dated), backfilled
create table class_memberships (
  id            uuid primary key default gen_random_uuid(),
  student_id    text not null references students(id) on delete cascade,
  class_id      text not null references classes(id) on delete restrict,
  joined_on     date not null,
  left_on       date null,
  leave_reason  text null,
  created_by    text null references users(id) on delete set null,   -- null for backfilled rows
  created_at    timestamptz not null default now(),
  check (left_on is null or left_on >= joined_on)
);
create unique index ux_class_memberships_one_active
  on class_memberships (student_id) where left_on is null;
create index ix_class_memberships_class on class_memberships (class_id) where left_on is null;
-- Backfill: one open row per non-archived pupil whose class_name matches classes.name,
-- joined_on = the pupil's enrollment_date when it parses as a date, else the migration date
-- (enrollment_date and archived_at are ISO *strings* on `students` — parse defensively, never
-- cast blindly); one closed row per archived pupil with left_on = its archived_at date. Pupils whose class_name matches no class are REPORTED by
-- the migration log, not guessed.

-- 6. Subjects
alter table subjects add column is_groupable boolean not null default false;

-- 7. Owner kind on the five lesson-keyed tables (group id stored in class_id when 'group')
alter table schedule_templates add column owner_kind text not null default 'class'
  check (owner_kind in ('class','group'));
alter table week_assignments   add column owner_kind text not null default 'class'
  check (owner_kind in ('class','group'));
alter table journal_entries    add column owner_kind text not null default 'class'
  check (owner_kind in ('class','group'));
alter table lesson_notes       add column owner_kind text not null default 'class'
  check (owner_kind in ('class','group'));
alter table quarter_grades     add column owner_kind text not null default 'class'
  check (owner_kind in ('class','group'));

-- 8. Cut-over switch
alter table school_meta add column group_lessons_enabled boolean not null default false;
```

Every `add column … default` above is metadata-only on PostgreSQL 11+; no table rewrite, no
`DROP`, no `ALTER TYPE`.

### 3.2 Batch B — Students P1 — migration `StudentsParityP1`

```sql
-- S-5 status tags
create table student_statuses (
  id          uuid primary key default gen_random_uuid(),
  name        text not null unique check (btrim(name) <> ''),
  color       text null check (color is null or color ~ '^#[0-9a-fA-F]{6}$'),
  position    int  not null default 0,
  is_default  boolean not null default false,
  is_active   boolean not null default true,
  created_at  timestamptz not null default now()
);
alter table students add column status_id uuid null references student_statuses(id) on delete set null;
create index ix_students_status on students (status_id);

-- S-11 comments
create table student_comments (
  id          uuid primary key default gen_random_uuid(),
  student_id  text not null references students(id) on delete cascade,
  kind        text not null check (kind in ('positive','negative')),
  body        text not null check (btrim(body) <> ''),
  image_url   text null,
  file_url    text null,
  created_by  text not null references users(id) on delete restrict,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz null
);
create index ix_student_comments_student on student_comments (student_id, created_at desc);

-- S-8 pupil fields and guardian relation
alter table students add column phone        text null;
alter table students add column language     text null check (language in ('uz','ru','en','kaa'));
alter table students add column document_url text null;
alter table student_guardians drop constraint ck_student_guardians_relation;           -- constraint only
alter table student_guardians add constraint ck_student_guardians_relation
  check (relation in ('parent','father','mother','grandparent','trustee','other'));
alter table student_guardians add column relation_note text null;

-- K-1 pupil contract register
create table student_contracts (
  id           uuid primary key default gen_random_uuid(),
  student_id   text not null references students(id) on delete cascade,
  template_id  text null references contract_templates(id) on delete set null,
  number       text null,
  signed_on    date null,
  ends_on      date null,
  file_url     text null,
  source       text not null check (source in ('generated','uploaded')),
  comment      text null,
  created_by   text not null references users(id) on delete restrict,
  created_at   timestamptz not null default now(),
  check (ends_on is null or signed_on is null or ends_on >= signed_on)
);
create unique index ux_student_contracts_number on student_contracts (number) where number is not null;
create index ix_student_contracts_student on student_contracts (student_id, signed_on desc);

-- R-1 rooms: exactly SPEC.md §3.3 / warehouse.md §3.2, seeded from classes.room
create table rooms (
  id       uuid primary key default gen_random_uuid(),
  name     text not null unique,
  building text,
  floor    smallint,
  capacity smallint not null default 30,
  kind     text not null default 'classroom'      -- classroom | lab | gym | hall
);
```

The relation constraint is **replaced, not loosened**: the old three values stay valid, so no row
changes. It is the only `drop` in either batch and it drops a check constraint, not data — call
it out in the migration review.

### 3.3 Batch C — P2 (later wave) — migration `StudentsParityP2`

| Change | Gap |
|---|---|
| `classes.capacity smallint null` | C-4 |
| `subjects.color text null`, `subjects.is_active boolean not null default true` | F-3 |
| `students.target_grade smallint null check (target_grade between 0 and 11)` | S-9 |
| `certificate_subjects (certificate_id uuid fk cascade, subject_id text fk restrict, pk both)` + backfill from `certificates.subject_id` | Z-3 |
| `student_locations (id uuid pk, student_id text fk cascade, kind text check in ('home','school','pickup'), name text, lat numeric(9,6), lng numeric(9,6), pickup_from time null, pickup_to time null, created_at) unique (student_id, kind)` | L-2 |
| `user_table_settings (user_id text fk cascade, page text, settings jsonb not null, updated_at, pk (user_id, page))` | X-1 |
| `school_meta.contract_number_mode text not null default 'auto' check in ('auto','manual')` | K-6 |
| `assignments.owner_kind` (same shape as batch A §7) | G-20 |

---

## 4. Build slices and safe order

**Rule from `existing-module-gaps.md` §8 and `ASSUMPTIONS.md` 2026-09-16:** feature agents never
edit `App.tsx`, `navigation.ts`, `constants.ts`, `Program.cs`, `AppDbContext.cs`, migrations,
`Entities.cs` or `AuditService.cs`; the orchestrator wires those in one commit after each wave.
Below, a **contended** file is one that more than one slice needs; it has exactly one owner and
the others code against the contract written in this file.

### 4.1 Phase 0 — ships behind the scenes, no screen changes

| Slice | Gaps | Owns (writes) | Depends on | Visible change |
|---|---|---|---|---|
| **M — migration owner** | G-4 (batch A) **and** batch B schema, `SPEC.md` §3.3–3.5 amendment pointing at `study_groups` | `SchoolLms.Domain/StudyGroups.cs`, `StudentStatuses.cs`, `StudentComments.cs`, `StudentContracts.cs`, `Rooms.cs` (new); `Entities.cs` (additive properties); `Data/StudyGroupModel.cs`, `Data/StudentsParityModel.cs` (new); `AppDbContext.cs`; migration + `AppDbContextModelSnapshot.cs`; `Migrations/Sql/students_parity_guards.sql` + `.csproj`; `docs/SPEC.md` | — | none (columns default to today's behaviour; backfill only adds rows) |
| **W — foundations** | G-1, G-2, G-3, then G-5 | `ClassesController.cs` (Update), `StudentPortalController.cs:679`, `TelegramParentController.cs:373`, `TelegramTeacherController.cs:150`; new test files; new `LessonRoster.cs`, `PupilTimetable.cs`, `TeacherLessons.cs` and the call-site swaps listed in G-5 | tests first (parallel with M); resolvers after M | none — resolver output is identical while no group exists, proven by G-3 |

M and the test half of W run in parallel (no shared file). The resolver half of W starts when M
is merged.

### 4.2 Phase 1 — new screens, still no group lessons (parallel after M and W)

| Slice | Gaps | Owns | Contended files it needs, and owner |
|---|---|---|---|
| **1 — Groups & class roster** | G-6, G-7, G-8, G-9 (+F-1), C-1, C-2 | `StudyGroupsController.cs`, `StudyGroupService.cs`, `ClassMembershipsController.cs`, `ClassMembershipService.cs`, `pages/admin/groups/*`, `ClassRosterPage.tsx`, `ClassTransferModal.tsx`, `api/services/groups.ts`, `classMemberships.ts`; **owner of `StudentsController.cs`** (class change via membership service **and** S-8 payload fields), **owner of `SubjectsController.cs`**, `SubjectFormModal.tsx`, `SubjectsPage.tsx`, `ClassesPage.tsx`, `ClassDetailPage.tsx` | `App.tsx`, `navigation.ts` (Guruhlar item), `Program.cs`, `AuditService.cs` (`EntityStudyGroup`, `EntityClassMembership`) → wiring |
| **2 — Student list, import/export, statuses, archive** | S-1–S-7, K-4, A-1–A-3 | `StudentSearchController.cs`, `StudentImportController.cs`, `StudentStatusesController.cs`, `StudentListQuery.cs`, **owner of `StudentsPage.tsx`**, `api/services/students.ts`, `settings/StudentStatusesSettings.tsx`, `ExcelImport.cs` | `SettingsPage.tsx` (status tab), `MessagesController.cs` (S-6 scope) → slice 3 owns `MessagesController.cs`; `App.tsx`, `constants.ts` → wiring |
| **3 — Form, guardians, parents** | S-8 (frontend + `GuardianSync`), P-1–P-4 | **owner of `StudentFormModal.tsx`**, `GuardianSync`, `AdminGuardiansController.cs`, `ParentsController.cs`, `ParentsPage.tsx`, **owner of `MessagesController.cs`** (scopes `guardians` and `filter`), `SmsModal.tsx` | `StudentsController.cs` payload → slice 1 (contract: `phone`, `language`, `documentUrl`, `guardians[{fullName, phone, relation, relationNote, isPrimary}]`) |
| **4 — Profile** | S-10, S-11, S-12, G-10, Z-1, L-1 | **owner of `StudentDetailPage.tsx`** and `pages/admin/students/profile/*`, `StudentCommentsController.cs`, `AuditController.cs`, `LocationsController.cs`, `LocationPage.tsx`, `AttendanceController.cs` (range report only) | mounts slice 5's `ContractsTab.tsx` → wiring; reads slice 1 APIs (G-6/C-1 contracts in this file) |
| **5 — Contracts** | K-1, K-2, K-3 | `StudentContractsController.cs`, `ContractService.cs`, `ContractsController.cs`, `ContractsPage.tsx`, `profile/ContractsTab.tsx`, `profile/GenerateContractModal.tsx` | `StudentDetailPage.tsx` mount → wiring |
| **6 — Rooms** | R-1 | `RoomsController.cs`, `pages/admin/rooms/*`, `api/services/rooms.ts` | `App.tsx`, `navigation.ts` → wiring |
| **Wiring (orchestrator, sequential)** | — | `App.tsx`, `navigation.ts`, `constants.ts`, `Program.cs`, `AuditService.cs`, `SettingsPage.tsx`, the contracts tab mount in `StudentDetailPage.tsx`, `docs/SPEC.md` §5 rows | after slices 1–6 |

After Phase 1 the school can create groups, fill rosters, transfer pupils between classes and
groups, and see memberships on the profile. **No lesson, journal, report, salary or turnstile
figure changes**, because `school_meta.group_lessons_enabled` is off and the week-assignment
endpoint refuses group templates.

### 4.3 Phase 2 — the cut-over (parallel slices, then one switch)

| Slice | Gaps | Owns | Contended files and owner |
|---|---|---|---|
| **C1 — Schedule, salary, turnstile** | G-11, G-14, G-16 | `ScheduleTemplatesController.cs`, `WeekAssignmentsController.cs`, `ScheduleUtilsController.cs`, **owner of `PortalSchedule.cs`** and of the three resolvers from W, `AcademicYearController.cs`, `ClassesController.cs` (Delete), `TeacherSalaryCalc.cs`, `TurnstileAnalyticsQueries.cs`, `TurnstileService.cs`; FE `ScheduleBoard.tsx`, `LessonEditorPanel.tsx`, `ClassSchedulePage.tsx`, `TemplateEditorPage.tsx`, `WeekScheduleModal.tsx`, `TeacherSchedulePage.tsx`, `TodaySchedule.tsx`, `ClassScheduleViewPage.tsx`, `scheduleTemplates.ts`, `weekAssignments.ts` | `ClassesController.cs` shared with slice 1 → Phase 1 is merged first |
| **C2 — Journal, attendance, teacher access, chat** | G-12, G-13, G-17, evaluation board part of G-18 | `JournalService.cs`, `JournalSettingsGuard.cs`, `JournalController.cs`, **owner of `TeacherPortalController.cs`** and **`TelegramTeacherController.cs`**, `AttendanceController.cs`, `AttendanceAnalytics.cs`, `AttendanceDisciplineReport.cs`, `DashboardController.cs`, `StudentEvaluationController.cs`, `ChatService.cs`, `ChatHub.cs`; FE admin/teacher journal pages, ui-web journal screens, Mini App teacher tabs, `AttendancePage.tsx`, `ChatPanel.tsx`, `EvaluationPage.tsx` | `AttendanceController.cs` shared with slice 4 → Phase 1 merged first |
| **C3 — Reports and pupil/parent portals** | G-15, G-18 (pupil/parent part) | `Analytics.cs`, `RatingService.cs`, `ClassAnalyticsController.cs`, `SubjectAttainmentReport.cs`, `StudentReportBuilder.cs`, `StudentProfileBuilder.cs`, `SubjectProgressService.cs`, `TeacherActivityReport.cs`, `StudentPortalController.cs`, `TelegramParentController.cs`; FE grades-report pages, parent Mini App, ui-web pupil screens | consumes `PupilTimetable`/`TeacherLessons` → change requests go to C1 |

C1–C3 can merge in any order: with the flag off there are no group lessons for their new code
paths to see, and G-3 plus each slice's own tests must stay green on every merge.

**The cut-over step** (one person, one checklist):

1. All of C1–C3 merged; full backend suite green; frontend build green.
2. Restore a copy of production data into a scratch database; create one real group with its
   template and week assignment; compare, for its pupils and teachers, schedule, journal roster,
   attendance percentages, report averages, salary lesson count and turnstile expected arrival
   against hand-computed values. Nothing else may move.
3. Flip `school_meta.group_lessons_enabled` on production (a settings toggle, superadmin only,
   audited). Turning it off again hides group templates from week assignments; lessons already
   recorded stay.

### 4.4 Phase 3 — P2 (any time after Phase 1)

Batch C schema first (one owner), then small slices that do not collide: Sinf P2 (C-3–C-6),
subjects P2 (F-3, F-4), certificates P2 (Z-2–Z-4), locations P2 (L-2), contracts P2 (K-5, K-6),
cross-cutting (X-1, X-2, X-3 — X-2 touches every controller of this module, so it runs alone),
G-19, G-20.

---

## 5. Open questions for the client

Only questions that change scope. Each carries the decision that stands if nobody answers.

**Q1 — Can one pupil be in two groups of the same subject?**
*"Bitta o'quvchi bir fandan ikki guruhda bo'lishi mumkinmi (masalan, ingliz tili asosiy guruh va
ingliz tili qo'shimcha guruh)?"*
**If silent: no** — enforced by the database, as EduSchool's picker does (§2.1.1). Saying *yes*
removes one index and adds a "which group's grade counts" rule to reports (~6 h).

**Q2 — Can a group lesson overlap a class lesson for the same pupil?**
*"Guruh darsi paytida o'quvchining sinfida boshqa dars bo'lishi mumkinmi, yoki sinf jadvalida u
soat bo'sh qoladimi?"*
**If silent: it cannot** — saving such a cell is refused with the list of pupils (§2.1.4).

**Q3 — Is a group a paid extra?**
*"Guruh darslari alohida to'lanadimi yoki oylik to'lov ichidami?"*
**If silent: included in the monthly fee** — no billing change. *Paid* adds a subscription line per
group membership (~18 h, Moliya).

**Q4 — Contracts: is .docx enough, or must we produce PDF?**
**If silent: .docx only.** PDF needs a converter (LibreOffice) inside the server image, roughly
300 MB and a new moving part (~8 h + infra).

**Q5 — Does each pupil have their own payment day?**
EduSchool stores `paymentDay` per pupil and filters by it.
**If silent: no** — the due day stays school-wide (`BillingSettings.PaymentDueDay`); a payment
day written on a contract is informational. A per-pupil due day changes invoice accrual and
belongs to the Moliya parity pass.

**Q6 — Address search on the map?**
Search by address text and click-to-address need an external geocoder (a paid Yandex key, or
OSM Nominatim under its usage policy) — the request leaves our server.
**If silent: no geocoding** — staff click the map and type the address.

**Q7 — Moving the data from EduSchool at switch-over.**
Exporting from EduSchool creates an export job in a live tenant, which our rules treat as a write.
**If silent: the school exports the pupil, parent, class and group lists themselves** and hands
us the files; a one-off mapping script (`build-data-migration`) loads them through S-3's
two-step import. Nobody on our side touches the tenant.

**Q8 — Groups at year end.**
**If silent: the year rollover archives all groups** and closes their memberships; the school
re-creates next year's groups with *duplicate*. Carrying groups over instead means promoting
feeding classes inside each group (~8 h).

**Q9 — Responsible staff ("moderator") per class, group or pupil.**
EduSchool assigns moderators to classes and groups and an `assignedModerator` to each pupil
(behind a setting).
**If silent: not built.** Saying *yes* adds one link table per owner and a filter on each list
(~14 h).

