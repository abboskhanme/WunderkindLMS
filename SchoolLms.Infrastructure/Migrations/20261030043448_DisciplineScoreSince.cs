using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Xulq-atvor balli qaysi kundan hisoblanadi (mijoz, 2026-10-30: yangi o'quv yili — hamma 100 dan):
    /// `school_meta.discipline_score_since` ('' = butun tarix, avvalgidek). Eski yozuvlar o'chmaydi.
    /// </summary>
    public partial class DisciplineScoreSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discipline_score_since",
                table: "school_meta",
                type: "text",
                nullable: false,
                defaultValue: "");

            // The MCP discipline tools and student_profile read the start day through app_ro — the column
            // is on the mcp_guards.sql allow-list; re-running it re-applies the grants.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discipline_score_since",
                table: "school_meta");
        }
    }
}
