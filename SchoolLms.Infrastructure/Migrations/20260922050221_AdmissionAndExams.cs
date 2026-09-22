using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Admission, block test and seasonal assessment — the schema of the whole
    //  module. Spec: docs/modules/admission-and-testing.md §5 (data model),
    //  §10 Phase A, unit A3. Every later unit (B1–B5, C1–C6, D1–D4) builds on it.
    // ===========================================================================
    //
    //  TWELVE NEW TABLES (§5.1–§5.12)
    //  ------------------------------
    //  question_banks, questions, question_options, exam_types, exams,
    //  exam_sections, exam_participants, exam_section_scores, exam_invitations,
    //  exam_attempts, exam_answers, seasonal_marks. `text` PKs/FKs and
    //  `timestamp without time zone` (Tashkent wall clock, AppClock.Now), as the
    //  spec prescribes for these non-financial tables. EF orders the CREATE TABLEs
    //  by dependency; the order differs from §10's list but is equally FK-valid.
    //  Every CHECK, partial unique index and FK lives in the EF model
    //  (ExamModel.cs), so a later --autogenerate will not DROP it. The only raw
    //  SQL index is `ux_exam_types_name` (an expression index EF cannot model).
    //
    //  EXISTING TABLES — ADDITIVE ONLY (§5.13)
    //  ---------------------------------------
    //  `leads.admission_status text not null default 'none'` + CHECK + index.
    //  Every existing lead gets 'none' — true: none of them has sat an admission
    //  exam. There is NO `leads.student_id` and the CHECK has NO 'enrolled':
    //  since 2026-09-22 enrolment deletes the lead (LeadConversions migration).
    //  `school_meta.admission_show_answers_to_candidate boolean not null default
    //  false` (§7.7). Both ADD COLUMNs with a constant default are metadata-only
    //  on PostgreSQL 11+ — no table rewrite, the board keeps working.
    //
    //  DEVIATIONS FROM THE SPEC TEXT, ALL DELIBERATE (docs/ASSUMPTIONS.md, 2026-09-22)
    //  ---------------------------------------------------------------------------------
    //  * exam_participants.lead_id is ON DELETE SET NULL, not CASCADE: enrolment
    //    deletes the lead, and the candidate's sitting must survive it. Hence the
    //    pointer CHECK allows a `lead` participant with a NULL lead_id.
    //  * exam_attempts.invitation_id is NO ACTION, not RESTRICT: RESTRICT fires
    //    mid-cascade when a pupil or an exam is deleted (invitation and attempt
    //    both cascade from the participant) and would make that delete fail.
    //  * ck_exams_online_window applies to published/closed exams only, so the
    //    §8.1 "fill time_limit_min at publish" path is possible for a draft.
    //
    //  DATA CHANGE — `teachers.permissions` BACK-FILL (§4.2)
    //  ------------------------------------------------------
    //  "seasonalMarks" is appended to EVERY existing teacher's permission array
    //  (skipped where already present, so re-running is harmless). New teachers
    //  get it from TeacherPermissions.All; existing ones would otherwise never see
    //  the new teacher screen. Down() removes the key again.
    //
    //  NOT FINANCIAL — FULL CRUD FOR `app_rw`, NO REVOKE (exam_guards.sql).
    //
    //  THIS MIGRATION'S Up() CONTAINS NO DROP / ALTER COLUMN — read line by line.
    public partial class AdmissionAndExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "admission_show_answers_to_candidate",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "admission_status",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.CreateTable(
                name: "exam_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_types", x => x.id);
                    table.CheckConstraint("ck_exam_types_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "question_banks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    questions_per_test = table.Column<int>(type: "integer", nullable: true),
                    time_limit_min = table.Column<int>(type: "integer", nullable: true),
                    points_per_correct = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by_user_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_banks", x => x.id);
                    table.CheckConstraint("ck_question_banks_grade", "grade between 0 and 11");
                    table.CheckConstraint("ck_question_banks_points_per_correct", "points_per_correct is null or points_per_correct > 0");
                    table.CheckConstraint("ck_question_banks_questions_per_test", "questions_per_test is null or questions_per_test >= 1");
                    table.CheckConstraint("ck_question_banks_time_limit_min", "time_limit_min is null or time_limit_min between 1 and 600");
                    table.ForeignKey(
                        name: "fk_question_banks_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_question_banks_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "seasonal_marks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    period_kind = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: true),
                    quarter = table.Column<int>(type: "integer", nullable: true),
                    period_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "case period_kind when 'monthly' then 'M:' || year::text || '-' || lpad(month::text, 2, '0') when 'quarterly' then 'Q:' || year::text || '-' || quarter::text else 'Y:' || year::text end", stored: true),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasonal_marks", x => x.id);
                    table.CheckConstraint("ck_seasonal_marks_comment", "comment is null or char_length(btrim(comment)) >= 3");
                    table.CheckConstraint("ck_seasonal_marks_month", "month is null or month between 1 and 12");
                    table.CheckConstraint("ck_seasonal_marks_month_kind", "(period_kind = 'monthly') = (month is not null)");
                    table.CheckConstraint("ck_seasonal_marks_not_empty", "score is not null or comment is not null");
                    table.CheckConstraint("ck_seasonal_marks_period_kind", "period_kind in ('monthly','quarterly','yearly')");
                    table.CheckConstraint("ck_seasonal_marks_quarter", "quarter is null or quarter between 1 and 4");
                    table.CheckConstraint("ck_seasonal_marks_quarter_kind", "(period_kind = 'quarterly') = (quarter is not null)");
                    table.CheckConstraint("ck_seasonal_marks_score", "score is null or (score >= 0 and score <= 100)");
                    table.CheckConstraint("ck_seasonal_marks_year", "year between 2000 and 2100");
                    table.ForeignKey(
                        name: "fk_seasonal_marks_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seasonal_marks_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_seasonal_marks_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seasonal_marks_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    delivery = table.Column<string>(type: "text", nullable: false),
                    exam_type_id = table.Column<string>(type: "text", nullable: true),
                    grade = table.Column<int>(type: "integer", nullable: true),
                    exam_date = table.Column<string>(type: "text", nullable: true),
                    opens_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    closes_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    time_limit_min = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    created_by_user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.id);
                    table.CheckConstraint("ck_exams_delivery", "delivery in ('online','manual')");
                    table.CheckConstraint("ck_exams_grade", "grade is null or grade between 0 and 11");
                    table.CheckConstraint("ck_exams_kind", "kind in ('admission','block')");
                    table.CheckConstraint("ck_exams_online_window", "delivery <> 'online' or status in ('draft','cancelled') or (opens_at is not null and closes_at is not null and time_limit_min is not null)");
                    table.CheckConstraint("ck_exams_status", "status in ('draft','published','closed','cancelled')");
                    table.CheckConstraint("ck_exams_time_limit_min", "time_limit_min is null or time_limit_min >= 1");
                    table.CheckConstraint("ck_exams_title", "btrim(title) <> ''");
                    table.CheckConstraint("ck_exams_window_order", "opens_at is null or closes_at > opens_at");
                    table.ForeignKey(
                        name: "fk_exams_exam_types_exam_type_id",
                        column: x => x.exam_type_id,
                        principalTable: "exam_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_exams_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    bank_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.CheckConstraint("ck_questions_text", "btrim(text) <> ''");
                    table.ForeignKey(
                        name: "fk_questions_question_banks_bank_id",
                        column: x => x.bank_id,
                        principalTable: "question_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_participants",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    exam_id = table.Column<string>(type: "text", nullable: false),
                    participant_kind = table.Column<string>(type: "text", nullable: false),
                    lead_id = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    class_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "assigned"),
                    correct_count = table.Column<int>(type: "integer", nullable: true),
                    question_count = table.Column<int>(type: "integer", nullable: true),
                    total_points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    max_points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    scored_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    scored_by_user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_participants", x => x.id);
                    table.CheckConstraint("ck_exam_participants_kind", "participant_kind in ('lead','student')");
                    table.CheckConstraint("ck_exam_participants_lead_pointer", "participant_kind = 'lead' or lead_id is null");
                    table.CheckConstraint("ck_exam_participants_percent", "percent is null or percent between 0 and 100");
                    table.CheckConstraint("ck_exam_participants_points", "total_points is null or (total_points >= 0 and (max_points is null or total_points <= max_points))");
                    table.CheckConstraint("ck_exam_participants_status", "status in ('assigned','in_progress','finished','absent','cancelled')");
                    table.CheckConstraint("ck_exam_participants_student_pointer", "(participant_kind = 'student') = (student_id is not null)");
                    table.ForeignKey(
                        name: "fk_exam_participants_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_exam_participants_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_participants_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_exam_participants_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_participants_users_scored_by_user_id",
                        column: x => x.scored_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "exam_sections",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    exam_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    bank_id = table.Column<string>(type: "text", nullable: true),
                    question_count = table.Column<int>(type: "integer", nullable: true),
                    points_per_correct = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    max_score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_sections", x => x.id);
                    table.CheckConstraint("ck_exam_sections_max_score", "max_score >= 0");
                    table.CheckConstraint("ck_exam_sections_points_per_correct", "points_per_correct is null or points_per_correct > 0");
                    table.CheckConstraint("ck_exam_sections_question_count", "question_count is null or question_count >= 1");
                    table.ForeignKey(
                        name: "fk_exam_sections_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_sections_question_banks_bank_id",
                        column: x => x.bank_id,
                        principalTable: "question_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exam_sections_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "question_options",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    question_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_options", x => x.id);
                    table.CheckConstraint("ck_question_options_text", "btrim(text) <> ''");
                    table.ForeignKey(
                        name: "fk_question_options_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_invitations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    participant_id = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    token_hint = table.Column<string>(type: "text", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    issued_by_user_id = table.Column<string>(type: "text", nullable: true),
                    first_opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    revoked_by_user_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_invitations", x => x.id);
                    table.CheckConstraint("ck_exam_invitations_window", "valid_until > valid_from");
                    table.ForeignKey(
                        name: "fk_exam_invitations_exam_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "exam_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_invitations_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_exam_invitations_users_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "exam_section_scores",
                columns: table => new
                {
                    participant_id = table.Column<string>(type: "text", nullable: false),
                    section_id = table.Column<string>(type: "text", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: true),
                    question_count = table.Column<int>(type: "integer", nullable: true),
                    points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false, defaultValue: 0m),
                    max_points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_section_scores", x => new { x.participant_id, x.section_id });
                    table.CheckConstraint("ck_exam_section_scores_points", "points >= 0 and points <= max_points");
                    table.ForeignKey(
                        name: "fk_exam_section_scores_exam_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "exam_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_section_scores_exam_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "exam_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_attempts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    participant_id = table.Column<string>(type: "text", nullable: false),
                    invitation_id = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    deadline_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    finish_reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "in_progress"),
                    device_session_hash = table.Column<string>(type: "text", nullable: false),
                    device_label = table.Column<string>(type: "text", nullable: false),
                    first_ip = table.Column<string>(type: "text", nullable: false),
                    user_agent = table.Column<string>(type: "text", nullable: false),
                    answered_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    abuse_flagged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_attempts", x => x.id);
                    table.CheckConstraint("ck_exam_attempts_answered_count", "answered_count >= 0");
                    table.CheckConstraint("ck_exam_attempts_finish_reason", "finish_reason is null or finish_reason in ('manual','timer','admin')");
                    table.CheckConstraint("ck_exam_attempts_finished", "(status = 'finished') = (finished_at is not null) and (finished_at is null) = (finish_reason is null)");
                    table.CheckConstraint("ck_exam_attempts_status", "status in ('in_progress','finished')");
                    table.ForeignKey(
                        name: "fk_exam_attempts_exam_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "exam_invitations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_exam_attempts_exam_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "exam_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_answers",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    question_id = table.Column<string>(type: "text", nullable: false),
                    section_id = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    selected_option_id = table.Column<string>(type: "text", nullable: true),
                    answered_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_answers", x => new { x.attempt_id, x.question_id });
                    table.CheckConstraint("ck_exam_answers_answered", "(selected_option_id is null) = (answered_at is null)");
                    table.ForeignKey(
                        name: "fk_exam_answers_exam_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "exam_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_answers_exam_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "exam_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_answers_question_options_selected_option_id",
                        column: x => x.selected_option_id,
                        principalTable: "question_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exam_answers_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_admission_status",
                table: "leads",
                column: "admission_status");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_admission_status",
                table: "leads",
                sql: "admission_status in ('none','invited','testing','tested','accepted','rejected')");

            migrationBuilder.CreateIndex(
                name: "ix_exam_answers_question_id",
                table: "exam_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_answers_section_id",
                table: "exam_answers",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_answers_selected_option_id",
                table: "exam_answers",
                column: "selected_option_id");

            migrationBuilder.CreateIndex(
                name: "ux_exam_answers_attempt_order",
                table: "exam_answers",
                columns: new[] { "attempt_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_attempts_invitation_id",
                table: "exam_attempts",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_attempts_status_deadline",
                table: "exam_attempts",
                columns: new[] { "status", "deadline_at" });

            migrationBuilder.CreateIndex(
                name: "ux_exam_attempts_participant",
                table: "exam_attempts",
                column: "participant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_invitations_issued_by_user_id",
                table: "exam_invitations",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_invitations_participant",
                table: "exam_invitations",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_invitations_revoked_by_user_id",
                table: "exam_invitations",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_invitations_valid_until",
                table: "exam_invitations",
                column: "valid_until");

            migrationBuilder.CreateIndex(
                name: "ux_exam_invitations_live",
                table: "exam_invitations",
                column: "participant_id",
                unique: true,
                filter: "revoked_at is null");

            migrationBuilder.CreateIndex(
                name: "ux_exam_invitations_token_hash",
                table: "exam_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_participants_class_id",
                table: "exam_participants",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_participants_exam_status",
                table: "exam_participants",
                columns: new[] { "exam_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_participants_lead",
                table: "exam_participants",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_participants_scored_by_user_id",
                table: "exam_participants",
                column: "scored_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_participants_student",
                table: "exam_participants",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_exam_participants_exam_lead",
                table: "exam_participants",
                columns: new[] { "exam_id", "lead_id" },
                unique: true,
                filter: "lead_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_exam_participants_exam_student",
                table: "exam_participants",
                columns: new[] { "exam_id", "student_id" },
                unique: true,
                filter: "student_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_exam_section_scores_section_id",
                table: "exam_section_scores",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_sections_bank_id",
                table: "exam_sections",
                column: "bank_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_sections_exam_order",
                table: "exam_sections",
                columns: new[] { "exam_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_sections_subject_id",
                table: "exam_sections",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_exam_sections_exam_subject",
                table: "exam_sections",
                columns: new[] { "exam_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exams_created_by_user_id",
                table: "exams",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_exam_date",
                table: "exams",
                column: "exam_date");

            migrationBuilder.CreateIndex(
                name: "ix_exams_exam_type_id",
                table: "exams",
                column: "exam_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_kind_status",
                table: "exams",
                columns: new[] { "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_question_banks_created_by_user_id",
                table: "question_banks",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_question_banks_subject_id",
                table: "question_banks",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_question_banks_live",
                table: "question_banks",
                columns: new[] { "grade", "subject_id" },
                unique: true,
                filter: "not is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_question_options_question_order",
                table: "question_options",
                columns: new[] { "question_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ux_question_options_one_correct",
                table: "question_options",
                column: "question_id",
                unique: true,
                filter: "is_correct");

            migrationBuilder.CreateIndex(
                name: "ix_questions_bank_order",
                table: "questions",
                columns: new[] { "bank_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_marks_class_subject_period",
                table: "seasonal_marks",
                columns: new[] { "class_id", "subject_id", "period_key" });

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_marks_created_by_user_id",
                table: "seasonal_marks",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_marks_period_key",
                table: "seasonal_marks",
                column: "period_key");

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_marks_subject_id",
                table: "seasonal_marks",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_seasonal_marks_student_subject_period",
                table: "seasonal_marks",
                columns: new[] { "student_id", "subject_id", "period_key" },
                unique: true);

            // ---- One exam type per name, case- and whitespace-insensitive (§5.4) ----
            // An EXPRESSION index — EF Core cannot model it, so raw SQL (the
            // `ux_surveys_slug` pattern). The snapshot never sees it, so a later
            // --autogenerate never touches it; Down() drops it with the table.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_exam_types_name ON exam_types (lower(btrim(name)));");

            // ---- teachers.permissions back-fill (§4.2) ----
            // Every existing teacher gains the new "seasonalMarks" key; a teacher
            // who already has it is left alone. `permissions` is `text[] not null`.
            migrationBuilder.Sql(
                "UPDATE teachers SET permissions = array_append(permissions, 'seasonalMarks') "
                + "WHERE NOT ('seasonalMarks' = ANY (permissions));");

            // ---- `app_rw` grants ----
            // LAST — after every table exists. What is in it and why (full CRUD,
            // no REVOKE): the header of exam_guards.sql.
            migrationBuilder.Sql(MigrationSql.Read("exam_guards.sql"));
        }

        /// <summary>
        /// Removes exactly what <c>Up()</c> added: the twelve tables (their
        /// indexes — <c>ux_exam_types_name</c> included — and grants go with
        /// them), <c>leads.admission_status</c> with its CHECK and index,
        /// <c>school_meta.admission_show_answers_to_candidate</c>, and the
        /// <c>"seasonalMarks"</c> key from every teacher's permission array.
        ///
        /// <para>
        /// <b>Does rolling back lose data?</b> Only this module's own: question
        /// banks, exams, results, invitations, attempts and seasonal marks, and each
        /// lead's admission status. Nothing that existed before the migration is
        /// touched — leads, teachers and settings keep every other value.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The key means nothing without the module. Removed from every
            // teacher, including those created after Up() (TeacherPermissions.All).
            migrationBuilder.Sql(
                "UPDATE teachers SET permissions = array_remove(permissions, 'seasonalMarks') "
                + "WHERE 'seasonalMarks' = ANY (permissions);");

            migrationBuilder.DropTable(
                name: "exam_answers");

            migrationBuilder.DropTable(
                name: "exam_section_scores");

            migrationBuilder.DropTable(
                name: "seasonal_marks");

            migrationBuilder.DropTable(
                name: "exam_attempts");

            migrationBuilder.DropTable(
                name: "question_options");

            migrationBuilder.DropTable(
                name: "exam_sections");

            migrationBuilder.DropTable(
                name: "exam_invitations");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "exam_participants");

            migrationBuilder.DropTable(
                name: "question_banks");

            migrationBuilder.DropTable(
                name: "exams");

            migrationBuilder.DropTable(
                name: "exam_types");

            migrationBuilder.DropIndex(
                name: "ix_leads_admission_status",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_admission_status",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "admission_show_answers_to_candidate",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "admission_status",
                table: "leads");
        }
    }
}
