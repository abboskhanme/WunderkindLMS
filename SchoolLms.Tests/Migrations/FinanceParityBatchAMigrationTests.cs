using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `FinanceParityBatchA` migratsiyasi — docs/modules/finance-parity.md
//  §3.1 (A1…A5) va §3.4; SPEC §4.1.
// ===========================================================================
//
//  NEGA MIGRATSIYAGA ALOHIDA TEST YOZILADI
//  ---------------------------------------
//  Loyiha qoidasi: model o'zgarsa — migratsiya testi MAJBURIY. Migratsiya
//  jonli maktab bazasida bir marta yuradi va u yerda "orqaga qaytarish"
//  tugmasi yo'q. Ilova testlari migratsiyani KO'RMAYDI — ular baza
//  allaqachon to'g'ri degan taxmin ustida ishlaydi.
//
//  BU FAYLDA NIMA ISBOTLANADI
//  --------------------------
//  1. `Up()` toza bazada yuradi: uchta yangi jadval va `expenses` ning yangi
//     ustuni — turi, nullability va DEFAULT'i bilan (§3.1 dagi DDL bloklari).
//  2. MIGRATSIYADAN OLDIN yozilgan chiqim SMENASIZ omon qoladi. Bu eng
//     qimmat tasdiq: `expenses` jonli jadval va undagi har bir mavjud qator
//     yaroqli bo'lib qolishi SHART (§3.1 A1 — "no retro check on old rows").
//  3. GRANTLAR HAQIQATDA ishlaydi: `app_rw` uchta append-only jadvalga qator
//     QO'SHA OLADI, lekin ularni TAHRIRLAY va O'CHIRA OLMAYDI (42501).
//     NAZORAT sifatida `payments` dan DELETE hali ham yopiq — busiz bu
//     testlar "hamma narsaga ruxsat berilgan" bazada ham yashil bo'lardi.
//  4. `student_refunds` ning ustun darajasidagi GRANT'i: to'rtta "qaror"
//     ustuni yoziladi, summasi esa YOZILMAYDI.
//  5. Qaytarim QULFI (trigger) qaror qo'yilgach har qanday keyingi
//     o'zgarishni rad etadi — `app_rw` uchun ham, EGA rol uchun ham.
//     Trigger grantdan kuchliroq: `REVOKE` egaga ta'sir qilmaydi (SPEC §4.1
//     dagi tuzatish), trigger esa qiladi.
//  6. A5 — `ledger_entries.ref_type` bazada CHEKLANMAGAN, ya'ni yangi
//     `cash_handover` va `refund` qiymatlari DDL'siz yoziladi.
//  7. `Down()` AYNAN `Up()` yaratganini qaytaradi — jumladan qulf
//     FUNKSIYASINI, u jadval bilan birga ketmaydi.
//  8. Birorta MAVJUD jadval birorta ustunini yo'qotmaydi.
//
//  HAR TEST O'Z BAZASIDA
//  ---------------------
//  Quyidagi testlarning uchtasi migratsiyani orqaga qaytaradi — ya'ni
//  jadvallarni O'CHIRADI. Umumiy bazada bu boshqa test klassini o'rtasida
//  yiqitardi. xUnit har test uchun klassning yangi nusxasini yaratadi.
// ===========================================================================

