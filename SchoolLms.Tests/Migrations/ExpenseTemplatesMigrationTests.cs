using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `ExpenseTemplates` — docs/modules/finance-parity.md §2.6.3 (F6.01), §3.3 (C7).
// ===========================================================================
//
//  Bu migratsiya butunlay ADDITIV: bitta YANGI jadval, bironta mavjud jadval
//  yoki ustunga tegmaydi. Shuning uchun testlar ikkita narsani isbotlaydi:
//  (1) `Up()` da'vo qilgan hamma narsani qo'shadi — jadval, ustunlar, uchta
//  CHECK va indeks; (2) `app_rw` yangi jadvalda TO'LIQ CRUD qila oladi
//  (`expense_templates` moliyaviy emas — SPEC §4.1 emas, `expense_templates_guards.sql`
//  boshidagi izoh). Assertsiyalar shu migratsiya bilan chegaralangan (headga
//  emas, AYNAN shu migratsiyaga migrate qilinadi) — docs/modules/finance-parity.md
//  §9 dagi ogohlantirish: "scope your assertions to your own migration".

[Collection(SchoolLmsCollection.Name)]
public class ExpenseTemplatesMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260918000559_CashBoxes";
    private const string ThisMigration = "20260918052540_ExpenseTemplates";

    private static readonly (string Column, string DataType, string Nullable)[] ExpectedColumns =
    [
        ("id", "uuid", "NO"),
        ("name", "text", "NO"),
        ("category", "text", "NO"),
        ("amount", "numeric", "NO"),
        ("day_of_month", "smallint", "NO"),
        ("is_active", "boolean", "NO"),
        ("created_at", "timestamp with time zone", "NO"),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("expensetpl");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_expense_templates_jadvalini_deklaratsiya_qilingan_ustunlar_bilan_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.True(await TableExistsAsync(conn, "expense_templates"), "`expense_templates` jadvali yo'q");

        foreach (var (column, dataType, nullable) in ExpectedColumns)
        {
            var row = await ColumnAsync(conn, "expense_templates", column);
            Assert.True(row is not null, $"`expense_templates.{column}` yo'q");
            Assert.Equal(dataType, row!.Value.DataType);
            Assert.Equal(nullable, row.Value.Nullable);
        }
    }

    [Fact]
    public async Task Up_uchta_check_va_indeksni_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var name in new[]
                 {
                     "ck_expense_templates_amount",
                     "ck_expense_templates_name",
                     "ck_expense_templates_day_of_month",
                 })
            Assert.True(await ConstraintExistsAsync(conn, "expense_templates", name), $"`{name}` yo'q");

        Assert.True(
            await IndexExistsAsync(conn, "expense_templates", "ix_expense_templates_is_active_day_of_month"),
            "`ix_expense_templates_is_active_day_of_month` yo'q");
    }

    /// <summary>
    /// Uchta CHECK haqiqatan ISHLASHINI tekshiradi — mavjudligi kifoya emas,
    /// noto'g'ri qiymat HAQIQATAN rad etilishi kerak.
    /// </summary>
    [Fact]
    public async Task Check_cheklovlari_notogri_qiymatlarni_rad_etadi()
    {
        await using var conn = await OpenOwnerAsync();

        var badAmount = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into expense_templates (name, category, amount, day_of_month) "
            + "values ('Internet', 'utilities', 0, 5)"));
        Assert.Equal("23514", badAmount.SqlState);

        var badName = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into expense_templates (name, category, amount, day_of_month) "
            + "values ('   ', 'utilities', 100000, 5)"));
        Assert.Equal("23514", badName.SqlState);

        var badDay = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            "insert into expense_templates (name, category, amount, day_of_month) "
            + "values ('Internet', 'utilities', 100000, 29)"));
        Assert.Equal("23514", badDay.SqlState);

        // To'g'ri qator — muammosiz o'tadi va `is_active`/`id`/`created_at`
        // sukut qiymatlari o'z ishini qiladi.
        await ExecAsync(conn,
            "insert into expense_templates (name, category, amount, day_of_month) "
            + "values ('Internet', 'utilities', 350000, 5)");
        Assert.Equal(1L, await ScalarAsync(conn,
            "select count(*) from expense_templates where is_active = true and id is not null and created_at is not null"));
    }

    [Fact]
    public async Task App_rw_yangi_jadvalda_tolik_crud_qila_oladi()
    {
        _database.RequireRealAppRw();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        await ExecAsync(app,
            "insert into expense_templates (id, name, category, amount, day_of_month, is_active) "
            + "values (gen_random_uuid(), 'Ijara', 'rent', 4500000, 10, true)");

        await ExecAsync(app, "update expense_templates set amount = 4700000 where name = 'Ijara'");
        Assert.Equal(1L, await ScalarAsync(app,
            "select count(*) from expense_templates where name = 'Ijara' and amount = 4700000"));

        // To'liq CRUD — moliyaviy jadval EMAS (SPEC §4.1 bu yerga tegishli
        // emas), ya'ni DELETE ham ishlashi kerak.
        await ExecAsync(app, "delete from expense_templates where name = 'Ijara'");
        Assert.Equal(0L, await ScalarAsync(app, "select count(*) from expense_templates where name = 'Ijara'"));
    }

    [Fact]
    public async Task Down_expense_templates_jadvalini_olib_tashlaydi_va_boshqa_narsaga_tegmaydi()
    {
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.False(await TableExistsAsync(conn, "expense_templates"),
                "`expense_templates` `Down()` dan keyin qoldi");
        }

        await MigrateToAsync(ThisMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            // `expense_templates` o'zi qaytadan paydo bo'ladi (Up qayta
            // yurdi) — uni HISOBGA OLMAYDIGAN taqqoslash: faqat undan
            // TASHQARI hech narsa yo'qolmagan bo'lishi kerak.
            var after = await AllColumnsAsync(conn);
            var lost = before.Except(after)
                .Where(c => !c.StartsWith("expense_templates.", StringComparison.Ordinal))
                .ToList();
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
}
