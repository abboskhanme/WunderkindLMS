# Wunderkind International School — System Specification

**Version:** 1.0 · **Date:** 2026-09-10 · **Status:** approved for implementation

Engineering specification for evolving the existing SchoolLms platform into the Wunderkind
school management + CRM system. This document is the authoritative source for the data model,
module boundaries, security architecture and delivery plan.

---

## 1. Decisions taken

| Decision | Choice | Rationale |
|---|---|---|
| Build strategy | Evolve existing SchoolLms | ~70% of required functionality already built and deployed |
| Tenancy | Single school | Multi-tenancy was removed in July 2026; not needed |
| Database | **PostgreSQL 17, clean schema** | No production data exists yet — a redesign costs nothing to migrate |
| Student/parent UI | Telegram Mini App **+** web portal | Telegram-first, but not Telegram-only |
| Mini App stack | React + TypeScript, new codebase | Shares types and API client with the admin SPA |
| Cache / queue | Redis for cache only; background work stays in-process | Single school (~500 students) does not justify a job server |
| Native mobile app | **Dropped** | Web + Telegram Mini App covers all roles |

### 1.1 What "clean schema" means in practice

A full rename of all 53 entities would force a rewrite of 8 700 lines of controllers and
3 700 lines of services for no functional gain. The schema is therefore redesigned
**where the requirements demand it**, and preserved where the current shape is already correct:

- **Redesigned:** billing, scheduling, access control, gamification, guardians.
- **Preserved (ported as-is, renamed to `snake_case`):** subjects, classes, journal, grades,
  assignments, LMS, canteen, leads, chat, audit.

This keeps the rewrite to roughly one third of the backend.

---

## 2. Architecture

### 2.1 Technology stack

| Layer | Technology | Notes |
|---|---|---|
| Backend | ASP.NET Core (.NET 10 LTS), C# | Upgrade from .NET 8 during the Postgres work — one disruption, not two |
| ORM | EF Core 10 + Npgsql | Replaces the SQL Server provider |
| Database | PostgreSQL 17 | No size cap (SQL Server Express capped at 10 GB) |
| Cache | Redis 7 | Reference data, dashboards, session-scoped lookups |
| Background work | `IHostedService` / `BackgroundService` | Accrual, reminders, backups, Z-report closing |
| Realtime | SignalR | Chat, live turnstile feed (already built) |
| Admin frontend | React 19 + TypeScript + Vite + Tailwind 4 | Existing, unchanged |
| Parent web portal | Same SPA, `/student` and `/parent` routes | Routes already reserved |
| Telegram Mini App | React 19 + TypeScript, separate Vite app | Served from `/app/` on the same origin |
| Reverse proxy | Caddy | Automatic HTTPS once a domain exists |
| Container platform | Docker Compose | Already in place |

### 2.2 Layering

Clean Architecture is retained: `Server → Infrastructure → Application → Domain`.
Dependencies point inward; `Application` depends on `IAppDbContext`, never on `AppDbContext`.

New rule introduced by this spec: **financial writes never go through a generic repository.**
All money movement passes through `Application/Billing/LedgerService`, which is the only code
permitted to insert into `ledger_entries` and `payments`.

### 2.3 Deployment topology

```
Internet → Caddy (443, auto TLS)
             ├── /            → admin SPA + parent portal   (app container)
             ├── /app/        → Telegram Mini App           (app container, static)
             ├── /teacher/    → teacher PWA                 (app container, static)
             └── /api/        → ASP.NET Core                (app container)
                                    ├── PostgreSQL   (internal only)
                                    └── Redis        (internal only)
```

Only Caddy publishes ports. PostgreSQL and Redis are never exposed.

---

## 3. Data model (PostgreSQL)

Conventions: `snake_case`; primary keys are `uuid` (`gen_random_uuid()`); money is
`numeric(14,2)`; timestamps are `timestamptz`; soft deletes use a `status` column, never a
boolean pair. All tables carry `created_at`, and mutable tables carry `updated_at`.

### 3.1 Identity and access

```sql
create type user_role as enum ('superadmin','admin','cashier','staff','teacher','student','guardian');

create table users (
  id            uuid primary key default gen_random_uuid(),
  login         citext not null unique,          -- username, not e-mail
  full_name     text   not null,
  role          user_role not null,
  password_hash text   not null,                 -- PBKDF2-SHA256, 100k iterations
  is_active     boolean not null default true,
  last_login_at timestamptz,
  created_at    timestamptz not null default now()
);

-- Fine-grained section access for `staff` and `cashier`.
create table user_permissions (
  user_id    uuid not null references users(id) on delete cascade,
  permission text not null,                      -- 'finance', 'students', 'journal', ...
  primary key (user_id, permission)
);
```

