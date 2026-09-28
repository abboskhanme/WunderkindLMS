using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HrEmployeePhoneOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "hr_employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows synced without a phone would all become '' and break uq_hr_employee_phone.
            // A per-row placeholder keeps the downgrade possible; it is not a real number.
            migrationBuilder.Sql(
                "UPDATE hr_employees SET phone = '+0' || substr(md5(id), 1, 12) WHERE phone IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "hr_employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
