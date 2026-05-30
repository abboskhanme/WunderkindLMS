using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Permissions",
                table: "Teachers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            // Mavjud o'qituvchilarga barcha bo'limlar ruxsatini beramiz (orqaga moslik —
            // ular ilgari to'liq foydalana olgan). Admin keyin cheklashi mumkin.
            migrationBuilder.Sql(
                "UPDATE [Teachers] SET [Permissions] = '[\"journal\",\"assignments\",\"schedule\",\"messages\",\"salary\"]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "Teachers");
        }
    }
}