`cashier` is promoted from a permission to a **first-class role** because its restrictions are
enforced at the database level (§4), not by an attribute.

### 3.2 People

```sql
create table guardians (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid unique references users(id),
  full_name  text not null,
  phone      text not null,
  passport_url text,
  created_at timestamptz not null default now()
);

create table students (
  id             uuid primary key default gen_random_uuid(),
  user_id        uuid unique references users(id),
  last_name      text not null,
  first_name     text not null,
  middle_name    text,
  birth_date     date not null,
  gender         text not null check (gender in ('male','female')),
  address        text,
  photo_url      text,
  birth_cert_url text,
  class_id       uuid references classes(id),
  enrolled_on    date not null,
  status         text not null default 'active'
                 check (status in ('active','archived','graduated')),
  device_uid     text,                            -- turnstile / Face ID card id
  home_lat       double precision,
  home_lng       double precision,
  created_at     timestamptz not null default now()
);

-- Many-to-many: one guardian may have several children, one child several guardians.
-- This is the main modelling fix versus the current system (one family account per student).
create table student_guardians (
  student_id  uuid not null references students(id) on delete cascade,
  guardian_id uuid not null references guardians(id) on delete cascade,
  relation    text not null default 'parent',     -- parent | grandparent | trustee
  is_primary  boolean not null default false,
  primary key (student_id, guardian_id)
);

create table staff (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid unique references users(id),
  full_name  text not null,
  position   text not null,
  phone      text,
  hired_on   date,
  status     text not null default 'active' check (status in ('active','archived')),
  device_uid text
);

create table teacher_profiles (
  staff_id       uuid primary key references staff(id) on delete cascade,
  category       text,                            -- oliy | 1 | 2 | mutaxasis
  hourly_rate    numeric(14,2) not null default 0,
  monthly_salary numeric(14,2) not null default 0,
  bonus_pct      numeric(5,2)  not null default 0,
  salary_from    date
);

create table teacher_subjects (
  staff_id   uuid not null references staff(id) on delete cascade,
  subject_id uuid not null references subjects(id) on delete cascade,
  primary key (staff_id, subject_id)
);
```

### 3.3 Academic structure, shifts and rooms

```sql
create table academic_years (
  id         uuid primary key default gen_random_uuid(),
  name       text not null,                       -- "2026–2027"
  starts_on  date not null,
  ends_on    date not null,
  is_current boolean not null default false
);

create table terms (
  id               uuid primary key default gen_random_uuid(),
  academic_year_id uuid not null references academic_years(id) on delete cascade,
  index            smallint not null check (index between 1 and 4),
  starts_on        date not null,
  ends_on          date not null,
  grades_open      boolean not null default false,
  unique (academic_year_id, index)
);

-- NEW: shifts. Lesson times are defined per shift, not globally.
create table shifts (
  id        uuid primary key default gen_random_uuid(),
  name      text not null,                        -- "1-smena", "2-smena"
  starts_at time not null,
  ends_at   time not null
);

create table lesson_slots (
  id        uuid primary key default gen_random_uuid(),
  shift_id  uuid not null references shifts(id) on delete cascade,
  period    smallint not null,
  starts_at time not null,
  ends_at   time not null,
  unique (shift_id, period)
);

-- NEW: rooms are a bookable resource, not a text field on the class.
create table rooms (
  id       uuid primary key default gen_random_uuid(),
  name     text not null unique,
  building text,
  floor    smallint,
  capacity smallint not null default 30,
  kind     text not null default 'classroom'      -- classroom | lab | gym | hall
);

create table subjects (
  id         uuid primary key default gen_random_uuid(),
  name       text not null unique,
  short_name text
);

create table classes (
  id                  uuid primary key default gen_random_uuid(),
  academic_year_id    uuid not null references academic_years(id),
  name                text not null,              -- "1-A"
  grade               smallint not null,
  language            text not null default 'uz',
  shift_id            uuid references shifts(id),
  home_room_id        uuid references rooms(id),
  homeroom_teacher_id uuid references staff(id),
  capacity            smallint,
  status              text not null default 'active',
  unique (academic_year_id, name)
);

-- NEW: replaces the fixed 1/2 subgroup. Covers both split groups and level groups.
create table class_groups (
  id         uuid primary key default gen_random_uuid(),
  class_id   uuid not null references classes(id) on delete cascade,
  name       text not null,                       -- "1-guruh", "A-level"
  kind       text not null check (kind in ('subgroup','level')),
  level_code text,                                -- A | B | C ...
  unique (class_id, name)
);

create table class_group_members (
  class_group_id uuid not null references class_groups(id) on delete cascade,
  student_id     uuid not null references students(id) on delete cascade,
  primary key (class_group_id, student_id)
);

create table holidays (
  on_date date primary key,
  name    text not null
);
```

