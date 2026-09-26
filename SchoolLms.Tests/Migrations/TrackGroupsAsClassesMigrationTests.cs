using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `TrackGroupsAsClasses` — docs/modules/track-groups-as-classes.md.
//  study_groups.subject_id and study_group_members.subject_id become nullable;
//  existing track groups lose their arbitrary subject (members keep their rows);
//  ck_study_groups_subject binds "track <=> no subject"; the composite FK
//  (group_id, subject_id) stays in the database and still guards ordinary groups.
//  COVERAGE: 1) Up() on prod-like data (track groups WITH a subject + members);
//  2) constraints after Up(); 3) Down() on data with subject-less tracks, then Up() again.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TrackGroupsAsClassesMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260926061213_StaffSalary";

    private const string TrackId = "11111111-aaaa-4aaa-8aaa-111111111111";
    private const string OrdinaryId = "22222222-bbbb-4bbb-8bbb-222222222222";

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("trackmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_yonalish_fanini_tozalaydi_azolar_qoladi()
    {
        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            await SeedOldShapeAsync(conn);
        }

        await MigrateToAsync(null);

        await using var db = await OpenOwnerAsync();
        Assert.Null(await ScalarAsync(db, $"select subject_id from study_groups where id = '{TrackId}'"));
        Assert.Equal("s-mig-1", await ScalarAsync(db, $"select subject_id from study_groups where id = '{OrdinaryId}'"));
        Assert.Equal(2L, await ScalarAsync(db, $"select count(*) from study_group_members where group_id = '{TrackId}'"));
        Assert.Equal(0L, await ScalarAsync(db,
            $"select count(*) from study_group_members where group_id = '{TrackId}' and subject_id is not null"));
        Assert.Equal("s-mig-1", await ScalarAsync(db,
            $"select subject_id from study_group_members where group_id = '{OrdinaryId}'"));
        Assert.Equal("YES", await ScalarAsync(db,
            "select is_nullable from information_schema.columns where table_name = 'study_groups' and column_name = 'subject_id'"));
    }

    [Fact]
    public async Task Cheklovlar_ishlaydi_kompozit_fk_saqlanadi()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedBaseAsync(conn, "ck");

        // Yo'nalish — fansiz; oddiy — fanli.
        await InsertGroupAsync(conn, "33333333-cccc-4ccc-8ccc-333333333333", "Aniq ck", null, true, "u-ck");
        await InsertGroupAsync(conn, "44444444-dddd-4ddd-8ddd-444444444444", "Oddiy ck", "'s-ck-1'", false, "u-ck");
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertGroupAsync(conn, "55555555-eeee-4eee-8eee-555555555555", "Yomon 1", "'s-ck-1'", true, "u-ck"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertGroupAsync(conn, "66666666-ffff-4fff-8fff-666666666666", "Yomon 2", null, false, "u-ck"))).SqlState);

        // Yo'nalish a'zosi fansiz yoziladi; guruhga bog'lanishni oddiy FK ushlaydi.
        await InsertMemberAsync(conn, "33333333-cccc-4ccc-8ccc-333333333333", null, "st-ck-1", "u-ck");
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertMemberAsync(conn, "77777777-0000-4000-8000-777777777777", null, "st-ck-1", "u-ck"))).SqlState);

        // Kompozit FK hamon oddiy guruhni qo'riqlaydi: a'zo fani guruh fanidan boshqa bo'lolmaydi.
        await InsertMemberAsync(conn, "44444444-dddd-4ddd-8ddd-444444444444", "'s-ck-1'", "st-ck-1", "u-ck");
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertMemberAsync(conn, "44444444-dddd-4ddd-8ddd-444444444444", "'s-ck-2'", "st-ck-2", "u-ck"))).SqlState);
    }

    [Fact]
    public async Task Down_fansiz_yonalishga_fan_qoyadi_va_qayta_Up_ishlaydi()
    {
        long groups, members;
        await using (var conn = await OpenOwnerAsync())
        {
            await SeedBaseAsync(conn, "dn");
            await InsertGroupAsync(conn, "88888888-1111-4111-8111-888888888888", "Aniq dn", null, true, "u-dn");
            await InsertMemberAsync(conn, "88888888-1111-4111-8111-888888888888", null, "st-dn-1", "u-dn");
            groups = (long)(await ScalarAsync(conn, "select count(*) from study_groups"))!;
            members = (long)(await ScalarAsync(conn, "select count(*) from study_group_members"))!;
        }

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Equal("NO", await ScalarAsync(conn,
                "select is_nullable from information_schema.columns where table_name = 'study_groups' and column_name = 'subject_id'"));
            Assert.Equal(groups, await ScalarAsync(conn, "select count(*) from study_groups"));
            Assert.Equal(members, await ScalarAsync(conn, "select count(*) from study_group_members"));
            Assert.NotNull(await ScalarAsync(conn,
                "select subject_id from study_groups where id = '88888888-1111-4111-8111-888888888888'"));
            Assert.Equal(
                await ScalarAsync(conn, "select subject_id from study_groups where id = '88888888-1111-4111-8111-888888888888'"),
                await ScalarAsync(conn, "select subject_id from study_group_members where group_id = '88888888-1111-4111-8111-888888888888'"));
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Null(await ScalarAsync(conn,
                "select subject_id from study_groups where id = '88888888-1111-4111-8111-888888888888'"));
            Assert.Equal(members, await ScalarAsync(conn, "select count(*) from study_group_members"));
        }
    }

    // =====================================================================
    //  Seed
    // =====================================================================

    /// <summary>Prod shakli (migratsiyadan OLDIN): yo'nalish guruhida tasodifiy fan bor.</summary>
    private static async Task SeedOldShapeAsync(NpgsqlConnection conn)
    {
        await SeedBaseAsync(conn, "mig");
        await InsertGroupAsync(conn, TrackId, "Aniq fanlar", "'s-mig-1'", true, "u-mig");
        await InsertGroupAsync(conn, OrdinaryId, "Ingliz", "'s-mig-1'", false, "u-mig");
        await InsertMemberAsync(conn, TrackId, "'s-mig-1'", "st-mig-1", "u-mig");
        await InsertMemberAsync(conn, TrackId, "'s-mig-1'", "st-mig-2", "u-mig");
        // Oddiy guruhda boshqa bola — "bitta fandan bitta guruh" indeksi buzilmasin.
        await InsertMemberAsync(conn, OrdinaryId, "'s-mig-1'", "st-mig-3", "u-mig");
    }

    private static async Task SeedBaseAsync(NpgsqlConnection conn, string tag)
    {
        await InsertMinimalRowAsync(conn, "users", new()
        {
            ["id"] = $"'u-{tag}'", ["email"] = $"'{tag}.track'", ["role"] = "'admin'",
        });
        await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = $"'s-{tag}-1'", ["name"] = $"'Fan {tag} 1'" });
        await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = $"'s-{tag}-2'", ["name"] = $"'Fan {tag} 2'" });
        for (var i = 1; i <= 3; i++)
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = $"'st-{tag}-{i}'", ["full_name"] = $"'Bola {i}'" });
    }

    private static Task InsertGroupAsync(
        NpgsqlConnection conn, string id, string name, string? subjectSql, bool track, string createdBy) =>
        InsertMinimalRowAsync(conn, "study_groups", new()
        {
            ["id"] = $"'{id}'", ["name"] = $"'{name}'", ["subject_id"] = subjectSql ?? "null",
            ["is_track"] = track ? "true" : "false", ["created_by"] = $"'{createdBy}'",
        });

    private static Task InsertMemberAsync(
        NpgsqlConnection conn, string groupId, string? subjectSql, string studentId, string createdBy) =>
        InsertMinimalRowAsync(conn, "study_group_members", new()
        {
            ["group_id"] = $"'{groupId}'", ["subject_id"] = subjectSql ?? "null",
            ["student_id"] = $"'{studentId}'", ["joined_on"] = "'2026-09-01'", ["created_by"] = $"'{createdBy}'",
        });

    // =====================================================================
    //  Helpers (same pattern as StaffSalaryMigrationTests)
    // =====================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
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

    /// <summary>
    /// Minimal row: every NOT NULL column without a DEFAULT gets an "empty" value of its type,
    /// so a later column does not break the test.
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null "
                         + "and is_generated = 'NEVER' and identity_generation is null", conn))
        {
            cmd.Parameters.AddWithValue("t", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = reader.GetString(0);
                if (values.ContainsKey(column)) continue;
                values[column] = reader.GetString(1) switch
                {
                    "text" or "character varying" or "character" => "''",
                    "boolean" => "false",
                    "integer" or "bigint" or "smallint" => "0",
                    "numeric" or "double precision" or "real" => "0",
                    "uuid" => "gen_random_uuid()",
                    "date" => "current_date",
                    "jsonb" or "json" => "'{}'",
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }
}
