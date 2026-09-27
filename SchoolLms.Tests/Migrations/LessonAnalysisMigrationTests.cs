using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `LessonAnalysis` — dars tahlili (docs/modules/lesson-analysis.md §3).
//  COVERS: 1) four tables; 2) app_rw full CRUD, status check, cascade of details,
//  RESTRICT on teacher / criterion, legacy_id unique; 3) Down() removes only these.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class LessonAnalysisMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260926150755_McpReadOnly";
    private const string ThisMigration = "20260927143357_LessonAnalysis";
    private static readonly string[] Tables =
        ["lesson_observation_sections", "lesson_observation_criteria", "lesson_observations", "lesson_observation_details"];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("lamig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Jadvallar_va_app_rw_huquqlari()
    {
        _database.RequireRealAppRw();
        await using var owner = await OpenAsync(_database.OwnerConnectionString);
        foreach (var t in Tables)
            Assert.Equal(1L, await ScalarAsync(owner, $"select count(*) from information_schema.tables where table_name = '{t}'"));

        await using (var db = PostgresFixture.NewContext(_database.OwnerConnectionString))
        {
            db.Teachers.Add(new SchoolLms.Domain.Teacher { Id = "t1", FullName = "T" });
            db.Users.Add(new SchoolLms.Domain.AppUser { Id = "u1", FullName = "U", Role = "admin", Email = "la-mig-u1", PasswordHash = "x" });
            await db.SaveChangesAsync();
        }

        await using var rw = await OpenAsync(_database.AppRwConnectionString);
        await ExecAsync(rw, "insert into lesson_observation_sections (id, title, order_index, color, legacy_id) values ('s1', 'S', 1, '#000000', 7)");
        await ExecAsync(rw, "insert into lesson_observation_criteria (id, section_id, text, order_index, is_active) values ('c1', 's1', 'C', 1, true)");
        await ExecAsync(rw, "insert into lesson_observations (id, teacher_id, observer_user_id, lesson_date, status, score_done, score_total, "
                            + "score_percent, created_at, updated_at) values ('o1', 't1', 'u1', '2026-09-27', 'draft', 1, 3, 33.33, now(), now())");
        await ExecAsync(rw, "insert into lesson_observation_details (id, observation_id, criterion_id, is_done) values ('d1', 'o1', 'c1', null)");
        await ExecAsync(rw, "update lesson_observations set status = 'completed' where id = 'o1'");
        Assert.Equal(33.33m, await ScalarAsync(rw, "select score_percent from lesson_observations where id = 'o1'"));

        // status is a closed set; one answer per criterion; legacy ids are unique
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(rw, "update lesson_observations set status = 'DRAFT' where id = 'o1'"))).SqlState);
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(rw, "insert into lesson_observation_details (id, observation_id, criterion_id) values ('d2', 'o1', 'c1')"))).SqlState);
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(rw, "insert into lesson_observation_sections (id, title, order_index, color, legacy_id) values ('s2', 'S2', 2, '#fff', 7)"))).SqlState);

        // RESTRICT: a criterion with answers and a teacher with observations cannot disappear
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(owner, "delete from lesson_observation_criteria where id = 'c1'"))).SqlState);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(owner, "delete from teachers where id = 't1'"))).SqlState);

        // deleting an observation takes its answers with it
        await ExecAsync(rw, "delete from lesson_observations where id = 'o1'");
        Assert.Equal(0L, await ScalarAsync(rw, "select count(*) from lesson_observation_details"));
    }

    [Fact]
    public async Task Down_faqat_dars_tahlili_jadvallarini_olib_tashlaydi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var c = await OpenAsync(_database.OwnerConnectionString)) before = await AllColumnsAsync(c);
        await MigrateToAsync(ThisMigration);
        await using (var c = await OpenAsync(_database.OwnerConnectionString))
        {
            var after = await AllColumnsAsync(c);
            Assert.Empty(before.Except(after));
            Assert.All(after.Except(before), col => Assert.StartsWith("lesson_observation", col));
        }
        await MigrateToAsync(PreviousMigration);
        await using (var c = await OpenAsync(_database.OwnerConnectionString))
            Assert.True(before.SetEquals(await AllColumnsAsync(c)), "Down() did not restore the schema");
        await MigrateToAsync(null);
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var db = PostgresFixture.NewContext(_database.OwnerConnectionString);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string cs)
    {
        var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        return c;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<HashSet<string>> AllColumnsAsync(NpgsqlConnection conn)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            "select table_name, column_name from information_schema.columns where table_schema = 'public'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return columns;
    }
}