### 3.4 Schedule — conflict-free by construction

The current system checks teacher conflicts in application code. In PostgreSQL the
constraint is moved into the database, so a conflicting row simply cannot be written.

```sql
create table schedule_templates (
  id        uuid primary key default gen_random_uuid(),
  class_id  uuid not null references classes(id) on delete cascade,
  name      text not null,
  is_active boolean not null default true
);

create table schedule_entries (
  id             uuid primary key default gen_random_uuid(),
  template_id    uuid not null references schedule_templates(id) on delete cascade,
  weekday        smallint not null check (weekday between 1 and 6),
  slot_id        uuid not null references lesson_slots(id),
  subject_id     uuid not null references subjects(id),
  teacher_id     uuid not null references staff(id),
  room_id        uuid references rooms(id),
  class_group_id uuid references class_groups(id),   -- null = whole class
  created_at     timestamptz not null default now()
);

-- Three conflicts, three constraints. Only active templates participate.
create unique index schedule_teacher_conflict
  on schedule_entries (teacher_id, weekday, slot_id)
  where teacher_id is not null;

create unique index schedule_room_conflict
  on schedule_entries (room_id, weekday, slot_id)
  where room_id is not null;

create unique index schedule_group_conflict
  on schedule_entries (template_id, weekday, slot_id, coalesce(class_group_id, '00000000-0000-0000-0000-000000000000'::uuid));

-- Which template a class follows in a given week of a term.
create table term_week_templates (
  class_id    uuid not null references classes(id) on delete cascade,
  term_id     uuid not null references terms(id) on delete cascade,
  week_no     smallint not null,
  template_id uuid not null references schedule_templates(id),
  primary key (class_id, term_id, week_no)
);
```

> **Note for implementation:** the unique indexes above assume one active template per class.
> If several templates coexist (alternating weeks), replace them with `EXCLUDE` constraints
> scoped by the week range. Decided in Phase 2.

### 3.5 Journal, attendance, grades

```sql
create table lessons (
  id             uuid primary key default gen_random_uuid(),
  class_id       uuid not null references classes(id),
  class_group_id uuid references class_groups(id),
  subject_id     uuid not null references subjects(id),
  teacher_id     uuid not null references staff(id),
  room_id        uuid references rooms(id),
  term_id        uuid not null references terms(id),
  on_date        date not null,
  slot_id        uuid not null references lesson_slots(id),
  topic          text,
  homework       text,
  conducted      boolean not null default false,
  unique (class_id, subject_id, on_date, slot_id, class_group_id)
);

create table absence_reasons (
  id               uuid primary key default gen_random_uuid(),
  name             text not null,
  short_code       text not null,
  is_late          boolean not null default false,
  discipline_delta smallint not null default 0
);

create type attendance_status as enum ('present','absent','late','excused');

create table attendance (
  id         uuid primary key default gen_random_uuid(),
  lesson_id  uuid not null references lessons(id) on delete cascade,
  student_id uuid not null references students(id) on delete cascade,
  status     attendance_status not null,
  reason_id  uuid references absence_reasons(id),
  note       text,
  marked_by  uuid not null references users(id),
  marked_at  timestamptz not null default now(),
  unique (lesson_id, student_id)
);

create type grade_kind as enum ('daily','homework','behavior','mastery');

create table grades (
  id         uuid primary key default gen_random_uuid(),
  lesson_id  uuid not null references lessons(id) on delete cascade,
  student_id uuid not null references students(id) on delete cascade,
  kind       grade_kind not null default 'daily',
  value      smallint not null check (value between 1 and 5),
  graded_by  uuid not null references users(id),
  graded_at  timestamptz not null default now(),
  unique (lesson_id, student_id, kind)
);

create table term_grades (
  class_id   uuid not null references classes(id),
  subject_id uuid not null references subjects(id),
  term_id    uuid not null references terms(id),
  student_id uuid not null references students(id),
  value      smallint check (value between 1 and 5),
  primary key (class_id, subject_id, term_id, student_id)
);
```

