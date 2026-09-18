using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `StudentsParityP2` — docs/modules/students-parity.md §3.3 (Batch C).
// ===========================================================================
//
//  NIMA XAVFLI
//  -----------
//  Bu migratsiya ikkita JONLI, ma'lumot bilan to'lgan jadvalga tegadi:
//  `assignments` (maktabning butun topshiriqlar tarixi) va `subjects`
//  (fan katalogi — unga jurnal, baho, jadval va sertifikat bog'langan).
//  Ularga NOT NULL ustun qo'shiladi, ya'ni MAVJUD har bir qator DEFAULT
//  qiymatni oladi. DEFAULT noto'g'ri bo'lsa (yoki umuman bo'lmasa) maktab
//  ertalab topshiriqlari "guruh topshirig'i" bo'lib qolganini yoki
//  fanlarining yarmi yo'qolganini ko'rardi.
//
//  Shuning uchun quyidagi testlar migratsiyadan OLDIN qator yozadi, keyin
//  oldinga suradi va natijani talab qiladi — `StudyGroupsMigrationTests`
//  dagi bir xil yo'nalish.
//
//  Ikkinchi xavf — BACKFILL: `certificate_subjects` mavjud
//  `certificates.subject_id` dan to'ldiriladi va eski ustun JOYIDA qolishi
//  SHART. Uni "ko'chirish" sertifikatning fanini ikkita joyga bo'lib
//  yuborardi.

