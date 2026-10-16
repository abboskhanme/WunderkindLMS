-- ===========================================================================
--  `student_events` from the data that existed before it (backfill)
--  Migration: AuditStudentFlowAndShop
--  Source: docs/modules/student-flow.md §4
-- ===========================================================================
--
--  DO NOT EDIT AFTER RELEASE. A migration runs once and then freezes; a change
--  needs a new migration and a new .sql file (the billing_guards.sql rule).
--
--  WHAT IT WRITES (every row: source = 'backfill')
--  -----------------------------------------------
--   1. ARRIVALS — one per pupil: `students.enrollment_date`, else the first
--      class membership's `joined_on`; a pupil with neither gets no row. Class:
--      that first membership's, else the pupil's class now.
--   2. CURRENT DEPARTURES — one per archived pupil, on `archived_at` (today
--      when unreadable). Class: the closed membership nearest that day, else
--      `students.class_name` (graduates keep it). Reason: the catalog id; the
--      graduation text "Bitirdi" → the catalog's "Maktabni bitirdi" row.
--      Who: the archive audit row of that day, when there is one.
--   3. EARLIER DEPARTURES — archive audit rows ("O'quvchi arxivga ko'chirildi",
--      "O'quvchi ommaviy arxivlandi") of pupils who still exist, on the row's
--      day; the note is the quoted text of the summary, no catalog reason.
--   4. RETURNS — restore audit rows ("O'quvchi arxivdan qaytarildi") of pupils
--      who still exist. Class unknown (a single restore never gave one back).
--   5. DELETIONS — "StudentDelete" audit rows of pupils who are gone and were
--      not archived when deleted (an archived pupil had already left), with the
--      name and class kept in the row's `before`.
--
--  NOT RECOVERABLE: pupils deleted one by one before 2026-10-15 (that path
--  wrote nothing), class restores (one audit row per class, none per pupil),
--  and catalog reasons of earlier departures.
--
--  DATES ARE ISO TEXT — READ CAREFULLY
--  -----------------------------------
--  `enrollment_date` / `archived_at` / `audit_logs.timestamp` are `text`; an
--  empty or malformed value must not fail the migration ('2025-02-30'::date
--  would). `pg_temp.se_day` returns null for anything it cannot read.
--  "Today" is the school's day (Asia/Tashkent), like AppClock.Today.
--
--  RE-RUN SAFE
--  -----------
--  Every insert skips a (pupil, kind, day) that already has a row (arrivals:
--  any arrival of the pupil), and DISTINCT ON keeps one row per (pupil, day)
--  inside one statement — a second run inserts nothing.
-- ===========================================================================

create or replace function pg_temp.se_day(v text) returns date
language plpgsql immutable as $fn$
begin
    if v is null or v !~ '^(19|20)[0-9]{2}-[0-9]{2}-[0-9]{2}' then
        return null;
    end if;
    return left(v, 10)::date;
exception when others then
    return null;
end
$fn$;


-- ---------------------------------------------------------------------------
-- 1) Arrivals
-- ---------------------------------------------------------------------------
insert into student_events (id, student_id, student_name, kind, on_date, class_id, class_name, source, created_at)
select gen_random_uuid(), s.id, left(s.full_name, 300), 'enrolled',
       coalesce(pg_temp.se_day(s.enrollment_date), fm.joined_on),
       coalesce(fc.id, cn.id),
       left(coalesce(fc.name, nullif(btrim(s.class_name), '')), 100),
       'backfill', now()
from students s
left join lateral (
    select m.class_id, m.joined_on
    from class_memberships m
    where m.student_id = s.id
    order by m.joined_on, m.created_at
    limit 1
) fm on true
left join classes fc on fc.id = fm.class_id
left join lateral (
    select c.id from classes c where c.name = s.class_name order by c.id limit 1
) cn on fc.id is null
where coalesce(pg_temp.se_day(s.enrollment_date), fm.joined_on) is not null
  and not exists (
      select 1 from student_events e where e.student_id = s.id and e.kind = 'enrolled');


-- ---------------------------------------------------------------------------
-- 2) Current departures — archived pupils
-- ---------------------------------------------------------------------------
insert into student_events (id, student_id, student_name, kind, on_date, class_id, class_name,
                            reason_id, reason_text, source, actor_id, actor_name, created_at)
select gen_random_uuid(), s.id, left(s.full_name, 300), 'left', d.day,
       coalesce(lc.id, cn.id),
       left(coalesce(lc.name, nullif(btrim(s.class_name), '')), 100),
       coalesce(s.archive_reason_id, case when btrim(s.archive_reason) = 'Bitirdi' then grad.id end),
       left(nullif(btrim(s.archive_reason), ''), 500),
       'backfill', a.actor_id, left(a.actor_name, 300), now()