**One-tap class attendance** is an API concern, not a schema one:
`POST /api/teacher/lessons/{id}/attendance/bulk` accepts `{ defaultStatus: 'present', exceptions: [...] }`
and writes all rows in one transaction.

### 3.6 Access control — turnstile, Face ID, cameras

```sql
create table access_devices (
  id        uuid primary key default gen_random_uuid(),
  name      text not null,
  kind      text not null check (kind in ('turnstile','faceid','camera')),
  location  text,
  host      text,
  is_active boolean not null default true
);

-- Append-only. The application role has INSERT and SELECT only (see §4.3).
create table access_events (
  id          bigserial primary key,
  device_id   uuid references access_devices(id),
  person_type text not null check (person_type in ('student','staff','unknown')),
  person_id   uuid,
  raw_uid     text,
  direction   text not null check (direction in ('in','out')),
  happened_at timestamptz not null,
  received_at timestamptz not null default now()
);

create index access_events_person_day
  on access_events (person_type, person_id, happened_at desc);
```

Ingestion endpoint `POST /api/devices/access` accepts batches, is idempotent on
`(device_id, raw_uid, happened_at)` and authenticates with a device API key — never a user JWT.

Daily staff presence (`first_in`, `last_out`, `late_minutes`) is a **materialized view**
refreshed by a background job, not a table.

### 3.7 Billing — multi-category, immutable

This is the core of the new work.

```sql
create table fee_categories (
  id        uuid primary key default gen_random_uuid(),
  code      text not null unique,        -- tuition | bus | dormitory | meals | other
  name      text not null,
  is_active boolean not null default true
);

-- What a given student is signed up for, and for how much.
create table student_subscriptions (
  id             uuid primary key default gen_random_uuid(),
  student_id     uuid not null references students(id) on delete cascade,
  category_id    uuid not null references fee_categories(id),
  monthly_amount numeric(14,2) not null check (monthly_amount >= 0),
  detail         text,                   -- bus route, dormitory room, ...
  starts_on      date not null,
  ends_on        date,
  created_by     uuid not null references users(id),
  created_at     timestamptz not null default now()
);

create table discounts (
  id          uuid primary key default gen_random_uuid(),
  student_id  uuid not null references students(id) on delete cascade,
  category_id uuid references fee_categories(id),   -- null = all categories
  percent     numeric(5,2) not null default 0,
  amount      numeric(14,2) not null default 0,
  reason      text not null,
  starts_on   date not null,
  ends_on     date,
  created_by  uuid not null references users(id),
  approved_by uuid references users(id),            -- required above a threshold (§4.5)
  created_at  timestamptz not null default now()
);

-- Monthly accrual, one row per student per category per month.
create table invoices (
  id           uuid primary key default gen_random_uuid(),
  student_id   uuid not null references students(id),
  category_id  uuid not null references fee_categories(id),
  period_month date not null,                       -- first day of month
  amount       numeric(14,2) not null,              -- gross
  discount     numeric(14,2) not null default 0,
  due_on       date not null,
  status       text not null default 'open'
               check (status in ('open','partial','paid','void')),
  created_at   timestamptz not null default now(),
  unique (student_id, category_id, period_month)
);

-- IMMUTABLE. No UPDATE, no DELETE — see §4.
create table payments (
  id            uuid primary key default gen_random_uuid(),
  receipt_no    bigint not null,
  student_id    uuid not null references students(id),
  amount        numeric(14,2) not null check (amount > 0),
  method        text not null check (method in ('cash','card','transfer')),
  cash_shift_id uuid not null references cash_shifts(id),
  cashier_id    uuid not null references users(id),
  note          text,
  received_at   timestamptz not null default now(),
  reversal_of   uuid references payments(id),       -- storno link
  unique (cash_shift_id, receipt_no)
);

-- THE requirement: one payment split across categories.
create table payment_allocations (
  id          uuid primary key default gen_random_uuid(),
  payment_id  uuid not null references payments(id) on delete restrict,
  invoice_id  uuid not null references invoices(id) on delete restrict,
  amount      numeric(14,2) not null check (amount > 0)
);

-- Cashier shift: opened, closed, counted.
create table cash_shifts (
  id            uuid primary key default gen_random_uuid(),
  cashier_id    uuid not null references users(id),
  opened_at     timestamptz not null default now(),
  closed_at     timestamptz,
  opening_float numeric(14,2) not null default 0,
  expected_cash numeric(14,2),
  counted_cash  numeric(14,2),
  variance      numeric(14,2) generated always as (counted_cash - expected_cash) stored,
  status        text not null default 'open' check (status in ('open','closed')),
  closed_by     uuid references users(id)
);

create table expenses (
  id          uuid primary key default gen_random_uuid(),
  on_date     date not null,
  category    text not null,
  amount      numeric(14,2) not null check (amount > 0),
  note        text,
  created_by  uuid not null references users(id),
  approved_by uuid references users(id),
  created_at  timestamptz not null default now()
);

-- Double-entry journal. Append-only, the single source of financial truth.
create table ledger_entries (
  id          bigserial primary key,
  entry_date  date not null,
  account     text not null,          -- cash | bank | receivable | revenue:tuition | expense:salary ...
  direction   text not null check (direction in ('debit','credit')),
  amount      numeric(14,2) not null check (amount > 0),
  ref_type    text not null,          -- payment | invoice | expense | salary | reversal
  ref_id      uuid,
  memo        text,
  created_by  uuid not null references users(id),
  created_at  timestamptz not null default now(),
  reversal_of bigint references ledger_entries(id)
);

create index ledger_entries_date_account on ledger_entries (entry_date, account);
```

