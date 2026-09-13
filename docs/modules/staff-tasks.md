# Module SPEC — Staff task board ("Vazifalar")

**Status:** specification, not yet built. **No code exists for this module.**
**Author:** plan-architect · **Date:** 2026-09-13
**Source of truth for EduSchool facts:** `.eduschool-bundle/all.js` (read-only copy of the
client's live tenant's JavaScript bundle), `.eduschool-bundle/edu-menu.json`,
`docs/EDUSCHOOL-INVENTORY.md`.
**Source of truth for our facts:** the repository at the commit this file was written on.

Every claim below is one of three kinds and is always labelled:

| Label | Meaning |
|---|---|
| **Observed** | Read directly out of the EduSchool bundle. A quoted string or path is given. |
| **Inferred** | Not in the bundle; deduced from names plus a sibling implementation that *is* in the bundle. The reasoning is stated. |
| **Our decision** | Ours, with the reason. Not an EduSchool fact. If the reason no longer holds, revisit it. |

Nothing here is "we'll decide later". Where the client has not been asked, §10 records the
decision that stands if nobody objects.

---

## 0. Naming — read this first

### 0.1 The collision

We already ship a module called **Topshiriqlar**. It is **homework**:

- `schoollms.client/src/config/navigation.ts` → admin nav, group `Ilova`,
  `{ label: 'Topshiriqlar', to: '/admin/assignments' }` and
  `{ label: 'Topshiriqlar bali', to: '/admin/assignment-scores' }`
- teacher nav → `{ label: 'Topshiriqlar', to: '/teacher/assignments' }`
- `schoollms.client/src/config/constants.ts` → `{ key: 'assignments', label: 'Topshiriqlar' }`
- Domain: `Assignment`, `AssignmentMaterial`, `AssignmentSubmission`, `AssignmentType`,
  `TestQuestion` in `SchoolLms.Domain/Entities.cs`
- Server: `SchoolLms.Server/Controllers/AssignmentsController.cs`
- Client: `schoollms.client/src/pages/admin/assignments/`,
  `schoollms.client/src/components/assignments/AssignmentWizard.tsx`,
  `schoollms.client/src/api/services/assignments.ts`

EduSchool's **Topshiriqlar** (menu key `TASK`, permission `getEmployeeTasks`, route `/task`)
is something else entirely: an internal work tracker for **staff**. Same Uzbek word, different
product.

### 0.2 What we call ours — decision

| Surface | Name | Why |
|---|---|---|
| Uzbek UI label (sidebar, page titles) | **Vazifalar** | Short, unambiguous in an office context, and not the word already spent on homework. "Topshiriq" in our product means "something a student must hand in"; "vazifa" means "a job someone at the school must do". |
| Route | `/admin/tasks` | `/admin/assignments` is taken by homework. |
| Domain type prefix | `StaffTask*` | `Task` alone collides with `System.Threading.Tasks.Task` in every C# file — a real, daily annoyance, not a theoretical one. |
| Table prefix | `staff_tasks*` | Matches the domain type; makes a `grep staff_task` find everything. |
| RBAC permission key | `tasks` | One key, consistent with existing keys (`leads`, `journal`, `finance`, …). See §4.1. |
| API base | `/api/admin/tasks` | Consistent with `api/admin/<module>`. |
| Client folder | `schoollms.client/src/pages/admin/tasks/` | Matches the route. |
| API service file | `schoollms.client/src/api/services/staffTasks.ts` | `tasks.ts` would be too easy to mistake for homework. |

**Do not rename the homework module.** It is shipped, the client uses it, and renaming it
would break bookmarks and muscle memory for zero gain. The two are distinguished by placement
(`Ilova → Topshiriqlar` vs top-level `Vazifalar`) and by word.

### 0.3 Reference implementation

The closest existing module by shape is **Lidlar** (leads):
`schoollms.client/src/pages/admin/leads/LeadsPage.tsx` + `LeadColumn.tsx` + `LeadCard.tsx`,
`schoollms.client/src/api/services/leads.ts`, `schoollms.client/src/api/services/stages.ts`,
`SchoolLms.Server/Controllers/LeadsController.cs`, `LeadStagesController.cs`,
`SchoolLms.Domain/Entities.cs` → `Lead`, `LeadStage`.

Read those before writing a line. They establish: a board with user-defined ordered columns, a
column entity with `Title`/`Color`/`Order`, drag-and-drop with `@dnd-kit/core`
(`PointerSensor`, `activationConstraint: { distance: 6 }`), a detail modal, a form modal, and a
column-editor modal.

> ### PROTECTED — do not touch the leads board
>
> `CLAUDE.md` (repo root) freezes the design of these six files:
> `LeadsPage.tsx`, `LeadColumn.tsx`, `LeadCard.tsx`, `LeadDetailModal.tsx`,
> `LeadFormModal.tsx`, `StageFormModal.tsx`.
>
> **Copy from them. Do not refactor them into shared components.** If the task board needs
> `LeadColumn`-like behaviour, write `TaskColumn.tsx` next to it. A shared `<KanbanColumn>`
> extracted out of `LeadColumn.tsx` would change the leads board's render tree, and that is
> exactly what is forbidden. Duplicating ~100 lines of column layout is the cheaper mistake.
>
> The same rule applies to appearance in general: we are rebuilding EduSchool's
> **functionality**, never its look. EduSchool's task board is MUI/DataGrid; ours is the
> Tailwind + iOS-flavoured minimalism already in `schoollms.client/src/components/ui/`.

---

## 1. Goal

Give the school's own staff — administrators, the academic office, the cashier, the head
teacher — one place to assign, track and close internal work: chase a parent about a debt,
collect a missing birth certificate, prepare the report for the district, fix the projector in
5-B. Today that work lives in Telegram messages and paper, so nothing has an owner, a due date
or a record that it was done.

Scope: staff only. Students and guardians never see this module. It is not homework, not a
CRM pipeline, and not a helpdesk for parents.

---

## 2. What EduSchool actually has

### 2.1 Menu entry (Observed)

`.eduschool-bundle/edu-menu.json`:

```json
{ "title": "Topshiriqlar", "key": "TASK", "group": null, "role": "getEmployeeTasks" }
```

Sidebar definition in the bundle:
`{icon:…, title:"Topshiriqlar", path:nb, translate:"TASK", role:"getEmployeeTasks"}` where
`nb = "/task"`.

### 2.2 Routes (Observed) — the brief's route list needs three corrections

The router lives in the parent chunk (bundle offset ≈ 1 932 500):

```js
<Routes>
  <Route path=""          element={<Board/>}/>       // /task
  <Route path="/summary"  element={<TaskSummary/>}/>
  <Route path="/calendar" element={<TaskCalendar/>}/>
  <Route path="/timeline" element={<TaskTimeline/>}/>
  <Route path="/reports"  element={<TaskReports/>}/>
  <Route path="/list"     element={<TaskTable/>}/>
  <Route path="/type"     element={<TaskType/>}/>
</Routes>
```

and the view-tab table:

```js
le = [{key:"summary",segment:"/summary"},{key:"board",segment:""},{key:"list",segment:"/list"},
      {key:"calendar",segment:"/calendar"},{key:"timeline",segment:"/timeline"},
      {key:"reports",segment:"/reports"}]
```

Corrections to the routes given in the task brief:

1. **`/create` and `/:id` are not routes.** They are query parameters on whatever view you are
   on. Evidence: the tab component strips `["task","create","settings"]` from the query string
   before building the export URL; the drawer opens from `searchParams.get("task")`; the
   "add" button does `params.set("create","1")`; the drawer's edit button does
   `params.delete("task"); params.set("edit", String(task._id))`; and "copy link" produces
   `` `${origin}${pathname}?task=${id}` ``. So a task is addressable as
   `/task/list?task=<id>`, `/task/calendar?task=<id>`, and so on — the drawer overlays the
   current view rather than replacing it.
2. **`/column-view` is not part of this module.** It belongs to the **leads** kanban
   (`/order/column-view`, bundle offset ≈ 1 731 800): a stage editor that saves the whole
   ordered stage list with `PUT stages { statuses: [...] }`. There is no `/task/column-view`.
3. **`/task/type` exists and the brief omits it** — the task-type catalogue
   (`TaskType-_VU-TbQ6.js`).

### 2.3 API surface (Observed)

From the endpoint constant table (bundle offset ≈ 3 330 900). Base URL is
`https://backend.eduschool.uz/moderator-api/`; a leading `/` on a constant is stripped when
axios joins, so `"employee-task/comment"` and `"/employee-task"` resolve the same way.

```
/employee-tasks                              /employee-task
/employee-task/board                          employee-task/board/column
/employee-task/move                           employee-task/bulk
 employee-task/comment                        employee-task/watcher
 employee-task/summary                        employee-task/calendar
 employee-task/timeline                       employee-task/assignees
 employee-task/archive                        employee-task/export
 employee-task/reports                       /employee-task/analytics/state-breakdown
/employee-task-statuses                      /employee-task-status
 employee-task-types                          employee-task-tags   ·  employee-task-tag
 audits                                      /timeline
 back-api/admin/task/create                   back-api/admin/task/update
```

### 2.4 Realtime (Observed)

The task module opens **its own socket**, separate from the REST base:

```js
const Re = ["task.created","task.updated","task.deleted"], Me = "comment.created";
io("https://backend.eduschool.uz/", {
  path: "/moderator-chat-api/socket.io",
  extraHeaders: { Authorization: `Bearer ${localStorage.getItem("token")}` },
  transports: ["polling","websocket"], reconnectionAttempts: 5,
  reconnectionDelay: 2000, reconnectionDelayMax: 15000
})
```

Event payload is `{ data: { actorId, branchId, taskId } }`. The handler:

- **drops the event if `actorId === myEmployeeId`** — you do not get invalidated by your own
  write;
- **drops the event if `data.branchId !== currentBranchId`**;
- `task.*` → debounced board refresh (400 ms, and suppressed entirely while a drag is in
  progress, then fired once on drop);
- `comment.created` → refetch **only** if `taskId === the task open in the drawer`.

That debounce-and-suppress logic is worth copying verbatim; it is the difference between a
board that is usable during drag-and-drop and one that is not.

### 2.5 Every field we can see on a task (Observed)

From the drawer component (bundle offset ≈ 1 921 800 – 1 928 500):

