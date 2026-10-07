using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FeedbackWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "answer",
                table: "feedbacks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_id",
                table: "feedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                table: "feedbacks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                table: "feedbacks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sender_user_id",
                table: "feedbacks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "feedbacks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "feedback_departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_departments", x => x.id);
                    table.CheckConstraint("ck_feedback_departments_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "feedback_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_id = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_events_feedbacks_feedback_id",
                        column: x => x.feedback_id,
                        principalTable: "feedbacks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback_department_members",
                columns: table => new
                {
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_department_members", x => new { x.department_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_feedback_department_members_feedback_departments_department",
                        column: x => x.department_id,
                        principalTable: "feedback_departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feedback_department_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "feedback_departments",
                columns: new[] { "id", "created_at", "is_active", "name", "position" },
                values: new object[,]
                {
                    { new Guid("0f3d1a00-0000-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "O'quv bo'limi", 1 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Moliya", 2 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Ovqatlanish", 3 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Transport", 4 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Yotoqxona", 5 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000006"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "HR", 6 },
                    { new Guid("0f3d1a00-0000-4000-8000-000000000007"), new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Rahbariyat", 7 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_department_id_status",
                table: "feedbacks",
                columns: new[] { "department_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_sender_user_id",
                table: "feedbacks",
                column: "sender_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_department_members_user_id",
                table: "feedback_department_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_departments_name",
                table: "feedback_departments",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_events_feedback_id_created_at",
                table: "feedback_events",
                columns: new[] { "feedback_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_feedbacks_feedback_departments_department_id",
                table: "feedbacks",
                column: "department_id",
                principalTable: "feedback_departments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_feedbacks_feedback_departments_department_id",
                table: "feedbacks");

            migrationBuilder.DropTable(
                name: "feedback_department_members");

            migrationBuilder.DropTable(
                name: "feedback_events");

            migrationBuilder.DropTable(
                name: "feedback_departments");

            migrationBuilder.DropIndex(
                name: "ix_feedbacks_department_id_status",
                table: "feedbacks");

            migrationBuilder.DropIndex(
                name: "ix_feedbacks_sender_user_id",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "answer",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "sender_user_id",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "feedbacks");
        }
    }
}
