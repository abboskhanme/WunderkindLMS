using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `TransactionTypes` — tranzaksiya turi katalogi (Kirim/Chiqim), mijoz
//  yuborgan EduSchool kassa kirim shakli va moliya sozlamalari ekrani
//  (2026-09-18). Shakli `ExpenseTemplatesMigrationTests` (yangi jadval,
//  to'liq CRUD katalog) va `CashBoxesMigrationTests` (mavjud jadvalga
//  nullable ustun + FK) bilan bir xil.
// ===========================================================================
//
//  QAMROV
//  ------
//  1) `transaction_types` — jadval, ustunlar, ikkita CHECK, ikkita indeks.
//  2) CHECK'lar HAQIQATAN yomon qiymatni rad etishi (mavjudligi kifoya emas).
//  3) `cash_box_transactions.transaction_type_id` — yangi, NULLABLE ustun +
//     FK (noto'g'ri id rad etiladi, to'g'ri id ishlaydi).
//  4) Seed: 12 ta tur (6 Kirim + 6 Chiqim), hammasi `is_seeded = true`.
//  5) `app_rw` — `transaction_types`da TO'LIQ CRUD (moliyaviy emas).
//  6) `Down()` — faqat SHU migratsiya qo'shgan narsani olib tashlaydi,
//     boshqa hech narsaga tegmaydi (assertsiya SHU migratsiyaga chegaralangan).

