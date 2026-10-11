using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BellSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bell_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bell_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bell_schedule_owners",
                columns: table => new
                {
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bell_schedule_owners", x => x.owner_id);
                    table.ForeignKey(
                        name: "fk_bell_schedule_owners_bell_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "bell_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bell_schedule_periods",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    end_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bell_schedule_periods", x => new { x.schedule_id, x.period });
                    table.CheckConstraint("ck_bell_schedule_periods_period", "period between 1 and 20");
                    table.ForeignKey(
                        name: "fk_bell_schedule_periods_bell_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "bell_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bell_schedule_owners_schedule",
                table: "bell_schedule_owners",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ux_bell_schedules_name",
                table: "bell_schedules",
                column: "name",
                unique: true);

            // app_ro: the MCP timetable tool reads lesson times per owner (bell_* tables).
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bell_schedule_owners");

            migrationBuilder.DropTable(
                name: "bell_schedule_periods");

            migrationBuilder.DropTable(
                name: "bell_schedules");
        }
    }
}