**Allocation invariant** — enforced by a trigger, not by application code:

```sql
create or replace function check_allocation_total() returns trigger as $$
begin
  if (select coalesce(sum(amount),0) from payment_allocations where payment_id = new.payment_id)
     > (select amount from payments where id = new.payment_id) then
    raise exception 'Allocation exceeds payment amount';
  end if;
  return new;
end $$ language plpgsql;

create trigger payment_allocations_total
  after insert or update on payment_allocations
  for each row execute function check_allocation_total();
```

### 3.8 Gamification — points and auction

```sql
create table point_reasons (
  id    uuid primary key default gen_random_uuid(),
  name  text not null,
  delta integer not null,                 -- negative = penalty, positive = reward
  kind  text not null default 'manual'    -- manual | attendance | assessment | auction
);

-- Append-only; balance is derived, never stored as a mutable number.
create table point_transactions (
  id         bigserial primary key,
  student_id uuid not null references students(id) on delete cascade,
  delta      integer not null,
  reason_id  uuid references point_reasons(id),
  source     text not null default 'manual',
  memo       text,
  created_by uuid references users(id),
  created_at timestamptz not null default now()
);

create table auction_items (
  id          uuid primary key default gen_random_uuid(),
  title       text not null,
  description text,
  image_url   text,
  start_price integer not null check (start_price >= 0),
  stock       smallint not null default 1,
  opens_at    timestamptz not null,
  closes_at   timestamptz not null,
  status      text not null default 'draft' check (status in ('draft','open','closed','settled'))
);

create table auction_bids (
  id         bigserial primary key,
  item_id    uuid not null references auction_items(id) on delete cascade,
  student_id uuid not null references students(id) on delete cascade,
  amount     integer not null check (amount > 0),
  placed_at  timestamptz not null default now()
);

create table reward_orders (
  id           uuid primary key default gen_random_uuid(),
  item_id      uuid not null references auction_items(id),
  student_id   uuid not null references students(id),
  points_spent integer not null,
  status       text not null default 'pending'
               check (status in ('pending','delivered','cancelled')),
  created_at   timestamptz not null default now()
);
```

A bid must not exceed the student's derived balance; this is checked in
`GamificationService` inside the same transaction that inserts the bid.

### 3.9 Telegram and notifications

```sql
create table telegram_accounts (
  id               uuid primary key default gen_random_uuid(),
  user_id          uuid not null references users(id) on delete cascade,
  telegram_user_id bigint not null unique,
  username         text,
  linked_at        timestamptz not null default now()
);

create table notification_reads (
  user_id uuid primary key references users(id) on delete cascade,
  read_at timestamptz not null
);
```

The notification feed itself stays **derived** (computed from feedback, pickups, chat,
birthdays and now overdue invoices) — the approach already implemented and working.

### 3.10 Ported unchanged (renamed to snake_case)

`assignments`, `assignment_targets`, `assignment_questions`, `assignment_submissions`,
`courses`, `course_modules`, `course_topics`, `topic_materials`, `topic_progress`,
`dishes`, `menu_entries`, `leads`, `lead_stages`, `lead_activities`, `chat_messages`,
`broadcasts`, `feedback`, `pickup_requests`, `branches`, `buses`, `bus_locations`,
`cameras`, `contract_templates`, `contracts`, `assessment_types`, `assessment_scores`,
`audit_log`, `school_meta`, `user_settings`.