| Field | Evidence | Notes |
|---|---|---|
| `_id` | `` `${Dt}/${t}` `` → `GET /employee-task/<id>` | |
| `key` | rendered top-left of the drawer in bold small caps | a human-readable issue key, Jira-style |
| `text` | rendered as the 16 px title | this is the **title**, not the body |
| `description` | rendered `whiteSpace:"pre-wrap"` under the title | plain text, no rich text |
| `status` | `{ _id, name, color }`; label = `te(r.status.name)` | `name` is an **i18n object** `{uz,ru,en}` — `te()` picks `uz ?? ru ?? en` |
| `priority` | chip, outlined, colour from a fixed map | fixed enum, see below |
| `resolution` | chip, outlined | fixed enum, see below; only present once finished |
| `employee` | `task.drawer.assignee` → `employee.fullName` | **singular** — exactly one assignee |
| `createdBy` | `task.drawer.reporter` → `createdBy.fullName` | |
| `date` | `task.drawer.deadline`, `DD.MM.YYYY HH:mm` | deadline, has a **time** component |
| `createdAt` | `task.drawer.created` | |
| `completedAt` | used by `Os()` to decide the "done" state | |
| `watchers[]` | `task.drawer.watchers` → `.map(w => w.fullName).join(", ")` | |
| `watcherIds[]` | used to decide whether *I* am watching | |
| `attachments[]` | `{ fileUrl, name, size }` | see §2.9 |
| `comments[]` | `{ _id, employeeId, employee:{fullName}, text, createdAt }` | **embedded on the task**, flat |

Fixed enums, hard-coded in the frontend (bundle offset ≈ 1 905 000):

```js
re = [{_id:"urgent", labelKey:"task.priority.urgent", color:"#DC2626", order:4},
      {_id:"high",   labelKey:"task.priority.high",   color:"#F97316", order:3},
      {_id:"medium", labelKey:"task.priority.medium", color:"#3B82F6", order:2},
      {_id:"low",    labelKey:"task.priority.low",    color:"#9CA3AF", order:1}]

qt = [{_id:"done"},{_id:"wont_do"},{_id:"duplicate"},{_id:"obsolete"}]   // resolutions

zs = [{_id:"student", labelKey:"general.student"},{_id:"class", labelKey:"general.class"}]
```

`zs` is the **link-target** list: a task can be attached to a student or to a class. The
bulk-create form from the leads board confirms both (`studentId`, `classId`) and adds
`leadId` / `attached_to:"order"` (§2.11).

Derived lifecycle bucket, computed client-side, not stored:

```js
Os = (t, now = Date.now()) => {
  if (t.completedAt) return "done";
  if (!t.date)       return "";
  const d = diffInDays(t.date, now);
  return d < 0 ? "over" : d === 0 ? "today" : "";
}
```

### 2.6 The column model — user-defined, and a column is *not* a lifecycle state (Observed + Inferred)

This was the main open question in the brief. The answer is: **both**. There are two
independent dimensions, and conflating them is the mistake to avoid.

**Dimension 1 — columns (user-defined).** Observed:

- `/employee-task-statuses` (list) and `/employee-task-status` (singular write) are a CRUD'd
  entity, exactly parallel to `LeadStage` in our own code.
- Permissions exist for `getEmployeeTaskStatuses`, `updateEmployeeTaskStatus`,
  `deleteEmployeeTaskStatus` — **and no `createEmployeeTaskStatus`**. The leads module by
  contrast has all three (`createStage`, `updateStage`, `deleteStage`). Inferred: the task
  status writer is an **upsert of the whole ordered list**, like the leads column editor's
  `PUT stages { statuses: [...] }` — one save, positions recomputed, new rows flagged.
- A status carries `{ _id, name: {uz,ru,en}, color }` (from the drawer chip and the filter
  autocomplete, which uses `getOptionLabel: n => te(n.name)`).
- The filter parameter is `statusIds` (plural array), and the history tab treats a change of
  `statusId` as a first-class event kind, separate from generic field changes.

**Dimension 2 — lifecycle (fixed, three states).** Observed:

- `completedAt` + `resolution` — set by a **dedicated** action. Evidence: the permission
  registry lists `finishEmployeeTask (FINISH_EMPLOYEE_TASK)` as a *separate* permission from
  `updateEmployeeTask (UPDATE)`. If finishing were "drag the card to the last column" it would
  need no permission of its own.
- `archivedAt` — again a **dedicated** action: `employee-task/archive`, permission
  `archiveEmployeeTask (ARCHIVE_EMPLOYEE_TASK)`, and a boolean filter `archived=1` that puts
  the whole board into archive mode (a warning chip appears in the tab bar).

So: **moving a card to the rightmost column does not close the task.** A task in the "Bajarildi"
column with no `completedAt` is still open, still counts as overdue, still appears in the
default board. That is deliberate on EduSchool's side and we will copy it — see §5.3 for why
it is the right call.

**Dimension 3 — the drag itself.** `employee-task/move` (Observed, endpoint only). Inferred
payload from the sibling leads implementation in the same bundle, which is the only
drag-and-drop code we can actually read: the leads board keeps
`columnsData[stageId] = { stage, cards[], total }`, moves a card optimistically, adjusts both
column totals, and then calls `setStage({ stageId, _id })`. The task equivalent therefore
almost certainly carries the target column and the target index.

**`employee-task/board` vs `employee-task/board/column`** (Inferred, same reasoning): `board`
returns the column skeleton with per-column totals and the first page of cards;
`board/column` returns one column's next page. The leads board in this bundle does exactly
this (`fetchNextPageKanbanData`, per-column `total`, an "observing column" sentinel that
triggers the next page). A board that loads every card in every column would not survive a
column with 400 archived tasks.

### 2.7 Watcher vs assignee (Observed)

| | Assignee | Watcher |
|---|---|---|
| Cardinality | exactly one, nullable | zero or more |
| Field | `employeeId` → `employee` | `watcherIds[]` → `watchers[]` |
| Meaning | the person accountable | subscribers who want to know |
| Who sets it | whoever can `updateEmployeeTask` | **yourself** |
| How | task form | `PUT employee-task/watcher { _id, watching: <bool> }` |
| Effect | appears in your "mine" preset, in the assignee avatar strip, in reports | receives `comment.created` on that task; drawer shows an eye / eye-off toggle |

The watcher toggle sends **no employee id** — only `{_id, watching}`. So in EduSchool a
watcher can only subscribe or unsubscribe **themselves**; there is no "add Aziza as a watcher".
That is an Observed limitation, not a design principle. See §10 Q3.

`unassigned` is a real, filterable state: the assignee avatar strip renders a distinct
"unassigned" avatar with a count, and `unassigned=1` is one of the persisted filter keys.

### 2.8 Comments do not thread (Observed)

The drawer renders `task.comments` as a flat `.map()`. There is no `parentId`, no reply
control, no indentation, no "N replies". Write is `POST employee-task/comment { _id, text }`;
delete is `DELETE employee-task/comment { _id, commentId }` and the button only renders when
`String(comment.employeeId) === String(currentEmployeeId) && canWrite`. There is no edit.

Comments are stored **on the task** (`r.comments`), and separately surface in the History tab
through `POST /timeline { taskId }`. The History tab merges three streams and sorts them
descending by `createdAt`:

1. `GET audits { entity: "employee_task", entityId, limit: 50 }` → field diffs
   (`changes[] = { field, fieldKey, oldValue, newValue }`), **excluding** `statusId`;
2. `POST /timeline { taskId }` rows whose `data.field === "statusId"` → status transitions,
   rendered as `prevName → nextName`;
3. the remaining `/timeline` rows → comments.

So `audits` is a **generic, entity-agnostic audit service** in EduSchool, and `timeline` is a
generic activity feed shared with the leads module.

### 2.9 Attachments (Observed)

`attachments[] = { fileUrl, name, size }`. The renderer classifies by extension:

```js
images = ["jpg","jpeg","png","gif","webp","bmp","svg","heic"]
docs   = ["pdf","doc","docx","xls","xlsx","ppt","pptx","txt","csv"]
```

Images get a thumbnail with an `onError` fallback; documents get a blue icon; anything else a
grey icon. Size is rendered `B / KB / MB`. Upload goes through the shared `/upload-file`
endpoint.

### 2.10 Filters, presets and grouping (Observed)

The complete persisted filter key list, straight from the source:

```js
ss = ["search","employeeIds","studentIds","classIds","typeIds","tagIds","statusIds",
      "priorities","fromDate","toDate","onlyMine","overdue","unassigned","archived","groupBy"]
```

Encoding rules, also Observed:

- **All of it lives in the URL query string** (`useSearchParams`, `{replace:true}`), so a
  filtered board is a shareable link. Nothing is kept in component state.
- Array values are stored **JSON-encoded in a single parameter**:
  `employeeIds=["a","b"]`. The parser (`K()`) is defensive — it accepts a JSON array, a JSON
  scalar, or a bare string, and it accepts both `"id"` and `{_id:"id"}` elements.
- Booleans are the literal string `"1"`; absent means false.
- Empty / `""` / `false` / empty-array values are **deleted** from the query, never sent.
- Search is debounced 400 ms.
- Any filter change resets `page`.

Presets (mutually exclusive shortcuts that rewrite several params at once):

| Preset | Sets |
|---|---|
| `all` | clears `onlyMine`, `overdue`, `fromDate`, `toDate` |
| `mine` | `onlyMine=1`, clears the rest |
| `overdue` | `overdue=1`, clears the rest |
| `today` | `fromDate=<today 00:00 ISO>`, `toDate=<today 23:59 ISO>`, clears the rest |

`groupBy ∈ { "", "employee", "priority" }` (`Zt = ["","employee","priority"]`).

The assignee filter is an **avatar strip**, not a dropdown: `GET employee-task/assignees`
returns `{ currentEmployeeId, assignees: [{ employeeId, fullName, imageUrl }], unassigned: <int> }`;
the strip shows the first 6, pins *you* first, dims unselected avatars to 45 % opacity once any
filter is active, and overflows the rest into a `+N` menu. It is queried with **the current
filters minus** `["task","create","settings","employeeIds","onlyMine"]` — i.e. the counts
respond to the other filters but not to the assignee filter itself, which is what makes
multi-select feel right.

Due-date wording (Observed, `Ls()`): `today` (with `HH:mm`), `tomorrow`, `yesterday`,
`daysAgo(n)`, `inDays(n)` for < 7 days, otherwise `onDate(DD MMM)`.

### 2.11 What `bulk` and `multi-actions` operate on — the brief conflates two things (Observed)

They are **different features in different modules**.

