# Plan — Students and Finance at full EduSchool parity

**Priority set by the client on 2026-09-17:** Students and Finance are "havodek muhim" — as
vital as air. Both modules, with every inner part of every screen, come before anything else.
Everything else in `docs/modules/existing-module-gaps.md` waits.

---

## 1. Scope — 23 screens

Menu-level parity already exists. Screen-level parity — columns, filters, forms, validation,
row and bulk actions, exports — has never been captured (`docs/EDUSCHOOL-INVENTORY.md`, last
paragraph). That is the gap this plan closes.

### O'quv bo'limi — `BARN_ALL` (10)

| Screen | Menu-level today | Known big item |
|---|---|---|
| Sinf | have | — |
| Group | **missing** | `Group ≠ Sinf`, ~184 h — **in scope**, client confirmed cross-class groups (§4) |
| Fanlar | have | — |
| Xonalar | **missing** | owned by `warehouse.md`; the room list alone is small |
| O'quvchilar (+ profile, modals, transfer, import, bulk) | have | screen-level unknown |
| Arxiv o'quvchilar | have (reasons shipped 2026-09-17) | — |
| Sertifikat | have (shipped 2026-09-16) | — |
| O'quvchilar manzili | have | — |
| Ota-onalar | have | — |
| Shartnomalar | have | `custom-fields` declined (§2.6) |

### Moliya — `FINANCE_ALL` (13)

| Screen | Menu-level today | Known big item |
|---|---|---|
| Moliya (`/cash` — the cash desk) | partial — `/cashier` + Chiqimlar | expense create/approve broken (F1.01–F1.03) |
| Qarzdorlar bilan ishlash | have (shipped 2026-09-17) | — |
| Ish haqi | partial — teacher salary calc | payroll spine, `hr.md` HR-1 — **206 h** by its own task table, not 118 |
| Moliya hisobotlari | have | screen-level unknown |
| P&L | have | — |
| P&L 2.0 | **missing** | deferred a year by decision (§3.6) |
| Pul oqimi | have | — |
| Moliya analitikasi (`fin-map`) | partial — Kassa kuni, Pul aylanmasi | `fin-map` is this screen, not *Moliya* (finance-parity §0.4) |
| Tranzaksiyalar | partial | cancel/delete/back-date **refused on principle** (§7.4) |
| Abonement tranzaksiyalari | partial | transaction list (§3.4.2) |
| Bonus | **missing** | `hr.md` |
| Jarima | **missing** | `hr.md` |
| Qarzdorlik oyma-oy | have (shipped 2026-09-16) | — |

---

## 1a. What the audits found (2026-09-17)

Both screen-level audits are in: `docs/modules/students-parity.md` and
`docs/modules/finance-parity.md`.

| Module | P0 | P1 | P2 | Total |
|---|---|---|---|---|
| Students | 302 h (Group 282 + class roster 20) | 211 h | 141 h | 654 h |
| Finance | 69 h | 398 h (206 of it payroll HR-1) | 193 h | 660 h |

**Live defects — verified in the code by the orchestrator, fixed first and deployable alone:**

| Id | Defect |
|---|---|
| G-1 | Renaming a class orphans every pupil in it — `students.class_name` is never updated |
| G-2 | A split lesson (two `LessonNote` rows, same period/subject, different `SubGroup`) crashes the pupil dashboard and the parent Mini App with a 500 — `ToDictionary` on a duplicate key |
| F1.01 | "Yangi chiqim" always fails — the client sends no `method`, the server requires one |
| F1.02 | Expense approval always fails — no body posted; client hard-codes the 5 M threshold; no double-approval guard |
| F1.03 | A cash expense never lowers the shift's expected cash — every one shows as a shortage (needs a column) |
| F3.05 | Staff can post salary payments and change pay rates (reported; verified by its slice) |
| F10.02 | `VoidAsync` has no endpoint, refuses invoices whose payment was reversed, and would 500 |
| F0.03 | `finance_anomaly_flags` is missing from `init-roles.sql` §5 — re-running the script undoes its column lock |

---

## 1b. Execution map

| Wave | Slice | What | Needs schema | State |
|---|---|---|---|---|
| S-0 | **M** | Students migrations `StudyGroupsAndMemberships` + `StudentsParityP1` | owns it | running |
| S-0 | **W** | G-1, G-2 (deploy-first commit), G-3 characterisation tests for journal, schedule, salary, teacher access | no | running |
| F-1 | **FD** | F1.01, F1.02 (deploy-first commit), F3.05 + S6 access | no | running |
| F-1 | **S1** | transaction journal, storno UI, invoice register, void fixes | no | running |
| F-1 | **S4+S5** | finance dashboard, P&L year×month drill-down, cash flow by category, debtor month filter + status bug | no | running |
| F-2 | **MF** | Finance Batch A migration (`expenses.cash_shift_id`, `cash_handovers`, `student_refunds`, `expense_attachments`) + F0.03 — **after M merges**, one migration owner at a time | owns it | queued |
| F-2 | S2 → S3 | cash expenses on the shift, handover, attachments → refunds, subscription end/void, automatic Telegram receipt | yes | queued |
| S-1 | 6 slices | groups + class roster, student list/import/statuses, form/guardians, profile, contracts, rooms — **after M merges** | yes | queued |
| S-2 | C1–C3 | the Group cut-over: schedule/salary/turnstile, journal/attendance/access/chat, reports/portals — then one switch | yes | queued |
| F-3 | S7 | payroll HR-1 + employee Bonus/Jarima — **after the payroll questions are answered** | yes | queued |

