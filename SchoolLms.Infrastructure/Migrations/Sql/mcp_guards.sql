-- ===========================================================================
--  mcp_* — read-only MCP server for AI clients (McpReadOnly migration,
--  docs/modules/mcp-readonly.md).
--
--  app_rw (the web app / OAuth path):
--    mcp_clients, mcp_grants, mcp_auth_codes, mcp_tokens — full CRUD (codes and
--      refresh tokens are consumed, grants revoked, expired rows pruned).
--    mcp_audit — APPEND-ONLY: SELECT + INSERT, no UPDATE/DELETE. What an AI client
--      read cannot be edited away afterwards. The ONLY way rows leave is
--      mcp_prune_audit() below (retention, ≥ 365 days). KEEP IN SYNC with
--      deploy/init-roles.sql step 5, which re-grants full CRUD on ALL TABLES.
--
--  app_ro (the MCP tools' read-only role, created by deploy/init-roles.sql):
--    an explicit ALLOW-LIST, applied by mcp_apply_app_ro_grants() (below). Every table
--    not listed there — including every table a FUTURE migration creates — is closed.
--    Secret columns (password hash, initial password, login, bot/FCM/turnstile/GPS
--    secrets, passport scans) are excluded with column-level grants. init-roles.sql
--    step 5c calls the same function, so there is ONE list.
-- ===========================================================================

CREATE OR REPLACE FUNCTION public.mcp_apply_app_ro_grants() RETURNS void
LANGUAGE plpgsql
SET search_path = public, pg_temp
AS $fn$
DECLARE
    t    text;
    cols text;
    -- Tables the MCP tools (and the query services they reuse) read. Nothing else.
    -- Adding a table here needs a NEW migration that re-creates this function.
    allowed text[] := ARRAY[
        'absence_reasons', 'access_roles', 'assignment_materials', 'assignment_submissions',
        'assignment_types', 'assignments', 'billing_settings', 'boarding_attendance', 'broadcasts',
        'cash_box_transactions', 'cash_boxes', 'cash_handovers', 'cash_shifts',
        'certificate_subjects', 'certificate_types', 'certificates', 'class_memberships', 'classes',
        'daily_attendance_marks', 'debtor_actions', 'debtor_statuses', 'discipline_points',
        'discipline_reasons', 'discounts', 'evaluation_grades', 'evaluation_types',
        'exam_participants', 'exam_section_scores', 'exam_sections', 'exam_types', 'exams',
        'expense_attachments', 'expenses', 'fee_categories', 'holidays', 'invoices',
        'journal_entries', 'lead_conversions', 'lead_stages', 'leads', 'ledger_entries',
        'lesson_notes', 'lesson_times', 'payment_allocations', 'payments', 'payroll_adjustments',
        'push_messages', 'quarter_grades', 'quarters', 'rooms', 'schedule_lesson',
        'schedule_templates', 'seasonal_marks', 'student_archive_reasons', 'student_contracts',
        'student_guardians', 'student_refunds', 'student_statuses', 'student_subscriptions',
        'students', 'study_group_classes', 'study_group_members', 'study_group_teachers',
        'study_groups', 'subjects', 'survey_submissions', 'surveys', 'teachers',
        -- homework quiz questions (assignments): the pupil card's assignment scores load them
        'test_questions', 'transaction_types', 'week_assignments'
    ];
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_ro') THEN
        RETURN;
    END IF;

    -- Start from nothing: whatever was granted before (e.g. an older "SELECT ON ALL TABLES")
    -- is taken back, table AND column level.
    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
        EXECUTE format('REVOKE ALL ON public.%I FROM app_ro', t);
    END LOOP;

    FOREACH t IN ARRAY allowed LOOP
        IF to_regclass('public.' || quote_ident(t)) IS NOT NULL THEN
            EXECUTE format('GRANT SELECT ON public.%I TO app_ro', t);
        END IF;
    END LOOP;

    -- users: names/roles/positions for display. NOT email (= login), password_hash,
    -- initial_password, permissions.
    IF to_regclass('public.users') IS NOT NULL THEN
        SELECT string_agg(quote_ident(column_name), ', ') INTO cols
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'users'
          AND column_name = ANY (ARRAY['id', 'full_name', 'role', 'avatar_url', 'first_login_at',
                'last_login_at', 'position', 'access_role_id', 'phone', 'salary', 'salary_start_date']);
        EXECUTE format('GRANT SELECT (%s) ON public.users TO app_ro', cols);
    END IF;

    -- school_meta: settings flags only. Every secret-bearing column is left out; a NEW
    -- column is closed until listed here (deny-by-default: allow-list of columns).
    IF to_regclass('public.school_meta') IS NOT NULL THEN
        SELECT string_agg(quote_ident(column_name), ', ') INTO cols
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'school_meta'
          AND column_name = ANY (ARRAY['id', 'current_year', 'name', 'director', 'phone', 'address',
                'region', 'district', 'salary_rate_oliy', 'salary_rate1', 'salary_rate2',
                'salary_rate_mutaxasis', 'work_start_time', 'late_grace_minutes',
                'archive_only_non_debtor_students', 'is_student_grade_required',
                'make_attendance_reason_required', 'show_learning_progress_in_parent_dashboard',
                'group_lessons_enabled', 'contract_number_mode']);
        EXECUTE format('GRANT SELECT (%s) ON public.school_meta TO app_ro', cols);
    END IF;

    -- guardians: no passport scan URL.
    IF to_regclass('public.guardians') IS NOT NULL THEN
        SELECT string_agg(quote_ident(column_name), ', ') INTO cols
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'guardians'
          AND column_name = ANY (ARRAY['id', 'user_id', 'full_name', 'phone', 'phone_key', 'created_at']);
        EXECUTE format('GRANT SELECT (%s) ON public.guardians TO app_ro', cols);
    END IF;
END
$fn$;

REVOKE ALL ON FUNCTION public.mcp_apply_app_ro_grants() FROM PUBLIC;

-- Retention: the only way rows leave the append-only audit. SECURITY DEFINER (runs as the
-- table owner) but it can never delete anything younger than 365 days, whatever it is passed.
CREATE OR REPLACE FUNCTION public.mcp_prune_audit(keep_days integer DEFAULT 365) RETURNS integer
LANGUAGE sql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $fn$
    WITH gone AS (
        DELETE FROM public.mcp_audit
        WHERE at < now() - make_interval(days => greatest(coalesce(keep_days, 365), 365))
        RETURNING 1)
    SELECT count(*)::integer FROM gone;
$fn$;

REVOKE ALL ON FUNCTION public.mcp_prune_audit(integer) FROM PUBLIC;

DO $guards$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.mcp_clients, public.mcp_grants, '
             || 'public.mcp_auth_codes, public.mcp_tokens TO app_rw';
        EXECUTE 'GRANT SELECT, INSERT ON public.mcp_audit TO app_rw';
        EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON public.mcp_audit FROM app_rw';
        EXECUTE 'GRANT USAGE, SELECT ON SEQUENCE public.mcp_audit_id_seq TO app_rw';
        EXECUTE 'GRANT EXECUTE ON FUNCTION public.mcp_prune_audit(integer) TO app_rw';
    ELSE
        RAISE NOTICE '[mcp_guards] `app_rw` role not found — GRANT skipped. Run deploy/init-roles.sql.';
    END IF;
END
$guards$;

SELECT public.mcp_apply_app_ro_grants();
