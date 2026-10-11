using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BroadcastRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcast_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    broadcast_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcast_recipients", x => x.id);
                    table.CheckConstraint("ck_broadcast_recipients_status", "status in ('sent','failed','no_telegram')");
                    table.ForeignKey(
                        name: "fk_broadcast_recipients_broadcasts_broadcast_id",
                        column: x => x.broadcast_id,
                        principalTable: "broadcasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_recipients_broadcast",
                table: "broadcast_recipients",
                column: "broadcast_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_recipients");
        }
    }
}