[Collection(SchoolLmsCollection.Name)]
public class TransactionTypesMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260918052540_ExpenseTemplates";
    private const string ThisMigration = "20260918090000_TransactionTypes";

    private const string DefaultBoxId = "00000000-0000-0000-0000-0000000000cb";
    private const string SeedInId = "00000000-0000-0000-0000-0000000000a1";
    private const string SeedOutId = "00000000-0000-0000-0000-0000000000e1";

    private static readonly (string Column, string DataType, string Nullable)[] ExpectedColumns =
    [
        ("id", "uuid", "NO"),
        ("kind", "text", "NO"),
        ("name", "text", "NO"),
        ("is_active", "boolean", "NO"),
        ("is_seeded", "boolean", "NO"),
        ("position", "integer", "NO"),
        ("created_at", "timestamp with time zone", "NO"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("transtypesmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1) Sxema — jadval, ustunlar, CHECK'lar, indekslar
    // =====================================================================

    [Fact]
    public async Task Up_transaction_types_jadvalini_deklaratsiya_qilingan_ustunlar_bilan_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(await TableExistsAsync(conn, "transaction_types"), "`transaction_types` jadvali yo'q");

        foreach (var (column, dataType, nullable) in ExpectedColumns)
        {
            var row = await ColumnAsync(conn, "transaction_types", column);
            Assert.True(row is not null, $"`transaction_types.{column}` yo'q");
            Assert.Equal(dataType, row!.Value.DataType);
            Assert.Equal(nullable, row.Value.Nullable);
        }
    }

    [Fact]
    public async Task Up_cash_box_transactionsga_transaction_type_id_ustunini_nullable_qilib_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(await ColumnExistsAsync(conn, "cash_box_transactions", "transaction_type_id"),
            "`cash_box_transactions.transaction_type_id` yo'q");
        Assert.True(await IsNullableAsync(conn, "cash_box_transactions", "transaction_type_id"),
            "`transaction_type_id` `null` bo'la olishi kerak — mavjud/chiqim/ko'chirish/ayirboshlash qatorlari tur so'ramaydi");
    }

    [Fact]
    public async Task Up_check_va_indekslarni_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var name in new[] { "ck_transaction_types_kind", "ck_transaction_types_name" })
            Assert.True(await ConstraintExistsAsync(conn, "transaction_types", name), $"`{name}` yo'q");

        Assert.True(await IndexExistsAsync(conn, "transaction_types", "ix_transaction_types_kind_name"),
            "`ix_transaction_types_kind_name` (unikal) yo'q");
        Assert.True(
            await IndexExistsAsync(conn, "transaction_types", "ix_transaction_types_kind_is_active_position"),
            "`ix_transaction_types_kind_is_active_position` yo'q");
        Assert.True(
            await IndexExistsAsync(conn, "cash_box_transactions", "ix_cash_box_transactions_transaction_type_id"),
            "`ix_cash_box_transactions_transaction_type_id` yo'q");
    }

    /// <summary>Ikkita CHECK haqiqatan ISHLASHINI tekshiradi — mavjudligi kifoya emas.</summary>
    [Fact]
    public async Task Check_cheklovlari_notogri_qiymatlarni_rad_etadi()
    {
        await using var conn = await OpenOwnerAsync();

        var badKind = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into transaction_types (kind, name) values ('bonus', 'Yangi bonus turi')"));
        Assert.Equal("23514", badKind.SqlState);

        var badName = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into transaction_types (kind, name) values ('in', '   ')"));
        Assert.Equal("23514", badName.SqlState);

        // To'g'ri qator — muammosiz o'tadi, sukut qiymatlar o'z ishini qiladi.
        await ExecAsync(conn, "insert into transaction_types (kind, name) values ('in', 'Test turi')");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from transaction_types where name = 'Test turi' "
            + "and is_active = true and is_seeded = false and position = 0 and created_at is not null"));
    }

    /// <summary>Bitta kind ichida nom takrorlanmaydi — `ix_transaction_types_kind_name` unikal.</summary>
    [Fact]
    public async Task Bitta_kind_ichida_nom_takrorlanmaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await ExecAsync(conn, "insert into transaction_types (kind, name) values ('out', 'Noyob nom')");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into transaction_types (kind, name) values ('out', 'Noyob nom')"));
        Assert.Equal("23505", ex.SqlState);

        // Boshqa kind'da AYNAN shu nom — muammo emas (unikal indeks (kind, name) juftlik bo'yicha).
        await ExecAsync(conn, "insert into transaction_types (kind, name) values ('in', 'Noyob nom')");
    }

    // =====================================================================
    //  2) `transaction_type_id` FK — cash_box_transactions
    // =====================================================================

    [Fact]
    public async Task Notogri_transaction_type_idga_yozib_bolmaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await InsertMinimalRowAsync(conn, "users", new() { ["id"] = "'u-tt-1'", ["role"] = "'cashier'" });

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into cash_box_transactions (id, cash_box_id, kind, amount, method, status, created_by, transaction_type_id) "
            + $"values (gen_random_uuid(), '{DefaultBoxId}', 'pay_in', 1000, 'cash', 'posted', 'u-tt-1', gen_random_uuid())"));
        Assert.Equal("23503", ex.SqlState);
    }

    [Fact]
    public async Task Togri_transaction_type_idga_yozish_ishlaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await InsertMinimalRowAsync(conn, "users", new() { ["id"] = "'u-tt-2'", ["role"] = "'cashier'" });

        await ExecAsync(conn,
            "insert into cash_box_transactions (id, cash_box_id, kind, amount, method, status, created_by, transaction_type_id) "
            + $"values ('33333333-3333-3333-3333-333333330001', '{DefaultBoxId}', 'pay_in', 1000, 'cash', 'posted', 'u-tt-2', '{SeedInId}')");

        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from cash_box_transactions "
            + $"where id = '33333333-3333-3333-3333-333333330001' and transaction_type_id = '{SeedInId}'"));
    }

    // =====================================================================
    //  3) Seed — mijoz dropdown'i (Kirim) + kichik kassa chiqimlari (Chiqim)
    // =====================================================================

    [Fact]
    public async Task Seed_oltitadan_kirim_va_chiqim_turini_qoshadi_hammasi_seed_qilingan()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.Equal(6L, await ScalarAsync(conn, "select count(*) from transaction_types where kind = 'in'"));
        Assert.Equal(6L, await ScalarAsync(conn, "select count(*) from transaction_types where kind = 'out'"));
        Assert.Equal(12L, await ScalarAsync(conn, "select count(*) from transaction_types where is_seeded = true"));

        // Mijoz dropdown'idan aniq bitta nom — matn to'g'ri ko'chirilganini tasdiqlaydi.
        Assert.Equal(1L, await ScalarAsync(conn,
            $"select count(*) from transaction_types where id = '{SeedInId}' "
            + "and name = 'Do''ppi uchun' and kind = 'in' and is_seeded = true and is_active = true"));
    }

    // =====================================================================
    //  4) `app_rw` — to'liq CRUD (moliyaviy emas)
    // =====================================================================

    [Fact]
    public async Task App_rw_transaction_typesda_tolik_crud_qila_oladi()
    {
        _database.RequireRealAppRw();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app, "insert into transaction_types (id, kind, name, is_active, is_seeded, position) "
            + "values (gen_random_uuid(), 'out', 'Grant testi', true, false, 9)");

        await ExecAsync(app, "update transaction_types set name = 'Grant testi 2' where name = 'Grant testi'");
        Assert.Equal(1L, await ScalarAsync(app,
            "select count(*) from transaction_types where name = 'Grant testi 2'"));

        await ExecAsync(app, "delete from transaction_types where name = 'Grant testi 2'");
        Assert.Equal(0L, await ScalarAsync(app, "select count(*) from transaction_types where name = 'Grant testi 2'"));
    }

    // =====================================================================
    //  5) Down() — faqat SHU migratsiyaga chegaralangan
    // =====================================================================

    /// <summary>
    /// `before` VA `after` — IKKALASI HAM shu migratsiyaning ikki CHEKKASIDA
    /// olinadi (`PreviousMigration` va `ThisMigration`), `head`da EMAS.
    /// Bu ATAYLAB shunday — <c>ExpenseTemplatesMigrationTests</c> "before"ni
    /// fresh (head) bazadan olib, keyin faqat `ThisMigration`gacha (head
    /// gacha EMAS) qaytgani uchun, undan keyin `TransactionTypes` qo'shilganda
    /// SOXTA "yo'qotildi" xatosi berdi — bu migratsiya keyingisi bo'lganda
    /// xuddi shu tuzoqqa tushmasin uchun to'g'ridan-to'g'ri xavfsiz naqsh
    /// bilan yozildi (`PayrollAdjustmentsMigrationTests`/
    /// `FinanceParityBatchAMigrationTests` dagi bilan bir xil, avvaldan
    /// hujjatlashtirilgan yechim): ikkala o'lchov ham `head`ga bog'liq emas,
    /// shuning uchun keyingi HECH QANDAY migratsiya bu testni buzolmaydi.
    /// </summary>
    [Fact]
    public async Task Down_transaction_types_va_ustunini_olib_tashlaydi_boshqa_narsaga_tegmaydi()
    {
        await MigrateToAsync(PreviousMigration);

        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "transaction_types"),
                "`transaction_types` `PreviousMigration`da hali bo'lmasligi kerak");
            Assert.False(await ColumnExistsAsync(conn, "cash_box_transactions", "transaction_type_id"),
                "`cash_box_transactions.transaction_type_id` `PreviousMigration`da hali bo'lmasligi kerak");
            before = await AllColumnsAsync(conn);
        }

        await MigrateToAsync(ThisMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.True(await TableExistsAsync(conn, "transaction_types"));
            Assert.True(await ColumnExistsAsync(conn, "cash_box_transactions", "transaction_type_id"));

            var after = await AllColumnsAsync(conn);
            var lost = before.Except(after).ToList();
            Assert.True(lost.Count == 0, "Migratsiya boshqa ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));
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

    private static async Task<(string DataType, string Nullable)?> ColumnAsync(
        NpgsqlConnection conn, string table, string column)
    {
        await using var cmd = new NpgsqlCommand(
            "select data_type, is_nullable from information_schema.columns "
            + "where table_schema = 'public' and table_name = @t and column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<bool> ConstraintExistsAsync(NpgsqlConnection conn, string table, string name) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.table_constraints "
            + $"where table_schema = 'public' and table_name = '{table}' and constraint_name = '{name}'") is 1L;

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection conn, string table, string name) =>
        await ScalarAsync(conn,
            "select count(*) from pg_indexes "
            + $"where schemaname = 'public' and tablename = '{table}' and indexname = '{name}'") is 1L;

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
    /// qiymat (`CashBoxesMigrationTests` bilan bir xil yordamchi).
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
