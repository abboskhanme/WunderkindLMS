-- ===========================================================================
--  SchoolLms — database role separation (SPEC §4.1, task P1-02)
-- ===========================================================================
--
--  WHY THIS FILE EXISTS
--  --------------------
--  The application used to connect as `schoollms`, the role that OWNS every
--  table (and is a superuser, because it is the role initdb created).
--  In PostgreSQL a table owner BYPASSES `REVOKE` — verified on the live
--  server: after `REVOKE DELETE ... FROM schoollms` the row was still
--  deleted. So the "a cashier cannot delete a payment" guarantee in SPEC §4.1
--  was worth exactly nothing.
--
--  This script creates the separation that makes it real:
--
--    schoollms         bootstrap superuser (POSTGRES_USER, made by initdb).
--                      Kept for pg_isready, pg_dump and break-glass access.
--                      The application MUST NOT use it.
--    schoollms_owner   owns schema `public` and every table in it.
--                      Used ONLY by ConnectionStrings:Migrator.
--    app_rw            LOGIN, NOINHERIT, OWNS NOTHING.
--                      Used by ConnectionStrings:Default — i.e. the app.
--                      Because it owns nothing, REVOKE actually binds.
--
--  WHEN IT RUNS
--  ------------
--   1. Automatically on a brand-new database — it is mounted into
--      /docker-entrypoint-initdb.d/ (runs before any migration).
--   2. MANUALLY on databases that already exist (local + server both have
--      demo data). See deploy/README.md § "Mavjud bazaga rollarni qo'llash".
--   3. MANUALLY AFTER EVERY MIGRATION that adds tables. New tables inherit
--      privileges from ALTER DEFAULT PRIVILEGES below, which means a freshly
--      created `payments` table WOULD hand app_rw the DELETE right. Re-running
--      this script takes it back. This is a required deploy step, not an
--      optional one.
--
--  It is IDEMPOTENT: running it ten times in a row changes nothing after the
--  first and raises no error.
--
--  ROLLBACK
--  --------
--  Point ConnectionStrings__Default back at the owner (or at `schoollms`) and
--  restart the backend. The roles can stay; they do no harm while unused.
--  To remove them completely:
--      REASSIGN OWNED BY schoollms_owner TO schoollms;
--      DROP OWNED BY app_rw;  DROP ROLE app_rw;  DROP ROLE schoollms_owner;
-- ===========================================================================

\set ON_ERROR_STOP on

-- Passwords come from the container environment, never from this file.
-- They are set on the `database` service in docker-compose.yml, which reads
-- them from .env (see .env.example).
-- Pre-set to empty so an unset environment variable produces the readable
-- error below instead of a psql syntax error.
\set app_pw ''
\set owner_pw ''
\getenv app_pw APP_DB_PASSWORD
\getenv owner_pw MIGRATOR_DB_PASSWORD

-- Fail loudly instead of silently creating passwordless roles.
SELECT CASE
    WHEN :'app_pw' = '' OR :'owner_pw' = ''
    THEN 'DO $guard$ BEGIN RAISE EXCEPTION ''APP_DB_PASSWORD / MIGRATOR_DB_PASSWORD not set - init-roles.sql stopped''; END $guard$;'
    ELSE 'SELECT 1 AS passwords_present'
END
\gexec


-- ---------------------------------------------------------------------------
-- 1) Roles. CREATE on first run, ALTER (password refresh) afterwards.
--    \gexec is used because CREATE ROLE cannot take a bound parameter and
--    psql does not interpolate :'vars' inside dollar-quoted blocks.
-- ---------------------------------------------------------------------------
SELECT format('%s ROLE schoollms_owner LOGIN PASSWORD %L',
              CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'schoollms_owner')
                   THEN 'ALTER' ELSE 'CREATE' END,
              :'owner_pw')
\gexec

SELECT format('%s ROLE app_rw LOGIN PASSWORD %L',
              CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw')
                   THEN 'ALTER' ELSE 'CREATE' END,
              :'app_pw')
\gexec

