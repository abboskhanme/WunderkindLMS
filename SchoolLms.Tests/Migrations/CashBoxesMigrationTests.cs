using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `CashBoxes` — kassalar modeli, "smena" tushunchasini almashtiradi
//  (mijoz javobi, 2026-09). Shakli `StudentsParityP1MigrationTests`
//  ("T1"ning naqshi) bilan bir xil.
// ===========================================================================
//
//  BU MIGRATSIYANING YAGONA XAVFLI JOYI — `payments.cash_shift_id` NOT
//  NULL'dan NULLABLE'ga o'tadi. Bu CHEKLOVNI KENGAYTIRADI (torroqdan
//  kengroqqa), ya'ni MAVJUD qatorlar (har doim to'ldirilgan) avtomatik
//  yaroqli qoladi — quyidagi test buni ANIQ tekshiradi: migratsiyadan OLDIN
//  yozilgan qator (haqiqiy `cash_shift_id` bilan) migratsiyadan KEYIN ham
//  o'zgarishsiz turadi.
//
//  Qolgani additiv: ikkita yangi jadval va uchta `null` bo'la oladigan
//  ustun (`payments`/`expenses`/`student_refunds`.`cash_box_id`).

[Collection(SchoolLmsCollection.Name)]
public class CashBoxesMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260917100831_FinanceParityBatchA";

    private static readonly string DefaultBoxId = "00000000-0000-0000-0000-0000000000cb";

    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("payments", "cash_box_id"),
        ("expenses", "cash_box_id"),
        ("student_refunds", "cash_box_id"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("cashboxesmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1) Sxema — ikkita yangi jadval, uchta yangi ustun
    // =====================================================================

    [Fact]
    public async Task Up_ikkita_jadval_va_uchta_cash_box_id_ustuni_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(await TableExistsAsync(conn, "cash_boxes"));
        Assert.True(await TableExistsAsync(conn, "cash_box_transactions"));

        foreach (var (table, column) in NewColumns)
        {
            Assert.True(await ColumnExistsAsync(conn, table, column), $"`{table}.{column}` yo'q");
            Assert.True(await IsNullableAsync(conn, table, column),
                $"`{table}.{column}` `null` bo'la olishi kerak — mavjud qatorlarda qiymat yo'q");
        }
    }

    /// <summary>
    /// <b>Eng muhim test.</b> <c>payments.cash_shift_id</c> NOT NULL'dan
    /// NULLABLE'ga o'tadi. Eski qiymatli qator (haqiqiy smena bilan) omon
    /// qolishi SHART; yangi qator esa smenasiz (<c>null</c>) yozilishi
    /// mumkin bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task Payments_cash_shift_id_nullable_boladi_va_eski_qator_omon_qoladi()
    {
        await MigrateToAsync(PreviousMigration);

        Guid shiftId, studentId0;
        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-cb-1'" });
            await InsertMinimalRowAsync(conn, "users", new() { ["id"] = "'u-cb-1'", ["role"] = "'cashier'" });
            await InsertMinimalRowAsync(conn, "cash_shifts", new()
            {
                ["id"] = "'11111111-1111-1111-1111-111111111111'",
                ["cashier_id"] = "'u-cb-1'",
                ["status"] = "'open'",
            });
            await InsertMinimalRowAsync(conn, "payments", new()
            {
                ["id"] = "'22222222-2222-2222-2222-222222222222'",
                ["receipt_no"] = "1",
                ["student_id"] = "'st-cb-1'",
                ["amount"] = "1000",
                ["method"] = "'cash'",
                ["cash_shift_id"] = "'11111111-1111-1111-1111-111111111111'",
                ["cashier_id"] = "'u-cb-1'",
            });
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            // Eski qator — cash_shift_id joyida, cash_box_id null.
            var stored = await ScalarAsync(conn,
                "select cash_shift_id::text from payments where id = '22222222-2222-2222-2222-222222222222'");
            Assert.Equal("11111111-1111-1111-1111-111111111111", stored);

            var boxId = await ScalarAsync(conn,
                "select cash_box_id from payments where id = '22222222-2222-2222-2222-222222222222'");
            Assert.Null(boxId);

            Assert.True(await IsNullableAsync(conn, "payments", "cash_shift_id"),
                "cash_shift_id nullable bo'lishi kerak edi");

            // Yangi qator — cash_shift_id NULL bo'la oladi (avval NOT NULL edi).
            await InsertMinimalRowAsync(conn, "payments", new()
            {
                ["id"] = "'33333333-3333-3333-3333-333333333333'",
                ["receipt_no"] = "1",
                ["student_id"] = "'st-cb-1'",
                ["amount"] = "500",
                ["method"] = "'cash'",
                ["cashier_id"] = "'u-cb-1'",
                ["cash_box_id"] = $"'{DefaultBoxId}'",
            });

            var nullShift = await ScalarAsync(conn,
                "select cash_shift_id from payments where id = '33333333-3333-3333-3333-333333333333'");
            Assert.Null(nullShift);
        }
    }

    /// <summary>Sukut (default) kassa migratsiyada seed qilinadi — birinchi to'lovdayoq kerak bo'ladi.</summary>
    [Fact]
    public async Task Sukut_kassa_seed_qilinadi()
    {
        await using var conn = await OpenOwnerAsync();

        var count = await ScalarAsync(conn,
            $"select count(*) from cash_boxes where id = '{DefaultBoxId}' and is_default = true and is_active = true");
        Assert.Equal(1L, count);
    }

    /// <summary>SPEC: "Exactly one box must be the default — enforce it" — qisman unikal indeks.</summary>
    [Fact]
    public async Task Faqat_bitta_kassa_sukut_bola_oladi()
    {
        await using var conn = await OpenOwnerAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into cash_boxes (id, name, is_default, is_active) "
            + "values (gen_random_uuid(), 'Ikkinchi sukut', true, true)"));
        Assert.Equal("23505", ex.SqlState);
    }

    // =====================================================================
    //  2) app_rw huquqlari — SPEC §4.1
    // =====================================================================

    [Fact]
    public async Task App_rw_cash_box_transactionsga_faqat_qosha_oladi_ozgartira_olmaydi()
    {
        _database.RequireRealAppRw();

        await using var owner = await OpenOwnerAsync();
        await InsertMinimalRowAsync(owner, "users", new() { ["id"] = "'u-grant-1'", ["role"] = "'admin'" });

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app,
            "insert into cash_box_transactions (id, cash_box_id, kind, amount, method, status, created_by) "
            + $"values (gen_random_uuid(), '{DefaultBoxId}', 'pay_in', 1000, 'cash', 'posted', 'u-grant-1')");

        var id = await ScalarAsync(app,
            $"select id::text from cash_box_transactions where cash_box_id = '{DefaultBoxId}' "
            + "and created_by = 'u-grant-1' limit 1");

        var updateEx = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(app,
            $"update cash_box_transactions set amount = 1 where id = '{id}'"));
        Assert.Equal("42501", updateEx.SqlState);

        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(app,
            $"delete from cash_box_transactions where id = '{id}'"));
        Assert.Equal("42501", deleteEx.SqlState);
    }

    /// <summary>
    /// <c>cash_boxes</c> — kataloq, <c>payments</c>/<c>ledger_entries</c> kabi
    /// pul YOZUVI emas: rename/deactivate uchun UPDATE OCHIQ, faqat DELETE
    /// yopiladi.
    /// </summary>
    [Fact]
    public async Task App_rw_cash_boxlarni_yangilay_oladi_ochira_olmaydi()
    {
        _database.RequireRealAppRw();

        await using var owner = await OpenOwnerAsync();
        var boxId = Guid.NewGuid();
        await ExecAsync(owner,
            $"insert into cash_boxes (id, name, is_default, is_active) values ('{boxId}', 'Rename testi', false, true)");

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app, $"update cash_boxes set name = 'Yangi nom' where id = '{boxId}'");
        Assert.Equal("Yangi nom", await ScalarAsync(app, $"select name from cash_boxes where id = '{boxId}'"));

        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(app,
            $"delete from cash_boxes where id = '{boxId}'"));
        Assert.Equal("42501", deleteEx.SqlState);
    }

    // =====================================================================
    //  3) Down() — orqaga qaytarish (bo'sh bazada)
    // =====================================================================

    [Fact]
    public async Task Down_orqaga_qaytaradi_va_birorta_ustun_yoqolmaydi()
    {
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "cash_boxes"));
            Assert.False(await TableExistsAsync(conn, "cash_box_transactions"));
            foreach (var (table, column) in NewColumns)
                Assert.False(await ColumnExistsAsync(conn, table, column), $"`{table}.{column}` qoldi");

            // cash_shift_id eski holiga (NOT NULL) qaytadi.
            Assert.False(await IsNullableAsync(conn, "payments", "cash_shift_id"));
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
    //  Yordamchi qism (StudentsParityP1MigrationTests bilan bir xil naqsh)
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
                         // Hisoblanadigan ustunga qiymat yozib bo'lmaydi — 428C9.
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
