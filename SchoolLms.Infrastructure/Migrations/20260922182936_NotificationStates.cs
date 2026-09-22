using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Bildirishnoma holati (mijoz, 2026-09-22): o'qilgani 1 kundan keyin yo'qoladi,
    //  qo'lda (bittalab yoki hammasini belgilab) o'chirish mumkin. Faqat CreateTable;
    //  birorta DROP yo'q, mavjud jadvalga tegilmaydi (qatorma-qator o'qilgan).
    //  Grantlar: notification_states_guards.sql.
    // ===========================================================================
    public partial class NotificationStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_states",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    notification_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dismissed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_states", x => new { x.user_id, x.notification_id });
                    table.ForeignKey(
                        name: "fk_notification_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(MigrationSql.Read("notification_states_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_states");
        }
    }
}