-- Attributes are set separately so they are re-asserted on every run — if
-- somebody hand-grants SUPERUSER to app_rw in an incident, the next deploy
-- takes it away again.
ALTER ROLE schoollms_owner NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT NOBYPASSRLS;
ALTER ROLE app_rw          NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT NOBYPASSRLS;

-- app_rw owns nothing and must never be able to become the owner.
-- (Membership would let it `SET ROLE schoollms_owner` and bypass everything.)
SELECT format('REVOKE schoollms_owner FROM app_rw')
WHERE pg_has_role('app_rw', 'schoollms_owner', 'MEMBER')
\gexec


-- ---------------------------------------------------------------------------
-- 2) Database + schema ownership.
-- ---------------------------------------------------------------------------
SELECT format('GRANT CONNECT ON DATABASE %I TO schoollms_owner, app_rw', current_database())
\gexec

ALTER SCHEMA public OWNER TO schoollms_owner;

-- Nobody creates objects in `public` except the owner. (PostgreSQL 15+ already
-- defaults to this; restated so the guarantee does not depend on the version.)
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM app_rw;
GRANT  USAGE  ON SCHEMA public TO   app_rw;


-- ---------------------------------------------------------------------------
-- 3) Hand existing objects over to schoollms_owner.
--    On a fresh database this loop matches nothing (no tables yet — migrations
--    have not run). On the existing local/server databases it moves all 54
--    tables off the superuser. That is the whole point of the task.
-- ---------------------------------------------------------------------------
SELECT format('ALTER TABLE %I.%I OWNER TO schoollms_owner', schemaname, tablename)
FROM pg_tables
WHERE schemaname = 'public' AND tableowner <> 'schoollms_owner'
\gexec

SELECT format('ALTER SEQUENCE %I.%I OWNER TO schoollms_owner', schemaname, sequencename)
FROM pg_sequences
WHERE schemaname = 'public' AND sequenceowner <> 'schoollms_owner'
\gexec

SELECT format('ALTER VIEW %I.%I OWNER TO schoollms_owner', schemaname, viewname)
FROM pg_views
WHERE schemaname = 'public' AND viewowner <> 'schoollms_owner'
\gexec

SELECT format('ALTER MATERIALIZED VIEW %I.%I OWNER TO schoollms_owner', schemaname, matviewname)
FROM pg_matviews
WHERE schemaname = 'public' AND matviewowner <> 'schoollms_owner'
\gexec


-- ---------------------------------------------------------------------------
-- 4) app_rw privileges — ordinary read/write on everything.
--    The app really does write: seeding, the journal, attendance, chat. Only
--    the financial tables in step 5 are narrowed.
-- ---------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA public TO app_rw;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA public TO app_rw;

-- Tables created LATER by the migrator get the same rights automatically.
-- `FOR ROLE schoollms_owner` is load-bearing: without it the default applies
-- to objects created by whoever ran this script (the superuser), and every
-- table a migration creates would come out unreadable to the app.
ALTER DEFAULT PRIVILEGES FOR ROLE schoollms_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_rw;
ALTER DEFAULT PRIVILEGES FOR ROLE schoollms_owner IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_rw;


