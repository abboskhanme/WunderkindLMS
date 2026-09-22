using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `BoardingAttendance` — kechki dars va yotoqxona davomati + study_groups.is_track.
//  QAMROV: 1) jadval, ustunlar, is_track sukut false; 2) (sana, sessiya, o'quvchi) takrorlanmaydi,
//  noto'g'ri sessiya/holat rad etiladi; 3) app_rw to'liq CRUD; 4) Down() faqat shu o'zgarishlarni
//  qaytaradi, boshqa ustun yo'qolmaydi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class BoardingAttendanceMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260922182936_NotificationStates";
    private const string ThisMigration = "20260922212925_BoardingAttendance";

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("boardingmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_jadval_va_ustun_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();
        Assert.True(await TableExistsAsync(conn, "boarding_attendance"));
        foreach (var (col, type) in new[] { ("date", "date"), ("session", "character varying"), ("student_id", "text"),
                     ("status", "character varying"), ("notified_at", "timestamp with time zone") })
            Assert.Equal(type, (await ColumnAsync(conn, col))!.Value.DataType);
        Assert.Equal("false", await ScalarAsync(conn,
            "select column_default from information_schema.columns where table_name = 'study_groups' and column_name = 'is_track'"));
    }

    [Fact]
    public async Task Takror_va_notogri_qiymat_rad_etiladi()
    {
        await using var conn = await OpenOwnerAsync();
        var sid = await SeedStudentAsync(conn);
        await ExecAsync(conn, Insert(sid, "evening", "present"));
        var dup = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Insert(sid, "evening", "absent")));
        Assert.Equal("23505", dup.SqlState);
        await ExecAsync(conn, Insert(sid, "dorm", "absent")); // boshqa sessiya — mumkin
        var bad = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Insert(sid, "night", "present")));
        Assert.Equal("23514", bad.SqlState);
    }

    [Fact]
    public async Task App_rw_tolik_crud()
    {
        _database.RequireRealAppRw();
        string sid;
        await using (var owner = await OpenOwnerAsync()) sid = await SeedStudentAsync(owner);
        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();
        await ExecAsync(app, Insert(sid, "dorm", "present"));
        await ExecAsync(app, $"update boarding_attendance set status = 'excused' where student_id = '{sid}'");
        await ExecAsync(app, $"delete from boarding_attendance where student_id = '{sid}'");
        Assert.Equal(0L, await ScalarAsync(app, $"select count(*) from boarding_attendance where student_id = '{sid}'"));
    }

    [Fact]
    public async Task Down_faqat_shu_ozgarishlarni_qaytaradi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);
        await MigrateToAsync(ThisMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            var lost = before.Except(await AllColumnsAsync(conn)).ToList();
            Assert.True(lost.Count == 0, "Migratsiya ustun yo'qotdi: " + string.Join(", ", lost));
        }
        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
            Assert.True(before.SetEquals(await AllColumnsAsync(conn)), "Down() sxemani qaytarmadi");
        await MigrateToAsync(null);
    }

    private static string Insert(string sid, string session, string status) =>
        "insert into boarding_attendance (id, date, session, student_id, status, marked_by, marked_at) "
        + $"values (gen_random_uuid(), '2026-09-23', '{session}', '{sid}', '{status}', 'u', now())";

    private static async Task<string> SeedStudentAsync(NpgsqlConnection conn)
    {
        var id = Guid.NewGuid().ToString();
        var required = new List<(string Name, string Type)>();
        await using (var cmd = new NpgsqlCommand(
            "select column_name, data_type from information_schema.columns where table_name = 'students' "
            + "and is_nullable = 'NO' and column_default is null", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) required.Add((r.GetString(0), r.GetString(1)));
        var cols = string.Join(", ", required.Select(c => c.Name));
        var vals = string.Join(", ", required.Select(c => c.Name == "id" ? $"'{id}'" : c.Type switch
        {
            "boolean" => "false",
            "integer" or "bigint" or "smallint" or "numeric" => "0",
            "jsonb" or "json" => "'[]'",
            "ARRAY" => "'{}'",
            "date" => "'2020-01-01'",
            var t when t.StartsWith("timestamp") => "now()",
            "uuid" => "gen_random_uuid()",
            _ => "''",
        }));
        await ExecAsync(conn, $"insert into students ({cols}) values ({vals})");
        return id;
    }

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

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table) =>
        await ScalarAsync(conn,
            $"select count(*) from information_schema.tables where table_schema = 'public' and table_name = '{table}'") is 1L;

    private static async Task<(string DataType, string Nullable)?> ColumnAsync(NpgsqlConnection conn, string column)
    {
        await using var cmd = new NpgsqlCommand(
            "select data_type, is_nullable from information_schema.columns "
            + $"where table_schema = 'public' and table_name = 'boarding_attendance' and column_name = '{column}'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1));
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
