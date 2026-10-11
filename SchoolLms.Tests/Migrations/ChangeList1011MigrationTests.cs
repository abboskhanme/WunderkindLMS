using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  2026-10-11 o'zgarishlar ro'yxati migratsiyalari:
//  · `AttendanceMarkPermission` — "Hamshira" roli kunduzgi davomatni belgilaydi (attendanceMark),
//    kechki dars/yotoqxona ruxsatlari undan olinadi; "Komendant" roli yaratiladi (bir marta).
//  · `BellSchedules` — qo'shimcha qo'ng'iroq jadvallari: faqat yangi jadvallar, cheklov va kaskad.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ChangeList1011MigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string BeforePermission = "20261011061520_BoardingRosters";
    private const string Permission = "20261011062425_AttendanceMarkPermission";
    private const string Bells = "20261011064801_BellSchedules";

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("changelist1011");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Hamshira_belgilaydi_komendant_yaratiladi()
    {
        await MigrateToAsync(BeforePermission);
        await using (var conn = await OpenOwnerAsync())
        {
            await ExecAsync(conn, "insert into access_roles (id, name, description, permissions, created_at) values "
                                  + "(gen_random_uuid(), 'Hamshira', '', array['attendance','attendanceEvening','attendanceDorm'], now()), "
                                  + "(gen_random_uuid(), 'Buxgalter', '', array['finance','attendanceEvening'], now())");
        }

        await MigrateToAsync(Permission);
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Equal("attendance,attendanceMark", await PermsAsync(conn, "Hamshira"));
            // Boshqa rollar tegilmaydi.
            Assert.Equal("finance,attendanceEvening", await PermsAsync(conn, "Buxgalter"));
            Assert.Equal("attendanceEvening,attendanceDorm", await PermsAsync(conn, "Komendant"));
            Assert.Equal(1L, await ScalarAsync(conn, "select count(*) from access_roles where lower(name) = 'komendant'"));
        }
        await MigrateToAsync(null);
    }

    [Fact]
    public async Task Qongiroq_jadvallari_faqat_qoshiladi_va_kaskad_ishlaydi()
    {
        await MigrateToAsync(Permission);
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        await MigrateToAsync(Bells);
        await using (var conn = await OpenOwnerAsync())
        {
            var after = await AllColumnsAsync(conn);
            Assert.Empty(before.Except(after));
            Assert.All(after.Except(before), c => Assert.StartsWith("bell_schedule", c));

            await ExecAsync(conn, "insert into bell_schedules (id, name, created_at) values "
                                  + "('11111111-1111-1111-1111-111111111111', 'Boshlang''ich', now())");
            await ExecAsync(conn, "insert into bell_schedule_periods (schedule_id, period, start_time, end_time) values "
                                  + "('11111111-1111-1111-1111-111111111111', 1, '08:00', '08:40')");
            await ExecAsync(conn, "insert into bell_schedule_owners (owner_id, schedule_id) values "
                                  + "('cls-1', '11111111-1111-1111-1111-111111111111')");
            var dupName = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
                "insert into bell_schedules (id, name, created_at) values (gen_random_uuid(), 'Boshlang''ich', now())"));
            Assert.Equal("23505", dupName.SqlState);
            var badPeriod = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
                "insert into bell_schedule_periods (schedule_id, period, start_time, end_time) values "
                + "('11111111-1111-1111-1111-111111111111', 0, '07:00', '07:40')"));
            Assert.Equal("23514", badPeriod.SqlState);
            // Ega — bitta jadvalda.
            var twice = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
                "insert into bell_schedule_owners (owner_id, schedule_id) values ('cls-1', '11111111-1111-1111-1111-111111111111')"));
            Assert.Equal("23505", twice.SqlState);

            await ExecAsync(conn, "delete from bell_schedules");
            Assert.Equal(0L, await ScalarAsync(conn, "select count(*) from bell_schedule_periods"));
            Assert.Equal(0L, await ScalarAsync(conn, "select count(*) from bell_schedule_owners"));
        }

        await MigrateToAsync(Permission);
        await using (var conn = await OpenOwnerAsync())
            Assert.True(before.SetEquals(await AllColumnsAsync(conn)), "Down() sxemani qaytarmadi");
        await MigrateToAsync(null);
    }

    private static async Task<string> PermsAsync(NpgsqlConnection conn, string role) =>
        (string)(await ScalarAsync(conn, $"select array_to_string(permissions, ',') from access_roles where name = '{role}'"))!;

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
}
