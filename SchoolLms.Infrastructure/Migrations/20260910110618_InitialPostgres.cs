using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absence_reasons",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    @short = table.Column<string>(name: "short", type: "text", nullable: false),
                    is_late = table.Column<bool>(type: "boolean", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_reasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_submissions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    assignment_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    completed = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_at = table.Column<string>(type: "text", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    answer_text = table.Column<string>(type: "text", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    class_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    start_date = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<string>(type: "text", nullable: true),
                    late_accept = table.Column<bool>(type: "boolean", nullable: false),
                    late_penalty_pct = table.Column<int>(type: "integer", nullable: false),
                    max_score = table.Column<int>(type: "integer", nullable: false),
                    auto_grade = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<string>(type: "text", nullable: true),
                    period = table.Column<int>(type: "integer", nullable: true),
                    type_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: true),
                    actor_name = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: false),
                    before = table.Column<string>(type: "text", nullable: true),
                    after = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    teacher_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broadcasts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_name = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    sender_user_id = table.Column<string>(type: "text", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    sent_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcasts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bus_locations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    bus_id = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    speed = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "buses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    plate_number = table.Column<string>(type: "text", nullable: false),
                    driver_name = table.Column<string>(type: "text", nullable: false),
                    driver_phone = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    route = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cameras",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: false),
                    rtsp_url = table.Column<string>(type: "text", nullable: false),
                    rtsp_sub_url = table.Column<string>(type: "text", nullable: false),
                    retention_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cameras", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_name = table.Column<string>(type: "text", nullable: false),
                    sender_user_id = table.Column<string>(type: "text", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    sender_role = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    monthly_fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    room = table.Column<string>(type: "text", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    recipient_key = table.Column<string>(type: "text", nullable: false),
                    recipient_name = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    delivered = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    device_name = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discipline_points",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    reason_id = table.Column<string>(type: "text", nullable: false),
                    reason_name = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discipline_points", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discipline_reasons",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discipline_reasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dishes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    meal = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    ingredients = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dishes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_grades",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    evaluation_type_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<string>(type: "text", nullable: false),
                    week = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_grades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedbacks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    parent_name = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    sender_role = table.Column<string>(type: "text", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedbacks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "finance_transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    month = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finance_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: true),
                    reason_id = table.Column<string>(type: "text", nullable: true),
                    homework = table.Column<int>(type: "integer", nullable: false),
                    behavior = table.Column<int>(type: "integer", nullable: false),
                    mastery = table.Column<int>(type: "integer", nullable: true),
                    sub_group = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lead_stages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_stages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false),
                    birth_date = table.Column<string>(type: "text", nullable: false),
                    parent_full_name = table.Column<string>(type: "text", nullable: false),
                    parent_phone = table.Column<string>(type: "text", nullable: false),
                    target_grade = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    stage = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_notes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    homework = table.Column<string>(type: "text", nullable: true),
                    conducted = table.Column<bool>(type: "boolean", nullable: false),
                    sub_group = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_times",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<string>(type: "text", nullable: false),
                    end_time = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_times", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lms_subjects",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    unlock_mode = table.Column<string>(type: "text", nullable: false),
                    batch_size = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_subjects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monthly_charges",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    date = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_charges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pickup_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    student_name = table.Column<string>(type: "text", nullable: false),
                    class_name = table.Column<string>(type: "text", nullable: false),
                    requested_by_user_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    accepted_at = table.Column<string>(type: "text", nullable: true),
                    accepted_by_teacher_id = table.Column<string>(type: "text", nullable: true),
                    accepted_by_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    audience = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    sender_user_id = table.Column<string>(type: "text", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    sent_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quarter_grades",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quarter_grades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quarters",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<string>(type: "text", nullable: false),
                    end_date = table.Column<string>(type: "text", nullable: false),
                    grades_open = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quarters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_meta",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    current_year = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    director = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    district = table.Column<string>(type: "text", nullable: false),
                    telegram_bot_token = table.Column<string>(type: "text", nullable: false),
                    telegram_bot_username = table.Column<string>(type: "text", nullable: false),
                    telegram_bot_name = table.Column<string>(type: "text", nullable: false),
                    fcm_service_account_json = table.Column<string>(type: "text", nullable: false),
                    fcm_web_config_json = table.Column<string>(type: "text", nullable: false),
                    fcm_vapid_key = table.Column<string>(type: "text", nullable: false),
                    salary_rate_oliy = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_rate1 = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_rate2 = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_rate_mutaxasis = table.Column<decimal>(type: "numeric", nullable: false),
                    turnstile_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    turnstile_vendor = table.Column<string>(type: "text", nullable: false),
                    turnstile_host = table.Column<string>(type: "text", nullable: false),
                    turnstile_port = table.Column<int>(type: "integer", nullable: false),
                    turnstile_username = table.Column<string>(type: "text", nullable: false),
                    turnstile_password = table.Column<string>(type: "text", nullable: false),
                    work_start_time = table.Column<string>(type: "text", nullable: false),
                    late_grace_minutes = table.Column<int>(type: "integer", nullable: false),
                    turnstile_last_sync = table.Column<string>(type: "text", nullable: false),
                    gps_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    gps_ingest_token = table.Column<string>(type: "text", nullable: false),
                    gps_online_minutes = table.Column<int>(type: "integer", nullable: false),
                    gps_stop_radius_m = table.Column<int>(type: "integer", nullable: false),
                    gps_stop_min_minutes = table.Column<int>(type: "integer", nullable: false),
                    camera_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_meta", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_year_archives",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    students_count = table.Column<int>(type: "integer", nullable: false),
                    classes_count = table.Column<int>(type: "integer", nullable: false),
                    journal_count = table.Column<int>(type: "integer", nullable: false),
                    finance_count = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_year_archives", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    middle_name = table.Column<string>(type: "text", nullable: false),
                    birth_date = table.Column<string>(type: "text", nullable: false),
                    birth_certificate_url = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false),
                    parent_full_name = table.Column<string>(type: "text", nullable: false),
                    parent_last_name = table.Column<string>(type: "text", nullable: false),
                    parent_first_name = table.Column<string>(type: "text", nullable: false),
                    parent_middle_name = table.Column<string>(type: "text", nullable: false),
                    parent_phone = table.Column<string>(type: "text", nullable: false),
                    parent_passport_url = table.Column<string>(type: "text", nullable: true),
                    class_name = table.Column<string>(type: "text", nullable: false),
                    enrollment_date = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    discount_pct = table.Column<int>(type: "integer", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_note = table.Column<string>(type: "text", nullable: false),
                    sub_group = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<string>(type: "text", nullable: true),
                    archive_reason = table.Column<string>(type: "text", nullable: true),
                    archived_with_class = table.Column<bool>(type: "boolean", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    location_address = table.Column<string>(type: "text", nullable: true),
                    location_updated_at = table.Column<string>(type: "text", nullable: true),
                    device_user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_students", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_attendances",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    check_in = table.Column<string>(type: "text", nullable: false),
                    check_out = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_attendances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teachers",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    birth_date = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: false),
                    device_user_id = table.Column<string>(type: "text", nullable: false),
                    homeroom_class = table.Column<string>(type: "text", nullable: false),
                    subject_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    bonus_pct = table.Column<decimal>(type: "numeric", nullable: false),
                    salary_start_month = table.Column<string>(type: "text", nullable: false),
                    salary_start_date = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<string>(type: "text", nullable: true),
                    archive_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teachers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "telegram_registrations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_registrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "turnstile_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    device_user_id = table.Column<string>(type: "text", nullable: false),
                    event_at = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    device_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turnstile_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    theme = table.Column<string>(type: "text", nullable: false),
                    notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    notifications_read_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    initial_password = table.Column<string>(type: "text", nullable: true),
                    first_login_at = table.Column<string>(type: "text", nullable: true),
                    last_login_at = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<string>(type: "text", nullable: false),
                    permissions = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "week_assignments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    week = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_week_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_materials",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    assignment_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_materials_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "test_questions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    assignment_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_test_questions_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_modules",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_modules_lms_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "lms_subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_lesson",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    sub_group = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_lesson", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedule_lesson_schedule_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "schedule_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_topics",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    module_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    video_url = table.Column<string>(type: "text", nullable: true),
                    text_content = table.Column<string>(type: "text", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_topics", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_topics_lms_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "lms_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_materials",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    topic_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_materials_lms_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "lms_topics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lms_progresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    topic_id = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lms_progresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_lms_progresses_lms_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "lms_topics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_materials_assignment_id",
                table: "assignment_materials",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_assignment_id_student_id",
                table: "assignment_submissions",
                columns: new[] { "assignment_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submissions_student_id",
                table: "assignment_submissions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_class_id_subject_id_quarter",
                table: "assignments",
                columns: new[] { "class_id", "subject_id", "quarter" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_created_by_user_id",
                table: "assignments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_student_id",
                table: "audit_logs",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_teacher_id",
                table: "audit_logs",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_class_name_created_at",
                table: "broadcasts",
                columns: new[] { "class_name", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_class_name_created_at",
                table: "chat_messages",
                columns: new[] { "class_name", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_templates_target",
                table: "contract_templates",
                column: "target");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_target_recipient_key",
                table: "contracts",
                columns: new[] { "target", "recipient_key" });

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_token",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dishes_date",
                table: "dishes",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_status_created_at",
                table: "feedbacks",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_finance_transactions_date",
                table: "finance_transactions",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_class_id_subject_id_quarter",
                table: "journal_entries",
                columns: new[] { "class_id", "subject_id", "quarter" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_notes_class_id_subject_id_quarter",
                table: "lesson_notes",
                columns: new[] { "class_id", "subject_id", "quarter" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_materials_topic_id",
                table: "lms_materials",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_modules_subject_id_order",
                table: "lms_modules",
                columns: new[] { "subject_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_lms_progresses_student_id",
                table: "lms_progresses",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_progresses_student_id_topic_id",
                table: "lms_progresses",
                columns: new[] { "student_id", "topic_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lms_progresses_topic_id",
                table: "lms_progresses",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_subjects_class_id",
                table: "lms_subjects",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_lms_topics_module_id_order",
                table: "lms_topics",
                columns: new[] { "module_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_monthly_charges_student_id_month",
                table: "monthly_charges",
                columns: new[] { "student_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quarter_grades_class_id_subject_id_quarter_student_id",
                table: "quarter_grades",
                columns: new[] { "class_id", "subject_id", "quarter", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_lesson_template_id",
                table: "schedule_lesson",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_templates_class_id",
                table: "schedule_templates",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_registrations_chat_id",
                table: "telegram_registrations",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_registrations_student_id_chat_id",
                table: "telegram_registrations",
                columns: new[] { "student_id", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_questions_assignment_id",
                table: "test_questions",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_week_assignments_class_id_quarter",
                table: "week_assignments",
                columns: new[] { "class_id", "quarter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_reasons");

            migrationBuilder.DropTable(
                name: "assignment_materials");

            migrationBuilder.DropTable(
                name: "assignment_submissions");

            migrationBuilder.DropTable(
                name: "assignment_types");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "broadcasts");

            migrationBuilder.DropTable(
                name: "bus_locations");

            migrationBuilder.DropTable(
                name: "buses");

            migrationBuilder.DropTable(
                name: "cameras");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "classes");

            migrationBuilder.DropTable(
                name: "contract_templates");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "device_tokens");

            migrationBuilder.DropTable(
                name: "discipline_points");

            migrationBuilder.DropTable(
                name: "discipline_reasons");

            migrationBuilder.DropTable(
                name: "dishes");

            migrationBuilder.DropTable(
                name: "evaluation_grades");

            migrationBuilder.DropTable(
                name: "evaluation_types");

            migrationBuilder.DropTable(
                name: "feedbacks");

            migrationBuilder.DropTable(
                name: "finance_transactions");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "lead_stages");

            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropTable(
                name: "lesson_notes");

            migrationBuilder.DropTable(
                name: "lesson_times");

            migrationBuilder.DropTable(
                name: "lms_materials");

            migrationBuilder.DropTable(
                name: "lms_progresses");

            migrationBuilder.DropTable(
                name: "monthly_charges");

            migrationBuilder.DropTable(
                name: "pickup_requests");

            migrationBuilder.DropTable(
                name: "push_messages");

            migrationBuilder.DropTable(
                name: "quarter_grades");

            migrationBuilder.DropTable(
                name: "quarters");

            migrationBuilder.DropTable(
                name: "schedule_lesson");

            migrationBuilder.DropTable(
                name: "school_meta");

            migrationBuilder.DropTable(
                name: "school_year_archives");

            migrationBuilder.DropTable(
                name: "students");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropTable(
                name: "teacher_attendances");

            migrationBuilder.DropTable(
                name: "teachers");

            migrationBuilder.DropTable(
                name: "telegram_registrations");

            migrationBuilder.DropTable(
                name: "test_questions");

            migrationBuilder.DropTable(
                name: "turnstile_events");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "week_assignments");

            migrationBuilder.DropTable(
                name: "lms_topics");

            migrationBuilder.DropTable(
                name: "schedule_templates");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "lms_modules");

            migrationBuilder.DropTable(
                name: "lms_subjects");
        }
    }
}
