using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `StudentsParityP1` — docs/modules/students-parity.md §3.2.
// ===========================================================================
//
//  Bu migratsiyaning yagona xavfli joyi — `student_guardians.relation` dagi
//  CHECK cheklovi. U O'CHIRILIB, kengroq ro'yxat bilan qayta qo'yiladi
//  (`father`/`mother` qo'shiladi). Agar yangi ro'yxat eskisining ustini
//  qoplamasa, migratsiya MAVJUD qatorlarda yiqiladi — va bu jonli bazada
//  yarim qo'llangan migratsiya degani. Shuning uchun quyidagi test
//  migratsiyadan OLDIN eski qiymatli qator yozadi va u omon qolishini talab
//  qiladi.
//
//  Qolgani additiv: to'rtta yangi jadval va beshta `null` bo'la oladigan
//  ustun, ya'ni bugungi ekranlarning birortasi o'zgarmaydi.

[Collection(SchoolLmsCollection.Name)]
public class StudentsParityP1MigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260917063212_StudyGroupsAndMemberships";

    private static readonly string[] NewTables =
    [
        "student_statuses",
        "student_comments",
        "student_contracts",
        "rooms",
    ];

    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("students", "phone"),
        ("students", "language"),
        ("students", "document_url"),
        ("students", "status_id"),
        ("student_guardians", "relation_note"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("stparity1");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_tortta_jadval_va_beshta_ustun_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(conn, table), $"`{table}` jadvali yo'q");

        foreach (var (table, column) in NewColumns)
        {
            Assert.True(await ColumnExistsAsync(conn, table, column), $"`{table}.{column}` yo'q");
            Assert.True(await IsNullableAsync(conn, table, column),
                $"`{table}.{column}` `null` bo'la olishi kerak — mavjud qatorlarda qiymat yo'q");
        }
    }

    /// <summary>
    /// <b>Eng muhim test.</b> Vasiy turi cheklovi kengaytiriladi, ya'ni eski
    /// CHECK o'chirilib yangisi qo'yiladi. Eski qiymatli qator (masalan
    /// <c>parent</c>) migratsiyadan omon chiqishi SHART; yangi qiymatlar
    /// (<c>father</c>, <c>mother</c>) esa qabul qilinishi kerak.
    /// </summary>
    [Fact]
    public async Task Vasiy_turi_kengayadi_va_eski_qatorlar_omon_qoladi()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-g-1'" });
            await InsertMinimalRowAsync(conn, "guardians", new() { ["id"] = "'g-1'", ["phone"] = "'+998900000001'", ["full_name"] = "'Vasiy Bir'" });
            await InsertMinimalRowAsync(conn, "student_guardians", new()
            {
                ["student_id"] = "'st-g-1'",
                ["guardian_id"] = "'g-1'",
                ["relation"] = "'parent'",
            });
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            // Eski qator joyida.
            Assert.Equal(1L, await ScalarAsync(conn,
                "select count(*) from student_guardians where relation = 'parent'"));

            // Yangi qiymat qabul qilinadi.
            await InsertMinimalRowAsync(conn, "guardians", new() { ["id"] = "'g-2'", ["phone"] = "'+998900000002'", ["full_name"] = "'Vasiy Ikki'" });
            await InsertMinimalRowAsync(conn, "student_guardians", new()
            {
                ["student_id"] = "'st-g-1'", ["guardian_id"] = "'g-2'", ["relation"] = "'father'",
            });

            // O'ylab topilgani esa rad etiladi.
            var bad = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertMinimalRowAsync(conn, "student_guardians", new()
                {
                    ["student_id"] = "'st-g-1'", ["guardian_id"] = "'g-2'", ["relation"] = "'qo''shni'",
                }));
            Assert.Equal("23514", bad.SqlState);
        }
    }

    [Fact]
    public async Task App_rw_yangi_jadvallarda_ishlay_oladi()
    {
        _database.RequireRealAppRw();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app,
            "insert into student_statuses (id, name, color, position, is_active) "
            + "values (gen_random_uuid(), 'Sinovda', '#F59E0B', 1, true)");
        await ExecAsync(app, "update student_statuses set name = 'Sinov muddati' where name = 'Sinovda'");
        Assert.Equal(1L, await ScalarAsync(app,
            "select count(*) from student_statuses where name = 'Sinov muddati'"));
        await ExecAsync(app, "delete from student_statuses where name = 'Sinov muddati'");

        await ExecAsync(app,
            "insert into rooms (id, name, capacity, kind) values (gen_random_uuid(), '204', 30, 'classroom')");
        await ExecAsync(app, "delete from rooms where name = '204'");
    }

    [Fact]
    public async Task Down_orqaga_qaytaradi_va_birorta_ustun_yoqolmaydi()
    {
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var table in NewTables)
                Assert.False(await TableExistsAsync(conn, table), $"`{table}` `Down()` dan keyin qoldi");
            foreach (var (table, column) in NewColumns)
                Assert.False(await ColumnExistsAsync(conn, table, column), $"`{table}.{column}` qoldi");

            // Vasiy cheklovi eski holiga qaytadi va eski qiymat baribir ishlaydi.
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-down-1'" });
            await InsertMinimalRowAsync(conn, "guardians", new() { ["id"] = "'g-down'", ["phone"] = "'+998900000009'", ["full_name"] = "'Vasiy Uch'" });
            await InsertMinimalRowAsync(conn, "student_guardians", new()
            {
                ["student_id"] = "'st-down-1'", ["guardian_id"] = "'g-down'", ["relation"] = "'parent'",
            });
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            var after = await AllColumnsAsync(conn);
            var lost = before.Except(after).ToList();
            Assert.True(lost.Count == 0, "Migratsiya ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));
        }
    }

    // =====================================================================
    //  Yordamchi qism
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

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.tables "
            + $"where table_schema = 'public' and table_name = '{table}'") is 1L;

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string table, string column) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") is 1L;

    private static async Task<bool> IsNullableAsync(NpgsqlConnection conn, string table, string column) =>
        (string?)await ScalarAsync(conn,
            "select is_nullable from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") == "YES";

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
    /// Minimal qator: DEFAULT'siz har bir NOT NULL ustunga turiga mos "bo'sh"
    /// qiymat. Qo'lda yozilgan ustun ro'yxati birinchi yangi ustunda testni
    /// yiqitardi — bu migratsiyaning emas, testning nosozligi bo'lardi.
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null "
                         // Hisoblanadigan ustunga (masalan `guardians.phone_key`) qiymat
                         // yozib bo'lmaydi — 428C9.
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