/// <summary>
/// `FinanceParityBatchA` migratsiyasining o'zi — qo'llanishi, orqaga
/// qaytishi, grantlari va qaytarim qulfi. Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class FinanceParityBatchAMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Tekshirilayotgan migratsiya.</summary>
    private const string ThisMigration = "20260917100831_FinanceParityBatchA";

    /// <summary>Undan oldingisi — `Down()` shu nuqtaga qaytaradi.</summary>
    private const string PreviousMigration = "20260917084103_StudentsParityP1";

    /// <summary>Huquq rad etilganda PostgreSQL qaytaradigan kod.</summary>
    private const string PermissionDenied = "42501";

    /// <summary>CHECK constraint va trigger qaytaradigan kod.</summary>
    private const string CheckViolation = "23514";

    /// <summary>Migratsiya yaratadigan uchta jadval (§3.1 A2, A3, A4).</summary>
    private static readonly string[] NewTables =
        ["cash_handovers", "student_refunds", "expense_attachments"];

    /// <summary>Migratsiya MAVJUD jadvalga qo'shadigan yagona ustun (§3.1 A1).</summary>
    private static readonly (string Table, string Column)[] NewColumns =
        [("expenses", "cash_shift_id")];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("finparity");

    /// <summary>
    /// Har test O'Z bazasini oladi, ya'ni O'Z ulanish hovuzini ham. Hovuz
    /// tozalanmasa, tugagan testning ulanishlari ochiq qolib konteynerdagi
    /// <c>max_connections</c> ni yeb qo'yadi va KEYINGI test klasslari
    /// <c>53300</c> bilan yiqiladi. IKKITA satr yopiladi — owner va
    /// <c>app_rw</c>.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. `Up()` — jadvallar, ustunlar, indekslar
    // =====================================================================

    /// <summary>Migratsiya zanjiri oxirigacha yuradi va uchta jadval paydo bo'ladi.</summary>
    [Fact]
    public async Task Up_uchta_yangi_jadvalni_yaratadi()
    {
        await using var db = NewDb();
        Assert.Contains(ThisMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"`{table}` jadvali yaratilmagan.");
    }

    /// <summary>
    /// Yangi jadvallardagi ustunlar — turi va nullability bilan. §3.1 ning
    /// A2/A3/A4 DDL bloklaridan AYNAN ko'chirilgan; bu yerdagi har bir qator
    /// o'sha hujjatning bitta bo'lagiga to'g'ri keladi.
    /// </summary>
    [Fact]
    public async Task Yangi_jadval_ustunlari_spec_dagidek()
    {
        // (jadval, ustun, tur, null bo'la oladimi)
        (string Table, string Column, string Type, bool Nullable)[] expected =
        [
            // §3.1 A2 — cash_handovers
            ("cash_handovers", "id", "uuid", false),
            ("cash_handovers", "cash_shift_id", "uuid", false),
            ("cash_handovers", "amount", "numeric", false),
            ("cash_handovers", "destination", "text", false),
            ("cash_handovers", "note", "text", true),
            ("cash_handovers", "created_by", "text", false),
            ("cash_handovers", "created_at", "timestamp with time zone", false),
            ("cash_handovers", "reversal_of", "uuid", true),

            // §3.1 A3 — student_refunds. To'rtta "qaror" ustuni null bo'la
            // oladi: qaror hali qabul qilinmagan bo'lishi mumkin.
            ("student_refunds", "id", "uuid", false),
            ("student_refunds", "student_id", "text", false),
            ("student_refunds", "amount", "numeric", false),
            ("student_refunds", "method", "text", false),
            ("student_refunds", "reason", "text", false),
            ("student_refunds", "requested_by", "text", false),
            ("student_refunds", "requested_at", "timestamp with time zone", false),
            ("student_refunds", "approved_by", "text", true),
            ("student_refunds", "approved_at", "timestamp with time zone", true),
            ("student_refunds", "cash_shift_id", "uuid", true),
            ("student_refunds", "rejected_reason", "text", true),
            ("student_refunds", "reversal_of", "uuid", true),

            // §3.1 A4 — expense_attachments
            ("expense_attachments", "id", "uuid", false),
            ("expense_attachments", "expense_id", "uuid", false),
            ("expense_attachments", "file_url", "text", false),
            ("expense_attachments", "file_name", "text", false),
            ("expense_attachments", "content_type", "text", false),
            ("expense_attachments", "size_bytes", "bigint", false),
            ("expense_attachments", "uploaded_by", "text", false),
            ("expense_attachments", "uploaded_at", "timestamp with time zone", false),
        ];

        foreach (var (table, column, type, nullable) in expected)
        {
            var info = await ColumnAsync(table, column);
            Assert.True(info is not null, $"`{table}.{column}` ustuni yo'q.");
            Assert.Equal(type, info!.Value.Type);
            Assert.Equal(nullable, info.Value.Nullable);
        }

        // Pul — `numeric(14,2)`, moliyadagi HAR ustun kabi (Billing.cs izohi).
        // `numeric` ning o'zi cheksiz aniqlikni ham anglatishi mumkin, ya'ni
        // aniqlikni alohida tekshirish kerak: 14,2 bo'lmasa so'm tiyinlari
        // yaxlitlanib, hisobot jimgina siljirdi.
        foreach (var table in new[] { "cash_handovers", "student_refunds" })
        {
            Assert.Equal(14, await IntAsync(
                "select numeric_precision from information_schema.columns "
                + $"where table_name = '{table}' and column_name = 'amount'"));
            Assert.Equal(2, await IntAsync(
                "select numeric_scale from information_schema.columns "
                + $"where table_name = '{table}' and column_name = 'amount'"));
        }

        // Id va vaqt belgisining DEFAULT'i — §3.1 DDL'ida aynan shunday.
        Assert.Equal("gen_random_uuid()", (await ColumnAsync("cash_handovers", "id"))!.Value.Default);
        Assert.Equal("gen_random_uuid()", (await ColumnAsync("student_refunds", "id"))!.Value.Default);
        Assert.Equal("gen_random_uuid()", (await ColumnAsync("expense_attachments", "id"))!.Value.Default);
        Assert.Equal("now()", (await ColumnAsync("cash_handovers", "created_at"))!.Value.Default);
        Assert.Equal("now()", (await ColumnAsync("student_refunds", "requested_at"))!.Value.Default);
        Assert.Equal("now()", (await ColumnAsync("expense_attachments", "uploaded_at"))!.Value.Default);
    }

    /// <summary>
    /// A1 — `expenses.cash_shift_id`. NULL BO'LA OLADI va DEFAULT'i YO'Q:
    /// bugungacha yozilgan har bir chiqimda smena yo'q va ularga smena
    /// TAXMIN QILINMAYDI (§3.1 A1: "no retro check on old rows").
    /// </summary>
    [Fact]
    public async Task Expenses_cash_shift_id_nullable_va_indeksli()
    {
        var info = await ColumnAsync("expenses", "cash_shift_id");
        Assert.True(info is not null, "`expenses.cash_shift_id` ustuni yo'q.");
        Assert.Equal("uuid", info!.Value.Type);
        Assert.True(info.Value.Nullable, "Ustun NOT NULL bo'lib qolgan — eski chiqimlar yiqilardi.");
        Assert.Null(info.Value.Default);

        Assert.True(await IndexExistsAsync("ix_expenses_cash_shift_id"),
            "`(cash_shift_id)` indeksi yo'q — kutilgan naqdni hisoblash to'liq skan bo'lardi.");

        // FK bor va u RESTRICT: chiqimi bor smena o'chirilmaydi.
        Assert.Equal("RESTRICT", await TextAsync(
            "select rc.delete_rule from information_schema.referential_constraints rc "
            + "where rc.constraint_name = 'fk_expenses_cash_shifts_cash_shift_id'"));

        // Eski ustunlar JOYIDA — bu qo'shimcha migratsiya, qayta yozish emas.
        foreach (var column in new[] { "teacher_id", "approved_by", "created_by", "category", "amount" })
            Assert.True(await ColumnAsync("expenses", column) is not null,
                $"`expenses.{column}` yo'qolgan.");
    }

    /// <summary>
    /// Check constraint'lar va indekslar. Ular EF modelida
    /// (`FinanceParityModel.cs`) yozilgan, ya'ni snapshot'ga tushadi va
    /// keyingi `--autogenerate` ularni "ortiqcha" deb DROP qilmaydi — lekin
    /// ularning BAZADA borligini faqat shu test tekshiradi.
    /// </summary>
    [Fact]
    public async Task Constraintlar_va_indekslar_bazada_bor()
    {
        foreach (var name in new[]
                 {
                     "ck_cash_handovers_amount",
                     "ck_cash_handovers_destination",
                     "ck_cash_handovers_reversal_not_self",
                     "ck_student_refunds_amount",
                     "ck_student_refunds_method",
                     "ck_student_refunds_reason",
                     "ck_student_refunds_approver_differs",
                     "ck_student_refunds_cash_shift",
                     "ck_expense_attachments_size",
                 })
            Assert.True(await ConstraintExistsAsync(name), $"`{name}` check constraint'i yo'q.");

        foreach (var name in new[]
                 {
                     "ix_expenses_cash_shift_id",
                     "ix_cash_handovers_cash_shift_id",
                     "ix_cash_handovers_reversal_of",
                     "ix_student_refunds_student_id",
                     "ix_student_refunds_reversal_of",
                     "ix_expense_attachments_expense_id",
                 })
            Assert.True(await IndexExistsAsync(name), $"`{name}` indeksi yo'q.");

        // Storno indekslari UNIKAL: bitta topshiriq/qaytarimni ikki marta
        // bekor qilib, kutilgan naqdni ikki barobar "qaytarish" yopiq.
        foreach (var name in new[] { "ix_cash_handovers_reversal_of", "ix_student_refunds_reversal_of" })
            Assert.True(
                await BoolAsync($"select indexdef like 'CREATE UNIQUE%' from pg_indexes where indexname = '{name}'"),
                $"`{name}` unikal emas.");
    }

    /// <summary>
    /// Constraint'lar YOZILGANI yetarli emas — ular TISHLASHI kerak. Bu test
    /// xizmat qatlamini BUTUNLAY chetlab o'tadi va xom SQL yozadi (aynan
    /// chetlab o'tishga uringan odam shunday qiladi).
    /// </summary>
    [Fact]
    public async Task Constraintlar_haqiqatda_toxtatadi()
    {
        var world = await SeedAsync();
        await using var conn = await OpenOwnerAsync();

        // Manfiy/nol topshiriq — pul "chiqarib" kutilgan naqdni oshirish yo'li.
        await AssertSqlStateAsync(CheckViolation, conn,
            $"insert into cash_handovers (cash_shift_id, amount, destination, created_by) "
            + $"values ('{world.ShiftId}', 0, 'bank', '{world.RequesterId}')");

        // Uchinchi manzil yo'q (§5 Q1: bank yoki seyf).
        await AssertSqlStateAsync(CheckViolation, conn,
            $"insert into cash_handovers (cash_shift_id, amount, destination, created_by) "
            + $"values ('{world.ShiftId}', 100, 'cho''ntak', '{world.RequesterId}')");

        // Sababsiz qaytarim — tekshirib bo'lmaydigan qaytarim.
        await AssertSqlStateAsync(CheckViolation, conn,
            $"insert into student_refunds (student_id, amount, method, reason, requested_by) "
            + $"values ('{world.StudentId}', 100, 'cash', '   ', '{world.RequesterId}')");

        // SPEC §4.5 — so'ragan odam o'zi tasdiqlay olmaydi.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into student_refunds (student_id, amount, method, reason, requested_by, "
            + "approved_by, approved_at, cash_shift_id) values "
            + $"('{world.StudentId}', 100, 'cash', 'Ortiqcha to''lov', '{world.RequesterId}', "
            + $"'{world.RequesterId}', now(), '{world.ShiftId}')");

        // Naqd qaytarim SMENASIZ tasdiqlanmaydi — busiz F1.03 ning aynan
        // o'zi qaytarimda takrorlanardi.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into student_refunds (student_id, amount, method, reason, requested_by, "
            + "approved_by, approved_at) values "
            + $"('{world.StudentId}', 100, 'cash', 'Ortiqcha to''lov', '{world.RequesterId}', "
            + $"'{world.ApproverId}', now())");

        // Naqd BO'LMAGAN qaytarim esa smenasiz ham tasdiqlanadi — pul
        // kassadan chiqmaydi. NAZORAT: yuqoridagi tasdiq "hamma narsa
        // taqiqlangan" bazada ham yashil bo'lardi.
        await ExecAsync(conn,
            "insert into student_refunds (student_id, amount, method, reason, requested_by, "
            + "approved_by, approved_at) values "
            + $"('{world.StudentId}', 100, 'transfer', 'Bankka qaytarildi', '{world.RequesterId}', "
            + $"'{world.ApproverId}', now())");

        // Nol baytli "dalil" dalil emas.
        await AssertSqlStateAsync(CheckViolation, conn,
            "insert into expense_attachments (expense_id, file_url, file_name, content_type, "
            + $"size_bytes, uploaded_by) values ('{world.ExpenseId}', '/u/a.pdf', 'a.pdf', "
            + $"'application/pdf', 0, '{world.RequesterId}')");
    }

    // =====================================================================
    //  2. Eski qatorlar — ENG MUHIM TASDIQ
    // =====================================================================

    /// <summary>
    /// MIGRATSIYADAN OLDIN mavjud bo'lgan chiqim yangi ustunda qanday qiymat
    /// oladi.
    ///
    /// <para>
    /// Test migratsiyani ORQAGA qaytaradi, o'sha "eski" holatda chiqim
    /// qatorini yozadi, so'ng yana OLDINGA yuradi. Shundan keyingina savol
    /// haqiqiy bo'ladi: jonli maktab bazasidagi yuzlab chiqim migratsiyadan
    /// keyin ham yaroqlimi? Javob HA bo'lishi shart (§3.1 A1) — aks holda
    /// migratsiya `NOT NULL` bilan yiqilardi yoki, undan ham yomoni, eski
    /// chiqimlarga "qaysidir smena" taxmin qilinib kutilgan naqd orqaga
    /// qarab buzilardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Migratsiyadan_oldin_yozilgan_chiqim_smenasiz_omon_qoladi()
    {
        await MigrateToAsync(PreviousMigration);

        // Ustun hali YO'Q — ya'ni quyidagi qator haqiqatan "eski".
        Assert.Null(await ColumnAsync("expenses", "cash_shift_id"));

        var userId = Guid.NewGuid().ToString();
        var expenseId = Guid.NewGuid();
        await using (var conn = await OpenOwnerAsync())
        {
            await InsertMinimalRowAsync(conn, "users", new()
            {
                ["id"] = $"'{userId}'",
                ["full_name"] = "'Eski admin'",
                ["role"] = "'admin'",
                ["email"] = $"'eski.{Guid.NewGuid():N}'",
            });
            await InsertMinimalRowAsync(conn, "expenses", new()
            {
                ["id"] = $"'{expenseId}'",
                ["on_date"] = "date '2026-03-14'",
                ["category"] = "'utilities'",
                ["amount"] = "1234567.89",
                ["created_by"] = $"'{userId}'",
                ["created_at"] = "now()",
            });
        }

        await MigrateToAsync(null);

        // Qator JOYIDA va smenasi YO'Q.
        Assert.Equal(1L, await CountAsync($"select count(*) from expenses where id = '{expenseId}'"));
        Assert.Null(await RawScalarAsync($"select cash_shift_id from expenses where id = '{expenseId}'"));

        // Eski ma'lumot o'zgarmagan — na summasi, na toifasi, na sanasi.
        Assert.Equal("utilities", await TextAsync($"select category from expenses where id = '{expenseId}'"));
        Assert.Equal(1234567.89m,
            (decimal)(await RawScalarAsync($"select amount from expenses where id = '{expenseId}'"))!);
        Assert.Equal("2026-03-14",
            await TextAsync($"select to_char(on_date, 'YYYY-MM-DD') from expenses where id = '{expenseId}'"));
    }

    // =====================================================================
    //  3. Grantlar — §3.1 va §3.4
    // =====================================================================

    /// <summary>
    /// `app_rw` uchta yangi jadvalga qator QO'SHA OLADI, lekin ularni
    /// TAHRIRLAY va O'CHIRA OLMAYDI.
    ///
    /// <para>
    /// Harness rolga eng yomon holatni (hamma joyda to'liq CRUD) beradi —
    /// ya'ni bu yerdagi har bir "yo'q" AYNAN migratsiyadagi `REVOKE` ning
    /// natijasi. Pastda NAZORAT ham bor: `payments` da DELETE hali ham
    /// yopiq, `expenses` da esa UPDATE hali ham ochiq.
    /// </para>
    /// </summary>
    [Fact]
    public async Task App_rw_append_only_jadvallarga_yozadi_lekin_tahrirlay_va_ochira_olmaydi()
    {
        _database.RequireRealAppRw();
        var world = await SeedAsync();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        // ---- INSERT ochiq ----
        var handoverId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into cash_handovers (id, cash_shift_id, amount, destination, note, created_by) "
            + $"values ('{handoverId}', '{world.ShiftId}', 500000, 'bank', 'Kunlik tushum', "
            + $"'{world.RequesterId}')");

        var refundId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{refundId}', '{world.StudentId}', 250000, 'cash', 'Ortiqcha to''lov', "
            + $"'{world.RequesterId}')");

        var attachmentId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into expense_attachments (id, expense_id, file_url, file_name, content_type, "
            + $"size_bytes, uploaded_by) values ('{attachmentId}', '{world.ExpenseId}', "
            + $"'/uploads/chek.pdf', 'chek.pdf', 'application/pdf', 20480, '{world.RequesterId}')");

        // ---- UPDATE va DELETE yopiq ----
        // Har biri ALOHIDA tasdiq: "42501 keldi" degani yetarli emas, xato
        // AYNAN kutilgan jadvalga tegishli bo'lishi kerak.
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update cash_handovers set amount = 1 where id = '{handoverId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"delete from cash_handovers where id = '{handoverId}'");
        await AssertSqlStateAsync(PermissionDenied, app, "truncate cash_handovers cascade");

        await AssertSqlStateAsync(PermissionDenied, app,
            $"delete from student_refunds where id = '{refundId}'");
        await AssertSqlStateAsync(PermissionDenied, app, "truncate student_refunds cascade");

        await AssertSqlStateAsync(PermissionDenied, app,
            $"update expense_attachments set file_url = '/uploads/boshqa.pdf' where id = '{attachmentId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"delete from expense_attachments where id = '{attachmentId}'");
        await AssertSqlStateAsync(PermissionDenied, app, "truncate expense_attachments cascade");

        // Qatorlar O'RNIDA: xato qaytib, o'zgarish baribir o'tib ketgan
        // bo'lishi mumkin bo'lgan holat e'tiborsiz qolmasin.
        Assert.Equal(500000m,
            (decimal)(await RawScalarAsync($"select amount from cash_handovers where id = '{handoverId}'"))!);
        Assert.Equal(1L, await CountAsync($"select count(*) from student_refunds where id = '{refundId}'"));
        Assert.Equal("/uploads/chek.pdf",
            await TextAsync($"select file_url from expense_attachments where id = '{attachmentId}'"));

        // ---- NAZORAT 1: moliyaviy REVOKE hali ham kuchda ----
        Assert.False(
            await BoolAsync("select has_table_privilege('app_rw', 'public.payments', 'DELETE')"),
            "`payments` da DELETE paydo bo'lgan — SPEC §4.1 buzilgan.");

        // ---- NAZORAT 2: `expenses` TEGILMAGAN ----
        // A1 faqat ustun qo'shadi; chiqim tarix emas, joriy holat — u
        // tasdiqlanadi va tuzatiladi, ya'ni to'liq CRUD joyida qolishi kerak.
        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            Assert.True(
                await BoolAsync($"select has_table_privilege('app_rw', 'public.expenses', '{privilege}')"),
                $"`expenses` da {privilege} yo'qolgan — A1 ortiqcha narsaga tegdi.");

        await ExecAsync(app,
            $"update expenses set cash_shift_id = '{world.ShiftId}' where id = '{world.ExpenseId}'");
    }

    /// <summary>
    /// §3.1 A3 — ustun darajasidagi GRANT. `app_rw` qaytarimning to'rtta
    /// "qaror" ustunini yoza oladi, qolganini esa YO'Q: summa, usul, sabab,
    /// so'rovchi va so'rov vaqti keyinchalik "tuzatib" qo'yilishi mumkin
    /// emas.
    /// </summary>
    [Fact]
    public async Task App_rw_qaytarim_qarorini_yozadi_lekin_summasini_ozgartira_olmaydi()
    {
        _database.RequireRealAppRw();
        var world = await SeedAsync();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        var refundId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{refundId}', '{world.StudentId}', 250000, 'cash', 'Ortiqcha to''lov', "
            + $"'{world.RequesterId}')");

        // ---- Ruxsat etilmagan ustunlar: har biri alohida ----
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set amount = 1 where id = '{refundId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set reason = 'Boshqa sabab' where id = '{refundId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set method = 'transfer' where id = '{refundId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set requested_by = '{world.ApproverId}' where id = '{refundId}'");
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set requested_at = now() where id = '{refundId}'");
        // Storno havolasi ham qulflangan: uni keyin qo'yish "bu qaytarim
        // allaqachon bekor qilingan edi" degan soxta tarix yasardi.
        await AssertSqlStateAsync(PermissionDenied, app,
            $"update student_refunds set reversal_of = null where id = '{refundId}'");

        // ---- Qaror ustunlari ochiq ----
        await ExecAsync(app,
            $"update student_refunds set approved_by = '{world.ApproverId}', approved_at = now(), "
            + $"cash_shift_id = '{world.ShiftId}' where id = '{refundId}'");

        Assert.Equal(world.ApproverId,
            await TextAsync($"select approved_by from student_refunds where id = '{refundId}'"));
        // Summa tegilmagan.
        Assert.Equal(250000m,
            (decimal)(await RawScalarAsync($"select amount from student_refunds where id = '{refundId}'"))!);

        // Rad etish yo'li ham ochiq — boshqa (hali qarorsiz) qatorda.
        var rejectedId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{rejectedId}', '{world.StudentId}', 90000, 'cash', 'Xato so''rov', "
            + $"'{world.RequesterId}')");
        await ExecAsync(app,
            $"update student_refunds set rejected_reason = 'Qoldiq yetarli emas' where id = '{rejectedId}'");
        Assert.Equal("Qoldiq yetarli emas",
            await TextAsync($"select rejected_reason from student_refunds where id = '{rejectedId}'"));
    }

    // =====================================================================
    //  4. Qaytarim qulfi (trigger)
    // =====================================================================

    /// <summary>
    /// §3.1 A3 — "a BEFORE UPDATE trigger refusing any change once
    /// <c>approved_at</c> or <c>rejected_reason</c> is set".
    ///
    /// <para>
    /// Nega grant yetarli emas: grant "yozsa bo'ladi" deydi, "faqat bir
    /// marta" demaydi. Usiz direktor qaytarimni tasdiqlab, keyin
    /// <c>cash_shift_id</c> ni BOSHQA smenaga ko'chirib qo'ya olardi — pul
    /// bir smenadan chiqib, boshqasidan chiqqan bo'lib ko'rinardi va ikkala
    /// smena ham "to'g'ri" yopilardi.
    /// </para>
    /// <para>
    /// Oxirgi tasdiq EGA rol bilan: `REVOKE` egaga UMUMAN ta'sir qilmaydi
    /// (SPEC §4.1 dagi tuzatish), trigger esa qiladi — ya'ni bu qulf
    /// grantdan kuchliroq va migratsiya rolida ham ishlaydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Qaytarim_qulfi_qaror_qoyilgach_har_qanday_ozgarishni_rad_etadi()
    {
        _database.RequireRealAppRw();
        var world = await SeedAsync();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        // ---- 1) Tasdiqlangan qaytarim ----
        var approvedId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{approvedId}', '{world.StudentId}', 250000, 'cash', 'Ortiqcha to''lov', "
            + $"'{world.RequesterId}')");
        await ExecAsync(app,
            $"update student_refunds set approved_by = '{world.ApproverId}', approved_at = now(), "
            + $"cash_shift_id = '{world.ShiftId}' where id = '{approvedId}'");

        // Smenani ALMASHTIRISH — aynan qulf to'xtatishi kerak bo'lgan harakat.
        var error = await AssertSqlStateAsync(CheckViolation, app,
            $"update student_refunds set cash_shift_id = '{world.SecondShiftId}' where id = '{approvedId}'");
        // Xabar matni `finance_parity_guards.sql` dan AYNAN olingan — S3
        // xizmati shu bo'yicha xato qaytaradi.
        Assert.Contains("Refund decision is final", error.Message, StringComparison.Ordinal);

        // "Tasdiqni rad etishga aylantirish" ham yopiq.
        await AssertSqlStateAsync(CheckViolation, app,
            $"update student_refunds set rejected_reason = 'Fikrimni o''zgartirdim' where id = '{approvedId}'");
        // Tasdiqlovchini almashtirish ham.
        await AssertSqlStateAsync(CheckViolation, app,
            $"update student_refunds set approved_by = '{world.RequesterId}' where id = '{approvedId}'");

        // Qator TEGILMAGAN.
        Assert.Equal(world.ShiftId.ToString(),
            (await RawScalarAsync($"select cash_shift_id from student_refunds where id = '{approvedId}'"))!
                .ToString());
        Assert.Null(await TextAsync($"select rejected_reason from student_refunds where id = '{approvedId}'"));

        // ---- 2) Rad etilgan qaytarim ham qulflanadi ----
        var rejectedId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{rejectedId}', '{world.StudentId}', 90000, 'cash', 'Xato so''rov', "
            + $"'{world.RequesterId}')");
        await ExecAsync(app,
            $"update student_refunds set rejected_reason = 'Qoldiq yetarli emas' where id = '{rejectedId}'");

        // Rad etilganni keyin "tasdiqlab" yuborib bo'lmaydi.
        await AssertSqlStateAsync(CheckViolation, app,
            $"update student_refunds set approved_by = '{world.ApproverId}', approved_at = now(), "
            + $"cash_shift_id = '{world.ShiftId}' where id = '{rejectedId}'");

        // ---- 3) EGA rol ham chetlab o'ta olmaydi ----
        await using var owner = await OpenOwnerAsync();
        // NAZORAT: ega uchun grant to'sig'i YO'Q — u `amount` ni ham
        // yozishga haqli. Ya'ni pastdagi rad etish AYNAN trigger'dan keladi.
        Assert.True(await BoolAsync(
            $"select has_column_privilege('{PostgresFixture.OwnerRole}', 'public.student_refunds', "
            + "'amount', 'UPDATE')"));
        await AssertSqlStateAsync(CheckViolation, owner,
            $"update student_refunds set amount = 1 where id = '{approvedId}'");

        // NAZORAT: qaror QO'YILMAGAN qator hali ham tahrirlanadi — ya'ni
        // trigger "hamma UPDATE'ni rad et" emas, "qarordan keyin rad et".
        var openId = Guid.NewGuid();
        await ExecAsync(app,
            "insert into student_refunds (id, student_id, amount, method, reason, requested_by) "
            + $"values ('{openId}', '{world.StudentId}', 10000, 'cash', 'Hali qaror yo''q', "
            + $"'{world.RequesterId}')");
        await ExecAsync(app,
            $"update student_refunds set cash_shift_id = '{world.ShiftId}' where id = '{openId}'");
    }

    // =====================================================================
    //  5. A5 — yangi ledger ref_type qiymatlari
    // =====================================================================

    /// <summary>
    /// §3.1 A5: "code only, no DDL — `ledger_entries.ref_type` has no DB
    /// check". Bu test o'sha taxminni TEKSHIRADI: agar kimdir keyin
    /// `ref_type` ga CHECK qo'shsa, `LedgerRefType` ning yangi ikkita
    /// qiymati jimgina 23514 bera boshlardi va kassa topshirig'i umuman
    /// jurnalga tushmasdi.
    /// </summary>
    [Fact]
    public async Task Yangi_ledger_ref_type_qiymatlari_bazaga_yoziladi()
    {
        _database.RequireRealAppRw();
        var world = await SeedAsync();

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        foreach (var refType in new[] { LedgerRefType.CashHandover, LedgerRefType.Refund })
        {
            // Juftlik bo'lib yoziladi — yolg'iz qator umumiy jurnalni
            // nomutanosib qoldirardi (LedgerImmutabilityTests dagi qoida).
            await ExecAsync(app,
                "insert into ledger_entries (entry_date, account, direction, amount, ref_type, "
                + "created_by, created_at) values "
                + $"(current_date, 'expense:other', 'debit', 100000, '{refType}', "
                + $"'{world.RequesterId}', now())");
            await ExecAsync(app,
                "insert into ledger_entries (entry_date, account, direction, amount, ref_type, "
                + "created_by, created_at) values "
                + $"(current_date, 'cash', 'credit', 100000, '{refType}', "
                + $"'{world.RequesterId}', now())");

            Assert.Equal(2L, await CountAsync(
                $"select count(*) from ledger_entries where ref_type = '{refType}'"));
        }

        // `ref_type` da CHECK constraint YO'Q — A5 ning butun asosi shu.
        Assert.Equal(0L, await CountAsync(
            "select count(*) from pg_constraint c join pg_class t on t.oid = c.conrelid "
            + "where t.relname = 'ledger_entries' and c.contype = 'c' "
            + "and pg_get_constraintdef(c.oid) like '%ref_type%'"));

        // NAZORAT: jurnal hali ham O'ZGARMAS.
        await AssertSqlStateAsync(PermissionDenied, app,
            "update ledger_entries set amount = 1 where ref_type = 'refund'");
    }

    // =====================================================================
    //  6. `Down()` va ustun yo'qotmaslik
    // =====================================================================

    /// <summary>
    /// `Down()` AYNAN `Up()` yaratganini qaytaradi: uchta jadval, bitta
    /// ustun, indeks, FK va QULF FUNKSIYASI. Funksiya alohida tekshiriladi,
    /// chunki u jadval bilan birga KETMAYDI — u sxema darajasidagi mustaqil
    /// obyekt va uni unutish `Down()` ni to'liqsiz qilardi.
    ///
    /// <para>
    /// So'ng migratsiya QAYTA qo'llanadi va hammasi joyiga qaytadi — ya'ni
    /// orqaga qaytish bir tomonlama emas.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Down_yaratilgan_hamma_narsani_orqaga_qaytaradi()
    {
        // Qulf funksiyasi `Up()` dan keyin BOR — pastdagi "yo'q" ma'noli
        // bo'lishi uchun avval borligi tasdiqlanadi.
        Assert.True(await FunctionExistsAsync("student_refunds_lock_decided"));

        await MigrateToAsync(PreviousMigration);

        foreach (var table in NewTables)
            Assert.False(await TableExistsAsync(table), $"`Down()` dan keyin `{table}` qolib ketdi.");

        foreach (var (table, column) in NewColumns)
            Assert.Null(await ColumnAsync(table, column));

        Assert.False(await IndexExistsAsync("ix_expenses_cash_shift_id"));
        Assert.False(
            await BoolAsync("select exists (select 1 from pg_constraint "
                            + "where conname = 'fk_expenses_cash_shifts_cash_shift_id')"),
            "`expenses` ning FK'si `Down()` dan keyin qolib ketdi.");

        Assert.False(await FunctionExistsAsync("student_refunds_lock_decided"),
            "Qulf funksiyasi yetim bo'lib qolib ketdi — `Down()` to'liq emas.");

        await using (var db = NewDb())
            Assert.DoesNotContain(ThisMigration, await db.Database.GetAppliedMigrationsAsync());

        // ---- va yana oldinga ----
        await MigrateToAsync(null);

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(table), $"Qayta qo'llashdan keyin `{table}` yo'q.");
        foreach (var (table, column) in NewColumns)
            Assert.True(await ColumnAsync(table, column) is not null, $"`{table}.{column}` qaytmadi.");
        Assert.True(await IndexExistsAsync("ix_expenses_cash_shift_id"));
        Assert.True(await FunctionExistsAsync("student_refunds_lock_decided"));
        Assert.True(await ConstraintExistsAsync("ck_student_refunds_cash_shift"));
    }

    /// <summary>
    /// ENG QIMMAT XATO SINFI: EF'ning `--autogenerate` i model bilan snapshot
    /// kelishmaganda "ortiqcha" <c>DROP COLUMN</c> chiqaradi va jonli maktab
    /// bazasida yo'qolgan ustunni qaytarib bo'lmaydi.
    ///
    /// <para>
    /// Shuning uchun bu test bitta-ikkita ustunni emas, BUTUN SXEMANI
    /// solishtiradi. Moliya jadvallariga alohida e'tibor: `payments`,
    /// `payment_allocations` va `ledger_entries` shu migratsiyada UMUMAN
    /// tegilmasligi kerak (§3.4 — "No DDL").
    /// </para>
    /// </summary>
    [Fact]
    public async Task Migratsiya_birorta_mavjud_ustunni_yoqotmaydi()
    {
        await MigrateToAsync(PreviousMigration);
        var before = await AllColumnsAsync();

        // MUHIM: `null` (oxirigacha) emas, AYNAN `ThisMigration` — bu test
        // FAQAT `FinanceParityBatchA` ning o'zi nimani o'zgartirishini
        // tekshiradi (§3.4 — "bu migratsiyada"). `null` boshida to'g'ri edi,
        // chunki o'sha payt bu migratsiya "oxirgi" edi; endi undan keyin
        // `CashBoxes` (2026-09-18) keladi va u `payments`/`expenses`/
        // `student_refunds` ga ATAYLAB (vazifa talabi bilan) nullable
        // `cash_box_id` qo'shadi — bu keyingi migratsiyaning ishi, shu
        // migratsiyaning emas, shuning uchun pastdagi "o'zgarmas uchlik"
        // tekshiruvi aynan shu migratsiya doirasida qolishi kerak.
        await MigrateToAsync(ThisMigration);
        var after = await AllColumnsAsync();

        var lost = before.Except(after).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(lost.Count == 0,
            "Migratsiya quyidagi ustun(lar)ni yo'qotdi: " + string.Join(", ", lost));

        // NAZORAT: to'plam haqiqatan o'sgan bo'lishi kerak, aks holda
        // yuqoridagi tasdiq hech narsa qilmagan migratsiyada ham yashil
        // bo'lardi.
        var added = after.Except(before).ToList();
        Assert.NotEmpty(added);
        foreach (var table in NewTables)
            Assert.Contains(added, c => c.StartsWith(table + ".", StringComparison.Ordinal));
        foreach (var (table, column) in NewColumns)
            Assert.Contains($"{table}.{column}", added);

        // §3.4 — o'zgarmas uchlik bu migratsiyada UMUMAN o'zgarmaydi.
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

    /// <summary>Testlar uchun minimal "dunyo": ikki foydalanuvchi, o'quvchi, ikki smena, chiqim.</summary>
    private readonly record struct World(
        string RequesterId, string ApproverId, string StudentId,
        Guid ShiftId, Guid SecondShiftId, Guid ExpenseId);

    /// <summary>
    /// Ikkita foydalanuvchi ATAYLAB: <c>ck_student_refunds_approver_differs</c>
    /// bitta foydalanuvchi bilan sinovdan o'tkazib bo'lmaydi. Ikkita smena ham
    /// ataylab — qulf testi qaytarimni "boshqa smenaga ko'chirish" ga
    /// urinadi, va u uchun haqiqiy ikkinchi smena kerak.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        await using var db = NewDb();

        var requester = new AppUser { FullName = "Admin", Role = "admin", Email = $"adm.{Guid.NewGuid():N}" };
        var approver = new AppUser { FullName = "Direktor", Role = "superadmin", Email = $"dir.{Guid.NewGuid():N}" };
        var student = new Student
        {
            FullName = "Qaytarim oluvchi", ClassName = "1-A", EnrollmentDate = "2026-01-01",
        };
        db.Users.AddRange(requester, approver);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        // `ux_cash_shifts_one_open_per_cashier` — bir kassirda bir vaqtda
        // faqat BITTA ochiq smena. Shuning uchun ikkinchisi boshqa odamniki.
        var shift = new CashShift
        {
            CashierId = requester.Id, OpenedAt = DateTimeOffset.UtcNow,
            OpeningFloat = 0m, Status = CashShiftStatus.Open,
        };
        var secondShift = new CashShift
        {
            CashierId = approver.Id, OpenedAt = DateTimeOffset.UtcNow,
            OpeningFloat = 0m, Status = CashShiftStatus.Open,
        };
        var expense = new Expense
        {
            OnDate = DateOnly.FromDateTime(DateTime.UtcNow), Category = "utilities",
            Amount = 750_000m, CreatedBy = requester.Id, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CashShifts.AddRange(shift, secondShift);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();

        return new World(requester.Id, approver.Id, student.Id, shift.Id, secondShift.Id, expense.Id);
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Migratsiyani berilgan nuqtaga olib boradi. <c>null</c> = oxirigacha
    /// (<c>Up</c>), nom berilsa — o'sha migratsiyagacha ORQAGA (<c>Down</c>).
    /// </summary>
    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>
    /// Berilgan SQL AYNAN kutilgan SQLSTATE bilan yiqilishini tasdiqlaydi va
    /// xatoni qaytaradi (xabar matnini alohida tekshirish uchun).
    /// </summary>
    private static async Task<PostgresException> AssertSqlStateAsync(
        string expected, NpgsqlConnection conn, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, sql));
        Assert.Equal(expected, error.SqlState);
        return error;
    }

    /// <summary>
    /// Jadvalga MINIMAL qator qo'yadi: NOT NULL va DEFAULT'siz har bir ustunga
    /// turiga mos "bo'sh" qiymat, qolganiga tegilmaydi.
    ///
    /// <para>
    /// Nega qo'lda yozilgan <c>INSERT</c> emas: <c>users</c> da o'nlab NOT
    /// NULL ustun bor va ular vaqt o'tishi bilan o'zgaradi. Qo'lda yozilgan
    /// ro'yxat birinchi yangi ustunda testni yiqitardi — va bu migratsiyaning
    /// emas, testning nosozligi bo'lardi.
    /// (<c>ParityWave2MigrationTests</c> dan olingan.)
    /// </para>
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null", conn))
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
                    // `information_schema` massiv turini AYNAN "ARRAY" deb
                    // beradi (element turi alohida ustunda). `users.permissions`
                    // — `text[]`, va usiz u pastdagi `now()` ga tushib 42804
                    // bilan yiqilardi.
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
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

    /// <summary>Butun sxemaning "jadval.ustun" to'plami.</summary>
    private async Task<HashSet<string>> AllColumnsAsync()
    {
        var rows = await ListAsync(
            "select table_name || '.' || column_name from information_schema.columns "
            + "where table_schema = 'public' "
            // Migratsiya tarixi jadvali hisobga olinmaydi — u har qadamda o'zgaradi.
            + "and table_name <> '__EFMigrationsHistory'");
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    private Task<bool> TableExistsAsync(string table) => BoolAsync(
        "select exists (select 1 from information_schema.tables "
        + $"where table_schema = 'public' and table_name = '{table}')");

    private Task<bool> IndexExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_indexes where schemaname = 'public' and indexname = '{name}')");

    private Task<bool> ConstraintExistsAsync(string name) => BoolAsync(
        $"select exists (select 1 from pg_constraint where conname = '{name}' and contype = 'c')");

    private Task<bool> FunctionExistsAsync(string name) => BoolAsync(
        "select exists (select 1 from pg_proc p join pg_namespace n on n.oid = p.pronamespace "
        + $"where n.nspname = 'public' and p.proname = '{name}')");

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

    private async Task<string?> TextAsync(string sql) => (string?)await RawScalarAsync(sql);

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
