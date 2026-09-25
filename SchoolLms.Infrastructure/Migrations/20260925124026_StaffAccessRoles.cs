using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffAccessRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "access_role_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "access_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_access_role_id",
                table: "users",
                column: "access_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_name",
                table: "access_roles",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_users_access_roles_access_role_id",
                table: "users",
                column: "access_role_id",
                principalTable: "access_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_access_roles_access_role_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_users_access_role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_role_id",
                table: "users");
        }
    }
}