**`employee-task/bulk`** belongs to the task board. The endpoint constant exists; its only
caller is inside `index-B7DL6d7C.js` (the board chunk), which is **not present** in our copy of
the bundle. Inferred from the surrounding permission set (`updateEmployeeTask`,
`archiveEmployeeTask`, `finishEmployeeTask`, `deleteEmployeeTask`) that it applies one of those
verbs to a selected set of tasks.

**`multi-actions/*`** belongs to the **leads kanban**, and operates on **lead cards** — not on
tasks. Read at bundle offset ≈ 1 507 100:

```js
// "Change responsible person" on selected leads
ae("multi-actions/change-responsible","post")
body = { responsibleId, changeLinkedData: true, filters }

// "Add task" on selected leads — creates ONE staff task PER SELECTED LEAD
ae("multi-actions/employee-task","post")
body = { date, startHourDate, endHourDate, employeeId, typeId,
         studentId, classId, text, filters }

filters = [ { stageIds:[stageId], cardIds:[...], isAll:false }   // explicit selection
          , { stageIds:[stageId],                 isAll:true } ] // "everything in this column"
```

The selection idiom is worth stealing: the user picks cards, then a small modal asks
**"this page"** or **"all"** (`GENERAL.this_page` / `GENERAL.all`), and only then does the
request go out. Per column, if every loaded card is selected *and* the user chose "all", the
client sends `isAll:true` and lets the server re-run the filter; otherwise it sends the
explicit `cardIds`. This is how you make "select all 4 000" work without sending 4 000 ids.

The permission registry confirms the ownership: all five `multiActions*` permissions sit under
`{label:"LEADS", section:MAIN}`, not under `TASKS`:

```
multiActionsChangeResponsible · multiActionsCreateEmployeeTask · multiActionsChangeStage
multiActionsChangeFields      · multiActionsDelete
```

`multi-actions/change-status` maps to `multiActionsChangeStage` — moving **leads** between
kanban stages. `multi-actions/delete` deletes **leads**.

Consequence for us: `multi-actions/*` is a **Lidlar** feature request, not a Vazifalar one. It
is listed in §8 as a separate, later unit, and it is the *only* place where the task board and
the leads board meet.

### 2.12 What `back-api/` means (Observed + Inferred)

**Observed facts:**

1. There is exactly **one** axios instance for the whole admin app:
   `ud = axios.create({ baseURL: "https://backend.eduschool.uz/moderator-api/" })`.
   Axios joins a relative URL onto the base after stripping the leading slash, so
   `back-api/admin/task/create` resolves to
   `https://backend.eduschool.uz/moderator-api/back-api/admin/task/create`.
2. Its request interceptor stamps **every** call with
   `Authorization`, `Organization: test`, `Branch: <branchId>`, `language: <lng>`,
   `academicYearId: <id>`. So `back-api` shares the same auth and the same tenant/branch/year
   scoping — it is not a separate product with a separate login.
3. The **body naming convention differs**. The task board's own writes are camelCase
   (`employeeId`, `statusId`, `typeId`). `back-api/admin/task/update` is snake_case:

   ```js
   apiTaskUpdate.mutateAsync({
     _id, typeId, date, start_time, end_time,
     attached_to: "order", responsible_id, leadId, text
   })
   ```

   Note the mixture — `_id`, `typeId` and `leadId` came from the caller's own variables;
   `start_time`, `end_time`, `responsible_id`, `attached_to` are what this endpoint demands.
4. The **URL style differs**: verb-in-path (`/task/create`, `/task/update`) versus the REST-ish
   `POST /employee-task`, `PUT /employee-task` used by the board.
5. Its **only** callers are in the lead-detail timeline panel (bundle offset ≈ 1 664 900). The
   task board never touches it.
6. Sibling prefixes exist on the same host and are reached the same way:
   `moderator-chat-api/socket.io` (realtime), `mycalls-api/file-proxy?url=` (telephony), and an
   `admin-api` base derived by literal string replacement
   (`Yh.replace("moderator-api","admin-api")`) for the super-admin console.

**Inference.** `backend.eduschool.uz` is a **gateway in front of several services**, and
`back-api` is one of the upstreams — almost certainly the older one. `attached_to:"order"`
seals it: "order" is EduSchool's internal name for the leads module (its route is `/order`,
its "add lead" action is `/order/add`). So `back-api/admin/task/*` is a **first-generation
writer for the same task entity**, kept alive because the CRM timeline still calls it, while
the newer board speaks `employee-task`. Two write paths, two field-name conventions, one table.

**Consequence for us — decision:** we build **one** writer. `POST /api/admin/tasks` and
`PUT /api/admin/tasks/{id}` are the only ways a task is created or changed, whether the caller
is the board, the lead drawer, or a background rule. There is no compatibility twin, because
we have no legacy client to be compatible with. If a second caller needs a shorter payload, it
gets defaults on the server, not a second endpoint.

This is also a warning about what happens when you skip that decision: EduSchool now has a
field called `date` that one writer treats as a full timestamp and the other splits into
`date` + `start_time` + `end_time`.

### 2.13 Task types and tags (Observed)

Two separate taxonomies, on top of statuses:

- **Types** — `employee-task-types`, object `{ _id, title, color }`. Rendered as a coloured dot
  next to the select. Permissions: `getEmployeeTaskTypes`, `createEmployeeTaskType`,
  `updateEmployeeTaskType`, `deleteEmployeeTaskType`. Managed on `/task/type`.
- **Tags** — `employee-task-tags` / `employee-task-tag`, object `{ _id, name }`, multi-select.
  Permissions: `updateEmployeeTaskTag`, `deleteEmployeeTaskTag` (no separate create → upsert,
  same pattern as statuses).

Both are filterable (`typeIds`, `tagIds`).

### 2.14 Automatic task creation (Observed)

The general-settings screen (bundle offset ≈ 1 968 700 – 1 977 000) exposes school-wide flags
that make the system create tasks by itself:

| Setting | Type | What it does |
|---|---|---|
| `createChildPickUpTask` | bool | a guardian's pickup request opens a task |
| `absenceTaskEnabled` | bool | absence-threshold rule on/off |
| `absenceTaskThreshold` | int ≥ 1 | how many absences trigger it |
| `absenceTaskRecipients` | `[{ branchId, employeeId }]` | who the generated task is assigned to, per branch |

This is the feature that turns a task board from "a nicer Trello" into something the school
actually keeps using: the work arrives on its own.

### 2.15 The permission registry (Observed)

The whole EduSchool RBAC catalogue is in the bundle at offset ≈ 2 273 200. The `TASKS`
section, verbatim:

```
EMPLOYEE_TASK_TYPE  →  getEmployeeTaskTypes(GET) createEmployeeTaskType(CREATE)
                       updateEmployeeTaskType(UPDATE) deleteEmployeeTaskType(DELETE)

EMPLOYEE_TASKS      →  getEmployeeTasks(GET)
                       getAllEmployeeTasks(GET_ALL_EMPLOYEE_TASKS)
                       createEmployeeTask(CREATE)
                       updateEmployeeTask(UPDATE)
                       deleteEmployeeTask(DELETE)
                       getEmployeeTaskStatuses(GET_EMPLOYEE_TASK_STATUS)
                       updateEmployeeTaskStatus(UPDATE_EMPLOYEE_TASK_STATUS)
                       deleteEmployeeTaskStatus(DELETE_EMPLOYEE_TASK_STATUS)
                       archiveEmployeeTask(ARCHIVE_EMPLOYEE_TASK)
                       finishEmployeeTask(FINISH_EMPLOYEE_TASK)
                       updateEmployeeTaskTag(UPDATE_EMPLOYEE_TASK_TAG)
                       deleteEmployeeTaskTag(DELETE_EMPLOYEE_TASK_TAG)
                       getEmployeeTaskReports(GET_EMPLOYEE_TASK_REPORTS)
                       exportEmployeeTasks(EXPORT_EMPLOYEE_TASKS)
```

