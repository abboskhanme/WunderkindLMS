using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `NotificationStates` — foydalanuvchi bildirishnomani qachon o'qigani /
//  o'chirgani. Shakli `DailyAttendanceMarksMigrationTests` bilan bir xil:
//  yangi jadval, hech bir mavjud jadvalga tegmaydi.
//
//  QAMROV
//  1) Jadval va ustunlar — deklaratsiya qilingan turlar bilan.
//  2) Kalit (user_id, notification_id) takrorni rad etadi; foydalanuvchi
//     o'chirilsa uning qatorlari ham o'chadi.
//  3) `app_rw` — to'liq CRUD.
//  4) `Down()` — faqat shu jadvalni olib tashlaydi.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class NotificationStatesMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260922050221_AdmissionAndExams";
    private const string ThisMigration = "20260922182936_NotificationStates";

    private static readonly (string Column, string DataType, string Nullable)[] ExpectedColumns =
    [
        ("user_id", "text", "NO"),
        ("notification_id", "character varying", "NO"),
        ("read_at", "timestamp without time zone", "YES"),
        ("dismissed_at", "timestamp without time zone", "YES"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("notifstatemig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_jadvalni_deklaratsiya_qilingan_ustunlar_bilan_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();
        Assert.True(await TableExistsAsync(conn, "notification_states"), "`notification_states` jadvali yo'q");

        foreach (var (column, dataType, nullable) in ExpectedColumns)
        {
            var row = await ColumnAsync(conn, column);
            Assert.True(row is not null, $"`notification_states.{column}` yo'q");
            Assert.Equal(dataType, row!.Value.DataType);
            Assert.Equal(nullable, row.Value.Nullable);
        }
    }

    [Fact]
    public async Task Kalit_takrorni_rad_etadi_va_foydalanuvchi_bilan_ochadi()
    {
        await using var conn = await OpenOwnerAsync();
        var uid = await SeedUserAsync(conn);

        await ExecAsync(conn, Insert(uid, "chat:1"));
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, Insert(uid, "chat:1")));
        Assert.Equal("23505", ex.SqlState);

        await ExecAsync(conn, $"delete from users where id = '{uid}'");
        Assert.Equal(0L, await ScalarAsync(conn, $"select count(*) from notification_states where user_id = '{uid}'"));
    }

    [Fact]
    public async Task App_rw_tolik_crud_qila_oladi()
    {
        _database.RequireRealAppRw();
        string uid;
        await using (var owner = await OpenOwnerAsync()) uid = await SeedUserAsync(owner);

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();
        await ExecAsync(app, Insert(uid, "news:1"));
        await ExecAsync(app, $"update notification_states set dismissed_at = now() where user_id = '{uid}'");
        Assert.Equal(1L, await ScalarAsync(app,
            $"select count(*) from notification_states where user_id = '{uid}' and dismissed_at is not null"));
        await ExecAsync(app, $"delete from notification_states where user_id = '{uid}'");
        Assert.Equal(0L, await ScalarAsync(app, $"select count(*) from notification_states where user_id = '{uid}'"));
    }

    [Fact]
    public async Task Down_faqat_shu_jadvalni_olib_tashlaydi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "notification_states"));
            before = await AllColumnsAsync(conn);
        }

        await MigrateToAsync(ThisMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.True(await TableExistsAsync(conn, "notification_states"));
            // Eski umumiy belgi joyida qoladi — undan hamon "yangi"ni ajratishda foydalaniladi.
            Assert.Equal(1L, await ScalarAsync(conn,
                "select count(*) from information_schema.columns where table_name = 'user_settings' and column_name = 'notifications_read_at'"));
            var lost = before.Except(await AllColumnsAsync(conn)).ToList();
            Assert.True(lost.Count == 0, "Migratsiya boshqa ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));
        }

        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "notification_states"));
            Assert.True(before.SetEquals(await AllColumnsAsync(conn)), "Down() dan keyin sxema avvalgi holatga qaytmadi");
        }
        await MigrateToAsync(null);
    }

    // ------------------------------------------------------------------ yordamchi

    private static string Insert(string uid, string id) =>
        $"insert into notification_states (user_id, notification_id, read_at) values ('{uid}', '{id}', now())";

    private static async Task<string> SeedUserAsync(NpgsqlConnection conn)
    {
        var id = Guid.NewGuid().ToString();
        // Faqat NOT NULL ustunlar — qolganlari o'z default qiymati bilan.
        var required = new List<(string Name, string Type)>();
        await using (var cmd = new NpgsqlCommand(
            "select column_name, data_type from information_schema.columns where table_name = 'users' "
            + "and is_nullable = 'NO' and column_default is null", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) required.Add((r.GetString(0), r.GetString(1)));

        var cols = string.Join(", ", required.Select(c => c.Name));
        var vals = string.Join(", ", required.Select(c => c.Name == "id" ? $"'{id}'" : c.Name == "email"
            ? $"'n.{id}'" : c.Type switch
            {
                "boolean" => "false",
                "integer" or "bigint" or "smallint" or "numeric" => "0",
                "jsonb" or "json" => "'[]'",
                "ARRAY" => "'{}'",
                var t when t.StartsWith("timestamp") => "now()",
                _ => "''",
            }));
        await ExecAsync(conn, $"insert into users ({cols}) values ({vals})");
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
            + $"where table_schema = 'public' and table_name = 'notification_states' and column_name = '{column}'", conn);
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