[Collection(SchoolLmsCollection.Name)]
public class StudentsParityP2MigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20260917100831_FinanceParityBatchA";

    /// <summary>§3.3 ning uchta yangi jadvali.</summary>
    private static readonly string[] NewTables =
    [
        "certificate_subjects",
        "student_locations",
        "user_table_settings",
    ];

    /// <summary>
    /// Mavjud jadvallarga qo'shilgan yettita ustun: nomi, `null` bo'la
    /// oladimi va (NOT NULL bo'lsa) qanday DEFAULT bilan keladi.
    /// </summary>
    private static readonly (string Table, string Column, string Type, bool Nullable)[] NewColumns =
    [
        ("classes", "capacity", "smallint", true),
        ("subjects", "color", "text", true),
        ("subjects", "is_active", "boolean", false),
        ("students", "target_grade", "smallint", true),
        ("school_meta", "contract_number_mode", "text", false),
        ("assignments", "owner_kind", "text", false),
        ("rooms", "is_active", "boolean", false),
    ];

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("stparity2");

    /// <summary>
    /// Har test o'z bazasini oladi, ya'ni o'z ulanish hovuzini ham. Tozalanmasa
    /// konteynerdagi <c>max_connections</c> tugaydi va KEYINGI klasslar
    /// <c>53300</c> bilan yiqiladi.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. Up — jadvallar, ustunlar, turlar va nullability
    // =====================================================================

    [Fact]
    public async Task Up_uchta_jadval_va_yettita_ustun_qoshadi()
    {
        await using var conn = await OpenOwnerAsync();

        foreach (var table in NewTables)
            Assert.True(await TableExistsAsync(conn, table), $"`{table}` jadvali yo'q");

        foreach (var (table, column, type, nullable) in NewColumns)
        {
            Assert.True(await ColumnExistsAsync(conn, table, column), $"`{table}.{column}` yo'q");
            Assert.Equal(type, await DataTypeAsync(conn, table, column));
            Assert.Equal(nullable, await IsNullableAsync(conn, table, column));
        }
    }

    /// <summary>
    /// Yangi jadvallarning turlari §3.3 da yozilganidek: koordinata
    /// <c>numeric(9,6)</c>, olib ketish vaqti <c>time</c>, sozlamalar
    /// <c>jsonb</c>. Bularni keyin o'zgartirish `ALTER COLUMN` talab qiladi,
    /// shuning uchun birinchi kundan to'g'ri bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task Yangi_jadvallarning_turlari_spetsifikatsiyaga_mos()
    {
        await using var conn = await OpenOwnerAsync();

        Assert.Equal("numeric", await DataTypeAsync(conn, "student_locations", "lat"));
        Assert.Equal(9, await ScalarIntAsync(conn,
            "select numeric_precision from information_schema.columns "
            + "where table_name = 'student_locations' and column_name = 'lat'"));
        Assert.Equal(6, await ScalarIntAsync(conn,
            "select numeric_scale from information_schema.columns "
            + "where table_name = 'student_locations' and column_name = 'lat'"));

        Assert.Equal("time without time zone",
            await DataTypeAsync(conn, "student_locations", "pickup_from"));
        Assert.True(await IsNullableAsync(conn, "student_locations", "pickup_from"));

        Assert.Equal("jsonb", await DataTypeAsync(conn, "user_table_settings", "settings"));
        Assert.False(await IsNullableAsync(conn, "user_table_settings", "settings"));
    }

    // =====================================================================
    //  2. ENG MUHIM — migratsiyadan OLDINGI qatorlar bugungi ma'nosini
    //     saqlaydi
    // =====================================================================

    /// <summary>
    /// <b>Eng muhim test.</b> Migratsiyadan OLDIN yozilgan qatorlar
    /// o'zgarmagan xatti-harakatni oladi: topshiriq — SINF topshirig'i,
    /// fan va xona — FAOL, shartnoma raqami — AVTOMATIK. Sig'im, rang va
    /// mo'ljal esa <c>null</c>, ya'ni "ko'rsatilmagan".
    ///
    /// <para>
    /// Agar DEFAULT tushib qolsa yoki noto'g'ri bo'lsa, aynan shu test
    /// qizil bo'ladi — ekranlar esa jimgina boshqacha ishlay boshlardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Eski_qatorlar_bugungi_manosini_saqlaydi()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var (table, column, _, _) in NewColumns)
                Assert.False(await ColumnExistsAsync(conn, table, column),
                    $"`{table}.{column}` migratsiyadan OLDIN bo'lmasligi kerak");

            await InsertMinimalRowAsync(conn, "assignments", new() { ["id"] = "'as-p2-1'" });
            await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = "'sub-p2-1'", ["name"] = "'Kimyo'" });
            await InsertMinimalRowAsync(conn, "classes", new() { ["id"] = "'cls-p2-1'", ["name"] = "'8-V'" });
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-p2-1'" });
            await InsertMinimalRowAsync(conn, "rooms", new() { ["name"] = "'Ximiya xonasi'" });
            await InsertMinimalRowAsync(conn, "school_meta", []);
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            // Butun topshiriqlar tarixi — SINF topshirig'i (G-20).
            Assert.Equal("class", await ScalarAsync(conn,
                "select string_agg(distinct owner_kind, ',') from assignments"));

            // Fan va xona faol bo'lib qoladi (F-3, R-1).
            Assert.Equal(true, await ScalarAsync(conn, "select bool_and(is_active) from subjects"));
            Assert.Equal(true, await ScalarAsync(conn, "select bool_and(is_active) from rooms"));

            // Shartnoma raqamini tizim beradi (K-6).
            Assert.Equal("auto", await ScalarAsync(conn,
                "select string_agg(distinct contract_number_mode, ',') from school_meta"));

            // "Ko'rsatilmagan" uchtasi — hammasi null (C-4, F-3, S-9).
            Assert.Null(await ScalarAsync(conn, "select capacity from classes where id = 'cls-p2-1'"));
            Assert.Null(await ScalarAsync(conn, "select color from subjects where id = 'sub-p2-1'"));
            Assert.Null(await ScalarAsync(conn, "select target_grade from students where id = 'st-p2-1'"));
        }
    }

    /// <summary>
    /// Yangi qiymatlar QABUL qilinadi, o'ylab topilganlari esa rad etiladi —
    /// ya'ni CHECK cheklovlari haqiqatan ham qo'yilgan.
    /// </summary>
    [Fact]
    public async Task Cheklovlar_notogri_qiymatni_rad_etadi()
    {
        await using var conn = await OpenOwnerAsync();

        await InsertMinimalRowAsync(conn, "classes", new() { ["id"] = "'cls-chk'", ["name"] = "'9-A'" });
        await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = "'sub-chk'", ["name"] = "'Tarix'" });
        await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-chk'" });
        await InsertMinimalRowAsync(conn, "assignments", new() { ["id"] = "'as-chk'" });
        // Bazada `school_meta` qatori bo'lmasa `update` 0 ta qatorga tegadi va
        // CHECK umuman tekshirilmaydi — test soxta yashil bo'lardi.
        await InsertMinimalRowAsync(conn, "school_meta", []);

        // To'g'ri qiymatlar o'tadi.
        await ExecAsync(conn, "update classes set capacity = 24 where id = 'cls-chk'");
        await ExecAsync(conn, "update subjects set color = '#F59E0B', is_active = false where id = 'sub-chk'");
        await ExecAsync(conn, "update students set target_grade = 0 where id = 'st-chk'");
        await ExecAsync(conn, "update assignments set owner_kind = 'group' where id = 'as-chk'");
        await ExecAsync(conn, "update school_meta set contract_number_mode = 'manual'");

        // Noto'g'rilari — 23514.
        await RefusedAsync(conn, "update classes set capacity = 0 where id = 'cls-chk'");
        await RefusedAsync(conn, "update subjects set color = 'qizil' where id = 'sub-chk'");
        await RefusedAsync(conn, "update students set target_grade = 12 where id = 'st-chk'");
        await RefusedAsync(conn, "update assignments set owner_kind = 'sinfcha' where id = 'as-chk'");
        await RefusedAsync(conn, "update school_meta set contract_number_mode = 'qolda'");
    }

    /// <summary>
    /// Bitta o'quvchida har turdan BITTA joylashuv (L-2, "uchtagacha") —
    /// buni ilova emas, BAZA rad etadi.
    /// </summary>
    [Fact]
    public async Task Bir_turdan_ikkinchi_joylashuv_rad_etiladi()
    {
        await using var conn = await OpenOwnerAsync();

        await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-loc-1'" });

        await ExecAsync(conn, Location("home", "41.311081", "69.240562"));
        await ExecAsync(conn, Location("pickup", "41.300000", "69.200000"));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, Location("home", "41.000000", "69.000000")));
        Assert.Equal("23505", duplicate.SqlState);

        // Ro'yxatdan tashqari tur ham o'tmaydi.
        var badKind = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, Location("dala", "41.000000", "69.000000")));
        Assert.Equal("23514", badKind.SqlState);

        static string Location(string kind, string lat, string lng) =>
            "insert into student_locations (id, student_id, kind, lat, lng, created_at) values "
            + $"(gen_random_uuid(), 'st-loc-1', '{kind}', {lat}, {lng}, now())";
    }

    // =====================================================================
    //  3. Z-3 backfill — eski ustundan to'ldirish, uni YO'QOTMASDAN
    // =====================================================================

    /// <summary>
    /// Fani ko'rsatilgan sertifikat migratsiyadan keyin bog'lanish qatorini
    /// oladi; fansizi olmaydi; va eng muhimi — eski
    /// <c>certificates.subject_id</c> JOYIDA qoladi.
    /// </summary>
    [Fact]
    public async Task Sertifikat_fanlari_eski_ustundan_toldiriladi()
    {
        await MigrateToAsync(PreviousMigration);

        const string typeId = "0f8c4b7e-1111-4f21-9a3a-aaaaaaaaaaaa";

        await using (var conn = await OpenOwnerAsync())
        {
            await InsertActorAsync(conn, "u-cert");
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = "'st-cert'" });
            await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = "'sub-cert'", ["name"] = "'Ingliz tili'" });
            await InsertMinimalRowAsync(conn, "certificate_types", new()
            {
                ["id"] = $"'{typeId}'", ["name"] = "'IELTS'",
            });

            await InsertMinimalRowAsync(conn, "certificates", new()
            {
                ["id"] = "'11111111-aaaa-4bbb-8ccc-111111111111'",
                ["student_id"] = "'st-cert'",
                ["type_id"] = $"'{typeId}'",
                ["subject_id"] = "'sub-cert'",
                ["created_by"] = "'u-cert'",
                ["issued_on"] = "date '2026-05-01'",
            });

            // Fanga bog'liq bo'lmagan hujjat — bog'lanish OLMAYDI.
            await InsertMinimalRowAsync(conn, "certificates", new()
            {
                ["id"] = "'22222222-aaaa-4bbb-8ccc-222222222222'",
                ["student_id"] = "'st-cert'",
                ["type_id"] = $"'{typeId}'",
                ["subject_id"] = "null",
                ["created_by"] = "'u-cert'",
                ["issued_on"] = "date '2026-05-02'",
            });
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Equal(1L, await ScalarAsync(conn, "select count(*) from certificate_subjects"));
            Assert.Equal("sub-cert", await ScalarAsync(conn,
                "select subject_id from certificate_subjects "
                + "where certificate_id = '11111111-aaaa-4bbb-8ccc-111111111111'"));

            // ESKI USTUN JOYIDA — ko'chirilmadi, o'chirilmadi.
            Assert.Equal("sub-cert", await ScalarAsync(conn,
                "select subject_id from certificates "
                + "where id = '11111111-aaaa-4bbb-8ccc-111111111111'"));

            // Ikkinchi fanni qo'shish mumkin — Z-3 ning butun maqsadi.
            await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = "'sub-cert-2'", ["name"] = "'Adabiyot'" });
            await ExecAsync(conn,
                "insert into certificate_subjects (certificate_id, subject_id) values "
                + "('11111111-aaaa-4bbb-8ccc-111111111111', 'sub-cert-2')");
            Assert.Equal(2L, await ScalarAsync(conn,
                "select count(*) from certificate_subjects "
                + "where certificate_id = '11111111-aaaa-4bbb-8ccc-111111111111'"));
        }
    }

    // =====================================================================
    //  4. Grantlar — ilova roli uchala jadvalda ishlay oladi
    // =====================================================================

    /// <summary>
    /// Ilova roli (<c>app_rw</c>) yangi jadvallarda TO'LIQ ishlay olishi
    /// kerak: bularning birortasi moliyaviy jadval emas va uchalasida ham
    /// TUZATISH funksiyaning o'zi (noto'g'ri fan, ko'chgan uy, o'zgargan
    /// ustun tartibi). Grant migratsiyadan tushib qolsa ilova birinchi
    /// so'rovda 42501 bilan yiqilardi.
    /// </summary>
    [Fact]
    public async Task App_rw_yangi_jadvallarda_ishlay_oladi()
    {
        _database.RequireRealAppRw();

        const string typeId = "0f8c4b7e-2222-4f21-9a3a-bbbbbbbbbbbb";
        const string certId = "33333333-aaaa-4bbb-8ccc-333333333333";

        await using (var owner = await OpenOwnerAsync())
        {
            await InsertActorAsync(owner, "u-rw-p2");
            await InsertMinimalRowAsync(owner, "students", new() { ["id"] = "'st-rw-p2'" });
            await InsertMinimalRowAsync(owner, "subjects", new() { ["id"] = "'sub-rw-p2'", ["name"] = "'Biologiya'" });
            await InsertMinimalRowAsync(owner, "certificate_types", new()
            {
                ["id"] = $"'{typeId}'", ["name"] = "'Olimpiada'",
            });
            await InsertMinimalRowAsync(owner, "certificates", new()
            {
                ["id"] = $"'{certId}'",
                ["student_id"] = "'st-rw-p2'",
                ["type_id"] = $"'{typeId}'",
                ["subject_id"] = "null",
                ["created_by"] = "'u-rw-p2'",
                ["issued_on"] = "current_date",
            });
        }

        await using var app = new NpgsqlConnection(_database.AppRwConnectionString);
        await app.OpenAsync();

        // certificate_subjects — INSERT va DELETE (noto'g'ri fan tuzatiladi).
        await ExecAsync(app,
            "insert into certificate_subjects (certificate_id, subject_id) "
            + $"values ('{certId}', 'sub-rw-p2')");
        Assert.Equal(1L, await ScalarAsync(app, "select count(*) from certificate_subjects"));
        await ExecAsync(app, "delete from certificate_subjects");

        // student_locations — to'liq CRUD (uy ko'chadi).
        await ExecAsync(app,
            "insert into student_locations (id, student_id, kind, name, lat, lng, pickup_from, pickup_to, created_at) "
            + "values (gen_random_uuid(), 'st-rw-p2', 'pickup', 'Chorsu', 41.326100, 69.235200, "
            + "time '07:30', time '07:45', now())");
        await ExecAsync(app, "update student_locations set name = 'Chorsu bekati' where kind = 'pickup'");
        Assert.Equal("Chorsu bekati", await ScalarAsync(app,
            "select name from student_locations where kind = 'pickup'"));
        await ExecAsync(app, "delete from student_locations where kind = 'pickup'");

        // user_table_settings — to'liq CRUD (ustun tartibi o'zgaradi).
        await ExecAsync(app,
            "insert into user_table_settings (user_id, page, settings, updated_at) "
            + "values ('u-rw-p2', 'admin.students', '{\"hidden\":[\"phone\"]}'::jsonb, now())");
        await ExecAsync(app,
            "update user_table_settings set settings = '{}'::jsonb "
            + "where user_id = 'u-rw-p2' and page = 'admin.students'");
        Assert.Equal(1L, await ScalarAsync(app, "select count(*) from user_table_settings"));
        await ExecAsync(app, "delete from user_table_settings");

        // Yangi ustunlar MAVJUD jadvallarda — alohida grant talab qilmaydi
        // (huquq jadval darajasida beriladi).
        await ExecAsync(app, "update subjects set is_active = false, color = '#10B981' where id = 'sub-rw-p2'");
        await ExecAsync(app, "update students set target_grade = 5 where id = 'st-rw-p2'");
    }

    // =====================================================================
    //  5. Down — iz qoldirmaydi, ustun yo'qotmaydi
    // =====================================================================

    [Fact]
    public async Task Down_yaratilgan_hamma_narsani_orqaga_qaytaradi()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            foreach (var table in NewTables)
                Assert.False(await TableExistsAsync(conn, table), $"`{table}` `Down()` dan keyin qolib ketdi");

            foreach (var (table, column, _, _) in NewColumns)
                Assert.False(await ColumnExistsAsync(conn, table, column),
                    $"`{table}.{column}` `Down()` dan keyin qolib ketdi");

            // Cheklovlar ham ketdi.
            Assert.False(await CheckConstraintExistsAsync(conn, "ck_assignments_owner_kind"));
            Assert.False(await CheckConstraintExistsAsync(conn, "ck_subjects_color"));
            Assert.False(await CheckConstraintExistsAsync(conn, "ck_students_target_grade"));
            Assert.False(await CheckConstraintExistsAsync(conn, "ck_classes_capacity"));
            Assert.False(await CheckConstraintExistsAsync(conn, "ck_school_meta_contract_number_mode"));
        }

        // Va qaytadan oldinga — migratsiya ikkinchi marta ham toza yuradi
        // (backfill `ON CONFLICT DO NOTHING` bilan idempotent).
        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
            foreach (var table in NewTables)
                Assert.True(await TableExistsAsync(conn, table));
    }

    /// <summary>
    /// Migratsiya birorta MAVJUD ustunni yo'qotmasligi kerak: oldingi
    /// holatdagi (jadval, ustun) to'plami keyingisining QISM to'plami
    /// bo'lishi shart. Avtogeneratsiya so'ralmagan <c>DROP</c> yozganda
    /// aynan shu test qizil bo'ladi — va bu yerda u
    /// <c>certificates.subject_id</c> ni ham qo'riqlaydi.
    /// </summary>
    [Fact]
    public async Task Migratsiya_birorta_mavjud_ustunni_yoqotmaydi()
    {
        await MigrateToAsync(PreviousMigration);
        HashSet<string> before;
        await using (var conn = await OpenOwnerAsync()) before = await AllColumnsAsync(conn);

        Assert.Contains("certificates.subject_id", before);

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

    /// <summary>null = oxirigacha (Up), nom = o'sha migratsiyagacha orqaga (Down).</summary>
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

    /// <summary>CHECK cheklovi buyruqni 23514 bilan rad etishini talab qiladi.</summary>
    private static async Task RefusedAsync(NpgsqlConnection conn, string sql)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn, sql));
        Assert.Equal("23514", refused.SqlState);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<int?> ScalarIntAsync(NpgsqlConnection conn, string sql) =>
        await ScalarAsync(conn, sql) is int value ? value : null;

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.tables "
            + $"where table_schema = 'public' and table_name = '{table}'") is 1L;

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string table, string column) =>
        await ScalarAsync(conn,
            "select count(*) from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") is 1L;

    private static async Task<string?> DataTypeAsync(NpgsqlConnection conn, string table, string column) =>
        (string?)await ScalarAsync(conn,
            "select data_type from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'");

    private static async Task<bool> IsNullableAsync(NpgsqlConnection conn, string table, string column) =>
        (string?)await ScalarAsync(conn,
            "select is_nullable from information_schema.columns "
            + $"where table_schema = 'public' and table_name = '{table}' and column_name = '{column}'") == "YES";

    private static async Task<bool> CheckConstraintExistsAsync(NpgsqlConnection conn, string name) =>
        await ScalarAsync(conn,
            "select count(*) from pg_constraint "
            + $"where contype = 'c' and conname = '{name}'") is 1L;

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
    /// `created_by` foydalanuvchiga FK bilan bog'langan, shuning uchun har bir
    /// yozuv uchun aktyor kerak.
    /// </summary>
    private static Task InsertActorAsync(NpgsqlConnection conn, string id) =>
        InsertMinimalRowAsync(conn, "users", new()
        {
            ["id"] = $"'{id}'",
            ["email"] = $"'{id}@test.local'",
            ["full_name"] = "'Test aktyor'",
        });

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
                    // `users.permissions` va `assignments.class_ids` — `text[]`;
                    // information_schema ularni "ARRAY" deb ataydi. Busiz
                    // 42804 (turlar mos kelmadi).
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }
}
