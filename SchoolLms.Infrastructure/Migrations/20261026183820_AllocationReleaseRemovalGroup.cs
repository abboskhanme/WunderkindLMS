using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllocationReleaseRemovalGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations");

            migrationBuilder.AddColumn<Guid>(
                name: "removal_group_id",
                table: "payment_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_removal_group_id",
                table: "payment_allocations",
                column: "removal_group_id",
                filter: "removal_group_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations",
                sql: "(kind = 'normal' and amount > 0) or (kind = 'release' and amount < 0 and (withdrawal_id is not null or removal_group_id is not null))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_allocations_removal_group_id",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "removal_group_id",
                table: "payment_allocations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations",
                sql: "(kind = 'normal' and amount > 0) or (kind = 'release' and amount < 0 and withdrawal_id is not null)");
        }
    }
}