-- ---------------------------------------------------------------------------
-- 5) Financial immutability — SPEC §4.1.
--    These tables do not exist yet; they arrive in P1-04. The list is written
--    now so that the moment the migration creates them, the very next run of
--    this script locks them. Tables that do not exist are skipped silently,
--    which is what makes the script safe to run at any point in time.
--
--    UPDATE and DELETE are removed. INSERT stays: a wrong payment is fixed by
--    inserting a reversal (`reversal_of`), never by editing history.
--    `access_events` and `point_transactions` are append-only logs.
--
--    KEEP THIS LIST IN SYNC WITH THE MIGRATION GUARDS. Every table whose
--    migration narrows app_rw (`Migrations/Sql/*_guards.sql`) must appear
--    here too, because step 4 above hands full CRUD back to app_rw on ALL
--    TABLES every time this script runs. A table that is revoked only in its
--    migration is silently un-revoked by the next deploy, and nothing fails:
--    the app keeps working, only the money protection is gone. That was the
--    `finance_anomaly_flags` defect (finance-parity.md F0.03) — the flag
--    table's column lock survived exactly until the next run of this file.
-- ---------------------------------------------------------------------------
SELECT format('REVOKE UPDATE, DELETE ON public.%I FROM app_rw', t.name)
FROM (VALUES
        ('payments'),
        ('payment_allocations'),
        ('ledger_entries'),
        ('access_events'),
        ('point_transactions'),
        -- SPEC §4.6 — a flag is closed with a written reason, never deleted.
        -- Column-level UPDATE is restored in step 5b. (F0.03)
        ('finance_anomaly_flags'),
        -- finance-parity.md §3.1 A2 — cash leaving the drawer. Append-only;
        -- a wrong handover is corrected with a `reversal_of` row.
        ('cash_handovers'),
        -- finance-parity.md §3.1 A3 — money leaving the school. Append-only;
        -- the four decision columns are restored in step 5b.
        ('student_refunds'),
        -- finance-parity.md §3.1 A4 — the evidence behind an expense.
        -- Replacing or deleting it is the fraud SPEC §4 describes; a wrong
        -- upload is superseded by a new row, never edited away.
        ('expense_attachments'),
        -- finance-parity.md §3.2 B3 (F11.01) — a bonus/penalty entry that
        -- reaches an employee's pay. Append-only; a wrong entry is corrected
        -- with a `reversal_of` row, never edited. `adjustment_reasons` (B2)
        -- is NOT in this list on purpose: it is an ordinary catalogue with
        -- no money in it, so it keeps the full CRUD step 4 already grants.
        ('payroll_adjustments')
     ) AS t(name)
WHERE to_regclass('public.' || quote_ident(t.name)) IS NOT NULL
\gexec


-- ---------------------------------------------------------------------------
-- 5b) Column-level UPDATE for the two tables that have a decision flow.
--
--     ORDER IS LOAD-BEARING and this block must stay AFTER step 5. In
--     PostgreSQL, revoking a privilege at table level also revokes the
--     matching column-level privileges on every column of that table. So the
--     step-5 `REVOKE UPDATE` above wipes the column grants that
--     `anomaly_guards.sql` and `finance_parity_guards.sql` installed, and
--     they have to be re-issued here. Put this block first and the revoke
--     would take them away again — silently, leaving a database where nobody
--     can resolve a flag or approve a refund.
--
--     The columns are the ONLY writable ones: a flag's text, amount or kind
--     and a refund's amount, method, reason or requester can never be
--     "corrected" after the fact. On `student_refunds` a database trigger
--     (`student_refunds_locked`) narrows it further — each of those four is
--     written once, and a decision is final.
-- ---------------------------------------------------------------------------
SELECT format('GRANT UPDATE (%s) ON public.%I TO app_rw', t.columns, t.name)
FROM (VALUES
        ('finance_anomaly_flags', 'resolved_at, resolved_by, resolved_reason'),
        ('student_refunds', 'approved_by, approved_at, cash_shift_id, rejected_reason')
     ) AS t(name, columns)
WHERE to_regclass('public.' || quote_ident(t.name)) IS NOT NULL
\gexec


-- ---------------------------------------------------------------------------
-- 6) Report what was done — this output is the deploy evidence.
-- ---------------------------------------------------------------------------
-- `owns_in_public` is the number that matters: app_rw MUST show 0, for ever.
-- The moment it shows anything else, REVOKE stops binding and SPEC §4.1 is void.
-- (Only schema `public` is counted — the bootstrap superuser owns every pg_catalog
-- object by definition, and counting those would just look alarming at 3am.)
SELECT r.rolname AS role,
       r.rolsuper AS superuser,
       r.rolcanlogin AS login,
       r.rolinherit AS inherit,
       (SELECT count(*)
          FROM pg_class c
          JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE c.relowner = r.oid
           AND n.nspname = 'public'
           AND c.relkind IN ('r','p','S','v','m')) AS owns_in_public
FROM pg_roles r
WHERE r.rolname IN ('schoollms', 'schoollms_owner', 'app_rw')
ORDER BY r.rolname;
