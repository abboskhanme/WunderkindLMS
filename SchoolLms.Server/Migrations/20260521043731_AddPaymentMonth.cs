using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Month",
                table: "FinanceTransactions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Month",
                table: "FinanceTransactions");
        }
    }
}