---

## 4. Financial security architecture

The stated risk is a cashier taking cash and deleting the record. Application-level
permission checks are not sufficient, because a bug, a compromised token or a
misconfigured role defeats them. The defence is layered.

### 4.1 Immutability at the database level

> **Correction (verified 2026-09-11).** The application currently connects as `schoollms`,
> which **owns every table**. In PostgreSQL a table owner bypasses `REVOKE` — tested on the
> live server: after `REVOKE DELETE`, the owner still deleted the row. The design below only
> works once the application stops connecting as the owner. This is task P1-02 and must land
> before any ledger code.

Two database roles, strictly separated:

```sql
-- Owner: runs migrations only. Never used by the running application.
-- (this is the existing `schoollms` role)

-- Application: owns nothing, and physically cannot rewrite financial history.
create role app_rw login password '...';
grant usage on schema public to app_rw;
grant select, insert, update, delete on all tables in schema public to app_rw;

revoke update, delete on payments, payment_allocations, ledger_entries,
                          access_events, point_transactions from app_rw;

alter default privileges in schema public
  grant select, insert, update, delete on tables to app_rw;
```

> **Implemented in P1-02 — use `deploy/init-roles.sql`, not the sketch above.** The real
> script differs in three ways that matter, all of them learned by running it:
> 1. There are **three** roles, not two. The existing `schoollms` is not merely the owner,
>    it is the initdb **superuser**, and a superuser bypasses `REVOKE` even more thoroughly
>    than an owner does. A separate non-superuser `schoollms_owner` owns the schema;
>    `schoollms` stays only for `pg_isready`, `pg_dump` and break-glass.
> 2. `alter default privileges` **must** carry `for role schoollms_owner`. Without it the
>    default attaches to whoever runs the statement, and every table a future migration
>    creates comes out invisible to the app.
> 3. The `revoke` has to be **re-run after every migration**, because default privileges
>    hand each newly created table back to `app_rw` with `DELETE` included.
>
> Verified 2026-09-11 on a throwaway stack: `update payments set amount = 1` as `app_rw`
> returns SQLSTATE **42501**, and the row survives.

`ConnectionStrings__Default` uses `app_rw`; migrations run under a separate
`ConnectionStrings__Migrator` using the owner. If the application is ever pointed at the
owner role again the protection silently disappears — so the integration test in P1-22
asserts **both** that `app_rw` gets SQLSTATE 42501 on delete **and** that the owner succeeds.

An erroneous payment is corrected by inserting a **reversal** (`reversal_of`), never by
editing. The original stays visible. This alone removes the class of fraud described.

Migrations run as a separate owner role, not as `app_rw`.

### 4.2 Cashier shift discipline

- A cashier must **open a shift** before accepting payments; every payment carries `cash_shift_id`.
- Receipt numbers are gapless per shift (`unique (cash_shift_id, receipt_no)`).
- Closing a shift requires entering the **counted cash**. `expected_cash` is computed from the
  ledger; `variance` is stored as a generated column and cannot be edited afterwards.
- A shift with a non-zero variance is flagged on the director's dashboard the same day.
- A cashier cannot open a second shift while one is open, and cannot close another's shift.

### 4.3 Role boundaries

| Action | Cashier | Admin | Director (superadmin) |
|---|---|---|---|
| Accept payment, print receipt | ✅ | ✅ | ✅ |
| Reverse a payment | ⛔ | ✅ (reason required) | ✅ |
| Edit or delete a payment | ⛔ **impossible for anyone** | ⛔ | ⛔ |
| Change monthly fee / subscription | ⛔ | ✅ | ✅ |
| Grant a discount | ⛔ | ✅ up to threshold | ✅ any |
| Close own shift | ✅ | ✅ | ✅ |
| See variance report across cashiers | ⛔ | ✅ | ✅ |
| Record an expense | ⛔ | ✅ | ✅ |

### 4.4 Server-derived identity

`cashier_id`, `created_by` and `approved_by` are always taken from the JWT claims on the
server. They are **rejected if present in the request body**. This prevents a cashier from
recording a payment under someone else's name.

### 4.5 Dual control

Actions above a configurable threshold require a second, different approver:

- Payment reversal (any amount)
- Discount above N% or N so'm
- Expense above N so'm
- Manual ledger adjustment (always)

