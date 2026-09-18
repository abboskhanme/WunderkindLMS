using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `PayrollAdjustments` migratsiyasi — docs/modules/finance-parity.md §3.2
//  (Batch B, B2/B3), §2.11 (F11.01, F11.02); SPEC §4.1.
// ===========================================================================
//
//  NEGA BU TESTLAR BOR
//  --------------------
//  Loyiha qoidasi: model o'zgarsa — migratsiya testi MAJBURIY. Bu ikkita
//  yangi jadval — `adjustment_reasons` (F11.02, oddiy katalog) va
//  `payroll_adjustments` (F11.01, PUL yozuvi: xodimning oylik hisob-kitobiga
//  kiradigan summa).
//
//  BU FAYLDA NIMA ISBOTLANADI
//  --------------------------
//  1. `Up()` toza bazada yuradi: ikkita jadval, ustunlari va DEFAULT'lari
//     bilan (finance-parity §3.2 B2/B3).
//  2. Check constraint'lar HAQIQATDA to'xtatadi: nol/manfiy summa, noto'g'ri
//     kind, ikkalasi ham/hech biri xodim identifikatsiyasi, sababsiz storno.
//  3. KOMPOZIT FK: `payroll_adjustments.(reason_id, kind)` faqat O'ZINING
//     kind'iga mos sababga ishora qila oladi — bonus yozuvga jarima sababini
//     bog'lab bo'lmaydi, hatto xizmat qatlami chetlab o'tilsa ham.
//  4. GRANTLAR: `app_rw` `payroll_adjustments` ga qator QO'SHA OLADI, lekin
//     TAHRIRLAY va O'CHIRA OLMAYDI (42501) — SPEC §4.1. `adjustment_reasons`
//     esa to'liq CRUD (katalog, moliyaviy emas).
//  5. `Down()` AYNAN `Up()` yaratganini qaytaradi va birorta MAVJUD ustunni
//     yo'qotmaydi.
//
//  HAR TEST O'Z BAZASIDA (xUnit klass-boshiga-bitta-nusxa qoidasi bilan bir xil).
// ===========================================================================