The pair worth copying is **`getEmployeeTasks` vs `getAllEmployeeTasks`**: see your own tasks,
versus see everyone's. Without that split, a task board is either useless (everyone sees
nothing) or a privacy problem (the cleaner reads the director's to-do list).

### 2.16 What we could **not** recover, and why

Our copy of the bundle contains the task **parent** chunk (context, filters, tab bar, drawer,
router) but **not** these lazily-imported children, which are referenced by filename only:

```
index-B7DL6d7C.js   → the board itself (columns, cards, drag, bulk toolbar)
TaskFormModal-BTpaG-D9.js → the create/edit form
TaskTable-DDnJ9InL.js     → /list
TaskSummary-BPy3EFkt.js   → /summary
TaskCalendar-19xB05vw.js  → /calendar
TaskTimeline-kdc7Xmj1.js  → /timeline
TaskReports-D_Q8PiSr.js   → /reports
TaskType-_VU-TbQ6.js      → /type
```

Verified absent: `employee-task/board/column`, `employee-task/bulk`, `employee-task/summary`,
`employee-task/reports`, `employee-task/calendar`, `employee-task/timeline` all appear exactly
once each in the file — in the constant table — and never at a call site.

Therefore **§5.5 (reports) and §5.6 (summary/calendar/timeline) are our design**, built from
the data model, not transcriptions. They are marked as such. Do not go looking for a "correct"
answer in the bundle; it is not there.

---

## 3. Data model

Conventions taken from the codebase, not invented:

- New modules use `Guid` primary keys and `DateTimeOffset` timestamps → PostgreSQL `uuid` and
  `timestamptz`. Evidence: `SchoolLms.Domain/Billing.cs` (`Payment.Id` is `Guid`,
  `Payment.ReceivedAt` is `DateTimeOffset`) and
  `SchoolLms.Infrastructure/Data/AppDbContext.cs` lines 196–207, which rewrite every **`DateTime`**
  column to `timestamp without time zone` and deliberately leave `DateTimeOffset` alone.
  **Use `DateTimeOffset`. Never `DateTime`** in this module.
- Foreign keys to the **legacy** tables (`app_users`, `students`, `school_classes`,
  `teachers`) are `string`, because those tables have `string` ids. Do not "fix" that here.
- Table and column names are `snake_case`.
- Comments in code and UI strings are Uzbek; identifiers are English.

### 3.1 `staff_task_statuses` — the board columns

Modelled on `LeadStage` (`SchoolLms.Domain/Entities.cs`).

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `gen_random_uuid()` | |
| `title` | text | no | | Uzbek label. **Single string, not an i18n object** — the staff UI is Uzbek-only (global rules: client-facing output is Uzbek). |
| `color` | text | no | `'slate'` | Same palette token set as `LeadStage.Color`: `slate\|blue\|emerald\|amber\|violet\|rose\|cyan\|orange`. A closed token list, not a hex string, so the board cannot be made unreadable. |
| `position` | int | no | | 0-based, contiguous. Rewritten on every save of the list. |
| `is_default` | boolean | no | `false` | The column a new task lands in when none is given. Exactly one row may be true — enforced by a partial unique index. |
| `created_at` | timestamptz | no | `now()` | |

Indexes / constraints:

```
unique (position)
unique (id) where is_default            -- partial unique index on a constant expression:
                                        -- create unique index staff_task_statuses_one_default
                                        --   on staff_task_statuses ((true)) where is_default;
```

Rules:

- **A status may not be deleted while any task references it.** `on delete restrict`. The UI
  offers "move the N tasks to <other column>, then delete" instead of a cascade. Reason: a
  cascade here silently deletes work items, and the whole point of this module is that work
  items stop disappearing.
- Minimum one status. Deleting the last one is rejected with `409`.
- Seeded on first migration with four rows (see §9.2).

### 3.2 `staff_tasks`

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | uuid | no | `gen_random_uuid()` | |
| `key_no` | bigint | no | sequence | See "key" below. |
| `title` | text | no | | 1–200 chars after trim. Maps to EduSchool's `text`. |
| `description` | text | yes | | Plain text, ≤ 10 000 chars. No HTML, no markdown rendering — it is displayed with `whitespace-pre-wrap`. |
| `status_id` | uuid | no | | FK → `staff_task_statuses(id)`, `on delete restrict`. |
| `position` | double precision | no | | Order **within** the column. Fractional midpoint ordering — see below. |
| `priority` | text | no | `'medium'` | `low \| medium \| high \| urgent`. Check constraint. |
| `assignee_user_id` | text | yes | | FK → `app_users(id)`, `on delete set null`. `null` = unassigned, a real and filterable state. |
| `created_by_user_id` | text | no | | FK → `app_users(id)`, `on delete restrict`. The reporter. **Taken from the JWT, never from the request body.** |
| `due_at` | timestamptz | yes | | Deadline. Has a time-of-day component. |
| `completed_at` | timestamptz | yes | | Set only by the finish action. |
| `resolution` | text | yes | | `done \| wont_do \| duplicate \| obsolete`. Check constraint. Non-null **iff** `completed_at` is non-null. |
| `completed_by_user_id` | text | yes | | FK → `app_users(id)`. |
| `archived_at` | timestamptz | yes | | Set only by the archive action. |
| `archived_by_user_id` | text | yes | | FK → `app_users(id)`. |
| `student_id` | text | yes | | FK → `students(id)`, `on delete set null`. |
| `class_name` | text | yes | | Our classes are still keyed by name (`Student.ClassName`, `JournalEntry`), so this is a name, not an id. See the note below. |
| `lead_id` | text | yes | | FK → `leads(id)`, `on delete set null`. Populated by the leads bulk action (§8, unit T9). |
| `created_at` | timestamptz | no | `now()` | |
| `updated_at` | timestamptz | no | `now()` | Bumped on every write. |

Check constraints, written in the EF model so they land in the snapshot and survive
`--autogenerate` (the rule stated in `SchoolLms.Infrastructure/Data/GuardianModel.cs`):

```
ck_staff_tasks_priority     priority in ('low','medium','high','urgent')
ck_staff_tasks_resolution   resolution is null or resolution in ('done','wont_do','duplicate','obsolete')
ck_staff_tasks_finish_pair  (completed_at is null) = (resolution is null)
ck_staff_tasks_title_len    length(btrim(title)) between 1 and 200
ck_staff_tasks_archive_gate archived_at is null or completed_at is not null
```

The last one encodes a rule: **you cannot archive an open task.** Finish it (possibly as
`wont_do`) first. Otherwise "archive" becomes the place where inconvenient work goes to be
forgotten without anyone having to say so.

Indexes:

```
staff_tasks_board      (status_id, position)            where archived_at is null
staff_tasks_assignee   (assignee_user_id, due_at)       where archived_at is null and completed_at is null
staff_tasks_due        (due_at)                         where archived_at is null and completed_at is null
staff_tasks_student    (student_id)                     where student_id is not null
staff_tasks_class      (class_name)                     where class_name is not null
staff_tasks_created    (created_at desc)
unique staff_tasks_key (key_no)
```

**The key.** `key_no` comes from a dedicated PostgreSQL sequence
`staff_task_key_seq`; the displayed key is `'V-' || key_no` (V for *vazifa*), computed in the
DTO, not stored. Gaps are acceptable here — unlike receipt numbers (SPEC §4.2), nobody audits
task numbering. Using a sequence rather than `max()+1` avoids a race for one line of DDL.

**`class_name`, not `class_id`.** `Student.ClassName` is a plain string column and
`SchoolClass.Name` is its only key in practice. Introducing a `class_id` FK here would be the
*first* place in the codebase to do so, and it would be inconsistent with everything around it.
This is recorded again in `docs/modules/existing-module-gaps.md` §2.5 as part of the larger
class-modelling problem; when that is fixed, this column moves with it. Do not fix it here.

### 3.3 `staff_task_watchers`

| Column | Type | Null | Notes |
|---|---|---|---|
| `task_id` | uuid | no | FK → `staff_tasks(id)` `on delete cascade` |
| `user_id` | text | no | FK → `app_users(id)` `on delete cascade` |
| `added_at` | timestamptz | no | `now()` |
| `added_by_user_id` | text | no | FK → `app_users(id)`. Equals `user_id` for self-subscribe. |

Primary key `(task_id, user_id)`. Index `(user_id)` for "everything I watch".

### 3.4 `staff_task_comments`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `task_id` | uuid | no | FK → `staff_tasks(id)` `on delete cascade` |
| `author_user_id` | text | no | FK → `app_users(id)` `on delete restrict`. From the JWT. |
| `body` | text | no | 1–4 000 chars after trim. Plain text. |
| `created_at` | timestamptz | no | `now()` |
| `deleted_at` | timestamptz | yes | Soft delete. |
| `deleted_by_user_id` | text | yes | |

Index `(task_id, created_at)`.

**Flat. No `parent_id`.** Reason: EduSchool's is flat (§2.8) and threading a 40-person
school's task comments buys nothing but a harder renderer and an ambiguous "reply to reply"
sort. If the client asks for threading later, adding a nullable `parent_id` is a one-column
migration — the reverse is not true.

Soft delete rather than a hard one, because a deleted comment still has to be absent from the
list but present in the history ("Aziza deleted a comment"), and because a hard delete of the
row would take the audit reference with it.

### 3.5 `staff_task_attachments`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `task_id` | uuid | no | FK → `staff_tasks(id)` `on delete cascade` |
| `file_url` | text | no | `/uploads/...`, produced by the existing `UploadsController` |
| `file_name` | text | no | original name, for display |
| `size_bytes` | bigint | no | |
| `content_type` | text | no | |
| `uploaded_by_user_id` | text | no | |
| `uploaded_at` | timestamptz | no | `now()` |

Uploads go through the **existing** `SchoolLms.Server/Controllers/UploadsController.cs` and
`SchoolLms.Application/Services/UploadGuard.cs`. Do not add a second upload path and do not
widen the allowlist (SPEC §7: "upload allowlist").

### 3.6 `staff_task_tags` and `staff_task_tag_links`

`staff_task_tags`: `id uuid`, `name text not null unique` (1–40 chars),
`color text not null default 'slate'` (same token list as statuses), `created_at timestamptz`.

`staff_task_tag_links`: `task_id uuid`, `tag_id uuid`, PK `(task_id, tag_id)`, both FKs
`on delete cascade`. Index `(tag_id)`.

**One taxonomy, not two — decision.** EduSchool has *statuses* **and** *types* **and** *tags*
(§2.13). For a single school with roughly forty staff, three orthogonal classification systems
is one and a half too many; in practice the "type" list becomes a duplicate of the tag list
with a different edit screen. We ship **tags** only. If the client later wants a
single-select, mutually-exclusive "type", it is a `staff_tasks.type_id` column plus a
catalogue table — additive, and by then we will know whether it is actually wanted.

### 3.7 `staff_task_rules` — automation

Mirrors EduSchool's settings flags (§2.14), but as rows rather than as columns on
`SchoolMeta`, because the set will grow and `SchoolMeta` is already 40 fields wide.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | |
| `code` | text | no | unique. Closed list: `absence_threshold`, `pickup_request`, `invoice_overdue`. |
| `is_enabled` | boolean | no | default `false` — **off until the client asks** |
| `threshold` | int | yes | meaning depends on `code`; see §5.7 |
| `assignee_user_id` | text | yes | FK → `app_users(id)`. Who the generated task goes to. |
| `status_id` | uuid | yes | FK → `staff_task_statuses(id)`. Which column. Default column if null. |
| `priority` | text | no | default `'medium'` |
| `due_in_days` | int | no | default `3` |
| `updated_at` | timestamptz | no | |

Rows are **seeded, never created by the UI** — the code list is closed because each code has
matching server logic. The settings screen edits the existing three rows.

### 3.8 Deduplication key for generated tasks

`staff_task_rule_hits`: `rule_code text`, `subject_key text`, `period_key text`,
`task_id uuid`, `created_at timestamptz`. PK `(rule_code, subject_key, period_key)`.

Without this, the nightly absence job creates the same task every night forever. `subject_key`
is the student id; `period_key` is `yyyy-MM` for `absence_threshold`, the invoice id for
`invoice_overdue`, and the pickup-request id for `pickup_request`.

### 3.9 Edge cases, nailed down

| Case | Decision | Reason |
|---|---|---|
| Timezone | Every timestamp is `timestamptz`. Every "today", "overdue", "this week" boundary is computed in **Asia/Tashkent (UTC+5)** via the existing `SchoolLms.Domain/AppClock.cs`. Never `DateTime.Now`, never the browser's clock. | SPEC §7 already mandates a single `AppClock`. A task due "today" must mean the same thing to the server job and to the browser. |
| Overdue definition | `due_at < AppClock.Now && completed_at is null && archived_at is null`. A completed task is never overdue, however late it was. | Otherwise the overdue counter never goes down and people stop looking at it. |
| Due date with no time | The client sends `due_at` with time `23:59:00` local when the user picked only a date. The server does not guess. | Avoids "due today" flipping to overdue at 00:00. |
| Soft delete | Tasks are **never** hard-deleted through the normal flow: `finish → archive`. `DELETE` exists (§4.2) for genuine mistakes, is restricted to `tasks:delete`, and does a real row delete with an audit entry. | Two mechanisms with clear, different meanings beats one ambiguous "delete" that sometimes hides and sometimes destroys. |
| Deleting a user who is an assignee | `on delete set null` → the task becomes unassigned and appears in the "unassigned" bucket. | The work does not disappear because the person left. |
| Deleting a user who is a reporter | `on delete restrict`. | We already never hard-delete `app_users` in practice (`Teacher.IsArchived`), so this constraint should never fire; if it does, that is information. |
| Money | None. This module holds no money and touches no financial table. If a task needs an amount, it goes in the description. | Keeps it entirely outside SPEC §4. |
| Concurrency on drag | Optimistic in the UI, last-write-wins on the server. No locking. | Two people reordering the same column at the same moment in a 40-person school is not a scenario worth a lock; the socket refresh corrects it within 400 ms. |

### 3.10 Ordering within a column

`position` is `double precision`, not an integer. On drop between neighbours `a` and `b`,
the new position is `(a.position + b.position) / 2`; at the top it is `first - 1`; at the
bottom `last + 1`; into an empty column, `0`.

This means a drag writes **one row**, not the whole column. A renumbering pass
(`row_number() * 1000`) runs when the gap between two neighbours falls below `1e-6`, which in
practice is never, and is implemented as a single `UPDATE ... FROM (select row_number() ...)`
inside the same transaction.

---

## 4. API contract

Base path `/api/admin/tasks`. All endpoints require `[Authorize]`.

### 4.1 RBAC

We do **not** copy EduSchool's fourteen task permissions. Our RBAC is a flat list of section
keys checked by `AdminPermAttribute` (`SchoolLms.Server/Controllers/AdminPermAttribute.cs`),
where **GET is open to any `staff` and writes require the section key**. Fourteen keys would
not fit that model without rewriting it, and rewriting RBAC is not in scope.

**Decision — one new section key, `tasks`, plus two role-level rules enforced in the
controller:**

| Rule | Who | Enforced by |
|---|---|---|
| Read the module at all | `admin`, `superadmin`, any `staff`, `cashier` | `[AdminPerm("tasks")]` (GET is open to staff by design) |
| Create / edit / comment / watch / finish a task | `admin`, `superadmin`, `staff` **with** the `tasks` permission | `AdminPermAttribute` |
| **See other people's tasks** | `admin`, `superadmin`, or `staff` with the `tasks` permission | `TaskScope` (below) |
| See only your own | `staff` **without** `tasks`, and `cashier` | `TaskScope` |
| Edit the columns / tags | `admin`, `superadmin` only | `[Authorize(Roles = Roles.AdminOrSuper)]` on those actions, mirroring `StaffController.UpdatePermissions` |
| Delete a task | `admin`, `superadmin` only | same |
| Edit automation rules | `superadmin` only | same |

`TaskScope` is one helper, used by every list/aggregate query:

```
scope(user) = user.IsInRole(admin|superadmin) || user.HasPerm("tasks")
                ? Everything
                : OnlyWhere(assignee_user_id == me || created_by_user_id == me
                            || watchers.Any(w => w.user_id == me))
```

This reproduces EduSchool's `getEmployeeTasks` / `getAllEmployeeTasks` split (§2.15) with the
RBAC machinery we already have. It must be applied in **one** place — a single
`IQueryable<StaffTask> Scoped(...)` extension — and every query must go through it. A second
copy of that predicate is how a leak happens.

Add `tasks` to the client permission list in `schoollms.client/src/config/constants.ts`
(shared file — see §8.2).

### 4.2 Endpoints

Conventions: request and response bodies are JSON; `camelCase`; errors are RFC 7807
`ProblemDetails` with the shape already produced by the project's exception handling; all
`4xx` bodies carry a Uzbek `detail`.

**Pagination — new convention.** The codebase has none today (verified: no `PagedResult`, no
`int page` parameter anywhere in `SchoolLms.Server/Controllers/`). This module introduces it,
additively:

```
Query:    ?page=1&pageSize=50          page ≥ 1, pageSize 1..200, default 50
Response: { "items": [...], "page": 1, "pageSize": 50, "total": 137 }
```

`total` is a separate `COUNT(*)` over the same filter. Do not retrofit this onto existing
endpoints in this work.

**Filter parameters** — accepted by `GET /`, `GET /board`, `GET /calendar`, `GET /summary`,
`GET /reports`, `GET /export`, and by `POST /bulk` inside `filter`:

| Param | Type | Notes |
|---|---|---|
| `search` | string | case-insensitive `ILIKE` over `title` and `description`. Trimmed; empty ignored. |
| `assigneeIds` | string[] | repeated query param: `?assigneeIds=a&assigneeIds=b`. **Not** JSON-in-a-string. See below. |
| `statusIds` | uuid[] | |
| `priorities` | string[] | |
| `tagIds` | uuid[] | |
| `studentIds` | string[] | |
| `classNames` | string[] | |
| `fromDate` / `toDate` | ISO 8601 | inclusive range over `due_at` |
| `onlyMine` | bool | `assignee_user_id == me` |
| `overdue` | bool | per §3.9 |
| `unassigned` | bool | `assignee_user_id is null` |
| `archived` | bool | `false` (default) → `archived_at is null`; `true` → `archived_at is not null`. Never both. |
| `groupBy` | `""\|assignee\|priority` | affects `GET /board` only |

> **Deliberate divergence from EduSchool.** They JSON-encode arrays into one parameter
> (`employeeIds=["a","b"]`) and hand-roll a defensive parser. We use repeated parameters,
> which ASP.NET Core binds to `string[]` with no parser at all. Their approach exists because
> their filter state is a single opaque `Object.entries(...).map(JSON.stringify)` blob; ours
> does not need to be. The **URL-as-state** idea from §2.10 we do keep — it is the reason a
> filtered board is shareable — just with normal encoding.

---

#### Board and lists

**`GET /api/admin/tasks/board`** — the kanban.
Query: all filters. Additional: `columnPageSize` (default 20, max 100).
Response:

```jsonc
{
  "columns": [
    { "statusId": "…", "title": "Yangi", "color": "blue", "position": 0,
      "total": 37,                       // matching the filter, not the column's whole size
      "items": [ TaskCard, … ] }         // first `columnPageSize`, ordered by position asc
  ],
  "unassignedCount": 4                   // for the avatar strip
}
```

`TaskCard` = `{ id, key, title, priority, dueAt, dueState, assignee: {id, fullName, avatarUrl} | null, tags: [{id,name,color}], commentCount, attachmentCount, student: {id, fullName} | null, className }`.
`dueState ∈ "" | "today" | "over" | "done"`, computed server-side with `AppClock` (§2.5's
`Os()` — but on the server, because the browser clock is not authoritative).

When `groupBy = assignee` or `priority`, `columns` is keyed by that dimension instead of by
status; the shape is identical, with `statusId: null` and a `groupKey` field. One response
shape, two meanings, so the renderer stays one component.

**`GET /api/admin/tasks/board/column`** — next page of one column.
Query: `statusId` (or `groupKey`), `afterPosition`, `pageSize`, plus all filters.
Response: `{ "items": [TaskCard], "hasMore": true }`.

**`GET /api/admin/tasks`** — the flat list (`/list` view).
Query: all filters + pagination + `sort` (`dueAt|createdAt|priority|title`, prefix `-` for
descending; default `-createdAt`).
Response: paged envelope of `TaskRow` (= `TaskCard` + `status`, `createdBy`, `createdAt`,
`completedAt`, `resolution`).

**`GET /api/admin/tasks/assignees`** — the avatar strip.
Query: all filters **except** `assigneeIds` and `onlyMine` (the strip must not filter itself).
Response:
`{ "currentUserId": "…", "assignees": [{ "userId", "fullName", "avatarUrl", "count" }], "unassigned": 4 }`,
current user first, then by `count` descending.

**`GET /api/admin/tasks/{id}`** — the drawer.
Response: the full task, including `watchers[]`, `comments[]` (non-deleted, ascending),
`attachments[]`, `isWatching`, and `canWrite` (so the client does not re-derive RBAC).
`404` if outside `TaskScope`, not `403` — do not confirm the existence of a task the caller
may not see.

**`GET /api/admin/tasks/{id}/history`** — the History tab.
Response: a single merged, descending list, computed **server-side**:

```jsonc
[ { "kind": "status",  "at": "…", "actor": "Aziza N.", "from": "Yangi", "to": "Jarayonda" },
  { "kind": "field",   "at": "…", "actor": "…", "changes": [ {"field":"Muddat","from":"…","to":"…"} ] },
  { "kind": "comment", "at": "…", "actor": "…", "body": "…" },
  { "kind": "system",  "at": "…", "actor": null, "text": "Davomat qoidasi bo'yicha yaratildi" } ]
```

> EduSchool merges these three streams **in the browser** from two different endpoints, and
> then has to special-case `statusId` in both. We merge on the server. Reason: it is one
> ordering rule in one place, and the client stays a renderer.

---

#### Writes

**`POST /api/admin/tasks`** — create. Perm: `tasks`.

```jsonc
{ "title": "…",                       // required, 1..200
  "description": "…",                 // optional
  "statusId": "…",                    // optional → the is_default column
  "priority": "medium",               // optional → "medium"
  "assigneeUserId": "…",              // optional → unassigned
  "dueAt": "2026-09-20T23:59:00+05:00", // optional
  "studentId": "…", "className": "5-B", "leadId": "…",   // all optional
  "tagIds": ["…"],                    // optional
  "watcherUserIds": ["…"] }           // optional; the creator is always added
```

Server sets `createdByUserId` from the JWT and **rejects the request with `400` if the body
contains it** — the same rule as SPEC §4.4 for `cashier_id`. Same for `completedAt`,
`archivedAt`, `keyNo`, `position`.
`201` + the full task. Errors: `400` validation, `404` unknown `statusId`/`assigneeUserId`.

**`PUT /api/admin/tasks/{id}`** — edit. Perm: `tasks`, and within `TaskScope`.
Same body, all fields optional (partial update; an explicit `null` clears, an absent key
leaves alone — document this in the DTO, it is the classic bug).
Rejects any change when `archived_at is not null` → `409 "Arxivlangan vazifani tahrirlab bo'lmaydi"`.

**`PATCH /api/admin/tasks/{id}/move`** — drag. Perm: `tasks`.
`{ "statusId": "…", "beforeTaskId": "…" | null, "afterTaskId": "…" | null }`.
Server computes `position` per §3.10. Response `{ "id", "statusId", "position" }`.
`409` if either neighbour is no longer in that column (someone else moved it) — the client
refetches the column rather than guessing.

**`POST /api/admin/tasks/{id}/finish`** — Perm: `tasks`.
`{ "resolution": "done|wont_do|duplicate|obsolete", "comment": "…" }` (comment optional; if
present it is posted as a normal comment in the same transaction).
Sets `completed_at`, `resolution`, `completed_by_user_id`. `409` if already finished.

**`POST /api/admin/tasks/{id}/reopen`** — Perm: `tasks`.
Clears `completed_at`, `resolution`, `completed_by_user_id`. `409` if archived.
*(EduSchool has no visible reopen. We add it because the alternative is a duplicate task with
no link to the original.)*

**`POST /api/admin/tasks/{id}/archive`** / **`/unarchive`** — Perm: `tasks`.
`409` if not finished (see `ck_staff_tasks_archive_gate`).

**`DELETE /api/admin/tasks/{id}`** — Perm: `admin`/`superadmin` only. Hard delete, cascades to
comments, watchers, attachments and tag links. Writes an `audit_log` row with the full task as
`Before`. `204`.

**`POST /api/admin/tasks/{id}/comments`** — Perm: `tasks`, within scope.
`{ "body": "…" }` → `201` comment.
**`DELETE /api/admin/tasks/{id}/comments/{commentId}`** — author only, or
`admin`/`superadmin`. Soft delete. `403` otherwise.

**`PUT /api/admin/tasks/{id}/watch`** — Perm: `tasks`, within scope.
`{ "watching": true }` — toggles **the caller**.
**`POST /api/admin/tasks/{id}/watchers`** `{ "userIds": [...] }` and
**`DELETE /api/admin/tasks/{id}/watchers/{userId}`** — add/remove **others**. Allowed to the
reporter, the assignee, and `admin`/`superadmin`. *(EduSchool only has self-subscribe, §2.7.
See §10 Q3 — this is our addition, and it is the difference between "I hope she noticed" and
"she is on it".)*

**`POST /api/admin/tasks/{id}/attachments`** — `multipart/form-data`, single file, routed
through the existing `UploadsController` allowlist.
**`DELETE /api/admin/tasks/{id}/attachments/{attachmentId}`** — uploader or admin.

**`POST /api/admin/tasks/bulk`** — Perm: `tasks`.

```jsonc
{ "target": { "ids": ["…"] }            // XOR
            | { "filter": { …filters… }, "statusIds": ["…"] },
  "action": "setStatus|setAssignee|setPriority|addTag|removeTag|finish|archive",
  "payload": { … } }
```

Rules, all of them enforced:

- `ids` and `filter` are **mutually exclusive**; sending both is `400`.
- `ids` is capped at 500.
- `filter` **must** be non-empty; an unbounded "everything" bulk is `400`. (EduSchool's
  `isAll:true` scopes to specific `stageIds`; we require the same discipline.)
- The response reports what actually happened, because a filter-scoped bulk can hit rows the
  user never saw: `{ "matched": 137, "changed": 131, "skipped": [{ "id": "…", "reason": "archived" }] }`.
- Every affected row still goes through `TaskScope`.
- One `audit_log` row per affected task, not one for the batch. A batch entry is unreadable six
  months later.

---

#### Catalogue and settings

**`GET /api/admin/tasks/statuses`** — any authenticated staff.
**`PUT /api/admin/tasks/statuses`** — `admin`/`superadmin`. **Whole-list upsert**, mirroring
`LeadStagesController` reordering and EduSchool's `PUT stages {statuses:[...]}`:

```jsonc
{ "statuses": [ { "id": "…" | null, "title": "…", "color": "blue", "isDefault": false } ] }
```

Position = array index. Rows absent from the array are deleted; deletion is rejected with
`409` if any task still references them, and the error body names them:
`{ "detail": "…", "blocked": [{ "statusId": "…", "title": "Jarayonda", "taskCount": 12 }] }`.
Exactly one `isDefault` required.

**`GET|PUT|DELETE /api/admin/tasks/tags`** — same whole-list upsert shape.
`DELETE` of a tag just removes the links; a tag is a label, not a container.

**`GET|PUT /api/admin/tasks/rules`** — `superadmin` only. Edits the three seeded rows (§3.7).

**`GET /api/admin/tasks/export`** — Perm: `tasks`. All filters. Returns `.xlsx` through the
existing `SchoolLms.Application/Services/ExcelExport.cs`. Columns: key, title, status,
priority, assignee, reporter, due, created, completed, resolution, student, class, tags,
comment count. Capped at 10 000 rows; beyond that, `400` telling the user to narrow the filter.

---

#### Aggregates

**`GET /api/admin/tasks/summary`** — the `/summary` view. All filters apply.

```jsonc
{ "open": 84, "overdue": 12, "dueToday": 5, "unassigned": 4,
  "completedThisWeek": 31,
  "byStatus":   [{ "statusId","title","color","count" }],
  "byPriority": [{ "priority","count" }],
  "byAssignee": [{ "userId","fullName","open","overdue" }] }
```

**`GET /api/admin/tasks/calendar`** — `?from=&to=` (max 62 days) + filters.
`[{ "date": "2026-09-14", "items": [TaskCard] }]`, grouped by the **local** date of `due_at`,
tasks with no `due_at` omitted.

**`GET /api/admin/tasks/timeline`** — `?from=&to=` + filters.
`[{ "date": "2026-09-14", "events": [{ kind, taskId, key, title, actor, at }] }]` — a
chronological feed of task activity (created / status changed / finished / commented),
descending. This is what EduSchool's `/task/timeline` almost certainly is, by analogy with the
leads timeline in the same bundle, which groups events into day buckets exactly this way
(bundle offset ≈ 1 612 500). **Inferred.**

**`GET /api/admin/tasks/reports`** — Perm: `tasks` **and** role `admin`/`superadmin`
(EduSchool gates it separately with `getEmployeeTaskReports`).
`?from=&to=&groupBy=assignee|status|priority|tag` + filters.

```jsonc
{ "range": { "from": "…", "to": "…" },
  "rows": [ { "key": "…", "label": "Aziza N.",
              "created": 24, "finished": 19, "overdueNow": 3,
              "avgAgeDays": 4.2, "avgTimeToFinishHours": 51.7,
              "byResolution": { "done": 16, "wont_do": 2, "duplicate": 1, "obsolete": 0 } } ],
  "totals": { … same shape … } }
```

**Our design, not EduSchool's** (§2.16). Definitions, so four people implement the same
numbers:

| Measure | Definition |
|---|---|
| `created` | `created_at` inside the range |
| `finished` | `completed_at` inside the range (regardless of when it was created) |
| `overdueNow` | overdue **at the moment of the request** (§3.9), ignores the range |
| `avgAgeDays` | over tasks still open now: `(now − created_at)` in days, 1 decimal |
| `avgTimeToFinishHours` | over tasks finished in the range: `(completed_at − created_at)` in hours, 1 decimal |
| `byResolution` | over tasks finished in the range |

Rounding: half away from zero, one decimal. Empty buckets are returned with zeros, not
omitted — a missing row and a zero row look identical in a chart and mean different things.

---

## 5. Business rules

### 5.1 State machine

```
              ┌──────────── reopen ────────────┐
              ▼                                │
  (create) → OPEN ──── finish(resolution) ── FINISHED ── archive ─→ ARCHIVED
                                                  ▲                    │
                                                  └──── unarchive ─────┘
```

- Moving between **columns** changes nothing about this machine (§2.6).
- `ARCHIVED` is read-only: no edit, no comment, no move, no watch change. Only `unarchive`.
- `finish` requires a `resolution`. There is no "finished, reason unknown".

### 5.2 Validation

| Field | Rule | Message (Uzbek) |
|---|---|---|
| `title` | trim, 1–200 | `Sarlavha 1–200 belgi bo'lishi kerak` |
| `description` | ≤ 10 000 | `Tavsif juda uzun` |
| `body` (comment) | trim, 1–4 000 | `Izoh bo'sh bo'lmasligi kerak` |
| `dueAt` | may be in the past (you can log work that was already late) | — |
| `priority` | in the enum | `Muhimlik darajasi noto'g'ri` |
| `statusId` | must exist | `Bunday ustun yo'q` |
| `assigneeUserId` | must exist, must be `admin\|superadmin\|staff\|cashier\|teacher`, must not be archived | `Bu xodimga vazifa biriktirib bo'lmaydi` |
| attachment | ≤ 20 MB, extension in the existing `UploadGuard` allowlist | reuse the existing message |
| tag `name` | trim, 1–40, unique case-insensitively | `Bu teg allaqachon bor` |

### 5.3 Why a column is not a state — the rule, restated

Someone will read this SPEC and think "surely the last column means done, let's just do that".
Here is why not:

1. Columns are user-defined. A school that renames "Bajarildi" to "Direktorga" has silently
   broken completion.
2. Reports need a *comparable* completion signal across time, and column definitions change.
3. `resolution` carries information a column cannot: `wont_do` and `done` are both "off the
   board" but they are not the same outcome, and the difference is exactly what a monthly
   report is for.
4. EduSchool reached the same conclusion — that is what the separate `finishEmployeeTask`
   permission is (§2.6).

The board therefore renders finished tasks with a strike-through and a resolution chip **in
whatever column they are in**, and the default filter hides them after they are archived, not
after they are finished.

### 5.4 Who may do what to a task

Beyond the RBAC in §4.1, three object-level rules:

- Only the **comment author** (or an admin) may delete a comment.
- Only the **reporter**, the **assignee**, or an admin may add or remove *other people's*
  watchers.
- Anyone within scope with `tasks` may reassign. *(Deliberately not restricted: in a school
  this size, "only the reporter can reassign" produces a queue at one person's desk.)*

### 5.5 Notifications

Reuse what exists — do not build a second notification system.

| Event | Recipients | Channel |
|---|---|---|
| assigned to you | new assignee | in-app feed + SignalR |
| comment added | assignee + watchers, **minus** the author | in-app feed + SignalR |
| status changed | assignee + watchers, minus the actor | SignalR only |
| due within 24 h | assignee | in-app feed, once, by the nightly job |
| became overdue | assignee + reporter | in-app feed, once |

In-app feed = the existing derived feed behind
`SchoolLms.Server/Controllers/NotificationsController.cs`. Tasks become one more source in it.
Realtime = the existing `SchoolLms.Application/Hubs/LiveHub.cs`, which already has
`Join(topic)` / `Leave(topic)` group semantics: topics `tasks` (board-level invalidation) and
`task:<id>` (drawer-level).

**Do not open a second WebSocket** the way EduSchool does with `moderator-chat-api` (§2.4). We
have one hub; adding a topic costs nothing.

Copy these two behaviours from EduSchool verbatim, because they are the ones that make a live
board tolerable:

1. **Ignore your own events** — compare the event's `actorId` to the current user id and drop
   the refresh. Otherwise every keystroke-triggered save yanks the board out from under you.
2. **Debounce 400 ms and suppress while dragging**, then fire once on drop.

No Telegram, no e-mail, no SMS for tasks. This is internal staff work; the in-app feed and the
board are where people already are.

### 5.6 Export

`ExcelExport` already exists and is used elsewhere; reuse it. Filenames:
`vazifalar-yyyy-MM-dd.xlsx`.

### 5.7 Automation rules

One `IHostedService` (in-process, per SPEC §1 "background work stays in-process"), running
nightly at 02:00 Asia/Tashkent, plus one synchronous hook.

| `code` | Trigger | `threshold` means | Generated task |
|---|---|---|---|
| `absence_threshold` | nightly | unexcused absences in the current calendar month | title `"<F.I.SH> — oyiga <n> ta sababsiz qoldirish"`, `student_id` set, `class_name` set, due `+ due_in_days` |
| `invoice_overdue` | nightly | days past `overdue_after_day` | title `"<F.I.SH> — to'lov qarzi <summa>"`, `student_id` set |
| `pickup_request` | synchronous, on `PickupRequest` insert | unused | title `"<F.I.SH> — ota-ona olib ketish so'rovi"` |

All three are **disabled by default** (`is_enabled = false`). Reason: a rule that starts firing
the day it ships, before anyone has agreed the threshold, produces 300 tasks overnight and the
module is dead on arrival.

Generated tasks are marked by a `staff_task_rule_hits` row (§3.8) and carry a `system` entry in
their history. They are ordinary tasks otherwise — editable, assignable, closable.

`invoice_overdue` reads `overdue_after_day` from `BillingSettings`
(`SchoolLms.Domain/Billing.cs`); it does **not** define its own overdue rule. There is exactly
one definition of "overdue invoice" in this system and it is not in this module.

---

## 6. UI

Our own visual language. `schoollms.client/src/components/ui/` (`Modal`, `Button`, `Input`,
`Textarea`, `Loader`) — the same primitives the leads board uses. Charts: `recharts` (already
a dependency). Drag: `@dnd-kit/core` (already a dependency).

### 6.1 Navigation

One new top-level entry in `schoollms.client/src/config/navigation.ts`, admin nav, placed
directly after **Lidlar** (both are "work in flight"):

```
{ label: 'Vazifalar', to: '/admin/tasks', icon: ListChecks, perm: 'tasks', children: [
    { label: 'Doska',      to: '/admin/tasks', end: true },
    { label: "Ro'yxat",    to: '/admin/tasks/list' },
    { label: 'Kalendar',   to: '/admin/tasks/calendar' },
    { label: 'Xronologiya',to: '/admin/tasks/timeline' },
    { label: 'Hisobotlar', to: '/admin/tasks/reports', roles: ['admin','superadmin'] },
    { label: 'Sozlamalar', to: '/admin/tasks/settings', roles: ['admin','superadmin'] } ] }
```

`icon` is from `lucide-react`, already the project's icon set.

`schoollms.client/src/config/navigation.ts` is a **shared file** — see §8.2.

### 6.2 Pages

All six share one header: title, filter toggle, preset selector, "Yangi vazifa" button, export
button, and the assignee avatar strip. Filters live in the URL (§2.10, kept).

| Route | Contents | Hidden from |
|---|---|---|
| `/admin/tasks` | Kanban. Columns from `staff_task_statuses`, horizontal scroll, per-column count and lazy "load more". Card: key, title, priority dot, due chip (green today / red overdue / grey otherwise), assignee avatar, tag pills, comment and attachment counts. Drag between and within columns. | — |
| `/admin/tasks/list` | Table with sortable columns and pagination. Row click → drawer. | — |
| `/admin/tasks/calendar` | Month grid by `due_at`; day cell shows up to 3 tasks + "+N". Click a day → filtered list. | — |
| `/admin/tasks/timeline` | Day-grouped activity feed. | — |
| `/admin/tasks/reports` | KPI row + a bar chart per `groupBy` + the table from §4.2. | `staff`, `cashier` (nav `roles` + a 403 from the endpoint) |
| `/admin/tasks/settings` | Two panels: **Ustunlar** (drag to reorder, colour picker from the token list, exactly one "default" radio, delete blocked with a task count) and **Teglar**. Third panel **Avtomatlashtirish** for `superadmin` only. | non-admin |

### 6.3 The drawer

Opens as `?task=<id>` on top of the current view (EduSchool's pattern, and it is the right one:
you keep your place on the board). Right-hand slide-over, 520 px on `sm+`, full width on
mobile.

Header: key, copy-link, watch toggle (eye / eye-off), close.
Body: title, chips (status, priority, resolution), tabs **Tafsilotlar** / **Tarix**.
Details tab: description, then a label/value grid — assignee, reporter, deadline, created,
watchers — then attachments, then comments with a compose box at the bottom.
Footer: **Tahrirlash** (opens the form modal), **Yakunlash** (resolution picker),
**Arxivlash**.

Everything write-shaped is hidden, not merely disabled, when `canWrite` is false.

### 6.4 What is hidden by permission

| Element | Requires |
|---|---|
| The whole nav entry | `tasks` (staff) or admin role |
| "Yangi vazifa", edit, finish, archive, comment box, watch toggle, drag handles | write access per §4.1 |
| Other people's tasks | `TaskScope = Everything` |
| Reports tab | `admin` / `superadmin` |
| Settings tab | `admin` / `superadmin` |
| Automation panel | `superadmin` |
| Delete | `admin` / `superadmin` |

---

## 7. What we already have

Honest inventory. **Nothing of this module exists.** What exists is reusable scaffolding:

| Need | Already in the repo | File |
|---|---|---|
| Board with user-defined ordered columns + dnd | Yes, working | `schoollms.client/src/pages/admin/leads/` (**read-only reference**, §0.3) |
| Column entity with title/colour/order | Yes | `SchoolLms.Domain/Entities.cs` → `LeadStage`; `SchoolLms.Server/Controllers/LeadStagesController.cs` |
| Section-key RBAC | Yes | `SchoolLms.Server/Controllers/AdminPermAttribute.cs`, `SchoolLms.Domain/Roles.cs` |
| Staff identity | Partly | `AppUser` (`Role`, `Position`, `Permissions[]`) + `Teacher.UserId`. **There is no single `employees` table** — see the note below. |
| Audit with before/after | Yes, but finance-scoped | `SchoolLms.Domain/Entities.cs` → `AuditLog`; `SchoolLms.Application/Services/AuditService.cs` |
| Realtime | Yes | `SchoolLms.Application/Hubs/LiveHub.cs` (`Join`/`Leave` topics) |
| In-app notifications | Yes, derived feed | `SchoolLms.Server/Controllers/NotificationsController.cs` |
| File upload + allowlist | Yes | `SchoolLms.Server/Controllers/UploadsController.cs`, `SchoolLms.Application/Services/UploadGuard.cs` |
| Excel export | Yes | `SchoolLms.Application/Services/ExcelExport.cs` |
| Background jobs | Yes, in-process | pattern per SPEC §2.1; see the accrual/anomaly services |
| Single clock | Yes | `SchoolLms.Domain/AppClock.cs` |
| Pagination | **No** | Nothing in `SchoolLms.Server/Controllers/` takes `page`. This module introduces it (§4.2). |
| Generic entity history UI | **No** | `AuditController` filters by `entityType`/`entityId` but there is no reusable history component. |

**The `employees` gap.** EduSchool has one `employees` collection; we have `AppUser` (roles
`admin`, `superadmin`, `staff`, `cashier`) **and** `Teacher` (with an optional `UserId` back to
`AppUser`). A task must be assignable to any of them.

Decision: **`assignee_user_id` references `app_users(id)`, and nothing else.** A teacher who
should receive tasks must have an `AppUser`. Reasons: `AppUser` is the only identity the JWT
carries, so it is the only one `TaskScope` can compare against; and `Teacher.UserId` already
exists, so the join is there. The alternative — a polymorphic `(assignee_type, assignee_id)` —
would make every query in this module a `UNION`.

Consequence to state in the release notes: teachers without an account cannot be assigned
tasks. That is correct behaviour (they could not open the board anyway), not a limitation to
work around.

---

## 8. Modules and phases

Fifteen units. Each names the files it touches, its dependencies, and whether it can run in
parallel.

### 8.1 Independent units

| # | Unit | Files | Depends on | Parallel? |
|---|---|---|---|---|
| **T1** | **Domain + EF model + migration.** Entities in `SchoolLms.Domain/StaffTasks.cs`; EF config in `SchoolLms.Infrastructure/Data/StaffTaskModel.cs` (one file, `Apply(ModelBuilder)`, following `GuardianModel.cs`); one migration; `Migrations/Sql/staff_task_guards.sql` with the `app_rw` GRANTs and the `staff_task_key_seq` sequence; the seed. | — | **No.** It is the only unit that touches `AppDbContextModelSnapshot.cs`. Nothing else in this module may generate a migration. |
| **T2** | **`TaskScope` + read queries.** `SchoolLms.Application/StaffTasks/TaskQueries.cs` — board, list, detail, assignees, history. `AsNoTracking`, no writes. Named `Queries`, following `FinanceReportQueries`. | T1 | Yes, with T3–T5 |
| **T3** | **Write service.** `SchoolLms.Application/StaffTasks/StaffTaskService.cs` — create, update, move, finish, reopen, archive, delete, bulk. Owns `position` arithmetic and audit writes. | T1 | Yes |
| **T4** | **Comments, watchers, attachments.** `SchoolLms.Application/StaffTasks/TaskCollaborationService.cs`. | T1 | Yes |
| **T5** | **Catalogue service.** `SchoolLms.Application/StaffTasks/TaskCatalogService.cs` — statuses and tags whole-list upsert, delete guards. | T1 | Yes |
| **T6** | **Controller.** `SchoolLms.Server/Controllers/StaffTasksController.cs` — every route in §4.2, `[AdminPerm("tasks")]`, JWT-derived actor. | T2–T5 | No (single file) |
| **T7** | **Aggregates.** `SchoolLms.Application/StaffTasks/TaskReportQueries.cs` — summary, calendar, timeline, reports. | T1, T2 | Yes, with T6 |
| **T8** | **Automation.** `SchoolLms.Application/StaffTasks/TaskRuleService.cs` + `SchoolLms.Server/…/TaskRuleHostedService.cs`; the pickup hook in `PickupService`. | T1, T3 | Yes, with T6/T7 |
| **T9** | **Leads bulk → task.** Bulk-create one task per selected lead, plus bulk change-responsible. Adds a toolbar to the leads board. | T3, and the whole module shipped | **No — and last.** It edits protected files (`LeadsPage.tsx`, `LeadColumn.tsx`, `LeadCard.tsx`). Additive only: a toolbar and a selection mode, **no change to card or column layout, spacing or colour**. Requires an explicit go-ahead. If in doubt, drop it. |
| **T10** | **Client API layer.** `schoollms.client/src/api/services/staffTasks.ts` + types in `schoollms.client/src/types/staffTasks.ts`. | contract frozen (this file) | Yes, immediately — does not wait for the backend |
| **T11** | **Board page.** `pages/admin/tasks/TasksBoardPage.tsx`, `TaskColumn.tsx`, `TaskCard.tsx`. Copied from the leads board, **not** imported from it. | T10 | Yes |
| **T12** | **Drawer + form.** `TaskDrawer.tsx`, `TaskFormModal.tsx`, `TaskHistoryTab.tsx`, `TaskComments.tsx`, `TaskAttachments.tsx`. | T10 | Yes |
| **T13** | **List / calendar / timeline.** `TasksListPage.tsx`, `TasksCalendarPage.tsx`, `TasksTimelinePage.tsx`. | T10 | Yes |
| **T14** | **Reports + settings.** `TasksReportsPage.tsx`, `TasksSettingsPage.tsx`, `StatusListEditor.tsx`, `TagListEditor.tsx`. | T10 | Yes |
| **T15** | **Tests.** `SchoolLms.Tests/StaffTasks/` — RBAC (mandatory, per the global rules), scope isolation, state machine, `position` arithmetic, bulk guards, rule dedup. | T6 | Yes across test files |

Suggested waves: **T1** → (T2 T3 T4 T5 T10 in parallel) → (T6 T7 T11 T12 T13 T14 in parallel)
→ (T8 T15) → shared files → T9 (optional).

### 8.2 Shared files — **sequential, never parallel**

Two agents editing any of these at once produces a conflict that is easy to resolve badly.
One agent, one pass, at the end.

| File | Change |
|---|---|
| `SchoolLms.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | Regenerated by **T1 only**. No other unit runs `dotnet ef migrations add`. |
| `SchoolLms.Infrastructure/Data/AppDbContext.cs` | One line: `StaffTaskModel.Apply(b);` in `OnModelCreating`, plus the `DbSet`s. |
| `SchoolLms.Server/Program.cs` | DI registrations + the hosted service. |
| `SchoolLms.Infrastructure/Migrations/Sql/staff_task_guards.sql` + its `csproj` `EmbeddedResource` entry | New file; the csproj line is the shared part. |
| `schoollms.client/src/config/navigation.ts` | The nav entry from §6.1. |
| `schoollms.client/src/config/constants.ts` | `{ key: 'tasks', label: 'Vazifalar' }` in the admin permission list. |
| `schoollms.client/src/App.tsx` | Six routes under `RequirePerm perm="tasks"`. |
| `SchoolLms.Application/Services/AuditService.cs` | One constant: `EntityStaffTask = "StaffTask"`. |
| `docs/SPEC.md` §5 module inventory | One row. |

### 8.3 Grants

Every new table needs a GRANT in `Migrations/Sql/staff_task_guards.sql`, following
`billing_guards.sql`:

```sql
GRANT SELECT, INSERT, UPDATE, DELETE ON
  public.staff_tasks, public.staff_task_statuses, public.staff_task_watchers,
  public.staff_task_comments, public.staff_task_attachments, public.staff_task_tags,
  public.staff_task_tag_links, public.staff_task_rules, public.staff_task_rule_hits
TO app_rw;
GRANT USAGE, SELECT ON SEQUENCE public.staff_task_key_seq TO app_rw;
```

Wrapped in the same `IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='app_rw') THEN RAISE
NOTICE …` guard the existing files use — the local stack sometimes has no `app_rw`.

**These tables are not financial.** They are ordinary read-write tables; the append-only
REVOKE pattern from SPEC §4.1 does **not** apply. Do not copy it here.

---

## 9. Definition of Done

### 9.1 Machine-checked

- [ ] `dotnet build` clean, no new warnings.
- [ ] `pytest`-equivalent for this stack: `dotnet test` green, including the new
      `SchoolLms.Tests/StaffTasks/`.
- [ ] `cd schoollms.client && npm run build` green (`tsc -b && vite build`).
- [ ] `npm run lint` clean.
- [ ] `dotnet ef database update` applies on an empty database **and** on a database at the
      current head.
- [ ] The migration's `Up()` contains **no `DROP`**. Read it by hand before committing — the
      global rule exists because `--autogenerate` emits spurious drops.
- [ ] `AppDbContextModelSnapshot.cs` has exactly one new diff, produced by T1.

### 9.2 Behavioural

- [ ] Seed on a fresh database creates four columns — **Yangi** (default, `blue`), **Jarayonda**
      (`amber`), **Tekshiruvda** (`violet`), **Bajarildi** (`emerald`) — and zero tasks.
- [ ] An `admin` can create a task, assign it, drag it between two columns, comment, attach a
      PDF, finish it as `done`, and archive it.
- [ ] A `staff` user **without** the `tasks` permission sees only tasks where they are
      assignee, reporter or watcher — verified by an integration test that creates two users
      and asserts the second sees `404` on the first's task id, on **every** read endpoint
      including `/board`, `/summary`, `/calendar` and `/export`.
- [ ] A `cashier` gets the same restriction.
- [ ] `POST /tasks` with `createdByUserId` in the body returns `400`.
- [ ] Deleting a status that has tasks returns `409` and the body names the blocked column and
      its task count.
- [ ] Archiving an unfinished task returns `409`.
- [ ] `POST /tasks/bulk` with both `ids` and `filter` returns `400`; with an empty `filter`
      returns `400`; with 501 ids returns `400`.
- [ ] Dragging writes exactly one row — asserted by counting the `UPDATE`s in the test.
- [ ] Two browsers: A moves a card, B's board updates within ~1 s; A's own board does **not**
      flicker; a drag in progress on B is not interrupted.
- [ ] Every filter round-trips through the URL: apply, copy the link, open in a new tab, same
      result.
- [ ] The absence rule, enabled with `threshold = 3` against seeded data, creates one task per
      qualifying student and **zero** additional tasks on the second run.
- [ ] Export produces a valid `.xlsx` that opens in Excel with the filtered rows only.
- [ ] Uzbek UI throughout; no English string reaches the screen; no Uzbek identifier reaches
      the code.

### 9.3 Not broken

- [ ] `/admin/assignments` (homework) is untouched and still works.
- [ ] `schoollms.client/src/pages/admin/leads/*` is **byte-identical** unless T9 was explicitly
      approved. Check with `git diff --stat` on those six files before opening the PR; the
      expected output is empty.
- [ ] No existing endpoint changed signature.
- [ ] `docs/ASSUMPTIONS.md` has one line per decision taken during the build that is not
      already in this file.

---

## 10. Open questions

Each carries the decision that **stands if the client says nothing**. None of them blocks the
start of work.

**Q1 — Is the board school-wide or per-department?**
EduSchool scopes everything by `Branch` and has `department` / `job-title` catalogues. We are
one school with no departments.
**Decision: one school-wide board.** Grouping by assignee (`groupBy=assignee`) covers "show me
the academic office's work". Adding a `department_id` later is one nullable column.

**Q2 — May a teacher see the task board?**
**Decision: no.** The teacher panel (`TeacherPermissions`: journal, assignments, schedule,
messages, salary) stays as it is. A teacher with an `AppUser` of role `teacher` can be *named*
as an assignee only if the client later asks for a teacher-facing view; today, assigning to a
teacher without a staff account is prevented by validation (§5.2). Reason: the teacher PWA is
a separate, deliberately small surface, and widening it is its own project.

**Q3 — Self-subscribe watchers only, or may I add you?**
EduSchool only allows self-subscribe (§2.7).
**Decision: allow the reporter, the assignee and admins to add others.** Reason: "I need the
head teacher to see this" is the actual use case, and self-subscribe cannot express it. Kept
narrow (reporter/assignee/admin) so it does not become a way to spam everyone.

**Q4 — Should finishing a task move it to a specific column?**
**Decision: no automatic move.** The card stays where it is and gets a strike-through plus a
resolution chip. Reason: any automatic move guesses which column means "done", and §5.3 is the
whole argument against that. If the client wants it, it becomes a single nullable setting
`staff_task_settings.finish_moves_to_status_id`.

**Q5 — Default automation thresholds.**
**Decision: all three rules ship disabled** (§5.7), with defaults `absence_threshold = 3`,
`invoice_overdue = 0 days past overdue_after_day`, `due_in_days = 3`. Reason: numbers picked
by us and switched on by us will be wrong, and the wrong number here means a flood of tasks on
day one.

**Q6 — Retention.**
**Decision: keep everything forever.** No purge job. Reason: a school generates on the order of
a few thousand tasks a year; the table will not be a problem in this decade, and "the task
that proves we called the parent" is the kind of record people need three years later.

**Q7 — Types as well as tags?**
**Decision: tags only** (§3.6). Revisit if the client, having used tags for a term, asks for a
mandatory single-select classification.

**Q8 — Bring in EduSchool's `custom-fields` idea?**
They have `custom-fields/{STUDENTS,PARENTS,EMPLOYEES,LEADS,AGREEMENT}` — user-defined fields on
core records.
**Decision: out of scope for this module, and worth a separate conversation for the platform.**
It is a schema-level capability, not a feature, and bolting it onto tasks first would be the
wrong place to start.