`approved_by <> created_by` is a check constraint, not a convention.

### 4.6 Detection

- Every financial write also lands in `audit_log` with `before`/`after` as `jsonb`.
- Daily Z-report per cashier: opening float, receipts by method, expected vs counted, variance.
- Nightly job flags: shifts closed with variance, reversals within 24 h of the original,
  payments recorded outside the cashier's normal hours, invoices marked paid without allocations.
- The director's dashboard shows an unresolved-variance counter that cannot be dismissed,
  only resolved with a written reason.

### 4.7 Receipts

Receipts are generated server-side as PDF and are also sent to the guardian's Telegram
account. The guardian therefore receives an independent copy of every payment — the
cheapest possible fraud control, because the payer holds evidence the school cannot alter.

---

## 5. Module inventory

| # | Module | State | Phase |
|---|---|---|---|
| 1 | Identity, RBAC, audit | Exists — extend with `cashier` role | 0 |
| 2 | Students, guardians, staff | Exists — add guardian many-to-many | 0 |
| 3 | Classes, subjects, terms, holidays | Exists — add shifts, rooms, groups | 0 / 2 |
| 4 | Journal, attendance, grades | Exists — add bulk attendance endpoint | 0 |
| 5 | **Billing: categories, invoices, payments, ledger** | **New** | 1 |
| 6 | **Cash shifts, Z-report, variance control** | **New** | 1 |
| 7 | P&L, Cash Flow, debtor reports | Partial — rebuild on ledger | 1 / 5 |
| 8 | **Schedule builder: shifts, rooms, groups, conflicts** | Partial — redesign | 2 |
| 9 | Access control (turnstile, Face ID) | Exists — generalise to devices | 2 |
| 10 | **Telegram Mini App** | **New** | 3 |
| 11 | Parent/student web portal | Routes reserved, empty | 3 |
| 12 | **Gamification: points, auction, rewards** | **New** | 4 |
| 13 | Global search (people, not menus) | Missing | 5 |
| 14 | LMS, assignments, canteen, CRM, chat | Exists — port only | 0 |
| 15 | Savdo va marketing: public enrolment form → lead, school news feed | Built 2026-09-21 — `docs/modules/sales-marketing.md` | — |

---

## 6. Delivery roadmap

Estimates assume one full-time backend developer and one frontend developer.

### Phase 0 — Foundation (2 weeks) · *not user-visible*

- PostgreSQL 17 schema per §3, EF Core + Npgsql, single baseline migration.
- Port the 45 SQL-Server-specific raw statements in `Program.cs` into real migrations.
- Redis cache wired for reference data.
- Caddy + HTTPS (requires a domain).
- .NET 10 upgrade.
- Seeder rewritten for the new schema.

**Done when:** every screen that works today works identically on PostgreSQL, with demo data.

### Phase 1 — Money (3 weeks) · **MVP core**

- Fee categories, student subscriptions, monthly accrual per category.
- Payment intake with split allocation across categories.
- Immutable ledger, reversals, database-level `REVOKE`.
- Cash shifts, gapless receipts, Z-report, variance flagging.
- `cashier` role and its restricted UI.
- PDF receipt + Telegram delivery.
- Debtor report, income dynamics, first P&L / Cash Flow.

**Done when:** a cashier can take a payment split across tuition + bus, print a receipt,
and cannot alter or delete it afterwards; the director sees the day's variance.

### Phase 2 — Schedule and access (3 weeks)

- Shifts, room fund, class groups (subgroup and level).
- Conflict-free schedule builder with database constraints and a drag-and-drop UI.
- Generalised access devices; batch ingestion API; staff lateness monitoring.
- One-tap class attendance for teachers.

**Done when:** a full two-shift timetable can be built for all classes with zero teacher
or room collisions, and the turnstile feeds attendance automatically.

### Phase 3 — Telegram Mini App and parent portal (3 weeks)

- Telegram account linking, `initData` validation, Mini App authentication.
- Mini App screens: attendance, schedule, grades and test dynamics, menu, invoices and
  debt, receipts, announcements, pickup request.
- Same screens as `/parent` and `/student` routes in the web SPA.
- Scheduled reminders: payment due, overdue debt, low attendance, birthdays.

**Done when:** a guardian with two children can switch between them in Telegram and see
both children's data, and receives a payment reminder three days before the due date.

### Phase 4 — Gamification (2 weeks)

- Point accounts and append-only transactions, automatic awards from attendance and assessments.
- Auction: items, bidding window, settlement, reward orders.
- Student-facing leaderboard and balance in the Mini App.