from students s
cross join lateral (
    select coalesce(pg_temp.se_day(s.archived_at), (now() at time zone 'Asia/Tashkent')::date) as day
) d
left join lateral (
    select m.class_id
    from class_memberships m
    where m.student_id = s.id and m.left_on is not null
    order by abs(m.left_on - d.day), m.left_on desc, m.created_at desc
    limit 1
) lm on true
left join classes lc on lc.id = lm.class_id
left join lateral (
    select c.id from classes c where c.name = s.class_name order by c.id limit 1
) cn on lc.id is null
left join lateral (
    select r.id
    from student_archive_reasons r
    where r.id = '00000000-0000-0000-0000-0000000000a1' or r.name = 'Maktabni bitirdi'
    order by (r.id = '00000000-0000-0000-0000-0000000000a1') desc
    limit 1
) grad on true
left join lateral (
    select al.actor_id, al.actor_name
    from audit_logs al
    where al.student_id = s.id
      and al.entity_type in ('StudentDiscount', 'Student')
      and (al.summary like 'O''quvchi arxivga ko''chirildi%' or al.summary like 'O''quvchi ommaviy arxivlandi%')
      and pg_temp.se_day(al.timestamp) = d.day
    order by al.timestamp desc
    limit 1
) a on true
where s.is_archived
  and not exists (
      select 1 from student_events e
      where e.student_id = s.id and e.kind = 'left' and e.on_date = d.day);


-- ---------------------------------------------------------------------------
-- 3) Earlier departures — archive audit rows of pupils who still exist
-- ---------------------------------------------------------------------------
insert into student_events (id, student_id, student_name, kind, on_date, class_id, class_name,
                            reason_text, source, actor_id, actor_name, created_at)
select distinct on (s.id, pg_temp.se_day(al.timestamp))
       gen_random_uuid(), s.id, left(s.full_name, 300), 'left', pg_temp.se_day(al.timestamp),
       lc.id, left(lc.name, 100),
       left(nullif(btrim(substring(al.summary from '"(.*)"')), ''), 500),
       'backfill', al.actor_id, left(al.actor_name, 300), now()
from audit_logs al
join students s on s.id = al.student_id
left join lateral (
    select m.class_id
    from class_memberships m
    where m.student_id = s.id and m.left_on = pg_temp.se_day(al.timestamp)
    order by m.created_at desc
    limit 1
) lm on true
left join classes lc on lc.id = lm.class_id
where al.entity_type = 'StudentDiscount'
  and (al.summary like 'O''quvchi arxivga ko''chirildi%' or al.summary like 'O''quvchi ommaviy arxivlandi%')
  and pg_temp.se_day(al.timestamp) is not null
  and not exists (
      select 1 from student_events e
      where e.student_id = s.id and e.kind = 'left' and e.on_date = pg_temp.se_day(al.timestamp))
order by s.id, pg_temp.se_day(al.timestamp), al.timestamp desc;


-- ---------------------------------------------------------------------------
-- 4) Returns — restore audit rows of pupils who still exist
-- ---------------------------------------------------------------------------
insert into student_events (id, student_id, student_name, kind, on_date, source, actor_id, actor_name, created_at)
select distinct on (s.id, pg_temp.se_day(al.timestamp))
       gen_random_uuid(), s.id, left(s.full_name, 300), 'returned', pg_temp.se_day(al.timestamp),
       'backfill', al.actor_id, left(al.actor_name, 300), now()
from audit_logs al
join students s on s.id = al.student_id
where al.entity_type = 'StudentDiscount'
  and al.summary like 'O''quvchi arxivdan qaytarildi%'
  and pg_temp.se_day(al.timestamp) is not null
  and not exists (
      select 1 from student_events e
      where e.student_id = s.id and e.kind = 'returned' and e.on_date = pg_temp.se_day(al.timestamp))
order by s.id, pg_temp.se_day(al.timestamp), al.timestamp desc;


-- ---------------------------------------------------------------------------
-- 5) Deletions — pupils deleted for good while active
-- ---------------------------------------------------------------------------
insert into student_events (id, student_id, student_name, kind, on_date, class_id, class_name,
                            reason_text, source, actor_id, actor_name, created_at)
select distinct on (al.student_id, pg_temp.se_day(al.timestamp))
       gen_random_uuid(), al.student_id,
       left(coalesce(nullif(al.before ->> 'FullName', ''), '?'), 300),
       'left', pg_temp.se_day(al.timestamp),
       cn.id, left(nullif(btrim(al.before ->> 'ClassName'), ''), 100),
       'O''chirildi', 'backfill', al.actor_id, left(al.actor_name, 300), now()
from audit_logs al
left join lateral (
    select c.id from classes c where c.name = al.before ->> 'ClassName' order by c.id limit 1
) cn on true
where al.entity_type = 'StudentDelete'
  and al.student_id is not null
  and coalesce(al.before ->> 'IsArchived', 'false') <> 'true'
  and pg_temp.se_day(al.timestamp) is not null
  and not exists (select 1 from students s where s.id = al.student_id)
  and not exists (
      select 1 from student_events e
      where e.student_id = al.student_id and e.kind = 'left' and e.on_date = pg_temp.se_day(al.timestamp))
order by al.student_id, pg_temp.se_day(al.timestamp), al.timestamp desc;