At most five build agents run at once.

## 2. Phases

### Phase 0 — Discovery · *running*

Two analyst agents, documentation only, in parallel:

- **Students** → `docs/modules/students-parity.md`
- **Finance** → `docs/modules/finance-parity.md`

Source: the local copy of EduSchool's compiled front-end (`.eduschool-bundle/`, gitignored).
**No request of any kind reaches the live tenant** — `CLAUDE.md`, hard rule. Each document
ends with: a gap list priced per item (P0 / P1 / P2), **one consolidated list of schema
changes**, and **build slices that do not share files**.

### Phase 1 — One migration, plus the migration-free gaps

- **One agent owns one migration** covering every table and column both documents list.
  Two concurrent migrations both regenerate `AppDbContextModelSnapshot.cs`; a badly merged
  snapshot is how an autogenerated migration ends up emitting a `DROP` against a live
  database. `Up()` is read line by line before merge — as with `ParityWave2Schema`.
- **In parallel**, the gaps that need no schema change start immediately, one agent per slice.

### Phase 2 — Feature slices on the new schema

Up to five build agents at once, each in its own worktree, each forbidden to touch shared
files. The orchestrator wires `App.tsx`, `navigation.ts`, `constants.ts`, `AuditService.cs`
in one pass per wave. Five is the ceiling that kept merges trivial in waves 1–2; above it the
merge and shared-file cost grows faster than the throughput.

### Phase 3 — Verification

- Full backend suite (668 today) and frontend build + lint after **every** merge, not per wave.
- `test-migration` on the Phase 1 migration.
- `qa-review` on every money path; `qa-security` before the deploy (global rule: going to
  prod → security review).

### Phase 4 — Deploy

Push, **database backup on the server**, `deploy/deploy.sh`, then verification of migrations,
tables, logs and the parent endpoints. Push and SSH are run by the client — the permission
system blocks them for the agent.

---

## 3. Rules carried into every brief

- **EduSchool is read-only.** Not one request to the live tenant, from any agent.
- **Telegram is the only channel.** An EduSchool SMS action maps to a Telegram message or is
  declined.
- **The ledger is append-only.** Multiple cashboxes, transfers and currency exchange are
  declined; a stored balance, cancel-instead-of-reverse, delete and back-dating are refused
  (`existing-module-gaps.md` §7.3–7.4, `SPEC.md` §4). Where EduSchool does one of those, parity
  means our equivalent — a reversal — not the feature.
- **The Leads board is design-frozen.**
- **Every test class that creates databases clears its pools** — the `53300` lesson.

---

## 4. Group — the one expensive item, and its safe order

**Client answer, 2026-09-17: cross-class teaching groups exist** (e.g. a strong English group
made of 5-A and 5-B pupils). So `Group ≠ Sinf` is in scope. `existing-module-gaps.md` §2.5.3
prices it at ~184 h and lists everything that assumes *one pupil = one class for every lesson*:
the journal cell key, the schedule lesson, attendance, grade reports, teacher salary, class chat
and the Mini App.

It is built in an order where **nothing the school uses today changes until the last step**:

| Step | What | Hours | Can run in parallel with other slices? |
|---|---|---|---|
| A | `class_memberships` backfilled from `students.class_name`; `class_name` kept as a maintained mirror so every existing query keeps working | 24 | **yes** — goes into the Phase 1 migration |
| B | `study_groups`, `study_group_members`; group CRUD, roster, transfer. Groups exist but are not scheduled yet | 30 | **yes** — Phase 2 slice |
| C | Schedule: a lesson can belong to a group; conflict rules see a pupil in a class lesson **and** a group lesson in the same period | 40 | **no** — sequential, owns the schedule files |
| D | Journal and attendance keyed by group as well as class | 36 | **no** — after C |
| E | Reports and salary count a group lesson exactly once | 30 | **no** — after D |
| F | Cut-over and full regression over journal, schedule, reports, salary | 24 | last |

A and B are safe to ship behind the scenes. C–F touch the files every daily screen depends on,
so they run one at a time, each with its own brief and a regression pass, and the cut-over
happens only when all four are green.

---

## 5. Client answers and open questions

1. **"EduSchool bilan birga bir bo'lib ishlaydigan" — ANSWERED 2026-09-17: feature parity.**
   Our system does everything EduSchool does in Students and Finance, then the school switches
   over. No side-by-side sync, no integration track.
2. **Cross-class teaching groups — ANSWERED 2026-09-17: yes.** See §4.

Further questions will come out of the two discovery documents.