### Phase 5 — Reporting and polish (2 weeks)

- Global search across students, guardians, staff and leads.
- Director dashboard: P&L, Cash Flow, collection rate, attendance trend, teacher load.
- Excel/PDF export for every report.
- Performance pass, security audit, end-to-end tests.

**Total: 15 weeks (~3.5 months). MVP (Phases 0–2): 8 weeks.**

---

## 7. Non-functional requirements

| Area | Requirement |
|---|---|
| Security | HTTPS only; JWT with revocation; PBKDF2 password hashing; rate limiting; upload allowlist; CSP/HSTS |
| Financial integrity | Append-only ledger enforced by database grants, not application code |
| Availability | `restart: unless-stopped`; health checks; nightly backup with 7-day retention **plus off-site copy** |
| Backup | Current backups live on the same server — off-site (S3-compatible) copy is required before go-live |
| Performance | Dashboard < 500 ms; journal grid < 1 s for 30 students × 10 lessons |
| Auditability | Every financial and grade write recorded with actor, timestamp, before/after |
| Localisation | UI in Uzbek; code, identifiers and internal docs in English |
| Time | Single `AppClock` (Asia/Tashkent, UTC+5); `timestamptz` in the database |
| Testing | Unit tests for billing arithmetic; integration tests for RBAC and ledger immutability; these are **mandatory** for Phase 1 |

---

## 8. Open questions

Answers are needed before the phase named in brackets.

1. **Domain name** — required for HTTPS and for Telegram Mini App (Telegram refuses plain IP). *(Phase 0)*
2. **Telegram bot** — is there an existing bot and token, or should a new one be registered? *(Phase 0)*
3. **Receipts** — thermal printer (ESC/POS) at the cashier desk, or PDF + Telegram only? *(Phase 1)*
4. **Bus and dormitory pricing** — flat rate, or per route / per room type? *(Phase 1)*
5. **Discount approval threshold** — above what percentage or amount is a second approver required? *(Phase 1)*
6. **Payment due date** — which day of the month, and what grace period before "overdue"? *(Phase 1)*
7. **Face ID hardware** — vendor and model, to confirm the integration protocol. *(Phase 2)*
8. **Auction mechanics** — real bidding with a closing time, or a fixed-price rewards shop? *(Phase 4)*
9. **Go-live date** — which academic period must the system be running for? Drives phase ordering.

---

### 8.1 Client answers (2026-09-11)

| # | Question | Answer | Consequence |
|---|---|---|---|
| 9 | Go-live date | **As soon as Phase 1 ships. Start from zero.** | No opening-balance import. Q17 is closed: existing student debt is *not* carried in. P1-21 loses its riskiest step. |
| 17 | Opening debt at go-live | **Not needed** | — |
| 13 | Payment methods | **Cash · card terminal · bank transfer · Payme/Click/Uzum** | A **plain dropdown on the payment form — no payment-provider integration, in this or any later phase.** `payments.method` is a label for reporting and for shift arithmetic: only `cash` counts toward `expected_cash` at shift close, the other three settle to the bank. The cashier records what the payer used; the system never talks to Payme, Click or a terminal. |
| 6 | Payment due date and overdue rule | **Must be configurable, not fixed** | Two school settings, editable in the UI: `payment_due_day` (default 10) and `overdue_after_day` (default 15). The accrual job and the overdue scan read them; changing either is an `UPDATE`, never a migration. |
| 5 | Discount approval threshold | **Every discount needs the director's approval — no threshold** | `discounts` is not "create and apply". A discount is created `pending` and only affects an invoice once `approved_by` is set by a `superadmin`. `approved_by <> created_by` stays a check constraint. The admin UI must show a pending queue, and the accrual job must ignore unapproved discounts. |
| 14 | One payment, two children | **No — one receipt per student** | `payments.student_id` stays. Schema unchanged. |

## 9. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| No automated tests exist today | A billing bug loses money silently | Tests are a Phase 1 deliverable, not optional |
| Backups are stored on the same server | Server loss = total data loss | Off-site copy in Phase 0 |
| Clean-schema rewrite touches ~1/3 of the backend | Schedule slip | Schema preserved where it already fits (§1.1) |
| Telegram as the primary parent channel | Service block or outage cuts off parents | Web portal built in parallel, same phase |
| 3 GB RAM on the current server | PostgreSQL + Redis + .NET is tight | Measure in Phase 0; 8 GB is the recommended target |
