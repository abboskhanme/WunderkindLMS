using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReceiptToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receipt_token",
                table: "payments",
                type: "text",
                nullable: false,
                defaultValueSql: "replace(gen_random_uuid()::text, '-', '')");

            migrationBuilder.CreateIndex(
                name: "ix_payments_receipt_token",
                table: "payments",
                column: "receipt_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_receipt_token",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "receipt_token",
                table: "payments");
        }
    }
}
