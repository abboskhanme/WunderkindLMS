using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SharedTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "WeekAssignments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "UserSettings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TestQuestions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TelegramRegistrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Teachers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Subjects",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Students",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SchoolYearArchives",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SchoolMeta",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ScheduleTemplates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ScheduleLesson",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Quarters",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "QuarterGrades",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "MonthlyCharges",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LmsTopics",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LmsSubjects",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LmsProgresses",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LmsMaterials",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LessonTimes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LessonNotes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "LeadStages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Leads",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "JournalEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "FinanceTransactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Feedbacks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Dishes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DeviceTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ContractTemplates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Contracts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Classes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ChatMessages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Broadcasts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Branches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AssignmentTypes",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AssignmentSubmissions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Assignments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AssignmentMaterials",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AbsenceReasons",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Owners",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastLoginAt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Owners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SuperAdminEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeekAssignments_TenantId",
                table: "WeekAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_TenantId",
                table: "UserSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TenantId",
                table: "TestQuestions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramRegistrations_TenantId",
                table: "TelegramRegistrations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_TenantId",
                table: "Teachers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TenantId",
                table: "Subjects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_TenantId",
                table: "Students",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolYearArchives_TenantId",
                table: "SchoolYearArchives",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolMeta_TenantId",
                table: "SchoolMeta",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTemplates_TenantId",
                table: "ScheduleTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleLesson_TenantId",
                table: "ScheduleLesson",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Quarters_TenantId",
                table: "Quarters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuarterGrades_TenantId",
                table: "QuarterGrades",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyCharges_TenantId",
                table: "MonthlyCharges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsTopics_TenantId",
                table: "LmsTopics",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsSubjects_TenantId",
                table: "LmsSubjects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_TenantId",
                table: "LmsProgresses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsMaterials_TenantId",
                table: "LmsMaterials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonTimes_TenantId",
                table: "LessonTimes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonNotes_TenantId",
                table: "LessonNotes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadStages_TenantId",
                table: "LeadStages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId",
                table: "Leads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId",
                table: "JournalEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_TenantId",
                table: "FinanceTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_TenantId",
                table: "Feedbacks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Dishes_TenantId",
                table: "Dishes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_TenantId",
                table: "DeviceTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTemplates_TenantId",
                table: "ContractTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId",
                table: "Contracts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_TenantId",
                table: "Classes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TenantId",
                table: "ChatMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Broadcasts_TenantId",
                table: "Broadcasts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId",
                table: "Branches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTypes_TenantId",
                table: "AssignmentTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_TenantId",
                table: "AssignmentSubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_TenantId",
                table: "Assignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentMaterials_TenantId",
                table: "AssignmentMaterials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceReasons_TenantId",
                table: "AbsenceReasons",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_Email",
                table: "Owners",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Owners");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_WeekAssignments_TenantId",
                table: "WeekAssignments");

            migrationBuilder.DropIndex(
                name: "IX_UserSettings_TenantId",
                table: "UserSettings");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TestQuestions_TenantId",
                table: "TestQuestions");

            migrationBuilder.DropIndex(
                name: "IX_TelegramRegistrations_TenantId",
                table: "TelegramRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_Teachers_TenantId",
                table: "Teachers");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TenantId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Students_TenantId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_SchoolYearArchives_TenantId",
                table: "SchoolYearArchives");

            migrationBuilder.DropIndex(
                name: "IX_SchoolMeta_TenantId",
                table: "SchoolMeta");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleTemplates_TenantId",
                table: "ScheduleTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleLesson_TenantId",
                table: "ScheduleLesson");

            migrationBuilder.DropIndex(
                name: "IX_Quarters_TenantId",
                table: "Quarters");

            migrationBuilder.DropIndex(
                name: "IX_QuarterGrades_TenantId",
                table: "QuarterGrades");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyCharges_TenantId",
                table: "MonthlyCharges");

            migrationBuilder.DropIndex(
                name: "IX_LmsTopics_TenantId",
                table: "LmsTopics");

            migrationBuilder.DropIndex(
                name: "IX_LmsSubjects_TenantId",
                table: "LmsSubjects");

            migrationBuilder.DropIndex(
                name: "IX_LmsProgresses_TenantId",
                table: "LmsProgresses");

            migrationBuilder.DropIndex(
                name: "IX_LmsMaterials_TenantId",
                table: "LmsMaterials");

            migrationBuilder.DropIndex(
                name: "IX_LessonTimes_TenantId",
                table: "LessonTimes");

            migrationBuilder.DropIndex(
                name: "IX_LessonNotes_TenantId",
                table: "LessonNotes");

            migrationBuilder.DropIndex(
                name: "IX_LeadStages_TenantId",
                table: "LeadStages");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_TenantId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_TenantId",
                table: "FinanceTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_TenantId",
                table: "Feedbacks");

            migrationBuilder.DropIndex(
                name: "IX_Dishes_TenantId",
                table: "Dishes");

            migrationBuilder.DropIndex(
                name: "IX_DeviceTokens_TenantId",
                table: "DeviceTokens");

            migrationBuilder.DropIndex(
                name: "IX_ContractTemplates_TenantId",
                table: "ContractTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_TenantId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Classes_TenantId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_TenantId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_Broadcasts_TenantId",
                table: "Broadcasts");

            migrationBuilder.DropIndex(
                name: "IX_Branches_TenantId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentTypes_TenantId",
                table: "AssignmentTypes");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentSubmissions_TenantId",
                table: "AssignmentSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_TenantId",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentMaterials_TenantId",
                table: "AssignmentMaterials");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceReasons_TenantId",
                table: "AbsenceReasons");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WeekAssignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TestQuestions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TelegramRegistrations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SchoolYearArchives");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ScheduleTemplates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ScheduleLesson");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Quarters");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "QuarterGrades");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MonthlyCharges");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LmsTopics");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LmsSubjects");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LmsProgresses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LmsMaterials");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LessonTimes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LeadStages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Dishes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContractTemplates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AssignmentTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AssignmentMaterials");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AbsenceReasons");
        }
    }
}