/// <summary>
/// `PayrollAdjustments` migratsiyasining o'zi — qo'llanishi, orqaga qaytishi
/// va grantlari. Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PayrollAdjustmentsMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Tekshirilayotgan migratsiya.</summary>
    private const string ThisMigration = "20260917180810_PayrollAdjustments";

    /// <summary>Undan oldingisi — `Down()` shu nuqtaga qaytaradi.</summary>
    private const string PreviousMigration = "20260917143939_StudentsParityP2";

    /// <summary>Huquq rad etilganda PostgreSQL qaytaradigan kod.</summary>
    private const string PermissionDenied = "42501";

    /// <summary>CHECK constraint qaytaradigan kod.</summary>
    private const string CheckViolation = "23514";

    /// <summary>FOREIGN KEY buzilganda qaytadigan kod.</summary>
    private const string ForeignKeyViolation = "23503";

    private static readonly string[] NewTables = ["adjustment_reasons", "payroll_adjustments"];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("payrolladj");

    /// <summary>Har test O'Z bazasini oladi — hovuz ikkalasi ham yopiladi (owner va app_rw).</summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. `Up()` — jadvallar, ustunlar
    // =====================================================================

    [Fact]
    public async Task Up_ikkita_yangi_jadvalni_yaratadi()
    {
        await using var db = NewDb();
        Assert.Contains(ThisMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"`{table}` jadvali yaratilmagan.");
    }

    /// <summary>
    /// Ustunlar — turi va nullability bilan, finance-parity §3.2 B2/B3 DDL
    /// bloklaridan AYNAN ko'chirilgan.
    /// </summary>
    [Fact]
    public async Task Yangi_jadval_ustunlari_spec_dagidek()
    {
        (string Table, string Column, string Type, bool Nullable)[] expected =
        [
            // B2 — adjustment_reasons
            ("adjustment_reasons", "id", "uuid", false),
            ("adjustment_reasons", "kind", "text", false),
            ("adjustment_reasons", "name", "text", false),
            ("adjustment_reasons", "is_active", "boolean", false),
            ("adjustment_reasons", "position", "integer", false),

            // B3 — payroll_adjustments. Xodim identifikatsiyasi: AYNAN bittasi
            // to'ladi (teacher_id yoki user_id), shuning uchun ikkalasi ham
            // null bo'la oladi.
            ("payroll_adjustments", "id", "uuid", false),
            ("payroll_adjustments", "teacher_id", "text", true),
            ("payroll_adjustments", "user_id", "text", true),
            ("payroll_adjustments", "kind", "text", false),
            ("payroll_adjustments", "reason_id", "uuid", false),
            ("payroll_adjustments", "amount", "numeric", false),
            ("payroll_adjustments", "period_year", "smallint", false),
            ("payroll_adjustments", "period_month", "smallint", false),
            ("payroll_adjustments", "comment", "text", true),
            ("payroll_adjustments", "image_url", "text", true),
            ("payroll_adjustments", "created_by", "text", false),
            ("payroll_adjustments", "created_at", "timestamp with time zone", false),
            ("payroll_adjustments", "reversal_of", "uuid", true),
            ("payroll_adjustments", "reversal_reason", "text", true),
        ];

        foreach (var (table, column, type, nullable) in expected)
        {
            var info = await ColumnAsync(table, column);
            Assert.True(info is not null, $"`{table}.{column}` ustuni yo'q.");
            Assert.Equal(type, info!.Value.Type);
            Assert.Equal(nullable, info.Value.Nullable);
        }

        // Pul — `numeric(14,2)` (Billing.cs izohi bo'yicha har moliya ustuni).
        Assert.Equal(14, await IntAsync(
            "select numeric_precision from information_schema.columns "
            + "where table_name = 'payroll_adjustments' and column_name = 'amount'"));
        Assert.Equal(2, await IntAsync(
            "select numeric_scale from information_schema.columns "
            + "where table_name = 'payroll_adjustments' and column_name = 'amount'"));

        Assert.Equal("gen_random_uuid()", (await ColumnAsync("adjustment_reasons", "id"))!.Value.Default);
        Assert.Equal("gen_random_uuid()", (await ColumnAsync("payroll_adjustments", "id"))!.Value.Default);
        Assert.Equal("now()", (await ColumnAsync("payroll_adjustments", "created_at"))!.Value.Default);
        Assert.Equal("true", (await ColumnAsync("adjustment_reasons", "is_active"))!.Value.Default);
    }

    [Fact]
    public async Task Constraintlar_va_indekslar_bazada_bor()
    {
        foreach (var name in new[]
                 {
                     "ck_adjustment_reasons_kind",
                     "ck_payroll_adjustments_amount",
                     "ck_payroll_adjustments_kind",
                     "ck_payroll_adjustments_identity",
                     "ck_payroll_adjustments_period_month",
                     "ck_payroll_adjustments_period_year",
                     "ck_payroll_adjustments_reversal",
                     "ck_payroll_adjustments_reversal_not_self",
                 })
            Assert.True(await ConstraintExistsAsync(name), $"`{name}` check constraint'i yo'q.");

        foreach (var name in new[]
                 {
                     "ix_adjustment_reasons_kind_name",
                     "ix_payroll_adjustments_reversal_of",
                     "ix_payroll_adjustments_teacher_id_period_year_period_month",
                     "ix_payroll_adjustments_user_id_period_year_period_month",
                     "ix_payroll_adjustments_reason_id_kind",
                 })
            Assert.True(await IndexExistsAsync(name), $"`{name}` indeksi yo'q.");

        // Storno bitta yozuvni ikki marta bekor qilmasin — to'liq unikal.
        Assert.True(
            await BoolAsync(
                "select indexdef like 'CREATE UNIQUE%' from pg_indexes "
                + "where indexname = 'ix_payroll_adjustments_reversal_of'"),
            "`ix_payroll_adjustments_reversal_of` unikal emas.");

        // Kompozit FK bazada bor — B3 ning "kind mos kelishi" kafolati.
        Assert.True(await BoolAsync(
            "select exists (select 1 from pg_constraint "
            + "where conname = 'fk_payroll_adjustments_adjustment_reasons_reason_id_kind')"));
    }

    /// <summary>
    /// Constraint'lar YOZILGANI yetarli emas — ular TISHLASHI kerak. Xizmat
    /// qatlamini chetlab o'tib, to'g'ridan-to'g'ri xom SQL yoziladi.
    /// </summary>
    [Fact]
    public async Task Constraintlar_haqiqatda_toxtatadi()
    {
        var world = await SeedAsync();
        await using var conn = await OpenOwnerAsync();

        // Nol/manfiy summa.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by) values ('{world.TeacherId}', 'bonus', "
            + $"'{world.BonusReasonId}', 0, {world.Year}, {world.Month}, '{world.CreatorId}')");

        // Noma'lum kind.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by) values ('{world.TeacherId}', 'gift', "
            + $"'{world.BonusReasonId}', 1000, {world.Year}, {world.Month}, '{world.CreatorId}')");

        // Ikkalasi ham to'lgan — na "faqat bitta" qoidasiga mos.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (teacher_id, user_id, kind, reason_id, amount, "
            + $"period_year, period_month, created_by) values ('{world.TeacherId}', "
            + $"'{world.StaffUserId}', 'bonus', '{world.BonusReasonId}', 1000, {world.Year}, "
            + $"{world.Month}, '{world.CreatorId}')");

        // Ikkalasi ham bo'sh.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (kind, reason_id, amount, period_year, period_month, "
            + $"created_by) values ('bonus', '{world.BonusReasonId}', 1000, {world.Year}, "
            + $"{world.Month}, '{world.CreatorId}')");

        // Oy diapazondan tashqari.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by) values ('{world.TeacherId}', 'bonus', "
            + $"'{world.BonusReasonId}', 1000, {world.Year}, 13, '{world.CreatorId}')");

        // Storno havolasi bor-u sababi yo'q (yoki aksincha) — `ck_payroll_adjustments_reversal`.
        var originalId = await InsertAdjustmentAsync(conn, world, "bonus", world.BonusReasonId, 50_000m);
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by, reversal_of) values ('{world.TeacherId}', 'bonus', "
            + $"'{world.BonusReasonId}', 50000, {world.Year}, {world.Month}, '{world.CreatorId}', "
            + $"'{originalId}')");
    }

    /// <summary>
    /// KOMPOZIT FK: `payroll_adjustments.(reason_id, kind)` faqat sababning
    /// O'ZINING kind'iga mos yozuvga ishora qiladi. Bonus sababini jarima
    /// yozuviga (yoki aksincha) bog'lab bo'lmaydi — bazadagi FK 23503 bilan
    /// rad etadi, hatto xizmat qatlamidagi tekshiruv chetlab o'tilsa ham.
    /// </summary>
    [Fact]
    public async Task Sabab_va_yozuv_kindi_mos_kelmasa_FK_rad_etadi()
    {
        var world = await SeedAsync();
        await using var conn = await OpenOwnerAsync();

        // Bonus yozuv, lekin jarima sababi — mos kelmaydi.
        await AssertSqlStateAsync(ForeignKeyViolation, conn,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by) values ('{world.TeacherId}', 'bonus', "
            + $"'{world.PenaltyReasonId}', 1000, {world.Year}, {world.Month}, '{world.CreatorId}')");

        // To'g'ri juftlik — o'tadi.
        var id = await InsertAdjustmentAsync(conn, world, "bonus", world.BonusReasonId, 20_000m);
        Assert.Equal(1L, await CountAsync($"select count(*) from payroll_adjustments where id = '{id}'"));
    }

    // =====================================================================
    //  2. Grantlar — SPEC §4.1
    // =====================================================================

    /// <summary>
    /// `app_rw` `payroll_adjustments` ga qator QO'SHA OLADI, lekin TAHRIRLAY
    /// va O'CHIRA OLMAYDI. `adjustment_reasons` esa to'liq CRUD — katalog,
    /// moliyaviy emas.
    /// </summary>
    [Fact]
    public async Task App_rw_payroll_adjustments_ga_yozadi_lekin_tahrirlay_va_ochira_olmaydi()
    {
        _database.RequireRealAppRw();
        var world = await SeedAsync();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        var id = await InsertAdjustmentAsync(app, world, "penalty", world.PenaltyReasonId, 30_000m);

        await AssertSqlStateAsync(PermissionDenied, app,
            $"update payroll_adjustments set amount = 1 where id = '{id}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"delete from payroll_adjustments where id = '{id}'");
        await AssertSqlStateAsync(PermissionDenied, app, "truncate payroll_adjustments cascade");

        // Qator O'RNIDA.
        Assert.Equal(30_000m,
            (decimal)(await RawScalarAsync($"select amount from payroll_adjustments where id = '{id}'"))!);

        // ---- Storno: qator QO'SHISH orqali tuzatiladi (UPDATE emas) ----
        await ExecAsync(app,
            "insert into payroll_adjustments (teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by, reversal_of, reversal_reason) values "
            + $"('{world.TeacherId}', 'penalty', '{world.PenaltyReasonId}', 30000, {world.Year}, "
            + $"{world.Month}, '{world.CreatorId}', '{id}', 'Xato yozildi')");

        // ---- Katalog: to'liq CRUD ----
        var reasonId = Guid.NewGuid();
        await ExecAsync(app,
            $"insert into adjustment_reasons (id, kind, name) values ('{reasonId}', 'bonus', "
            + "'app_rw testi')");
        await ExecAsync(app, $"update adjustment_reasons set is_active = false where id = '{reasonId}'");
        await ExecAsync(app, $"delete from adjustment_reasons where id = '{reasonId}'");
        Assert.Equal(0L, await CountAsync($"select count(*) from adjustment_reasons where id = '{reasonId}'"));

        // ---- NAZORAT: boshqa moliyaviy jadval hali ham qulflangan ----
        Assert.False(
            await BoolAsync("select has_table_privilege('app_rw', 'public.payments', 'DELETE')"),
            "`payments` da DELETE paydo bo'lgan — SPEC §4.1 buzilgan.");
    }

    // =====================================================================
    //  3. `Down()` va ustun yo'qotmaslik
    // =====================================================================

    [Fact]
    public async Task Down_ikkala_jadvalni_ham_ochiradi_va_qayta_qollash_tiklaydi()
    {
        await MigrateToAsync(PreviousMigration);

        foreach (var table in NewTables)
            Assert.False(await TableExistsAsync(table), $"`Down()` dan keyin `{table}` qolib ketdi.");

        await using (var db = NewDb())
            Assert.DoesNotContain(ThisMigration, await db.Database.GetAppliedMigrationsAsync());

        await MigrateToAsync(null);

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"Qayta qo'llashdan keyin `{table}` yo'q.");
        Assert.True(await ConstraintExistsAsync("ck_payroll_adjustments_identity"));
    }

    /// <summary>
    /// EF'ning `--autogenerate` i model bilan snapshot kelishmaganda
    /// "ortiqcha" DROP chiqarishi mumkin. Shuning uchun bu test bitta-ikkita
    /// ustunni emas, BUTUN SXEMANI solishtiradi — moliya uchligi
    /// (`payments`, `payment_allocations`, `ledger_entries`) UMUMAN
    /// tegilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Migratsiya_birorta_mavjud_ustunni_yoqotmaydi()
    {
        await MigrateToAsync(PreviousMigration);
        var before = await AllColumnsAsync();

        // MUHIM: `null` (oxirigacha) EMAS, aynan `ThisMigration`. Test o'z
        // nomida ham, izohida ham "BU migratsiyada" deydi — oxirigacha yurish
        // keyin kelgan begona migratsiyalarni ham shu testga bog'lab qo'yadi.
        // `CashBoxes` aynan shunday qildi: u `payments` ga qonuniy ravishda
        // `cash_box_id` qo'shadi va quyidagi "pul jadvallari o'zgarmasin"
        // tekshiruvi shundan yiqilardi. Qo'shni `FinanceParityBatchA` testida
        // ham xuddi shu yechim.
        await MigrateToAsync(ThisMigration);
        var after = await AllColumnsAsync();

        var lost = before.Except(after).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(lost.Count == 0,
            "Migratsiya quyidagi ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));

        var added = after.Except(before).ToList();
        Assert.NotEmpty(added);
        foreach (var table in NewTables)
            Assert.Contains(added, c => c.StartsWith(table + ".", StringComparison.Ordinal));

        foreach (var table in new[] { "payments", "payment_allocations", "ledger_entries" })
        {
            var beforeCols = before.Where(c => c.StartsWith(table + ".", StringComparison.Ordinal)).ToHashSet();
            var afterCols = after.Where(c => c.StartsWith(table + ".", StringComparison.Ordinal)).ToHashSet();
            Assert.Equal(beforeCols, afterCols);
        }
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private readonly record struct World(
        string TeacherId, string StaffUserId, string CreatorId,
        Guid BonusReasonId, Guid PenaltyReasonId, short Year, short Month);

    /// <summary>Bitta o'qituvchi, bitta xodim (app_users), yozuvchi va ikkita sabab (bonus/jarima).</summary>
    private async Task<World> SeedAsync()
    {
        await using var db = NewDb();

        var teacher = new Teacher
        {
            FullName = "Bonus Testi O'qituvchi", Category = "1",
            SalaryStartDate = "2023-09-01", SalaryStartMonth = "2023-09",
        };
        var staff = new AppUser
        {
            FullName = "Bonus Testi Xodim", Role = Roles.Staff, Email = $"staff.{Guid.NewGuid():N}",
        };
        var creator = new AppUser
        {
            FullName = "Admin", Role = Roles.Admin, Email = $"adm.{Guid.NewGuid():N}",
        };
        db.Teachers.Add(teacher);
        db.Users.AddRange(staff, creator);

        var bonusReason = new AdjustmentReason { Kind = AdjustmentKind.Bonus, Name = "Yaxshi ish" };
        var penaltyReason = new AdjustmentReason { Kind = AdjustmentKind.Penalty, Name = "Kechikish" };
        db.AdjustmentReasons.AddRange(bonusReason, penaltyReason);

        await db.SaveChangesAsync();

        var today = AppClock.Today;
        return new World(
            teacher.Id, staff.Id, creator.Id,
            bonusReason.Id, penaltyReason.Id,
            (short)today.Year, (short)today.Month);
    }

    private static async Task<Guid> InsertAdjustmentAsync(
        NpgsqlConnection conn, World world, string kind, Guid reasonId, decimal amount)
    {
        var id = Guid.NewGuid();
        await ExecAsync(conn,
            "insert into payroll_adjustments (id, teacher_id, kind, reason_id, amount, period_year, "
            + $"period_month, created_by) values ('{id}', '{world.TeacherId}', '{kind}', '{reasonId}', "
            + $"{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {world.Year}, "
            + $"{world.Month}, '{world.CreatorId}')");
        return id;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<PostgresException> AssertSqlStateAsync(
        string expected, NpgsqlConnection conn, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, sql));
        Assert.Equal(expected, error.SqlState);
        return error;
    }

    private readonly record struct ColumnInfo(string Type, bool Nullable, string? Default);

    private async Task<ColumnInfo?> ColumnAsync(string table, string column)
    {
        await using var conn = await OpenOwnerAsync();
        await using var cmd = new NpgsqlCommand(
            "select data_type, is_nullable, column_default from information_schema.columns "
            + "where table_schema = 'public' and table_name = @t and column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ColumnInfo(
            reader.GetString(0),
            reader.GetString(1) == "YES",
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<HashSet<string>> AllColumnsAsync()
    {
        var rows = await ListAsync(
            "select table_name || '.' || column_name from information_schema.columns "
            + "where table_schema = 'public' and table_name <> '__EFMigrationsHistory'");
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    private Task<bool> TableExistsAsync(string table) => BoolAsync(
        "select exists (select 1 from information_schema.tables "
        + $"where table_schema = 'public' and table_name = '{table}')");

    private Task<bool> IndexExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_indexes where schemaname = 'public' and indexname = '{name}')");

    private Task<bool> ConstraintExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_constraint where conname = '{name}')");

    private async Task<object?> RawScalarAsync(string sql)
    {
        await using var conn = await OpenOwnerAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task<bool> BoolAsync(string sql) => (bool)(await RawScalarAsync(sql))!;

    private async Task<long> CountAsync(string sql) => (long)(await RawScalarAsync(sql))!;

    private async Task<int> IntAsync(string sql) => (int)(await RawScalarAsync(sql))!;

    private async Task<List<string>> ListAsync(string sql)
    {
        await using var conn = await OpenOwnerAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
