using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `DailyAttendanceMarks` — "shu sinfning shu kuni belgilab bo'lindi" belgisi
//  (mijoz, 2026-09-18: bitta mas'ul xodim barcha sinflar davomatini qiladi).
//  Shakli `ExpenseTemplatesMigrationTests` bilan bir xil: yangi jadval, hech
//  bir mavjud jadvalga tegmaydi.
// ===========================================================================
//
//  QAMROV
//  ------
//  1) Jadval va ustunlar — deklaratsiya qilingan turlar bilan.
//  2) Unikal indeks (date, class_id) HAQIQATAN takrorni rad etishi.
//  3) `app_rw` — to'liq CRUD (jadval moliyaviy emas, izoh: guards.sql).
//  4) `Down()` — faqat shu jadvalni olib tashlaydi, birorta boshqa ustunni
//     yo'qotmaydi (JURNAL — yo'qlikning haqiqiy manbai — tegilmaydi).

[Collection(SchoolLmsCollection.Name)]
public class DailyAttendanceMarksMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260918090000_TransactionTypes";
    private const string ThisMigration = "20260918144730_DailyAttendanceMarks";
    /// <summary>Belgi DARS bo'yicha bo'lgan migratsiya (mijoz, 2026-09-18 ikkinchi xat).</summary>
    private const string PerLessonMigration = "20260918151831_AttendanceMarkPerLesson";

    private static readonly (string Column, string DataType, string Nullable)[] ExpectedColumns =
    [
        ("id", "text", "NO"),
        ("class_id", "text", "NO"),
        ("date", "text", "NO"),
        ("subject_id", "text", "NO"),
        ("period", "integer", "NO"),
        ("marked_by", "text", "NO"),
        ("marked_at", "timestamp with time zone", "NO"),
        ("absent_count", "integer", "NO"),
        ("late_count", "integer", "NO"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("dailyattmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1) Sxema
    // =====================================================================

    [Fact]
    public async Task Up_jadvalni_deklaratsiya_qilingan_ustunlar_bilan_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(await TableExistsAsync(conn, "daily_attendance_marks"),
            "`daily_attendance_marks` jadvali yo'q");

        foreach (var (column, dataType, nullable) in ExpectedColumns)
        {
            var row = await ColumnAsync(conn, column);
            Assert.True(row is not null, $"`daily_attendance_marks.{column}` yo'q");
            Assert.Equal(dataType, row!.Value.DataType);
            Assert.Equal(nullable, row.Value.Nullable);
        }
    }

    /// <summary>
    /// Bir sinf + bir kun + bir DARS = BITTA qator. Usiz qayta saqlash har
    /// safar yangi qator qo'shib, "belgilandi" hisobi ikkilanib ketardi.
    /// </summary>
    [Fact]
    public async Task Bir_sinfning_bir_darsi_takrorlanmaydi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(
            await IndexExistsAsync(conn, "ix_daily_attendance_marks_date_class_id_subject_id_period"),
            "`ix_daily_attendance_marks_date_class_id_subject_id_period` yo'q");

        await ExecAsync(conn, Insert("birinchi"));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Insert("ikkinchi")));
        Assert.Equal("23505", ex.SqlState); // unique_violation

        // Boshqa SOAT — bemalol yoziladi (kun bo'yicha qulf yo'q).
        await ExecAsync(conn, Insert("ikkinchi-soat", period: 2));
        Assert.Equal(2L, await ScalarAsync(conn,
            "select count(*) from daily_attendance_marks where date = '2026-01-12'"));
    }

    // =====================================================================
    //  2) `app_rw` — to'liq CRUD
    // =====================================================================

    [Fact]
    public async Task App_rw_tolik_crud_qila_oladi()
    {
        _database.RequireRealAppRw();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app, Insert("grant-testi"));

        await ExecAsync(app, "update daily_attendance_marks set absent_count = 3 where id = 'grant-testi'");
        Assert.Equal(1L, await ScalarAsync(app,
            "select count(*) from daily_attendance_marks where id = 'grant-testi' and absent_count = 3"));

        await ExecAsync(app, "delete from daily_attendance_marks where id = 'grant-testi'");
        Assert.Equal(0L, await ScalarAsync(app,
            "select count(*) from daily_attendance_marks where id = 'grant-testi'"));
    }

    // =====================================================================
    //  3) Down() — faqat shu jadval
    // =====================================================================

    /// <summary>
    /// O'lchov ikkala chekkada ham SHU migratsiyaga nisbatan olinadi
    /// (`TransactionTypesMigrationTests` dagi izoh: `head`ga bog'lansa,
    /// keyingi migratsiya bu testni soxta "yo'qotildi" bilan yiqitardi).
    /// </summary>
    [Fact]
    public async Task Down_faqat_shu_jadvalni_olib_tashlaydi()
    {
        await MigrateToAsync(PreviousMigration);

        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "daily_attendance_marks"),
                "`daily_attendance_marks` avvalgi migratsiyada bo'lmasligi kerak");
            before = await AllColumnsAsync(conn);
        }

        await MigrateToAsync(PerLessonMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.True(await TableExistsAsync(conn, "daily_attendance_marks"));
            Assert.True(await ColumnExistsAsync(conn, "daily_attendance_marks", "subject_id"),
                "`subject_id` qo'shilmagan — belgi DARS bo'yicha bo'lishi kerak");

            // Yo'qlikning haqiqiy manbai — jurnal. U tegilmaganini alohida
            // tekshiramiz: bu migratsiyaning butun g'oyasi shunda.
            Assert.True(await ColumnExistsAsync(conn, "journal_entries", "reason_id"),
                "`journal_entries.reason_id` yo'qoldi — davomat manbaiga tegilmasligi kerak");

            var after = await AllColumnsAsync(conn);
            var lost = before.Except(after).ToList();
            Assert.True(lost.Count == 0, "Migratsiya boshqa ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));
        }
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private static string Insert(string id, int period = 1) =>
        "insert into daily_attendance_marks "
        + "(id, class_id, date, subject_id, period, marked_by, marked_at, absent_count, late_count) "
        + $"values ('{id}', 'class-1', '2026-01-12', 'subject-1', {period}, 'user-1', now(), 0, 0)";

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
            "select count(*) from information_schema.tables "
            + $"where table_schema = 'public' and table_name = '{table}'") is 1L;

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string table, string column) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") is 1L;

    private static async Task<(string DataType, string Nullable)?> ColumnAsync(
        NpgsqlConnection conn, string column)
    {
        await using var cmd = new NpgsqlCommand(
            "select data_type, is_nullable from information_schema.columns "
            + "where table_schema = 'public' and table_name = 'daily_attendance_marks' "
            + $"and column_name = '{column}'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection conn, string index) =>
        await ScalarAsync(conn,
            "select count(*) from pg_indexes where schemaname = 'public' "
            + $"and tablename = 'daily_attendance_marks' and indexname = '{index}'") is 1L;

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
